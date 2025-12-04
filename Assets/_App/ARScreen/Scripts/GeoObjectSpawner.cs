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

    [SerializeField] private CreateBuilding buildingFactory;

    [Header("WPS / AR")]
    [SerializeField] private ARWorldPositioningObjectHelper positioningHelper;
    [SerializeField] private ARWorldPositioningManager wpsManager;

    [Header("Selection")]
    [SerializeField]
    private Color selectedColor = Color.cyan;

    private readonly Dictionary<string, GameObject> _buildingsByKey = new();

    private readonly Dictionary<string, SelectedTargetContext> _contextsByKey = new();

    public System.Action<bool> OnSelectionChanged;    private GameObject _selectedObject;
    private string _selectedKey;
    private readonly string COLOR_PROPERTY = "_Color";
    private Dictionary<Renderer, Color> _selectedOriginalColors;
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
        if (!hasPending)
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
            float t = 0f;
            const float timeout = 30f;
            Debug.Log("[GeoObjectSpawner] Waiting for WPS to become available...");
            while (!wpsManager.IsAvailable && t < timeout)
            {
                t += Time.deltaTime;
                if ((int)t % 5 == 0 && t > 0)
                    Debug.Log($"[GeoObjectSpawner] Still waiting for WPS... ({t:F0}s/{timeout}s)");
                yield return null;
            }

            _wpsReady = wpsManager.IsAvailable;
            Debug.Log($"[GeoObjectSpawner] WPS available: {_wpsReady}");

            if (!_wpsReady)
                Debug.LogError("[GeoObjectSpawner] WPS not ready after timeout!");
        } else
        {
#if UNITY_EDITOR
            _wpsReady = true;
            Debug.LogWarning("[GeoObjectSpawner] No WPS Manager found (Editor). Using fallback.");
#else
            _wpsReady = false;
            Debug.LogError("[GeoObjectSpawner] No WPS Manager found! Cannot position buildings.");
#endif
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

                if (_buildingsByKey.TryGetValue(key, out var existing) && existing != null)
                {
                    Debug.LogWarning($"[GeoObjectSpawner] Overwriting existing building for key={key}, destroying old instance.");
                    Destroy(existing);
                }

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

        var buildingNameToUse = string.IsNullOrWhiteSpace(name) ? "Building" : name;
        float altitude = (float)(elevation > 0.0 ? elevation : _altitudeMeters);

        buildingFactory.SetExtrusionHeight(objectHeightMeters);

        var building = buildingFactory.CreateBuildingFromCoordinates(coordinatesLv95, buildingNameToUse, altitude);
        if (building == null || building.GameObject == null)
            return false;

        buildingGo = building.GameObject;
        Debug.Log($"[GeoObjectSpawner] Building '{buildingNameToUse}' spawned | Altitude: {altitude}m | Height {objectHeightMeters}m");

        var renderers = buildingGo.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r != null)
                r.enabled = true;
        }

        if (!_wpsReady || positioningHelper == null)
        {
            Debug.LogWarning($"[GeoObjectSpawner] WPS not ready or no positioningHelper, building '{buildingNameToUse}' stays at factory origin.");
            buildingGo.transform.SetParent(transform, false);
            VibrationService.TriggerLoadVibration(1000);
            return true;
        }

        positioningHelper.AddOrUpdateObject(
            buildingGo,
            building.Latitude,
            building.Longitude,
            building.AltitudeMeters,
            Quaternion.identity);

        VibrationService.TriggerLoadVibration(1000);
        return true;
    }

    // --------------------------------------------------------
    // Clear / Selection
    // --------------------------------------------------------

    public void ClearAllBuildings()
    {

        foreach (var kvp in _buildingsByKey)
        {
            if (kvp.Value != null)
                Destroy(kvp.Value);
        }

        _buildingsByKey.Clear();
        _contextsByKey.Clear();

        _selectedObject = null;
        _selectedKey = null;
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

        RestoreSelectionColors();

        _selectedObject = null;
        _selectedKey = null;
        _selectedOriginalColors = null;

        if (!_buildingsByKey.TryGetValue(key, out var go) || go == null)
        {
            Debug.LogWarning($"[GeoObjectSpawner] No spawned building found for key={key}");
            return;
        }

        _selectedObject = go;
        _selectedKey = key;

        _selectedOriginalColors = new Dictionary<Renderer, Color>();

        var renderers = go.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r == null) continue;

            var originalColor = r.sharedMaterial.HasProperty(COLOR_PROPERTY)
                ? r.sharedMaterial.GetColor(COLOR_PROPERTY)
                : Color.white;

            _selectedOriginalColors[r] = originalColor;

            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor(COLOR_PROPERTY, selectedColor);
            r.SetPropertyBlock(block);
        }

        Debug.Log("[GeoObjectSpawner] Selected building with key=" + key);
    }

    private void RestoreSelectionColors()
    {
        if (_selectedObject == null || _selectedOriginalColors == null)
            return;

        foreach (var kvp in _selectedOriginalColors)
        {
            var r = kvp.Key;
            if (r == null) continue;

            var color = kvp.Value;

            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor(COLOR_PROPERTY, color);
            r.SetPropertyBlock(block);
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
        }
    }
}