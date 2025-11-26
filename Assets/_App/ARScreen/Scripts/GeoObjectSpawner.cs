using UnityEngine;
using Niantic.Lightship.AR.WorldPositioning;
using System.Collections;
using System.Collections.Generic;
using Shared.Scripts.Geo;
using Shared.Scripts.Building;
using Shared.Scripts.App;

/// <summary>
/// Spawnt Gebäude aus LV95-Polygonen in AR über Lightship WPS.
/// Unterstützt:
///  - Laden / Platzieren mehrerer Gebäude
///  - Auswahl eines Gebäudes (Selection + Highlight-Material)
///  - Anpassen von Höhe (Extrusion) und Altitude des selektierten Gebäudes
/// </summary>
public class GeoObjectSpawner : MonoBehaviour
{
    [Header("Building Settings")]
    [Tooltip("Gebäudehöhe in Metern (Extrusion).")]
    [Min(1f)]
    public float objectHeightMeters = 5f;

    [Header("Building Geometry (optional manuell)")]
    [Tooltip("Wenn true, wird die Geometrie aus dem Textfeld statt aus SelectedTargetContext gelesen.")]
    [SerializeField] private bool useBuildingGeometryFromTextField = false;
    [SerializeField, TextArea(4, 10)] private string buildingCoordinatesLv95;
    [SerializeField] private string buildingName = "Manual";
    [SerializeField] private CreateBuilding buildingFactory;

    [Header("WPS / AR")]
    [SerializeField] private ARWorldPositioningObjectHelper positioningHelper;
    [SerializeField] private ARWorldPositioningManager wpsManager;

    [Header("Selection")]
    [SerializeField] private Material selectedMaterial;

    private readonly Dictionary<string, GameObject> _buildingsByKey = new();

    private readonly Dictionary<string, SelectedTargetContext> _contextsByKey = new();

    public System.Action<bool> OnSelectionChanged;    private GameObject _selectedObject;
    private string _selectedKey;
    private Dictionary<Renderer, Material[]> _selectedOriginalMaterials;

    private double _altitudeMeters = 0.0;

    // WPS / Altitude Status
    private bool _wpsReady;
    private bool _altitudeReady;
    private bool _initializing;
    private Coroutine _initializationRoutine;
    private List<SelectedTargetContext> _pendingContexts;

    // Public Debug / Status Properties
    public double AltitudeMeters => _altitudeMeters;
    public bool IsWpsReady => _wpsReady;
    public bool IsReady => _altitudeReady && _wpsReady;

    private void Awake()
    {
        // Find or create WPS helper
        if (positioningHelper == null)
            positioningHelper = FindFirstObjectByType<ARWorldPositioningObjectHelper>();

        if (positioningHelper == null)
        {
            var go = new GameObject("ARWorldPositioningHelper");
            positioningHelper = go.AddComponent<ARWorldPositioningObjectHelper>();
        }

        if (wpsManager == null)
            wpsManager = FindFirstObjectByType<ARWorldPositioningManager>();

        if (wpsManager == null)
        {
            var go = new GameObject("ARWorldPositioningManager");
            wpsManager = go.AddComponent<ARWorldPositioningManager>();
        }

        if (buildingFactory == null)
            buildingFactory = FindFirstObjectByType<CreateBuilding>();

        EnsureInitializationRoutine();
    }

    private void EnsureInitializationRoutine()
    {
        if (_initializationRoutine == null)
            _initializationRoutine = StartCoroutine(InitializeSpatialDependencies());
    }

