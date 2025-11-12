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

    [SerializeField] private TMP_Dropdown distanceDropdown;
    [SerializeField] private Transform listContent;             // ScrollView/Viewport/Content
    [SerializeField] private BuildingListItemView itemPrefab;   // Your row prefab

    [Header("Status Labels")]
    [SerializeField] private TMP_Text loadingText;
    [SerializeField] private TMP_Text noProjectsText;
    [SerializeField] private RectTransform noPermissionsPanel;

    [Header("Options")]
    [SerializeField] private bool autoFetchOnStart = true;
    [SerializeField] private string arSceneName = "ARScreen";    // Change to your AR scene name

    private readonly List<BuildingListItemView> _pool = new();
    private List<ProjectedBuilding> _current = new();
    private bool _isLoading = false;

    private bool _locationReady = false;
    private double _userLat = double.NaN;
    private double _userLon = double.NaN;
    private bool _locationPermissionGranted = true;
    private bool _cameraPermissionGranted = true;
    
#if UNITY_ANDROID && !UNITY_EDITOR
    private bool _permissionRequestCompleted = false;
#endif

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
        if (distanceDropdown) distanceDropdown.onValueChanged.AddListener(OnDistanceDropDownClicked);
        if (wfs) wfs.ProjectedFeaturesFetched += OnFeaturesFetched;
        ShowState(loading: false, hasData: false);
        if (noPermissionsPanel) noPermissionsPanel.gameObject.SetActive(false);
    }

    void Start()
    {
        StartCoroutine(EnsurePermissionsAndInit());
    }

    void OnDestroy()
    {
        if (wfs) wfs.ProjectedFeaturesFetched -= OnFeaturesFetched;
        if (refreshButton) refreshButton.onClick.RemoveListener(OnRefreshClicked);
        if (distanceDropdown) distanceDropdown.onValueChanged.RemoveListener(OnDistanceDropDownClicked);
    }

    void OnDistanceDropDownClicked(int index)
    {
        if(wfs == null || distanceDropdown == null) return;
        wfs.boundingBoxSizeMeters = float.Parse(distanceDropdown.options[index].text.Split(' ')[0]);
        OnRefreshClicked();
    }

    void OnRefreshClicked()
    {
        if (_isLoading) return;
        _isLoading = true;

        if (refreshButton) refreshButton.interactable = false;
        if (distanceDropdown) distanceDropdown.interactable = false;


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
        if (distanceDropdown) distanceDropdown.interactable = true;

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

            view.Bind(enriched[i].B, i + 1, enriched[i].DistanceMeters, OnOpenGeoPortal, OnOpenAR);
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

    private IEnumerator EnsurePermissionsAndInit()
    {
        _locationPermissionGranted = false;
        _cameraPermissionGranted = false;

        yield return EnsureRuntimePermissions();

        if (!_locationPermissionGranted || !_cameraPermissionGranted)
        {
            OnPermissionsDenied();
            yield break;
        }

        if (noPermissionsPanel) noPermissionsPanel.gameObject.SetActive(false);

        yield return InitLocation();

        if (autoFetchOnStart) OnRefreshClicked();
    }

    private IEnumerator EnsureRuntimePermissions()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        List<string> missing = new();
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation))
            missing.Add(UnityEngine.Android.Permission.FineLocation);
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.CoarseLocation))
            missing.Add(UnityEngine.Android.Permission.CoarseLocation);
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            missing.Add(UnityEngine.Android.Permission.Camera);

        if (missing.Count > 0)
        {
            _permissionRequestCompleted = false;
            int responsesPending = missing.Count;

            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += permission =>
            {
                if (permission == UnityEngine.Android.Permission.Camera)
                    _cameraPermissionGranted = true;
                responsesPending--;
                if (responsesPending <= 0) _permissionRequestCompleted = true;
            };
            callbacks.PermissionDenied += permission =>
            {
                if (permission == UnityEngine.Android.Permission.Camera)
                    _cameraPermissionGranted = false;
                responsesPending--;
                if (responsesPending <= 0) _permissionRequestCompleted = true;
            };
            callbacks.PermissionDeniedAndDontAskAgain += permission =>
            {
                if (permission == UnityEngine.Android.Permission.Camera)
                    _cameraPermissionGranted = false;
                responsesPending--;
                if (responsesPending <= 0) _permissionRequestCompleted = true;
            };

            UnityEngine.Android.Permission.RequestUserPermissions(missing.ToArray(), callbacks);

            while (!_permissionRequestCompleted)
            {
                yield return null;
            }
        }

        _locationPermissionGranted = UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation) ||
                                     UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.CoarseLocation);
        _cameraPermissionGranted = UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera);
#elif UNITY_IOS && !UNITY_EDITOR
        if (!Application.HasUserAuthorization(UserAuthorization.Location))
            yield return Application.RequestUserAuthorization(UserAuthorization.Location);

        _locationPermissionGranted = Application.HasUserAuthorization(UserAuthorization.Location);

        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

        _cameraPermissionGranted = Application.HasUserAuthorization(UserAuthorization.WebCam);
#else
        _locationPermissionGranted = true;
        _cameraPermissionGranted = true;
#endif
    yield break;
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

    private void OnPermissionsDenied()
    {
        if (noPermissionsPanel) noPermissionsPanel.gameObject.SetActive(true);
    }

    // --- Button actions ---

    private void OnOpenGeoPortal(ProjectedBuilding b)
    {
        var url = $"https://www.geoportal.ch/ch/map/40?topic=coord&y={b.EastCentroid}&x={b.NorthCentroid}&scale=500&rotation=0&popup=1";
        Debug.Log("Opening GeoPortal URL: " + url);
        Application.OpenURL(url);
    }

    private void OnOpenAR(ProjectedBuilding b)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
        {
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
            Debug.Log("Camera permission requested; please retry once granted.");
            return;
        }
#elif UNITY_IOS && !UNITY_EDITOR
        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            StartCoroutine(RequestIosCameraAndOpenAR(b));
            return;
        }
#endif
        LaunchArScene(b);
    }

    private void LaunchArScene(ProjectedBuilding b)
    {
        double lat = 0, lon = 0;
        ProjNetTransformCH.LV95ToWGS84(b.EastCentroid, b.NorthCentroid, out lat, out lon);

        SelectedTargetContext.Egid = b.Egid;
        SelectedTargetContext.Name = b.GebHauptNutzung;
        SelectedTargetContext.RawCoordinates = b.Coordinates;
        SelectedTargetContext.ElevationMeters = b.ElevationMeters;
        SelectedTargetContext.Latitude = lat; SelectedTargetContext.Longitude = lon;

        Debug.Log("Opening AR scene for building EGID: " + b.Egid);
        SceneManager.LoadScene(arSceneName);
    }

#if UNITY_IOS && !UNITY_EDITOR
    private IEnumerator RequestIosCameraAndOpenAR(ProjectedBuilding building)
    {
        yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            Debug.LogWarning("Camera access denied by user.");
            yield break;
        }

        LaunchArScene(building);
    }
#endif
}