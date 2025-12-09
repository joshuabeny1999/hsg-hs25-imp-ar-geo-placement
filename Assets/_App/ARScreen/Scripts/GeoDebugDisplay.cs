using UnityEngine;
using TMPro;
using System;
using Niantic.Lightship.AR.WorldPositioning;
using Shared.Scripts.App;
using Shared.Scripts.Building;

/// <summary>
/// Field HUD for geo placement debugging.
/// Shows: device GPS, distance, WPS status, heading & bearing to target.
/// Attach to a TMP Text (TextMeshProUGUI) object.
/// </summary>
public class GeoDebugDisplay : MonoBehaviour
{
    [Header("References")] [Tooltip("Optional: WPS helper/manager for status readout")]
    public ARWorldPositioningObjectHelper wpsHelper; // optional

    public ARWorldPositioningManager wpsManager; // optional

    [Header("UI")] public bool showDebugDisplay = true;

    // Static projection status tracking
    public static int ProjectionsCreatedCount { get; set; } = 0;
    public static float ProjectionsCreatedTime { get; set; } = 0f;
    public static bool ProjectionsReady { get; set; } = false;
    
    // Last positioned building info (for debugging)
    public static double LastPositionedBuildingLat { get; set; } = 0;
    public static double LastPositionedBuildingLon { get; set; } = 0;
    public static float LastPositionedBuildingAlt { get; set; } = 0;

    [Tooltip("Update interval in seconds")]
    public float updateInterval = 0.5f;

    [Header("Proximity Bands (m)")] public float veryCloseM = 5f;
    public float nearM = 20f;
    public float visibleM = 100f;

    [Header("Heading / Bearing")] [Tooltip("Show device heading and bearing to target (needs compass)")]
    public bool showHeading = true;

    [Tooltip("If within this angular error, show 'On target'")]
    public float onTargetDegrees = 8f;

    private TextMeshProUGUI _text;
    private float _timer;
    private bool _gpsStarted;

    // cache last device lat/lon for simple speed/bearing deltas if ever needed
    private double _lastLat, _lastLon;
    private bool _hasLast;

    private void Start()
    {
        _text = GetComponent<TextMeshProUGUI>();
        if (_text == null)
        {
            Debug.LogError("[GeoDebugDisplay] Add to a TMP Text object.");
            enabled = false;
            return;
        }

        if (!wpsHelper) wpsHelper = FindFirstObjectByType<ARWorldPositioningObjectHelper>();
        if (!wpsManager) wpsManager = FindFirstObjectByType<ARWorldPositioningManager>();

        _text.gameObject.SetActive(showDebugDisplay);
        if (!showDebugDisplay) return;

        // GPS & Compass
        if (Input.location.isEnabledByUser)
        {
            Input.location.Start(0.1f, 0.5f);
            _gpsStarted = true;
        }

        if (showHeading) Input.compass.enabled = true;

        _text.text = "Starting GPS…";
    }

    private void Update()
    {
        if (!showDebugDisplay) return;
        _timer += Time.deltaTime;
        if (_timer < updateInterval) return;
        _timer = 0f;

        RenderPanel();
    }

    private void RenderPanel()
    {
        if (_gpsStarted == false)
        {
            _text.text = "GPS disabled by user";
            return;
        }

        var status = Input.location.status;
        if (status == LocationServiceStatus.Initializing)
        {
            _text.text = "GPS: Initializing…";
            return;
        }

        if (status == LocationServiceStatus.Failed)
        {
            _text.text = "GPS: FAILED";
            return;
        }

        if (status == LocationServiceStatus.Stopped)
        {
            _text.text = "GPS: STOPPED";
            return;
        }

        var loc = Input.location.lastData;
        double dLat = loc.latitude, dLon = loc.longitude;
        float dAlt = loc.altitude;
        float hAcc = loc.horizontalAccuracy;

        string wpsInfo = WpsStatusLine();

        // decide target: only use the selected context (no cube fallback)
        double targetLat = 0, targetLon = 0;
        bool hasSelected = (CurrentSelectedProjection.Building.Latitude != 0 || CurrentSelectedProjection.Building.Longitude != 0)
                           || !string.IsNullOrEmpty(CurrentSelectedProjection.Building.Egid)
                           || !string.IsNullOrEmpty(CurrentSelectedProjection.Building.RawCoordinates);

        if (hasSelected)
        {
            targetLat = CurrentSelectedProjection.Building.Latitude;
            targetLon = CurrentSelectedProjection.Building.Longitude;
        }

// Distance & proximity color bands
        float distanceM = float.NaN;
        string proximityInfo = "";
        if (hasSelected && (targetLat != 0 || targetLon != 0))
        {
            distanceM = BuildingGeometryUtils.HaversineMeters(dLat, dLon, targetLat, targetLon);
            proximityInfo = ProximityLine(distanceM);
        }

// Heading & bearing (uses the resolved target)
        string bearingInfo = "";
        if (showHeading && hasSelected && (targetLat != 0 || targetLon != 0))
        {
            float deviceHeading = Input.compass.enabled ? Input.compass.trueHeading : float.NaN; // 0..360°
            float bearingToTarget = (float)BuildingGeometryUtils.BearingDegrees(dLat, dLon, targetLat, targetLon);
            float turn = ShortestSignedAngle(deviceHeading, bearingToTarget); // left(-)/right(+)

            string arrow = Mathf.Abs(turn) <= onTargetDegrees
                ? "<color=purple>● On target</color>"
                : (turn > 0
                    ? $"→ turn <b>{Mathf.Abs(turn):F0}°</b> right"
                    : $"← turn <b>{Mathf.Abs(turn):F0}°</b> left");

            bearingInfo =
                $"\n<b>HEADING</b>\n" +
                $"Device: {deviceHeading:F0}°  |  Bearing→Target: {bearingToTarget:F0}°\n" +
                $"{arrow}";
        }

// selected building block (if any)
    string selectedInfo = SelectedInfoBlock(hasSelected, targetLat, targetLon);
    string selectedContextInfo = SelectedContextRawBlock();
    string projectionsInfo = ProjectionsStatusLine();

// Build UI
        _text.text =
            $"<b>DEVICE GPS</b>\n" +
            $"Lat: {dLat:F8}\n" +
            $"Lon: {dLon:F8}\n" +
            $"Alt: {dAlt:F1} m\n" +
            $"Accuracy: ±{hAcc:F1} m\n" +
            proximityInfo +
            (string.IsNullOrEmpty(bearingInfo) ? "" : "\n" + bearingInfo) +
            (string.IsNullOrEmpty(wpsInfo) ? "" : "\n\n" + wpsInfo) +
            projectionsInfo +
            selectedInfo +
            selectedContextInfo;
    }

