using System;
using UnityEngine;
using UnityEngine.UI;

public class ArrowToTarget : MonoBehaviour
{
    public double targetLat = 47.0;
    public double targetLon = 8.0;
    public Image arrowImage; // dein UI Image (Pfeil)
    public float hideWhenCloserThanMeters = 30f;

    float _bearingToTarget = 0f;
    float _distanceM = Mathf.Infinity;

    void Start()
    {
        if (arrowImage == null) arrowImage = GetComponent<Image>();
        Input.compass.enabled = true;
        if (Input.location.isEnabledByUser) Input.location.Start(1f, 0.1f);
    }

    void Update()
    {
        if (Input.location.status != LocationServiceStatus.Running || arrowImage == null) return;

        var coord = Input.location.lastData;
        _bearingToTarget = GeoDebugHUD_BearingDeg(coord.latitude, coord.longitude, targetLat, targetLon);
        _distanceM = GeoDebugHUD_HaversineMeters(coord.latitude, coord.longitude, targetLat, targetLon);

        // Geräteheading (0° = Norden), clockwise
        float heading = Input.compass.trueHeading; // fallback: .magneticHeading
        float relative = _bearingToTarget - heading;
        // Normalize to [0,360)
        if (relative < 0) relative += 360f;

        // Rotier UI-Pfeil (Z-Rotation)
        arrowImage.rectTransform.rotation = Quaternion.Euler(0, 0, -relative);

        // Optional: ausblenden, wenn du praktisch "da" bist
        arrowImage.enabled = _distanceM > hideWhenCloserThanMeters;
    }

    // kleine statische Helfer (du kannst die aus dem HUD kopieren, hier inline für Unabhängigkeit)
    static float GeoDebugHUD_HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000.0;
        double dLat = (lat2 - lat1) * Mathf.Deg2Rad;
        double dLon = (lon2 - lon1) * Mathf.Deg2Rad;
        lat1 *= Mathf.Deg2Rad; lat2 *= Mathf.Deg2Rad;
        double a = Mathf.Sin((float)dLat/2)*Mathf.Sin((float)dLat/2) +
                   Mathf.Cos((float)lat1)*Mathf.Cos((float)lat2) * Mathf.Sin((float)dLon/2)*Mathf.Sin((float)dLon/2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1-a));
        return (float)(R * c);
    }
    static float GeoDebugHUD_BearingDeg(double lat1, double lon1, double lat2, double lon2)
    {
        double φ1 = lat1 * Mathf.Deg2Rad, φ2 = lat2 * Mathf.Deg2Rad;
        double Δλ = (lon2 - lon1) * Mathf.Deg2Rad;
        double y = Math.Sin(Δλ) * Math.Cos(φ2);
        double x = Math.Cos(φ1)*Math.Sin(φ2) - Math.Sin(φ1)*Math.Cos(φ2)*Math.Cos(Δλ);
        double θ = Math.Atan2(y, x);
        double brng = (θ * Mathf.Rad2Deg + 360.0) % 360.0;
        return (float)brng;
    }
}