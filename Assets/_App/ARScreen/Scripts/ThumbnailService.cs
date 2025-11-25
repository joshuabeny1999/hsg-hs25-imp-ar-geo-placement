using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Shared.Scripts.Building;
using Shared.Scripts.Geo;

    /// <summary>
    /// Queues and renders building thumbnails off-screen using a dedicated rig.
    /// </summary>
    public class ThumbnailService : MonoBehaviour
    {
        public static ThumbnailService Instance { get; private set; }

        [Header("Rig / Factory")]
        [SerializeField] private GameObject previewRigPrefab;  // BuildingPreviewRig prefab
        [SerializeField] private CreateBuilding buildingFactoryPrefab; // a prefab or Scriptable host with materials

        [Header("Render Defaults")]
        [SerializeField] private int defaultSize = 256;
        [SerializeField, Range(1.0f, 2.5f)] private float padding = 1.2f; // how much air around bounds

        private Camera _cam;
        private Transform _stage;              // where we spawn building meshes
        private Light _light;
        private readonly Queue<Request> _queue = new();
        private bool _isRendering = false;

        private struct Request
        {
            public ProjectedBuilding Building;
            public int Size;
            public Action<Texture2D> OnReady;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Build the rig
            var rig = Instantiate(previewRigPrefab);
            rig.gameObject.hideFlags = HideFlags.HideAndDontSave;
            rig.layer = LayerMask.NameToLayer("ThumbnailPreview");

            _cam = rig.transform.Find("PreviewCamera").GetComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0, 0, 0, 0);
            _cam.enabled = false;
            
            _light = rig.GetComponentInChildren<Light>(true);

            // Stage = an empty child used as parent for temporary geometry
            _stage = new GameObject("Stage").transform;
            _stage.SetParent(rig.transform, false);
            _stage.gameObject.layer = LayerMask.NameToLayer("ThumbnailPreview");
        }

        /// <summary> Public API to request a thumbnail. </summary>
        public void RequestThumbnail(ProjectedBuilding building, int size, Action<Texture2D> onReady)
        {
            _queue.Enqueue(new Request { Building = building, Size = size > 0 ? size : defaultSize, OnReady = onReady });
            if (!_isRendering) StartCoroutine(ProcessQueue());
        }

        private IEnumerator ProcessQueue()
        {
            _isRendering = true;

            while (_queue.Count > 0)
            {
                var req = _queue.Dequeue();
                yield return RenderOne(req);
            }

            _isRendering = false;
        }

        private IEnumerator RenderOne(Request req)
        {
            // 1) Create building mesh using your factory (altitude 0, clear stage)
            ClearStage();
            
            Debug.Log("[ThumbnailService] Rendering thumbnail for building EGID: " + req.Building.Egid);

            // Use a throwaway factory instance so we don’t touch AR scene objects.
            var factory = Instantiate(buildingFactoryPrefab);
            factory.gameObject.hideFlags = HideFlags.HideAndDontSave;

            var built = factory.CreateBuildingFromCoordinates(
                req.Building.Coordinates,
                string.IsNullOrWhiteSpace(req.Building.GebHauptNutzung) ? req.Building.Egid : req.Building.GebHauptNutzung,
                0f,         // altitude for preview
                false);     // never clear user scene

            Destroy(factory.gameObject);

            if (built == null || built.GameObject == null)
            {
                req.OnReady?.Invoke(null);
                yield break;
            }

            var go = built.GameObject;
            go.transform.SetParent(_stage, false);
            SetLayerRecursive(go, LayerMask.NameToLayer("ThumbnailPreview"));

            // Re-enable renderers because building meshes were spawned hidden in AR factory mode
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
                r.enabled = true;

            // Reset any scaling/rotation (preview is local)
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            // 2) Frame the object in orthographic camera (use XZ footprint only)
            Bounds b = CalculateBounds(go);
            Vector3 center = b.center;

            // radius based on plan view, not height
            float radiusXZ = Mathf.Max(b.extents.x, b.extents.z) * padding;

            // Fixed isometric camera (consistent for all)
            _cam.orthographic = true;
            _cam.orthographicSize = radiusXZ;

            // Choose a nice iso angle (slightly lower tilt to show sides)
            Quaternion isoRot = Quaternion.Euler(22.5f, 45f, 0f);
            _cam.transform.rotation = isoRot;

            // Distance from center so object fits; push a bit and raise slightly
            float dist = radiusXZ * 2.2f;          // pull back
            Vector3 forward = _cam.transform.forward;
            _cam.transform.position = center - forward * dist + Vector3.up * (radiusXZ * 0.15f);

            // Keep light with camera
            if (_light)
            {
                _light.transform.position = _cam.transform.position;
                _light.transform.rotation = _cam.transform.rotation;
            }
            // 3) Render to a temporary RenderTexture → Texture2D
            var rt = new RenderTexture(req.Size, req.Size, 16, RenderTextureFormat.ARGB32);
            rt.useMipMap = false;
            rt.autoGenerateMips = false;
            rt.Create();
            _cam.targetTexture = rt;

            // Wait a frame to ensure everything is “settled”
            yield return new WaitForEndOfFrame();

            _cam.Render();

            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(req.Size, req.Size, TextureFormat.ARGB32, false, false);
            tex.ReadPixels(new Rect(0, 0, req.Size, req.Size), 0, 0);
            tex.Apply();

            _cam.targetTexture = null;
            RenderTexture.active = null;
            rt.Release();
            Destroy(rt);

            // 4) Cleanup spawned geometry
            ClearStage();
            
            Debug.Log("[ThumbnailService] Thumbnail rendered for building EGID: " + req.Building.Egid);

            // 5) Callback
            req.OnReady?.Invoke(tex);
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform)
                SetLayerRecursive(t.gameObject, layer);
        }

        private void ClearStage()
        {
            for (int i = _stage.childCount - 1; i >= 0; i--)
                DestroyImmediate(_stage.GetChild(i).gameObject);
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            Bounds? b = null;
            foreach (var r in renderers)
            {
                if (!b.HasValue) b = r.bounds;
                else b = Encapsulated(b.Value, r.bounds);
            }
            return b ?? new Bounds(Vector3.zero, Vector3.one);
        }

        private static Bounds Encapsulated(Bounds a, Bounds b)
        {
            a.Encapsulate(b.min);
            a.Encapsulate(b.max);
            return a;
        }
    }