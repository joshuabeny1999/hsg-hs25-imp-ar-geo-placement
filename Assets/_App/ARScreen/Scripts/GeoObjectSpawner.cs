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
    [SerializeField, Tooltip("Clear existing factory-spawned buildings before creating a new one.")]
    private bool clearExistingFactoryBuildings = false;
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

    // Public property to expose altitude for debug display
    public double AltitudeMeters => _altitudeMeters;


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
    }

    private void Start()
    {
        if (debugSpawnAtProvidedCoordinates)
        {
            Debug.Log("[GeoObjectSpawner] Debug spawn mode enabled; placing objects at world origin.");
            _altitudeMeters = 0.0;
            SpawnGeoObject();
        }
        else
        {
            StartCoroutine(WaitForWpsThenFetchAltitude());
        }
    }
    
    // <summary>
    /// Waits for WPS to become available (if applicable) before fetching altitude
    ///  </summary>
    private IEnumerator WaitForWpsThenFetchAltitude()
    {
        if (debugSpawnAtProvidedCoordinates)
            yield break;

        // If the manager exists, wait until WPS reports it’s available (with a short timeout)
        if (wpsManager != null)
        {
            float t = 0f, timeout = 10f;
            while (!wpsManager.IsAvailable && t < timeout)
            {
                t += Time.deltaTime;
                yield return null;
            }
            Debug.Log($"WPS available: {wpsManager.IsAvailable}");
        }

        yield return StartCoroutine(FetchAltitudeFromDevice());

        SpawnGeoObject();
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

    private void SpawnGeoObject()
    {
        if (useBuildingGeometryFromTextField)
        {
            TrySpawnBuildingGeometry(buildingCoordinatesLv95, buildingName, _altitudeMeters, out _, clearExistingFactoryBuildings);
            return;
        }

        if (!useBuildingGeometryFromTextField && !createCube)
        {
            double elevation = (SelectedTargetContext.ElevationMeters.HasValue && SelectedTargetContext.ElevationMeters.Value > 0.0)
                ? SelectedTargetContext.ElevationMeters.Value
                : _altitudeMeters;

            _altitudeMeters = elevation;

            TrySpawnBuildingGeometry(SelectedTargetContext.RawCoordinates, SelectedTargetContext.Name, _altitudeMeters, out _, clearExistingFactoryBuildings);
            return;
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


    public bool TrySpawnBuildingGeometry(string coordinatesLv95, string name, double elevation, out GameObject buildingGo, bool clearExisting = false)
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

        var building = buildingFactory.CreateBuildingFromCoordinates(targetCoordinates, buildingNameToUse, altitude, clearExisting);
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
            positioningHelper.AddOrUpdateObject(
                buildingGo,
                building.Latitude,
                building.Longitude,
                building.AltitudeMeters,
                Quaternion.identity);
        }

        _spawnedObject = buildingGo;
        _spawnedIsBuilding = true;
        _altitudeMeters = altitude;
        _lastBuildingCoordinates = targetCoordinates;
        _lastBuildingElevation = altitude;
        _lastBuildingName = buildingNameToUse;
        _lastClearExisting = clearExisting;

        return true;
    }

    private void RespawnLastBuilding()
    {
        if (string.IsNullOrWhiteSpace(_lastBuildingCoordinates))
            return;

        if (!TrySpawnBuildingGeometry(_lastBuildingCoordinates, _lastBuildingName, _altitudeMeters, out _, _lastClearExisting))
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
