using UnityEngine;
using Niantic.Lightship.AR.WorldPositioning;
using System.Collections;
using System.Collections.Generic;
using Shared.Scripts.Geo; 
using Shared.Scripts.Building;
using Shared.Scripts.App;
using UnityEngine.Serialization;

/// <summary>
/// Simple spawner that places a cube at a specific GPS location
/// Supports selecting altitude from the device sensor or the Open-Elevation API (comment toggle)
/// </summary>
public class GeoObjectSpawner : MonoBehaviour
{
    [Header("Common Object Settings")]
    [Tooltip("Object height in meters (cube Y scale or building extrusion).")]
    [Min(1f)]
    public float objectHeightMeters = 5f;

    [Header("Cube-Only Settings")]
    [SerializeField] private bool createCube = false;
    [Tooltip("Size of the cube in meters (larger = more visible from distance)")]
    public float cubeSize = 5.0f;
    [SerializeField] private Material cubeMaterial;


    [Header("Building Geometry")]
    [Tooltip("If true, CreateBuilding uses coordinates from the text field instead of SelectedTargetContext.")]
    [SerializeField] private bool useBuildingGeometryFromTextField = false;
    //This needs to be filled in from the start, information provided by the scene before 
    [SerializeField, TextArea(4, 10)] private string buildingCoordinatesLv95;
    [SerializeField] private string buildingName = "Manual";
    [SerializeField] private CreateBuilding buildingFactory;

    [Header("WPS Helper")]
    [SerializeField] private ARWorldPositioningObjectHelper positioningHelper;
    [SerializeField] private ARWorldPositioningManager wpsManager;

    [Header("Debug")]
    [SerializeField] private bool debugSpawnAtProvidedCoordinates = false;
    [SerializeField] public double east = 2739782.97;
    [SerializeField] public double north = 1250944.04;
    [SerializeField] private bool placeBuildingsAtZeroOrigin = false;

    [Header("Selection")]
    [SerializeField] private Material selectedMaterial;

    private readonly Dictionary<string, GameObject> _buildingsByKey = new();
    private Dictionary<Renderer, Material[]> _selectedOriginalMaterials;

    private readonly List<GameObject> _spawnedBuildings = new();
    private GameObject _selectedObject;
    private bool _spawnedIsBuilding;
    private double _altitudeMeters = 0.0;

    // (minimal) no persistent last-lat/lon stored — convert LV95->WGS84 on demand when needed

    private string _lastBuildingCoordinates;
    private double _lastBuildingElevation;
    private string _lastBuildingName;
    private bool _lastClearExisting;

    private bool _wpsReady;
    private bool _altitudeReady;
    private bool _initializing;
    private Coroutine _initializationRoutine;
    private List<SelectedTargetContext> _pendingContexts;

    // Public property to expose altitude for debug display
    public double AltitudeMeters => _altitudeMeters;
    public bool IsWpsReady => _wpsReady;
    
    // Ready once altitude is fetched AND WPS is available (buildings need WPS to be positioned correctly)
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

    public void CreateARProjections(List<SelectedTargetContext> enriched = null)
    {
        Debug.Log($"[GeoObjectSpawner] CreateARProjections called. IsReady={IsReady}, WpsReady={_wpsReady}, AltReady={_altitudeReady}, debugSpawn={debugSpawnAtProvidedCoordinates}");
        
        if (enriched != null)
            _pendingContexts = new List<SelectedTargetContext>(enriched);

        // CRITICAL: Check IsReady FIRST, before any debug modes
        if (!IsReady)
        {
            Debug.Log($"[GeoObjectSpawner] NOT READY - queuing {_pendingContexts?.Count ?? 0} projections. Waiting for WPS and altitude.");
            EnsureInitializationRoutine();
            return;
        }

        // Only allow debug spawn if explicitly enabled AND we're ready
        if (debugSpawnAtProvidedCoordinates)
        {
            Debug.Log("[GeoObjectSpawner] Debug spawn mode enabled; placing objects at world origin.");
            _altitudeMeters = 0.0;
            SpawnGeoObject(_pendingContexts);
            _pendingContexts = null;
            return;
        }

        bool hasPending = _pendingContexts != null && _pendingContexts.Count > 0;
        if (!hasPending && !useBuildingGeometryFromTextField && !createCube)
            return;

        Debug.Log($"[GeoObjectSpawner] READY - spawning {_pendingContexts?.Count ?? 0} buildings now.");
        SpawnGeoObject(_pendingContexts);
        _pendingContexts = null;
    }
    