    /// <summary>
    /// Public API: Spawnt all Projections (Buildings).
    /// </summary>
    public void CreateARProjections(List<SelectedTargetContext> enriched = null)
    {
        Debug.Log($"[GeoObjectSpawner] CreateARProjections called. IsReady={IsReady}, WpsReady={_wpsReady}, AltReady={_altitudeReady}");

        if (enriched != null)
            _pendingContexts = new List<SelectedTargetContext>(enriched);

        if (!IsReady)
        {
            Debug.Log($"[GeoObjectSpawner] NOT READY - queueing {_pendingContexts?.Count ?? 0} projections.");
            EnsureInitializationRoutine();
            return;
        }

        bool hasPending = _pendingContexts != null && _pendingContexts.Count > 0;
        if (!hasPending && !useBuildingGeometryFromTextField)
            return;

        Debug.Log($"[GeoObjectSpawner] READY - spawning {_pendingContexts?.Count ?? 0} buildings now.");
        SpawnGeoObjects(_pendingContexts);
        _pendingContexts = null;
    }

    /// <summary>
    /// Initialize WPS and Altitude (from Device).
    /// </summary>
    private IEnumerator InitializeSpatialDependencies()
    {
        if (_initializing)
            yield break;

        _initializing = true;

        if (wpsManager != null)
        {
            _wpsReady = wpsManager.IsAvailable;

            if (!_wpsReady)
                Debug.LogError("[GeoObjectSpawner] WPS not ready after timeout. Buildings cannot be positioned in AR.");
        }
        else
        {
            _wpsReady = true;
        }

        yield return StartCoroutine(FetchAltitudeFromDevice());
        _altitudeReady = true;

        _initializing = false;
        _initializationRoutine = null;

        if (IsReady && _pendingContexts != null && _pendingContexts.Count > 0)
        {
            SpawnGeoObjects(_pendingContexts);
            _pendingContexts = null;
        }
    }

    private IEnumerator FetchAltitudeFromDevice()
    {
        if (!Input.location.isEnabledByUser)
        {
            Debug.LogWarning("[GeoObjectSpawner] Device location services disabled, using default altitude 0m");
            yield break;
        }

        var status = Input.location.status;
        if (status == LocationServiceStatus.Stopped)
        {
            Input.location.Start(1f, 0.5f);
            status = Input.location.status;
        }

        float elapsed = 0f;
        const float locationTimeout = 10f;
        while ((status == LocationServiceStatus.Initializing || status == LocationServiceStatus.Stopped) &&
               elapsed < locationTimeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
            status = Input.location.status;
        }

        if (status == LocationServiceStatus.Running)
        {
            _altitudeMeters = Input.location.lastData.altitude;
            Debug.Log($"[GeoObjectSpawner] Altitude from device: {_altitudeMeters}m");
        }
        else
        {
            Debug.LogWarning("[GeoObjectSpawner] Location service unavailable, using altitude 0m");
        }
    }

    // --------------------------------------------------------
    // Spawning
    // --------------------------------------------------------

    private void SpawnGeoObjects(List<SelectedTargetContext> enriched)
    {
        if (!_wpsReady || positioningHelper == null)
        {
            Debug.LogError("[GeoObjectSpawner] SpawnGeoObjects called but WPS or positioningHelper not ready.");
            return;
        }

        if (useBuildingGeometryFromTextField)
        {
            // manueller Single-Building-Modus
            TrySpawnBuildingGeometry(buildingCoordinatesLv95, buildingName, _altitudeMeters, out _);
            return;
        }

        if (enriched == null || enriched.Count == 0)
            return;

        foreach (var ctx in enriched)
        {
            var elevation = ctx.ElevationMeters.HasValue && ctx.ElevationMeters.Value > 0.0
                ? ctx.ElevationMeters.Value
                : _altitudeMeters;

            if (TrySpawnBuildingFromContext(ctx, elevation, out var go))
            {
                if (go == null) continue;

                var key = MakeBuildingKey(ctx);
                _buildingsByKey[key] = go;
                _contextsByKey[key] = ctx;
            }
        }
    }

    private bool TrySpawnBuildingFromContext(SelectedTargetContext ctx, double elevation, out GameObject buildingGo)
    {
        return TrySpawnBuildingGeometry(ctx.RawCoordinates, ctx.Name, elevation, out buildingGo);
    }

