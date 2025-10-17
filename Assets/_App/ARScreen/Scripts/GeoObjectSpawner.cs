using UnityEngine;
using Niantic.Lightship.AR.WorldPositioning;
using System.Collections;
using Shared.Scripts.Geo; 

/// <summary>
/// Simple spawner that places a cube at a specific GPS location
/// Supports selecting altitude from the device sensor or the Open-Elevation API (comment toggle)
/// </summary>
public class GeoObjectSpawner : MonoBehaviour
{
    [Header("WPS Helper")]
    [SerializeField] private ARWorldPositioningObjectHelper positioningHelper;
    [SerializeField] private ARWorldPositioningManager wpsManager;

    [Header("LV95 Coordinates (EPSG:2056)")]
    public double east = 2739782.97;
    public double north = 1250944.04;

    [Header("Cube Settings")]
    [Tooltip("Size of the cube in meters (larger = more visible from distance)")]
    public float cubeSize = 5.0f;
    [Tooltip("Cube height in meters (Y scale)")]
    public float cubeHeightMeters = 5f;
    [SerializeField] private Material cubeMaterial; 
    [SerializeField] private bool instanceMaterial = true; 

    [Header("Building Geometry (optional)")]
    [Tooltip("If provided, CreateBuilding will be used instead of a primitive cube.")]
    [SerializeField] private bool useBuildingGeometry = false;
    [SerializeField, TextArea(4, 10)] private string buildingCoordinatesLv95;
    [SerializeField] private string buildingName = "Manual";
    [SerializeField, Tooltip("Clear existing factory-spawned buildings before creating a new one.")]
    private bool clearExistingFactoryBuildings = false;
    [SerializeField] private CreateBuilding buildingFactory;
    
    [Header("Debug")]
    [SerializeField, Tooltip("Bypass AR world positioning and place spawned objects at the scene origin.")]
    private bool debugSpawnAtOrigin = false;


    private GameObject _spawnedObject;
    private bool _spawnedIsBuilding;
    private double _altitudeMeters = 0.0;

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
        if (debugSpawnAtOrigin)
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
        if (debugSpawnAtOrigin)
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

        // Toggle altitude source by commenting/uncommenting the desired line below.
        yield return StartCoroutine(FetchAltitudeFromDevice());
        // yield return StartCoroutine(FetchAltitudeFromApi());

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

    /// <summary>
    /// Fetches real-world altitude from Open-Elevation API
    /// API: https://open-elevation.com/
    /// </summary>
    private IEnumerator FetchAltitudeFromApi()
    {
        yield return GeoInfoAPI.FetchElevation(east, north, resp =>
        {
            if (resp != null)
            {
                _altitudeMeters = resp.elevation;
                Debug.Log($"[GeoObjectSpawner] Altitude received: {_altitudeMeters}m");
            }
            else
            {
                Debug.LogWarning("[GeoObjectSpawner] Failed to fetch altitude using default 0m");
            }

            SpawnGeoObject();
        });

        SpawnGeoObject();
    }
    
