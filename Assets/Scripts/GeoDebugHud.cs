using System;
using UnityEngine;

public class GeoDebugHUD_v2 : MonoBehaviour
{
    [Header("Target (e.g., first vertex)")]
    public double targetLat = 47.000000;
    public double targetLon = 8.000000;

    [Header("UI")]
    public bool showOnScreen = true;
    [Tooltip("Panel size in pixels (width x height)")]
    public Vector2 panelSize = new Vector2(680, 170);
    [Tooltip("Bottom margin in pixels")]
    public float bottomMargin = 24f;
    [Tooltip("Panel opacity 0..1")]
    [Range(0f, 1f)] public float panelOpacity = 0.6f;

    float _distanceM = -1f;
    float _bearingDeg = 0f;
    string _status = "INIT";
    double _curLat, _curLon;
    double _lastTimestamp;

    Texture2D _panelTex;
    GUIStyle _labelStyle;

    void Start()
    {
        // init styles
        _panelTex = MakeTex(8, 8, new Color(1f, 1f, 1f, panelOpacity)); // semi-transparent white
        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            alignment = TextAnchor.UpperLeft,
            normal = { textColor = Color.black }
        };

        StartCoroutine(StartLocationServices());
    }

    System.Collections.IEnumerator StartLocationServices()
    {
        if (!Input.location.isEnabledByUser)
        {
            _status = "Location OFF (system)";
            yield break;
        }

        // Start services (accuracy, minDistance)
        Input.location.Start(1f, 0.1f);
        Input.compass.enabled = true;

        int maxWait = 20;
        while (Input.location.status == LocationServiceStatus.Initializing && maxWait-- > 0)
            yield return new WaitForSeconds(1f);

        if (Input.location.status != LocationServiceStatus.Running)
        {
            _status = "Location failed to start";
            yield break;
        }
        _status = "Location RUNNING";
    }

    void Update()
    {
        if (Input.location.status != LocationServiceStatus.Running) return;

        var coord = Input.location.lastData;
        _curLat = coord.latitude;
        _curLon = coord.longitude;
        _lastTimestamp = coord.timestamp;

        _distanceM = HaversineMeters(_curLat, _curLon, targetLat, targetLon);
        _bearingDeg = BearingDeg(_curLat, _curLon, targetLat, targetLon);

        // optional: quick debug every few seconds
        if (Time.frameCount % 60 == 0)
        {
            Debug.Log($"[HUD] GPS RUNNING | now=({_curLat:F6},{_curLon:F6}) ts={_lastTimestamp:F1} | dist={_distanceM:F1}m bearing={_bearingDeg:F0}°");
        }
    }

    void OnGUI()
    {
        if (!showOnScreen) return;

        var screenW = Screen.width;
        var rect = new Rect(
            (screenW - panelSize.x) * 0.5f,                        // centered
            Screen.height - bottomMargin - panelSize.y,           // bottom
            panelSize.x,
            panelSize.y
        );

        // background panel
        var bg = new GUIStyle(GUI.skin.box);
        bg.normal.background = _panelTex;
        GUI.Box(rect, GUIContent.none, bg);

        // text
        var pad = 10f;
        var textRect = new Rect(rect.x + pad, rect.y + pad, rect.width - 2*pad, rect.height - 2*pad);

        string gpsLine = $"GPS: {Input.location.status}  |  Compass: {(Input.compass.enabled ? "ON" : "OFF")}  |  ts: {_lastTimestamp:F1}";
        string nowLine = $"Lat/Lon now: {_curLat:F6}, {_curLon:F6}";
        string tgtLine = $"Target      : {targetLat:F6}, {targetLon:F6}";
        string metLine = $"Distance: {(_distanceM>=0? _distanceM.ToString("F1") : "-")} m   |   Bearing: {_bearingDeg:F0}°";
        string statLine = $"{_status}";

        GUI.Label(textRect, gpsLine + "\n" + nowLine + "\n" + tgtLine + "\n" + metLine + "\n" + statLine, _labelStyle);
    }

    // --- helpers ---
    static float HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000.0;
        double dLat = Deg2Rad(lat2 - lat1);
        double dLon = Deg2Rad(lon2 - lon1);
        double a = Math.Sin(dLat/2)*Math.Sin(dLat/2) +
                   Math.Cos(Deg2Rad(lat1))*Math.Cos(Deg2Rad(lat2)) * Math.Sin(dLon/2)*Math.Sin(dLon/2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1-a));
        return (float)(R * c);
    }

    static float BearingDeg(double lat1, double lon1, double lat2, double lon2)
    {
        double φ1 = Deg2Rad(lat1), φ2 = Deg2Rad(lat2);
        double λ1 = Deg2Rad(lon1), λ2 = Deg2Rad(lon2);
        double y = Math.Sin(λ2-λ1) * Math.Cos(φ2);
        double x = Math.Cos(φ1)*Math.Sin(φ2) - Math.Sin(φ1)*Math.Cos(φ2)*Math.Cos(λ2-λ1);
        double θ = Math.Atan2(y, x);
        return (float)((Rad2Deg(θ) + 360.0) % 360.0);
    }

    static double Deg2Rad(double d) => d * Math.PI / 180.0;
    static double Rad2Deg(double r) => r * 180.0 / Math.PI;

    static Texture2D MakeTex(int w, int h, Color c)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var pixels = new Color[w*h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = c;
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }
}