    // <summary>
    /// Waits for WPS to become available (if applicable) before fetching altitude
    ///  </summary>
    private IEnumerator InitializeSpatialDependencies()
    {
        if (_initializing)
            yield break;

        _initializing = true;

        if (wpsManager != null)
        {
            float t = 0f, timeout = 30f; // increased timeout to 30s
            Debug.Log("[GeoObjectSpawner] Waiting for WPS to become available...");
            while (!wpsManager.IsAvailable && t < timeout)
            {
                t += Time.deltaTime;
                if ((int)t % 5 == 0 && t > 0) // log every 5 seconds
                    Debug.Log($"[GeoObjectSpawner] Still waiting for WPS... ({t:F0}s / {timeout}s)");
                yield return null;
            }

            _wpsReady = wpsManager.IsAvailable;
            Debug.Log($"[GeoObjectSpawner] WPS available: {_wpsReady}");

            if (!_wpsReady)
                Debug.LogError("[GeoObjectSpawner] WPS not ready after timeout. Buildings cannot be positioned in AR. Check: 1) Lightship API key, 2) GPS enabled, 3) ARWorldPositioningManager in scene.");
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
            SpawnGeoObject(_pendingContexts);
            _pendingContexts = null;
        }
    }

    /// <summary>
    /// Fetches real-world altitude from Device GPS sensor
    /// </summary>
    private IEnumerator FetchAltitudeFromDevice()
    {
        if (!Input.location.isEnabledByUser)
        {
            Debug.LogWarning("[GeoObjectSpawner] Device location services disabled using default altitude 0m");
            yield break;
        }

        var status = Input.location.status;
        if (status == LocationServiceStatus.Stopped)
        {
            Input.location.Start(1f, 0.5f);
            status = Input.location.status;
        }

        float elapsed = 0f;
        float locationTimeout = 10f;
        while ((status == LocationServiceStatus.Initializing || status == LocationServiceStatus.Stopped) && elapsed < locationTimeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
            status = Input.location.status;
        }

        if (status == LocationServiceStatus.Running)
        {
            _altitudeMeters = Input.location.lastData.altitude;
            Debug.Log($"[GeoObjectSpawner] Altitude received from device sensor: {_altitudeMeters}m");
        }
        else
        {
            Debug.LogWarning("[GeoObjectSpawner] Location service unavailable using default altitude 0m");
        }
    }