    private void AddBillboardLabel(Transform parent, string text = "↓ This is a Demo Cube ↓")
    {
        var go = new GameObject("Billboard");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0, cubeHeightMeters + 0.5f, 0);

        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.fontSize = 64;
        tm.characterSize = 0.05f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = Color.cyan;
    }

    private void SpawnGeoObject()
    {
        if (useBuildingGeometry && TrySpawnBuildingGeometry(buildingCoordinatesLv95, buildingName, out var buildingGo, clearExistingFactoryBuildings))
        {
            _spawnedObject = buildingGo;
            _spawnedIsBuilding = true;
            return;
        }

        if (useBuildingGeometry)
        {
            Debug.LogWarning("[GeoObjectSpawner] Building geometry data missing or invalid; falling back to cube.");
        }

        var cube = SpawnCubeInternal();
        _spawnedObject = cube;
        _spawnedIsBuilding = false;
    }

    private GameObject SpawnCubeInternal()
    {
        // Convert LV95 -> WGS84 for Lightship
        ProjNetTransformCH.LV95ToWGS84(east, north, out double lat, out double lon);
        Debug.Log($"[GeoObjectSpawner] Converted LV95 to WGS84: {east}, {north} -> {lat}, {lon}");

        // Create cube
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "GeoCube";
        cube.transform.localScale = new Vector3(cubeSize, cubeHeightMeters, cubeSize);

        // Use linked material (with a safe instance)
        Material mat = cubeMaterial != null
            ? (instanceMaterial ? new Material(cubeMaterial) : cubeMaterial)
            : null;
        cube.GetComponent<Renderer>().material = mat;

        if (debugSpawnAtOrigin)
        {
            cube.transform.SetParent(transform, false);
            cube.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Debug.Log($"[GeoObjectSpawner] Debug cube spawned at world origin | Original GPS target: {lat}, {lon} | Altitude override: {_altitudeMeters}m");
        }
        else
        {
            // Position at GPS location
            positioningHelper.AddOrUpdateObject(cube, lat, lon, _altitudeMeters, Quaternion.identity);

            Debug.Log($"[GeoObjectSpawner] Cube spawned at GPS: {lat}, {lon} | Altitude: {_altitudeMeters}m | Size: {cubeSize}m | Height: {cubeHeightMeters}m");
        }

        AddBillboardLabel(cube.transform);

        return cube;
    }

    public bool TrySpawnBuildingGeometry(string coordinatesLv95, string name, out GameObject buildingGo, bool clearExisting = false)
    {
        buildingGo = null;

        if (!buildingFactory)
        {
            Debug.LogWarning("[GeoObjectSpawner] Building factory not assigned and none found in scene; falling back to cube.");
            return false;
        }

        var targetCoordinates = !string.IsNullOrWhiteSpace(coordinatesLv95) ? coordinatesLv95 : buildingCoordinatesLv95;
        if (string.IsNullOrWhiteSpace(targetCoordinates))
        {
            return false;
        }

        var buildingNameToUse = string.IsNullOrWhiteSpace(name) ? buildingName : name;
        float altitude = (float)_altitudeMeters;

        var building = buildingFactory.CreateBuildingFromCoordinates(targetCoordinates, buildingNameToUse, altitude, clearExisting);
        if (building == null)
        {
            return false;
        }

        if (debugSpawnAtOrigin)
        {
            building.GameObject.transform.SetParent(transform, false);
            building.GameObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            buildingGo = building.GameObject;
            Debug.Log($"[GeoObjectSpawner] Debug building spawned at world origin | Original GPS target: {building.Latitude}, {building.Longitude}");
            return true;
        }

        if (positioningHelper != null)
        {
            positioningHelper.AddOrUpdateObject(
                building.GameObject,
                building.Latitude,
                building.Longitude,
                building.AltitudeMeters,
                Quaternion.identity);

            buildingGo = building.GameObject;
            return true;
        }

        building.GameObject.transform.SetParent(transform, false);
        building.GameObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        buildingGo = building.GameObject;
        return true;
    }
    public void SetCubeHeightMeters(float h)
    {
        if (useBuildingGeometry)
        {
            Debug.Log("[GeoObjectSpawner] Height slider ignored while building geometry is active.");
            return;
        }

        cubeHeightMeters = Mathf.Max(0.01f, h);
        if (_spawnedObject != null && !_spawnedIsBuilding)
        {
            var s = _spawnedObject.transform.localScale;
            s.y = cubeHeightMeters;
            _spawnedObject.transform.localScale = s;

            Debug.Log("[GeoObjectSpawner] Cube height set to " + cubeHeightMeters + " meters.");

            var bb = _spawnedObject.transform.Find("Billboard");
            if (bb != null) bb.localPosition = new Vector3(0f, cubeHeightMeters + 0.5f, 0f);
        }
    }
}