    /// <summary>
    /// Build Building-Mesh per CreateBuilding and positioning with WPS.
    /// Renderers are disabled until positioning is finished.
    /// </summary>
    private bool TrySpawnBuildingGeometry(string coordinatesLv95, string name, double elevation, out GameObject buildingGo)
    {
        buildingGo = null;

        if (!buildingFactory)
        {
            Debug.LogWarning("[GeoObjectSpawner] No buildingFactory assigned.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(coordinatesLv95))
            return false;

        var buildingNameToUse = string.IsNullOrWhiteSpace(name) ? buildingName : name;
        float altitude = (float)(elevation > 0.0 ? elevation : _altitudeMeters);

        buildingFactory.SetExtrusionHeight(objectHeightMeters);

        var building = buildingFactory.CreateBuildingFromCoordinates(coordinatesLv95, buildingNameToUse, altitude);
        if (building == null || building.GameObject == null)
            return false;

        buildingGo = building.GameObject;
        Debug.Log($"[GeoObjectSpawner] Building '{buildingNameToUse}' spawned | Altitude: {altitude}m | Height {objectHeightMeters}m");

        var renderers = buildingGo.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
            r.enabled = false;

        if (!_wpsReady || positioningHelper == null)
        {
            Debug.LogWarning($"[GeoObjectSpawner] WPS not ready or no positioningHelper, destroying building '{buildingNameToUse}'.");
            Destroy(buildingGo);
            return false;
        }

        StartCoroutine(PositionAndRevealBuilding(
            buildingGo,
            building.Latitude,
            building.Longitude,
            building.AltitudeMeters,
            renderers));

        VibrationService.TriggerLoadVibration(1000);
        return true;
    }

    private IEnumerator PositionAndRevealBuilding(
        GameObject go,
        double lat,
        double lon,
        double alt,
        Renderer[] renderers)
    {
        if (go == null || positioningHelper == null)
            yield break;

        go.transform.position = new Vector3(0f, -10000f, 0f);

        positioningHelper.AddOrUpdateObject(go, lat, lon, alt, Quaternion.identity);

        const int maxFrames = 30;
        int frame = 0;
        while (go != null && frame < maxFrames)
        {
            var pos = go.transform.position;

            bool hasValidPos =
                pos != Vector3.zero &&
                pos.y > -9999f &&
                !float.IsNaN(pos.x) && !float.IsNaN(pos.y) && !float.IsNaN(pos.z) &&
                !float.IsInfinity(pos.x) && !float.IsInfinity(pos.y) && !float.IsInfinity(pos.z);

            if (hasValidPos)
                break;

            frame++;
            yield return null;
        }

        foreach (var r in renderers)
        {
            if (r != null)
                r.enabled = true;
        }

        Debug.Log($"[GeoObjectSpawner] Reveal building after {frame} frame(s). Pos={go.transform.position}");
    }

    // --------------------------------------------------------
    // Clear / Selection
    // --------------------------------------------------------

    public void ClearAllBuildings()
    {
        RestoreSelectionMaterials();

        foreach (var kvp in _buildingsByKey)
        {
            if (kvp.Value != null)
                Destroy(kvp.Value);
        }

        _buildingsByKey.Clear();
        _contextsByKey.Clear();

        _selectedObject = null;
        _selectedKey = null;
        _selectedOriginalMaterials = null;
        OnSelectionChanged?.Invoke(false);
    }

    public void SelectBuilding(SelectedTargetContext ctx)
    {
        if (ctx == null)
            return;

        string key = MakeBuildingKey(ctx);

        // Altitude aus Kontext uebernehmen (falls vorhanden)
        _altitudeMeters = ctx.ElevationMeters ?? _altitudeMeters;

        SelectBuildingByKey(key);
        OnSelectionChanged?.Invoke(true);
    }

    private void SelectBuildingByKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            return;

        RestoreSelectionMaterials();
        _selectedObject = null;
        _selectedKey = null;
        _selectedOriginalMaterials = null;

        if (!_buildingsByKey.TryGetValue(key, out var go) || go == null)
        {
            Debug.LogWarning($"[GeoObjectSpawner] No spawned building found for key={key}");
            return;
        }

        _selectedObject = go;
        _selectedKey = key;

        if (selectedMaterial == null)
            return;

        _selectedOriginalMaterials = new Dictionary<Renderer, Material[]>();
        var renderers = go.GetComponentsInChildren<Renderer>(true);

        foreach (var r in renderers)
        {
            if (r == null) continue;

            _selectedOriginalMaterials[r] = r.materials;

            int count = r.materials.Length;
            var mats = new Material[count];
            for (int i = 0; i < count; i++)
                mats[i] = selectedMaterial;

            r.materials = mats;
        }

        Debug.Log("[GeoObjectSpawner] Selected building with key=" + key);
    }

    private void RestoreSelectionMaterials()
    {
        if (_selectedObject == null || _selectedOriginalMaterials == null)
            return;

        foreach (var kvp in _selectedOriginalMaterials)
        {
            if (kvp.Key != null)
                kvp.Key.materials = kvp.Value;
        }
    }

    private string MakeBuildingKey(SelectedTargetContext ctx)
    {
        if (!string.IsNullOrEmpty(ctx.Egid))
            return ctx.Egid;

        if (!string.IsNullOrEmpty(ctx.RawCoordinates))
            return ctx.RawCoordinates.GetHashCode().ToString();

        return (ctx.Latitude.ToString("F6") + "_" + ctx.Longitude.ToString("F6"))
            .GetHashCode()
            .ToString();
    }

    public void SetObjectHeightMeters(float h)
    {
        objectHeightMeters = Mathf.Max(1f, h);

        if (string.IsNullOrEmpty(_selectedKey))
            return;

        if (!_contextsByKey.TryGetValue(_selectedKey, out var ctx))
            return;

        ctx.ElevationMeters = ctx.ElevationMeters ?? _altitudeMeters;
        _contextsByKey[_selectedKey] = ctx;

        RebuildSelectedBuilding(ctx);
        Debug.Log("[GeoObjectSpawner] Building height set to " + objectHeightMeters + " meters.");
    }

    public void SetBuildingAltitudeMeters(double delta)
    {
        _altitudeMeters += delta;
        _altitudeMeters = System.Math.Max(0.0, _altitudeMeters);

        if (string.IsNullOrEmpty(_selectedKey))
            return;

        if (!_contextsByKey.TryGetValue(_selectedKey, out var ctx))
            return;

        ctx.ElevationMeters = _altitudeMeters;
        _contextsByKey[_selectedKey] = ctx;

        RebuildSelectedBuilding(ctx);
        Debug.Log("[GeoObjectSpawner] Building altitude set to " + _altitudeMeters + " meters.");
    }

    private void RebuildSelectedBuilding(SelectedTargetContext ctx)
    {
        if (string.IsNullOrEmpty(_selectedKey))
            return;

        RestoreSelectionMaterials();

        if (_buildingsByKey.TryGetValue(_selectedKey, out var oldGo) && oldGo != null)
            Destroy(oldGo);

        _buildingsByKey.Remove(_selectedKey);

        // neu spawnen mit aktueller Hoehe / Altitude
        var elevation = ctx.ElevationMeters ?? _altitudeMeters;
        if (TrySpawnBuildingFromContext(ctx, elevation, out var newGo) && newGo != null)
        {
            _buildingsByKey[_selectedKey] = newGo;
            SelectBuildingByKey(_selectedKey);
        }
        else
        {
            _selectedObject = null;
            _selectedKey = null;
            _selectedOriginalMaterials = null;
        }
    }
}