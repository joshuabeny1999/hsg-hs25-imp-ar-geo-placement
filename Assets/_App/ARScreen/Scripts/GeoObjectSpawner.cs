using UnityEngine;
using UnityEngine.Networking;
using Niantic.Lightship.AR.WorldPositioning;
using System.Collections;

/// <summary>
/// Simple spawner that places a cube at a specific GPS location
/// Fetches real-world altitude from Open-Elevation API
/// </summary>
public class GeoObjectSpawner : MonoBehaviour
{
    [Header("WPS Helper")]
    [SerializeField] private ARWorldPositioningObjectHelper positioningHelper;
    [SerializeField] private ARWorldPositioningManager wpsManager;

    [Header("GPS Location")]
    public double latitude = 47.41041273038499;
    public double longitude = 9.333280815523262;
    
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
        string url = $"https://api.open-elevation.com/api/v1/lookup?locations={latitude},{longitude}";
        
        Debug.Log($"[GeoObjectSpawner] Fetching altitude for {latitude}, {longitude}...");
        
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    // Parse JSON response
                    string json = request.downloadHandler.text;
                    ElevationResponse response = JsonUtility.FromJson<ElevationResponse>(json);
                    
                    if (response != null && response.results != null && response.results.Length > 0)
                    {
                        _altitudeMeters = response.results[0].elevation;
                        Debug.Log($"[GeoObjectSpawner] Altitude fetched: {_altitudeMeters}m");
                    }
                    else
                    {
                        Debug.LogWarning("[GeoObjectSpawner] Failed to parse altitude, using default 0m");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[GeoObjectSpawner] Error parsing altitude: {e.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"[GeoObjectSpawner] Failed to fetch altitude: {request.error}. Using default altitude.");
            }
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
        positioningHelper.AddOrUpdateObject(_cube, latitude, longitude, _altitudeMeters, Quaternion.identity);
        
        Debug.Log($"[GeoObjectSpawner] Cube spawned at GPS: {latitude}, {longitude} | Altitude: {_altitudeMeters}m (API) | Size: {cubeSize}m | Height: {cubeHeightMeters}m");
        
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
public class ElevationResponse
{
    public ElevationResult[] results;
}

[System.Serializable]
public class ElevationResult
{
    public double latitude;
    public double longitude;
    public double elevation;
}
