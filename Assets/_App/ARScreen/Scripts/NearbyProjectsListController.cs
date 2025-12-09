using System.Collections.Generic;
using System.Collections;
using System.Globalization;
using Shared.Scripts.Building;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Shared.Scripts.Geo;
using Shared.Scripts.App;

public class NearbyProjectsListController : MonoBehaviour
{
    [Header("GeoInfo API")]
    [SerializeField] private GeoInfoWFSAPI wfs;   
    [SerializeField] private GeoInfoWFSMapAPI wfsMapAPI;

    [Header("GeoObject Spawner")]
    [SerializeField] private GeoObjectSpawner geoObjectSpawner;

    [Header("UI")]
    [SerializeField] private Button refreshButton;
    [SerializeField] private InfoPanelController infoPanelController;

    [SerializeField] private TMP_Dropdown distanceDropdown;
    [SerializeField] private Transform listContent;             // ScrollView/Viewport/Content
    [SerializeField] private BuildingListItemView itemPrefab;   // Your row prefab

    [SerializeField] private RawImage mapImage;

    [Header("Status Labels")]
    [SerializeField] private TMP_Text loadingText;
    [SerializeField] private TMP_Text noProjectsText;
    [SerializeField] private RectTransform noPermissionsPanel;

    [Header("Options")]
    [SerializeField] private bool autoFetchOnStart = true;

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

    private Coroutine _spawnWhenReadyRoutine;

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
        if (_spawnWhenReadyRoutine != null)
            StopCoroutine(_spawnWhenReadyRoutine);
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

        if (mapImage)
            mapImage.transform.parent.gameObject.SetActive(false);

        if (geoObjectSpawner)
            geoObjectSpawner.ClearAllBuildings();
        
        // Reset debug display projection status
        GeoDebugDisplay.ProjectionsReady = false;
        GeoDebugDisplay.ProjectionsCreatedCount = 0;

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

        // Also fetch map of the current value of the dropdown

        if (distanceDropdown != null)
        {
            int scale;
            int dropdownValue = int.Parse(distanceDropdown.options[distanceDropdown.value].text.Split(' ')[0]);
            switch (dropdownValue)
            {
                case 250:
                    scale = 1500;
                    break;
                case 500:
                    scale = 3000;
                    break;
                case 750:
                    scale = 4500;
                    break;
                case 1000:
                    scale = 6000;
                    break;
                default:
                    scale = 1000; 
                    break;
            }
            wfsMapAPI?.FetchMap(scale);
        }
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

            view.Bind(enriched[i].B, i + 1, enriched[i].DistanceMeters, OnOpenGeoPortal, OnOpenInformation);
        }

        for (; i < _pool.Count; i++)
            _pool[i].gameObject.SetActive(false);

        bool hasData = _current.Count > 0;
        ShowState(loading:false, hasData:hasData);

        // Convert enriched List<BuildingWithDistance> to List<SelectedTargetContext>
        List<SelectedTargetContext> enrichedContexts = new List<SelectedTargetContext>();
        foreach (var item in enriched)
        {
            var context = new SelectedTargetContext
            {
                Egid = item.B.Egid,
                Name = item.B.GebHauptNutzung,
                RawCoordinates = item.B.Coordinates,
                ElevationMeters = item.B.ElevationMeters,
                Latitude = item.Lat,
                Longitude = item.Lon
            };
            enrichedContexts.Add(context);
        }

        if (!geoObjectSpawner)
            return;

        var contextsCopy = new List<SelectedTargetContext>(enrichedContexts);
        if (geoObjectSpawner.IsReady)
        {
            geoObjectSpawner.CreateARProjections(contextsCopy);
            
            // Update debug display status
            GeoDebugDisplay.ProjectionsCreatedCount = contextsCopy.Count;
            GeoDebugDisplay.ProjectionsCreatedTime = 0f;
            GeoDebugDisplay.ProjectionsReady = true;
        }
        else
        {
            if (_spawnWhenReadyRoutine != null)
                StopCoroutine(_spawnWhenReadyRoutine);
            _spawnWhenReadyRoutine = StartCoroutine(SpawnWhenSpawnerReady(contextsCopy));
        }

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
#else
        // On iOS, permission granting works already and it ask for permissions on demand.

        _locationPermissionGranted = true;
        _cameraPermissionGranted = true;
#endif
    yield break;
    }

    private IEnumerator InitLocation()
    {
        if (!Input.location.isEnabledByUser)
            yield break;

        Input.location.Start(0.1f, 0.5f); // desiredAccuracyInMeters, updateDistanceInMeters

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

    private void OnOpenInformation(ProjectedBuilding b)
    {
        double lat = 0, lon = 0;
        ProjNetTransformCH.LV95ToWGS84(b.EastCentroid, b.NorthCentroid, out lat, out lon);
        
        // Set the SelectedTargetContext for the building
        SelectedTargetContext context = new SelectedTargetContext
        {
            Egid = b.Egid,
            Name = b.GebHauptNutzung,
            RawCoordinates = b.Coordinates,
            ElevationMeters = b.ElevationMeters,
            Latitude = lat,
            Longitude = lon
        };
        CurrentSelectedProjection.Building = context;

        geoObjectSpawner.SelectBuilding(context);

        if (infoPanelController != null)
        {
            infoPanelController.Open();
        }
    }

    private IEnumerator SpawnWhenSpawnerReady(List<SelectedTargetContext> contexts)
    {
        float elapsed = 0f;
        float maxWait = 45f; // max 45 seconds to wait for WPS
        
        while (geoObjectSpawner && !geoObjectSpawner.IsReady && elapsed < maxWait)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (geoObjectSpawner)
        {
            if (geoObjectSpawner.IsReady)
            {
                Debug.Log($"[NearbyProjectsListController] Spawner ready after {elapsed:F1}s, spawning {contexts.Count} buildings");
                geoObjectSpawner.CreateARProjections(contexts);
                
                // Update debug display status
                GeoDebugDisplay.ProjectionsCreatedCount = contexts.Count;
                GeoDebugDisplay.ProjectionsCreatedTime = elapsed;
                GeoDebugDisplay.ProjectionsReady = true;
            }
            else
            {
                Debug.LogWarning($"[NearbyProjectsListController] Spawner not ready after {maxWait}s timeout. WPS available: {geoObjectSpawner.IsWpsReady}. Buildings will NOT spawn to avoid black screen.");
            }
        }

        _spawnWhenReadyRoutine = null;
    }

}