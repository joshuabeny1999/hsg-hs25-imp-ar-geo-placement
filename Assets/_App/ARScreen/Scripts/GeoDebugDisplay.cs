using UnityEngine;
using TMPro;
using System;
using Niantic.Lightship.AR.WorldPositioning;
using Shared.Scripts.Geo;

/// <summary>
/// Field HUD for geo placement debugging.
/// Shows: device GPS, distance, WPS status, heading & bearing to target, vertical delta.
/// Attach to a TMP Text (TextMeshProUGUI) object.
/// </summary>
public class GeoDebugDisplay : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The GeoObjectSpawner to compare against")]
    public GeoObjectSpawner geoSpawner;

    [Tooltip("Optional: WPS helper/manager for status readout")]
    public ARWorldPositioningObjectHelper wpsHelper;         // optional
    public ARWorldPositioningManager wpsManager;             // optional

    [Header("UI")]
    public bool showDebugDisplay = true;
    [Tooltip("Update interval in seconds")]
    public float updateInterval = 0.5f;

    [Header("Proximity Bands (m)")]
    public float veryCloseM = 5f;
    public float nearM = 20f;
    public float visibleM = 100f;

    [Header("Heading / Bearing")]
    [Tooltip("Show device heading and bearing to target (needs compass)")]
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
            enabled = false; return;
        }

        if (!wpsHelper)  wpsHelper  = FindFirstObjectByType<ARWorldPositioningObjectHelper>();
        if (!wpsManager) wpsManager = FindFirstObjectByType<ARWorldPositioningManager>();
        if (!geoSpawner) geoSpawner = FindFirstObjectByType<GeoObjectSpawner>();

        _text.gameObject.SetActive(showDebugDisplay);
        if (!showDebugDisplay) return;

        // GPS & Compass
        if (Input.location.isEnabledByUser)
        {
            Input.location.Start(1f, 0.5f);
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
        if (status == LocationServiceStatus.Initializing) { _text.text = "GPS: Initializing…"; return; }
        if (status == LocationServiceStatus.Failed)        { _text.text = "GPS: FAILED"; return; }
        if (status == LocationServiceStatus.Stopped)       { _text.text = "GPS: STOPPED"; return; }

        var loc = Input.location.lastData;
        double dLat = loc.latitude, dLon = loc.longitude;
        float dAlt = loc.altitude;
        float hAcc = loc.horizontalAccuracy;

        string proximityInfo = "";
        string bearingInfo   = "";
        string wpsInfo       = WpsStatusLine();

        double targetEast = geoSpawner.east;
        double targetNorth = geoSpawner.north;
        ProjNetTransformCH.LV95ToWGS84(targetEast, targetNorth, out var targetLat, out var targetLon);

        // Distance & proximity color bands
        float distanceM = float.NaN;
        if (geoSpawner != null)
        {
            
            distanceM = HaversineMeters(dLat, dLon, targetLat, targetLon);
            proximityInfo = ProximityLine(distanceM);
        }

        // Heading & bearing
        if (showHeading && geoSpawner != null)
        {
            float deviceHeading = Input.compass.enabled ? Input.compass.trueHeading : float.NaN; // 0..360°
            float bearingToTarget = (float)BearingDegrees(dLat, dLon, targetLat, targetLon);
            float turn = ShortestSignedAngle(deviceHeading, bearingToTarget); // left(-)/right(+)

            string arrow = Mathf.Abs(turn) <= onTargetDegrees ? "<color=purple>● On target</color>" :
                           (turn > 0 ? $"→ turn <b>{Mathf.Abs(turn):F0}°</b> right" :
                                       $"← turn <b>{Mathf.Abs(turn):F0}°</b> left");

            bearingInfo =
                $"\n<b>HEADING</b>\n" +
                $"Device: {deviceHeading:F0}°  |  Bearing→Target: {bearingToTarget:F0}°\n" +
                $"{arrow}";
        }

        // Vertical difference vs. cube altitude (MSL)
        string verticalInfo = "";
        if (geoSpawner != null)
        {
            float dz = dAlt - (float)geoSpawner.AltitudeMeters;
            verticalInfo = $"\n<b>VERTICAL Δ</b>  device–cube: {dz:+0.0;-0.0;0.0} m";
        }

        // Build UI
        _text.text =
            $"<b>DEVICE GPS</b>\n" +
            $"Lat: {dLat:F8}\n" +
            $"Lon: {dLon:F8}\n" +
            $"Alt: {dAlt:F1} m\n" +
            $"Accuracy: ±{hAcc:F1} m\n" +
            proximityInfo +
            verticalInfo +
            (string.IsNullOrEmpty(bearingInfo) ? "" : "\n" + bearingInfo) +
            (string.IsNullOrEmpty(wpsInfo)     ? "" : "\n\n" + wpsInfo) +
            CubeInfoBlock();
    }

    private string CubeInfoBlock()
    {
        double targetEast = geoSpawner.east;
        double targetNorth = geoSpawner.north;
        ProjNetTransformCH.LV95ToWGS84(targetEast, targetNorth, out var targetLat, out var targetLon);

        if (geoSpawner == null) return "";
        return
            $"\n\n<b>CUBE (Target)</b>\n" +
            $"East: {targetEast:F8}\n" +
            $"North: {targetNorth:F8}\n" +
            $"Lat: {targetLat:F8}\n" +
            $"Lon: {targetLon:F8}\n" +
            $"Alt (API): {geoSpawner.AltitudeMeters:F2} m";
    }

    private string WpsStatusLine()
    {
        if (wpsManager == null && wpsHelper == null) return "";
        string mgr = (wpsManager != null) ? (wpsManager.IsAvailable ? "<color=purple>Available</color>" : "<color=orange>Not ready</color>") : "n/a";
        // If the helper exposes an altitude mode or similar, you could append it here (kept generic for version safety).
        return $"<b>WPS</b>  Status: {mgr}";
    }

    private string ProximityLine(float d)
    {
        if (float.IsNaN(d)) return "";
        string band =
            d < veryCloseM ? "<color=purple>★ VERY CLOSE ★</color>" :
            d < nearM      ? "<color=green>Near</color>" :
            d < visibleM   ? "<color=orange>Getting close…</color>" :
                             "<color=red>Far</color>";

        return $"\n<b>Distance→Target</b>: {d:F1} m  {band}";
    }

    private static float ShortestSignedAngle(float fromDeg, float toDeg)
    {
        if (float.IsNaN(fromDeg) || float.IsNaN(toDeg)) return float.NaN;
        float delta = Mathf.Repeat((toDeg - fromDeg) + 540f, 360f) - 180f;
        return delta; // negative = turn left, positive = turn right
    }

    /// Haversine (meters)
    private static float HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000.0;
        double dLat = Deg2Rad(lat2 - lat1);
        double dLon = Deg2Rad(lon2 - lon1);
        double a = Math.Sin(dLat/2)*Math.Sin(dLat/2) +
                   Math.Cos(Deg2Rad(lat1)) * Math.Cos(Deg2Rad(lat2)) *
                   Math.Sin(dLon/2)*Math.Sin(dLon/2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return (float)(R * c);
    }
    private static double Deg2Rad(double d) => d * Math.PI / 180.0;

    private void OnDestroy()
    {
        if (_gpsStarted) Input.location.Stop();
        if (showHeading) Input.compass.enabled = false;
    }
    
    /// <summary>
    /// Bearing from point A(lat1,lon1) to B(lat2,lon2) in degrees (0° = North, clockwise)
    /// </summary>
    private static double BearingDegrees(double lat1, double lon1, double lat2, double lon2)
    {
        double lat1Rad = Deg2Rad(lat1);
        double lat2Rad = Deg2Rad(lat2);
        double dLon = Deg2Rad(lon2 - lon1);

        double y = Math.Sin(dLon) * Math.Cos(lat2Rad);
        double x = Math.Cos(lat1Rad) * Math.Sin(lat2Rad) -
                   Math.Sin(lat1Rad) * Math.Cos(lat2Rad) * Math.Cos(dLon);
        double brng = Math.Atan2(y, x);
        return (brng * 180.0 / Math.PI + 360.0) % 360.0; // normalize to 0–360°
    }
}