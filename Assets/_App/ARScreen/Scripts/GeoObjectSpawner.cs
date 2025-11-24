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



    private GameObject _spawnedObject;
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
        if (enriched != null)
            _pendingContexts = new List<SelectedTargetContext>(enriched);

        if (debugSpawnAtProvidedCoordinates)
        {
            Debug.Log("[GeoObjectSpawner] Debug spawn mode enabled; placing objects at world origin.");
            _altitudeMeters = 0.0;
            SpawnGeoObject(_pendingContexts);
            _pendingContexts = null;
            return;
        }

        if (!IsReady)
        {
            Debug.Log("[GeoObjectSpawner] Not ready yet; queued projections until spatial services initialize.");
            EnsureInitializationRoutine();
            return;
        }

        bool hasPending = _pendingContexts != null && _pendingContexts.Count > 0;
        if (!hasPending && !useBuildingGeometryFromTextField && !createCube)
            return;

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
        if (useBuildingGeometryFromTextField)
        {
            TrySpawnBuildingGeometry(buildingCoordinatesLv95, buildingName, _altitudeMeters, out _);
            return;
        }

        if (!useBuildingGeometryFromTextField && !createCube && enriched != null)
        {
            // Needs to fetch the data from the request instead of the SelectedTargetContext. 

            foreach(SelectedTargetContext projection in enriched)
            {
                
                double elevation = (projection.ElevationMeters.HasValue && projection.ElevationMeters.Value > 0.0)
                ? projection.ElevationMeters.Value
                : _altitudeMeters;

            _altitudeMeters = elevation;

            TrySpawnBuildingGeometry(projection.RawCoordinates, projection.Name, _altitudeMeters, out _);
            }
            
        }

        if (createCube)
        {
            var cube = SpawnCubeInternal();
            _spawnedObject = cube;
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

        if (placeBuildingsAtZeroOrigin)
        {
            buildingGo.transform.SetParent(transform, false);
            buildingGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Debug.Log("[GeoObjectSpawner] Building forced to origin.");
        }
        else if (debugSpawnAtProvidedCoordinates)
        {
            buildingGo.transform.SetParent(transform, false);
            buildingGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Debug.Log($"[GeoObjectSpawner] Debug building spawned at provided coordinates | Original GPS target: {building.Latitude}, {building.Longitude}");
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
            positioningHelper.AddOrUpdateObject(
                buildingGo,
                building.Latitude,
                building.Longitude,
                building.AltitudeMeters,
                Quaternion.identity);
        }
        else
        {
            Debug.LogWarning($"[GeoObjectSpawner] No positioningHelper! Building '{buildingNameToUse}' stuck at factory origin.");
        }

        _spawnedObject = buildingGo;
        _spawnedIsBuilding = true;
        _altitudeMeters = altitude;
        _lastBuildingCoordinates = targetCoordinates;
        _lastBuildingElevation = altitude;
        _lastBuildingName = buildingNameToUse;

        VibrationService.TriggerLoadVibration(1000);

        return true;
    }

    private void RespawnLastBuilding()
    {
        if (string.IsNullOrWhiteSpace(_lastBuildingCoordinates))
            return;

        if (!TrySpawnBuildingGeometry(_lastBuildingCoordinates, _lastBuildingName, _altitudeMeters, out _))
        {
            _spawnedObject = null;
            _spawnedIsBuilding = false;
        }
    }

    public void SetObjectHeightMeters(float h)
    {
        objectHeightMeters = Mathf.Max(1f, h);
        if (_spawnedObject == null)
            return;

        if (_spawnedIsBuilding)
        {
            if (buildingFactory != null)
            {
                buildingFactory.SetExtrusionHeight(objectHeightMeters);
            }

            var toDestroy = _spawnedObject;
            _spawnedObject = null;
            if (toDestroy != null)
            {
                Destroy(toDestroy);
            }

            RespawnLastBuilding();
            Debug.Log("[GeoObjectSpawner] Building height set to " + objectHeightMeters + " meters.");
            return;
        }

        var s = _spawnedObject.transform.localScale;
        s.y = objectHeightMeters;
        _spawnedObject.transform.localScale = s;

        Debug.Log("[GeoObjectSpawner] Cube height set to " + objectHeightMeters + " meters.");

        var bb = _spawnedObject.transform.Find("Billboard");
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

        if (_spawnedObject == null)
            return;

        if (_spawnedIsBuilding)
        {
            // Recreate the building using the new altitude
            if (buildingFactory != null)
            {
                var toDestroy = _spawnedObject;
                _spawnedObject = null;
                if (toDestroy != null)
                {
                    Destroy(toDestroy);
                }

                RespawnLastBuilding();
                Debug.Log("[GeoObjectSpawner] Building altitude set to " + _altitudeMeters + " meters.");
            }

            return;
        }

        // If we have a cube spawned, update its altitude.
        if (debugSpawnAtProvidedCoordinates)
        {
            // In debug mode the cube is parented to this transform at Vector3.zero; move it locally along Y
            _spawnedObject.transform.localPosition = new Vector3(0f, (float)_altitudeMeters, 0f);
            Debug.Log($"[GeoObjectSpawner] Debug cube altitude set to {_altitudeMeters}m (local Y moved).");
            return;
        }

        // Minimal approach: convert LV95->WGS84 on demand using the stored east/north and update via positioning helper
        if (positioningHelper != null)
        {
            ProjNetTransformCH.LV95ToWGS84(east, north, out double lat, out double lon);
            positioningHelper.AddOrUpdateObject(_spawnedObject, lat, lon, _altitudeMeters, Quaternion.identity);
            Debug.Log("[GeoObjectSpawner] Cube altitude set to " + _altitudeMeters + " meters.");
        }
    }
}
