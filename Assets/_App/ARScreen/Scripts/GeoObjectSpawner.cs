using UnityEngine;
using UnityEngine.Networking;
using Niantic.Lightship.AR.WorldPositioning;
using System.Collections;
using Shared.Scripts.Geo; 

/// <summary>
/// Simple spawner that places a cube at a specific GPS location
/// Fetches real-world altitude from Open-Elevation API
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
    

    private GameObject _cube;
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
    }

    private void Start()
    {
        StartCoroutine(WaitForWpsThenFetchAltitude());
    }
    
    // <summary>
    /// Waits for WPS to become available (if applicable) before fetching altitude
    ///  </summary>
    private IEnumerator WaitForWpsThenFetchAltitude()
    {
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

        // proceed as before
        yield return StartCoroutine(FetchAltitudeAndSpawn());
    }

    /// <summary>
    /// Fetches real-world altitude from Open-Elevation API
    /// API: https://open-elevation.com/
    /// </summary>
    private IEnumerator FetchAltitudeAndSpawn()
    {
        string url = $"https://www.geoportal.ch/api/elevation/point?lang=de&east={east:F2}&north={north:F2}";
        using var req = UnityWebRequest.Get(url);
        req.timeout = 10;
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            var json = req.downloadHandler.text;
            var resp = JsonUtility.FromJson<SwissElevationResponse>(json);
            if (resp != null)
            {
                _altitudeMeters = resp.elevation;
            }
        }
        else
        {
            Debug.LogWarning($"Altitude fetch failed: {req.error}");
        }

        SpawnCube();
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

    private void SpawnCube()
    {
        
        // Convert LV95 -> WGS84 for Lightship
        ProjNetTransformCH.LV95ToWGS84(east, north, out double lat, out double lon);
        Debug.Log($"[GeoObjectSpawner] Converted LV95 to WGS84: {east}, {north} -> {lat}, {lon}");

        // Create cube
        
        _cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _cube.name = "GeoCube";
        _cube.transform.localScale = new Vector3(cubeSize, cubeHeightMeters, cubeSize);
        
        // Use linked material (with a safe instance)
        Material mat = cubeMaterial != null
            ? (instanceMaterial ? new Material(cubeMaterial) : cubeMaterial)
            : null;
        _cube.GetComponent<Renderer>().material = mat;

        // Position at GPS location
        positioningHelper.AddOrUpdateObject(_cube, lat, lon, _altitudeMeters, Quaternion.identity);
        
        Debug.Log($"[GeoObjectSpawner] Cube spawned at GPS: {lat}, {lon} | Altitude: {_altitudeMeters}m (API) | Size: {cubeSize}m | Height: {cubeHeightMeters}m");
        
        AddBillboardLabel(_cube.transform);
    }
    
    public void SetCubeHeightMeters(float h)
    {
        cubeHeightMeters = Mathf.Max(0.01f, h);
        if (_cube != null)
        {
            var s = _cube.transform.localScale;
            s.y = cubeHeightMeters;
            _cube.transform.localScale = s;
            
            Debug.Log("[GeoObjectSpawner] Cube height set to " + cubeHeightMeters + " meters.");

            // keep the billboard above the cube top if present
            var bb = _cube.transform.Find("Billboard");
            if (bb != null) bb.localPosition = new Vector3(0f, cubeHeightMeters + 0.5f, 0f);
        }
    }
}

// JSON response classes for Open-Elevation API
[System.Serializable]
public class SwissElevationResponse
{
    public double east;
    public double north;
    public double elevation; // terrain height (AMSL)
    public double surface;   // building top height (AMSL)
    public double elevationDifference;
}