    private string SelectedInfoBlock(bool hasSelected, double targetLat, double targetLon)
    {
        if (hasSelected)
        {
            // trim raw coordinates just so HUD stays readable
            string raw = CurrentSelectedProjection.Building.RawCoordinates;
            if (!string.IsNullOrEmpty(raw) && raw.Length > 80) raw = raw.Substring(0, 80) + "…";

            return
                $"\n\n<b>SELECTED BUILDING</b>\n" +
                $"EGID: {CurrentSelectedProjection.Building.Egid}\n" +
                $"Name: {CurrentSelectedProjection.Building.Name}\n" +
                $"Lat: {targetLat:F8}\n" +
                $"Lon: {targetLon:F8}\n" +
                (string.IsNullOrEmpty(raw) ? "" : $"Coords: {raw}");
        }

        return "";
    }

    private string WpsStatusLine()
    {
        if (wpsManager == null && wpsHelper == null) return "";
        string mgr = (wpsManager != null)
            ? (wpsManager.IsAvailable ? "<color=purple>Available</color>" : "<color=orange>Not ready</color>")
            : "n/a";
        // If the helper exposes an altitude mode or similar, you could append it here (kept generic for version safety).
        return $"<b>WPS</b>  Status: {mgr}";
    }

    private string ProjectionsStatusLine()
    {
        if (!ProjectionsReady)
            return "\n\n<b>AR PROJECTIONS</b>\n<color=orange>Not created yet</color>";
        
        return $"\n\n<b>AR PROJECTIONS</b>\n" +
               $"<color=green>✓ Created</color>: {ProjectionsCreatedCount} buildings";
    }

    // Always reflect exactly what's inside SelectedTargetContext (no parsing, no trimming)
    private string SelectedContextRawBlock()
    {
        string egid = CurrentSelectedProjection.Building.Egid;
        string name = CurrentSelectedProjection.Building.Name;
        string raw = CurrentSelectedProjection.Building.RawCoordinates;
        double latRaw = CurrentSelectedProjection.Building.Latitude;
        double lonRaw = CurrentSelectedProjection.Building.Longitude;

        bool any = !string.IsNullOrEmpty(egid)
                   || !string.IsNullOrEmpty(name)
                   || !string.IsNullOrEmpty(raw)
                   || latRaw != 0 || lonRaw != 0;
        if (!any) return "";

        return
            $"\n\n<b>SELECTED CONTEXT (raw)</b>\n" +
            $"EGID: {egid}\n" +
            $"Name: {name}\n" +
            $"Lat (raw): {latRaw:F8}\n" +
            $"Lon (raw): {lonRaw:F8}\n" +
            (string.IsNullOrEmpty(raw) ? "" : $"Coords (raw): {raw}");
    }

    private string ProximityLine(float d)
    {
        if (float.IsNaN(d)) return "";
        string band =
            d < veryCloseM ? "<color=purple>★ VERY CLOSE ★</color>" :
            d < nearM ? "<color=green>Near</color>" :
            d < visibleM ? "<color=orange>Getting close…</color>" :
            "<color=red>Far</color>";

        return $"\n<b>Distance→Target</b>: {d:F1} m  {band}";
    }

    private static float ShortestSignedAngle(float fromDeg, float toDeg)
    {
        if (float.IsNaN(fromDeg) || float.IsNaN(toDeg)) return float.NaN;
        float delta = Mathf.Repeat((toDeg - fromDeg) + 540f, 360f) - 180f;
        return delta; // negative = turn left, positive = turn right
    }

    private void OnDestroy()
    {
        if (_gpsStarted) Input.location.Stop();
        if (showHeading) Input.compass.enabled = false;
    }
}