    private void AddBillboardLabel(Transform parent, string text = "↓ This is a Demo Cube ↓")
    {
        var go = new GameObject("Billboard");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0, objectHeightMeters + 0.5f, 0);

        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.fontSize = 64;
        tm.characterSize = 0.05f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = Color.cyan;
    }

    private void SpawnGeoObject(List<SelectedTargetContext> enriched = null)
    {
        // SAFETY CHECK: Never spawn if WPS isn't ready (would cause black screen)
        if (!_wpsReady && !debugSpawnAtProvidedCoordinates && !placeBuildingsAtZeroOrigin)
        {
            Debug.LogError("[GeoObjectSpawner] SpawnGeoObject called but WPS not ready! Aborting to prevent black screen.");
            return;
        }
        
        if (useBuildingGeometryFromTextField)
        {
            TrySpawnBuildingGeometry(buildingCoordinatesLv95, buildingName, _altitudeMeters, out _);
            return;
        }

        if (!useBuildingGeometryFromTextField && !createCube && enriched != null)
        {
            Debug.Log($"[GeoObjectSpawner] Spawning {enriched.Count} buildings from enriched list...");
            
            foreach (SelectedTargetContext projection in enriched)
            {
                double elevation = (projection.ElevationMeters.HasValue && projection.ElevationMeters.Value > 0.0)
                    ? projection.ElevationMeters.Value
                    : _altitudeMeters;

                _altitudeMeters = elevation;

                if (TrySpawnBuildingGeometry(projection.RawCoordinates, projection.Name, _altitudeMeters, out var go))
                {
                    if (go != null)
                    {
                        _spawnedBuildings.Add(go);

                        string key = MakeBuildingKey(projection);
                        _buildingsByKey[key] = go;
                    }
                }
            }
        }

        if (createCube)
        {
            var cube = SpawnCubeInternal();
            _selectedObject = cube;
            _spawnedIsBuilding = false;
            _lastBuildingCoordinates = null;
            _lastBuildingName = null;
            _lastClearExisting = false;
        }
    }

    private GameObject SpawnCubeInternal()
    {
        // Convert LV95 -> WGS84 for Lightship
        ProjNetTransformCH.LV95ToWGS84(east, north, out double lat, out double lon);
        Debug.Log($"[GeoObjectSpawner] Converted LV95 to WGS84: {east}, {north} -> {lat}, {lon}");

        // Create cube
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "GeoCube";
        cube.transform.localScale = new Vector3(cubeSize, objectHeightMeters, cubeSize);

        // Use linked material (with a safe instance)
        Material mat = cubeMaterial;
        cube.GetComponent<Renderer>().material = mat;

        if (debugSpawnAtProvidedCoordinates)
        {
            cube.transform.SetParent(transform, false);
            cube.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Debug.Log($"[GeoObjectSpawner] Debug cube spawned at world origin | Original GPS target: {lat}, {lon} | Altitude override: {_altitudeMeters}m");
        }
        else
        {
            // Position at GPS location
            positioningHelper.AddOrUpdateObject(cube, lat, lon, _altitudeMeters, Quaternion.identity);

            Debug.Log($"[GeoObjectSpawner] Cube spawned at GPS: {lat}, {lon} | Altitude: {_altitudeMeters}m | Size: {cubeSize}m | Height: {objectHeightMeters}m");
        }

        AddBillboardLabel(cube.transform);

        return cube;
    }


    public bool TrySpawnBuildingGeometry(string coordinatesLv95, string name, double elevation, out GameObject buildingGo)
    {
        buildingGo = null;

        if (!buildingFactory)
        {
            Debug.LogWarning("[GeoObjectSpawner] Building factory not assigned and none found in scene; falling back to cube.");
            return false;
        }

        var targetCoordinates = coordinatesLv95;
        if (string.IsNullOrWhiteSpace(targetCoordinates))
        {
            return false;
        }

        var buildingNameToUse = string.IsNullOrWhiteSpace(name) ? buildingName : name;
        float altitude = (float)(elevation > 0.0 ? elevation : _altitudeMeters);

        buildingFactory.SetExtrusionHeight(objectHeightMeters);

        var building = buildingFactory.CreateBuildingFromCoordinates(targetCoordinates, buildingNameToUse, altitude);
        if (building == null || building.GameObject == null)
        {
            return false;
        }

        buildingGo = building.GameObject;
        Debug.Log($"[GeoObjectSpawner] Building '{buildingNameToUse}' spawned at coordinates: {targetCoordinates} | Altitude: {altitude}m | Height {objectHeightMeters}m");


        var renderers = buildingGo.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
            r.enabled = false;


        if (placeBuildingsAtZeroOrigin)
        {
            buildingGo.transform.SetParent(transform, false);
            buildingGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Debug.Log("[GeoObjectSpawner] Building forced to origin.");

            foreach (var r in renderers)
                r.enabled = true;

        }
        else if (debugSpawnAtProvidedCoordinates && positioningHelper != null)
        {
            ProjNetTransformCH.LV95ToWGS84(east, north, out double dbgLat, out double dbgLon);

            StartCoroutine(PositionAndRevealBuilding(
                buildingGo,
                dbgLat,
                dbgLon,
                altitude,
                renderers));

            Debug.Log($"[GeoObjectSpawner] Debug positioning building '{buildingNameToUse}' at GPS: {dbgLat:F6}, {dbgLon:F6}, alt={building.AltitudeMeters}m");
        }
        else if (positioningHelper != null)
        {
            // CRITICAL: Only position if WPS is actually ready, otherwise building stays at origin (black screen)
            if (!_wpsReady)
            {
                Debug.LogWarning($"[GeoObjectSpawner] WPS not ready! Destroying building '{buildingNameToUse}' to prevent black screen.");
                Destroy(buildingGo);
                return false;
            }
            
            Debug.Log($"[GeoObjectSpawner] Positioning building '{buildingNameToUse}' at GPS: {building.Latitude:F6}, {building.Longitude:F6}, alt={building.AltitudeMeters}m");
            StartCoroutine(PositionAndRevealBuilding(buildingGo, building.Latitude, building.Longitude, building.AltitudeMeters, renderers));
        }
        else
        {
            Debug.LogWarning($"[GeoObjectSpawner] No positioningHelper! Building '{buildingNameToUse}' stuck at factory origin.");
            foreach (var r in renderers)
                r.enabled = true;

        }

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

        // AR-Position anfragen
        positioningHelper.AddOrUpdateObject(go, lat, lon, alt, Quaternion.identity);

        // 1–N Frames warten, bis Transform plausibel ist
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

    private void RespawnSelectedBuilding()
    {
        if (string.IsNullOrWhiteSpace(_lastBuildingCoordinates))
            return;

        if (!TrySpawnBuildingGeometry(_lastBuildingCoordinates, _lastBuildingName, _altitudeMeters, out _))
        {
            _selectedObject = null;
            _spawnedIsBuilding = false;
        }
    }

    public void ClearAllBuildings()
    {
        // Materialien der Selection zurücksetzen
        if (_selectedObject != null && _selectedOriginalMaterials != null)
        {
            foreach (var kvp in _selectedOriginalMaterials)
            {
                if (kvp.Key != null)
                    kvp.Key.materials = kvp.Value;
            }
        }

        _selectedOriginalMaterials = null;
        _selectedObject = null;

        foreach (var go in _spawnedBuildings)
        {
            if (go != null)
                Destroy(go);
        }

        _spawnedBuildings.Clear();
        _buildingsByKey.Clear();

        _spawnedIsBuilding = false;
        _lastBuildingCoordinates = null;
        _lastBuildingName = null;
        _lastBuildingElevation = 0.0;
    }

    public void SetObjectHeightMeters(float h)
    {
        objectHeightMeters = Mathf.Max(1f, h);
        if (_selectedObject == null)
            return;

        if (_spawnedIsBuilding)
        {
            if (buildingFactory != null)
            {
                buildingFactory.SetExtrusionHeight(objectHeightMeters);
            }

            var toDestroy = _selectedObject;
            _selectedObject = null;
            if (toDestroy != null)
            {
                Destroy(toDestroy);
            }

            RespawnSelectedBuilding();
            Debug.Log("[GeoObjectSpawner] Building height set to " + objectHeightMeters + " meters.");
            return;
        }

        var s = _selectedObject.transform.localScale;
        s.y = objectHeightMeters;
        _selectedObject.transform.localScale = s;

        Debug.Log("[GeoObjectSpawner] Cube height set to " + objectHeightMeters + " meters.");

        var bb = _selectedObject.transform.Find("Billboard");
        if (bb != null) bb.localPosition = new Vector3(0f, objectHeightMeters + 0.5f, 0f);
    }

    /// <summary>
    /// Adjusts (adds/deducts) the altitude (meters) where the currently spawned object will be placed.
    /// The parameter `a` is treated as a delta: positive to raise, negative to lower.
    /// The resulting altitude is clamped to a minimum of 0 meters.
    /// If a building is spawned, it will be destroyed and respawned at the new altitude.
    /// If a cube is spawned, its AR position will be updated if possible (or moved locally in debug mode).
    /// </summary>
    public void SetBuildingAltitudeMeters(double a)
    {
    // apply as delta
    _altitudeMeters += a;
    // clamp to >= 0 meters
    _altitudeMeters = System.Math.Max(0.0, _altitudeMeters);

        if (_selectedObject == null)
            return;

        if (_spawnedIsBuilding)
        {
            // Recreate the building using the new altitude
            if (buildingFactory != null)
            {
                var toDestroy = _selectedObject;
                _selectedObject = null;
                if (toDestroy != null)
                {
                    Destroy(toDestroy);
                }

                RespawnSelectedBuilding();
                Debug.Log("[GeoObjectSpawner] Building altitude set to " + _altitudeMeters + " meters.");
            }

            return;
        }

        // If we have a cube spawned, update its altitude.
        if (debugSpawnAtProvidedCoordinates)
        {
            // In debug mode the cube is parented to this transform at Vector3.zero; move it locally along Y
            _selectedObject.transform.localPosition = new Vector3(0f, (float)_altitudeMeters, 0f);
            Debug.Log($"[GeoObjectSpawner] Debug cube altitude set to {_altitudeMeters}m (local Y moved).");
            return;
        }

        // Minimal approach: convert LV95->WGS84 on demand using the stored east/north and update via positioning helper
        if (positioningHelper != null)
        {
            ProjNetTransformCH.LV95ToWGS84(east, north, out double lat, out double lon);
            positioningHelper.AddOrUpdateObject(_selectedObject, lat, lon, _altitudeMeters, Quaternion.identity);
            Debug.Log("[GeoObjectSpawner] Cube altitude set to " + _altitudeMeters + " meters.");
        }
    }

    public void SelectBuilding(SelectedTargetContext ctx)
    {
        if (ctx == null)
            return;

        _spawnedIsBuilding       = true;
        _altitudeMeters          = ctx.ElevationMeters ?? _altitudeMeters;
        _lastBuildingCoordinates = ctx.RawCoordinates;
        _lastBuildingName        = ctx.Name;
        _lastBuildingElevation   = _altitudeMeters;

        string key = MakeBuildingKey(ctx);
        SelectBuildingByKey(key);
    }

    private void SelectBuildingByKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            return;

        // Reset previous selection
        if (_selectedObject != null && _selectedOriginalMaterials != null)
        {
            Debug.Log("[GeoObjectSpawner] Restoring original materials of previous selected building.");
            foreach (var kvp in _selectedOriginalMaterials)
            {
                if (kvp.Key != null)
                    kvp.Key.materials = kvp.Value;
            }
        }

        _selectedObject = null;
        _selectedOriginalMaterials = null;

        if (!_buildingsByKey.TryGetValue(key, out var go) || go == null)
        {
            Debug.LogWarning($"[GeoObjectSpawner] No spawned building found for key={key}");
            return;
        }

        _selectedObject = go;

        if (selectedMaterial == null)
            return;

        _selectedOriginalMaterials = new Dictionary<Renderer, Material[]>();
        var renderers = go.GetComponentsInChildren<Renderer>(true);

        foreach (var r in renderers)
        {
            if (r == null) continue;

            // Save original
            _selectedOriginalMaterials[r] = r.materials;

            // replace with selectedMat
            var count = r.materials.Length;
            var mats = new Material[count];
            for (int i = 0; i < count; i++)
                mats[i] = selectedMaterial;

            r.materials = mats;
        }
        Debug.Log("[GeoObjectSpawner] Selected building with key=" + key);
    }

    private string MakeBuildingKey(SelectedTargetContext ctx)
    {
        if (!string.IsNullOrEmpty(ctx.Egid))
            return ctx.Egid; // Falls vorhanden, immer nutzen

        // Fallback → RawCoordinates (100 % eindeutig)
        if (!string.IsNullOrEmpty(ctx.RawCoordinates))
            return ctx.RawCoordinates.GetHashCode().ToString();

        // Worst-case → Lat/Lon kombinieren
        return (ctx.Latitude.ToString("F6") + "_" + ctx.Longitude.ToString("F6")).GetHashCode().ToString();
    }
}
