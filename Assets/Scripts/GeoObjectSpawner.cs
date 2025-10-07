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

    [Header("GPS Location")]
    public double latitude = 47.41041273038499;
    public double longitude = 9.333280815523262;
    
    [Header("Cube Settings")]
    [Tooltip("Size of the cube in meters (larger = more visible from distance)")]
    public float cubeSize = 5.0f;

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
    }

    private void Start()
    {
        StartCoroutine(FetchAltitudeAndSpawn());
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

    private void SpawnCube()
    {
        // Create cube
        _cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _cube.name = "GeoCube";
        _cube.transform.localScale = Vector3.one * cubeSize;
        
        // Create bright, emissive material for better visibility
        Material brightMat = new Material(Shader.Find("Standard"));
        brightMat.color = Color.yellow;
        brightMat.EnableKeyword("_EMISSION");
        brightMat.SetColor("_EmissionColor", Color.yellow * 0.5f);
        _cube.GetComponent<Renderer>().material = brightMat;

        // Position at GPS location
        positioningHelper.AddOrUpdateObject(_cube, latitude, longitude, _altitudeMeters, Quaternion.identity);

        Debug.Log($"[GeoObjectSpawner] Cube spawned at GPS: {latitude}, {longitude} | Altitude: {_altitudeMeters}m (API) | Size: {cubeSize}m");
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
