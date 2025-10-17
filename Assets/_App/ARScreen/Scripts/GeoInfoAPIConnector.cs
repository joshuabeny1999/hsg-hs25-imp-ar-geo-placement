using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.Networking;
using Shared.Scripts.Geo;

[Serializable]
public class ProjectedBuilding
{
    public string GebVersNr { get; }
    public string GebHauptNutzung { get; }
    public string Coordinates { get; }

    public ProjectedBuilding(string gebVersNr, string gebHauptNutzung, string coordinates)
    {
        GebVersNr = gebVersNr;
        GebHauptNutzung = gebHauptNutzung;
        Coordinates = coordinates;
    }

    public override string ToString()
    {
        return $"{GebVersNr}; {GebHauptNutzung}; Coordinates={Coordinates}";
    }
}

public class GeoInfoAPIConnector : MonoBehaviour
{
    [Header("Geoportal WFS Settings")]
    [SerializeField] private string serviceEndpoint = "https://www.geoportal.ch/services/wfs";
    [SerializeField] private string typeNames = "geoportal:a111_avdm01_amtlverm_fla";
    [SerializeField] private int maxFeatureCount = 1000000;
    [SerializeField, Tooltip("Width/height of the square bounding box centered around the player in meters.")]
    private float boundingBoxSizeMeters = 300f;
    
    [Header("Debug Settings")]
    [SerializeField, Tooltip("Use manual LV95 coordinates instead of the device GPS (for in-editor testing).")]
    private bool useDebugCoordinates = false;
    [SerializeField, Tooltip("Manual LV95 (EPSG:2056) coordinates used when debug mode is enabled. X = Easting, Y = Northing." )]
    private Vector2 debugLv95Coordinates = new Vector2(2739782.97f, 1250944.04f);
    [SerializeField] private float locationDesiredAccuracyMeters = 5f;
    [SerializeField] private float locationUpdateDistanceMeters = 0.5f;
    [SerializeField] private float locationServiceTimeoutSeconds = 20f;

    [SerializeField] private GeoObjectSpawner buildingspawner;

    private const string StatusFilter = "projektiert";
    private const string SrsName = "urn:ogc:def:crs:EPSG::2056";

    private bool _locationInitialized;

    public event Action<List<ProjectedBuilding>> ProjectedFeaturesFetched;

    private void Start()
    {
        RefreshProjectedFeatures();
    }

    public void RefreshProjectedFeatures()
    {
        StartCoroutine(FetchProjectedFeatures(null));
    }

    public void RefreshProjectedFeatures(Action<List<ProjectedBuilding>> onCompleted)
    {
        StartCoroutine(FetchProjectedFeatures(onCompleted));
    }

    public IEnumerator FetchProjectedFeatures(Action<List<ProjectedBuilding>> onCompleted)
    {
        if (useDebugCoordinates)
        {
            Debug.Log($"GeoInfo API: using debug LV95 coordinates {debugLv95Coordinates.x}, {debugLv95Coordinates.y}");

            ProjNetTransformCH.LV95ToWGS84(debugLv95Coordinates.x, debugLv95Coordinates.y, out double lat, out double lon);
            yield return FetchProjectedFeatures(lat, lon, onCompleted);
            yield break;
        }

        if (!_locationInitialized)
        {
            yield return EnsureLocationReady();

            if (!_locationInitialized)
            {
                Debug.LogWarning("GeoInfo API: location service still not running; aborting fetch.");
                onCompleted?.Invoke(new List<ProjectedBuilding>());
                yield break;
            }
        }

        var lastKnown = Input.location.lastData;
        yield return FetchProjectedFeatures(lastKnown.latitude, lastKnown.longitude, onCompleted);
    }

    public IEnumerator FetchProjectedFeatures(double latitude, double longitude, Action<List<ProjectedBuilding>> onCompleted)
    {
        var requestUrl = BuildServiceUrl(latitude, longitude);

        if (string.IsNullOrWhiteSpace(requestUrl))
        {
            Debug.LogError("GeoInfo API: service URL is not configured.");
            onCompleted?.Invoke(new List<ProjectedBuilding>());
            yield break;
        }

        using (var request = UnityWebRequest.Get(requestUrl))
        {
 
            yield return request.SendWebRequest();
            var hasError = request.result != UnityWebRequest.Result.Success;

            if (hasError)
            {
                Debug.LogError($"GeoInfo API request failed: {request.error}");
                HandleFetchResults(new List<ProjectedBuilding>());
                onCompleted?.Invoke(new List<ProjectedBuilding>());
                yield break;
            }

            var features = ParseProjectedFeatures(request.downloadHandler.text);
            HandleFetchResults(features);
            onCompleted?.Invoke(features);
        }
    }

