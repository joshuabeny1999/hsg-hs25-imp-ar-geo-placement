using System.Collections.Generic;
using System.Collections;
using System.Globalization;
using Shared.Scripts.Building;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;                       // ← add
using Shared.Scripts.Geo;
using Shared.Scripts.App;

public class NearbyProjectsListController : MonoBehaviour
{
    [Header("GeoInfo API")]
    [SerializeField] private GeoInfoWFSAPI wfs;   // Drag your GeoInfoWFSAPI here

    [Header("UI")]
    [SerializeField] private Button refreshButton;
    [SerializeField] private Transform listContent;             // ScrollView/Viewport/Content
    [SerializeField] private BuildingListItemView itemPrefab;   // Your row prefab

    [Header("Status Labels")]
    [SerializeField] private TMP_Text loadingText;            
    [SerializeField] private TMP_Text noProjectsText;         

    [Header("Options")]
    [SerializeField] private bool autoFetchOnStart = true;
    [SerializeField] private string arSceneName = "ARScreen";    // Change to your AR scene name

    private readonly List<BuildingListItemView> _pool = new();
    private List<ProjectedBuilding> _current = new();
    private bool _isLoading = false;

    private bool _locationReady = false;
    private double _userLat = double.NaN;
    private double _userLon = double.NaN;

    private struct BuildingWithDistance
    {
        public ProjectedBuilding B;
        public double DistanceMeters;
        public double Lat;
        public double Lon;
    }

    void Awake()
    {
        if (refreshButton) refreshButton.onClick.AddListener(OnRefreshClicked);
        if (wfs) wfs.ProjectedFeaturesFetched += OnFeaturesFetched;
        ShowState(loading: false, hasData: false);
    }

    void Start()
    {
        StartCoroutine(InitLocation());
        if (autoFetchOnStart) OnRefreshClicked();
    }

    void OnDestroy()
    {
        if (wfs) wfs.ProjectedFeaturesFetched -= OnFeaturesFetched;
        if (refreshButton) refreshButton.onClick.RemoveListener(OnRefreshClicked);
    }

    void OnRefreshClicked()
    {
        if (_isLoading) return;
        _isLoading = true;
        
        if (refreshButton) refreshButton.interactable = false;

        if (Input.location.status == LocationServiceStatus.Running)
        {
            var data = Input.location.lastData;
            _userLat = data.latitude;
            _userLon = data.longitude;
            _locationReady = true;
        }
        else if (Input.location.isEnabledByUser && Input.location.status == LocationServiceStatus.Stopped)
        {
            Input.location.Start(1f, 1f);
        }

        // Show "loading", hide "no projects"
        ShowState(loading:true, hasData:false);

        // Hide existing items while loading (optional)
        for (int i = 0; i < _pool.Count; i++) _pool[i].gameObject.SetActive(false);

        wfs?.RefreshProjectedFeatures();
    }

    void OnFeaturesFetched(List<ProjectedBuilding> list)
    {
        _isLoading = false;
        if (refreshButton) refreshButton.interactable = true;

        _current = list ?? new List<ProjectedBuilding>();

        // Build list with lat/lon + distance
        List<BuildingWithDistance> enriched = new();
        for (int k = 0; k < _current.Count; k++)
        {
            var b = _current[k];

            // centroid -> WGS84
            double lat, lon;
            ProjNetTransformCH.LV95ToWGS84(b.EastCentroid, b.NorthCentroid, out lat, out lon);

            double dist = double.PositiveInfinity;
            if (_locationReady && !double.IsNaN(_userLat) && !double.IsNaN(_userLon))
            {
                dist = BuildingGeometryUtils.HaversineMeters(_userLat, _userLon, lat, lon);
            }

            enriched.Add(new BuildingWithDistance
            {
                B = b,
                DistanceMeters = dist,
                Lat = lat,
                Lon = lon
            });
        }

        // Sort: nearest first; items with unknown distance go to bottom
        enriched.Sort((a, b) => a.DistanceMeters.CompareTo(b.DistanceMeters));

        int i = 0;
        for (; i < enriched.Count; i++)
        {
            BuildingListItemView view;
            if (i < _pool.Count)
            {
                view = _pool[i];
                view.gameObject.SetActive(true);
            }
            else
            {
                view = Instantiate(itemPrefab, listContent);
                _pool.Add(view);
            }

            view.Bind(enriched[i].B, i + 1, enriched[i].DistanceMeters, OnOpenGeoPortal, OnOpenMaps, OnOpenAR);
        }

        for (; i < _pool.Count; i++)
            _pool[i].gameObject.SetActive(false);

        bool hasData = _current.Count > 0;
        ShowState(loading:false, hasData:hasData);
    }

    private void ShowState(bool loading, bool hasData)
    {
        if (loadingText)    loadingText.gameObject.SetActive(loading);
        if (noProjectsText) noProjectsText.gameObject.SetActive(!loading && !hasData);

        // If you want the ScrollView hidden when empty:
        if (listContent) listContent.transform.parent.gameObject.SetActive(hasData);
    }

    private IEnumerator InitLocation()
    {
        if (!Input.location.isEnabledByUser)
            yield break;

        Input.location.Start(1f, 1f); // desiredAccuracyInMeters, updateDistanceInMeters

        // wait up to ~5s for service to initialize
        const float timeout = 5f;
        float t = 0f;
        while (Input.location.status == LocationServiceStatus.Initializing && t < timeout)
        {
            t += Time.deltaTime;
            yield return null;
        }

        if (Input.location.status == LocationServiceStatus.Running)
        {
            var data = Input.location.lastData;
            _userLat = data.latitude;
            _userLon = data.longitude;
            _locationReady = true;
        }
    }

    // --- Button actions ---

    private void OnOpenMaps(ProjectedBuilding b)
    {
        Debug.Log("Opening Map link for building EGID: " + b.Egid);

        double lat, lon;
        if (!BuildingGeometryUtils.TryCentroidWGS84(b.Coordinates, out lat, out lon))
        {
            Debug.LogWarning("Could not parse coordinates for maps link.");
            return;
        }
#if UNITY_IOS
        var url = $"http://maps.apple.com/?ll={lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}&q=Building";
#elif UNITY_ANDROID
        var url = $"geo:{lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}?q={lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}(Building)";
#else
        var url = $"https://www.google.com/maps?q={lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
#endif
        Debug.Log("Opening URL: " + url);
        Application.OpenURL(url);
    }

    private void OnOpenGeoPortal(ProjectedBuilding b)
    {

        var url = $"https://www.geoportal.ch/iggis/map/40?y={b.EastCentroid}&x={b.NorthCentroid}&scale=500&rotation=0";
        Debug.Log("Opening GeoPortal URL: " + url);
        Application.OpenURL(url);
    }

    private void OnOpenAR(ProjectedBuilding b)
    {
        double lat = 0, lon = 0;
        ProjNetTransformCH.LV95ToWGS84(b.EastCentroid, b.NorthCentroid, out lat, out lon);

        SelectedTargetContext.Egid = b.Egid;
        SelectedTargetContext.Name = b.GebHauptNutzung;
        SelectedTargetContext.RawCoordinates = b.Coordinates;
        SelectedTargetContext.Latitude = lat; SelectedTargetContext.Longitude = lon;

        Debug.Log("Opening AR scene for building EGID: " + b.Egid);
        SceneManager.LoadScene(arSceneName);
    }
}