    private string BuildServiceUrl(double latitude, double longitude)
    {
        if (string.IsNullOrWhiteSpace(serviceEndpoint) || string.IsNullOrWhiteSpace(typeNames))
        {
            return string.Empty;
        }

        ProjNetTransformCH.WGS84ToLV95(latitude, longitude, out double east, out double north);

        double half = Mathf.Max(1f, boundingBoxSizeMeters) * 0.5f;

        var bbox = string.Format(
            CultureInfo.InvariantCulture,
            "{0},{1},{2},{3},{4}",
            east - half,
            north - half,
            east + half,
            north + half,
            SrsName);

        var builder = new StringBuilder();
        builder.Append(serviceEndpoint);

        if (!serviceEndpoint.Contains("?"))
        {
            builder.Append("?");
        }
        else if (!serviceEndpoint.EndsWith("&") && !serviceEndpoint.EndsWith("?"))
        {
            builder.Append("&");
        }

        builder.Append("SERVICE=WFS&REQUEST=GetFeature&VERSION=2.0.0");
        builder.Append("&TYPENAMES=");
        builder.Append(UnityWebRequest.EscapeURL(typeNames));
        builder.Append("&COUNT=");
        builder.Append(maxFeatureCount);
        builder.Append("&SRSNAME=");
        builder.Append(UnityWebRequest.EscapeURL(SrsName));
        builder.Append("&BBOX=");
        builder.Append(UnityWebRequest.EscapeURL(bbox));

        return builder.ToString();
    }

    private IEnumerator EnsureLocationReady()
    {
        if (_locationInitialized || useDebugCoordinates)
        {
            yield break;
        }

        if (!Input.location.isEnabledByUser)
        {
            Debug.LogWarning("GeoInfo API: device location services are disabled.");
            yield break;
        }

        var status = Input.location.status;

        if (status == LocationServiceStatus.Stopped)
        {
            Input.location.Start(locationDesiredAccuracyMeters, locationUpdateDistanceMeters);
            status = Input.location.status;
        }

        float elapsed = 0f;
        while (status == LocationServiceStatus.Initializing && elapsed < locationServiceTimeoutSeconds)
        {
            elapsed += Time.deltaTime;
            yield return null;
            status = Input.location.status;
        }

        if (status == LocationServiceStatus.Running)
        {
            _locationInitialized = true;
        }
        else
        {
            Debug.LogWarning($"GeoInfo API: location service not ready (status={status}).");
        }
    }

    private List<ProjectedBuilding> ParseProjectedFeatures(string xml)
    {
        var results = new List<ProjectedBuilding>();

        if (string.IsNullOrWhiteSpace(xml))
        {
            return results;
        }

        try
        {
            var document = XDocument.Parse(xml);

            var featureElements = document
                .Descendants()
                .Where(element => element.Elements().Any(child =>
                    string.Equals(child.Name.LocalName, "a111_avdm01_projboden_fla_status", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(child.Value?.Trim(), StatusFilter, StringComparison.OrdinalIgnoreCase)));

            foreach (var feature in featureElements)
            {
                var gebVersNr = feature.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, "a111_avdm01_projboden_fla_gebversnr", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? string.Empty;
                var gebHauptNutzung = feature.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, "a111_avdm01_projboden_fla_gebhauptnutzung", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? string.Empty;
                var coordinates = feature.Descendants().FirstOrDefault(e => string.Equals(e.Name.LocalName, "coordinates", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? string.Empty;

                if (string.IsNullOrEmpty(gebVersNr) && string.IsNullOrEmpty(gebHauptNutzung) && string.IsNullOrEmpty(coordinates))
                {
                    continue;
                }

                results.Add(new ProjectedBuilding(gebVersNr, gebHauptNutzung, coordinates));
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"GeoInfo API: failed to parse XML. {ex.Message}");
        }

        return results;
    }

    private void HandleFetchResults(List<ProjectedBuilding> buildings)
    {
        ProjectedFeaturesFetched?.Invoke(buildings);

        if (buildings == null || buildings.Count == 0)
        {
            Debug.Log("GeoInfo API: no projected features found.");
            return;
        }

        Debug.Log($"*****GeoInfo API: fetched {buildings.Count} projected features.*****");
        foreach (var building in buildings)
        {
            Debug.Log(building.ToString());

            if (buildingspawner != null && !string.IsNullOrWhiteSpace(building.Coordinates))
            {
                buildingspawner.TrySpawnBuildingGeometry(building.Coordinates, building.GebVersNr, out _, false);
            }
        }
    }
}
