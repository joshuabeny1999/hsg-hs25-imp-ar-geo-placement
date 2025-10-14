using System;
using Shared.Scripts.Geo;
using UnityEngine;
using UnityEngine.UI;

public class ArrowToTarget : MonoBehaviour
{
    
    [Header("References")]
    [Tooltip("The GeoObjectSpawner to compare against")]
    public GeoObjectSpawner geoSpawner;

    [Header("Settings")]
    [Tooltip("Hide arrow when closer than this distance (meters)")]
    public float hideWhenCloserThanMeters = 30f;

    private Image _arrowImage;
    float _bearingToTarget = 0f;
    float _distanceM = Mathf.Infinity;

    void Start()
    {
        // Get Image component
        _arrowImage = GetComponent<Image>();
        if (_arrowImage == null)
        {
            Debug.LogError("[ArrowToTarget] No Image component found! Add this script to a Image.");
            enabled = false;
            return;
        }
        
        // Auto-find spawner if not assigned
        if (geoSpawner == null)
        {
            geoSpawner = FindFirstObjectByType<GeoObjectSpawner>();
        }
        
        Input.compass.enabled = true;
        if (Input.location.isEnabledByUser) Input.location.Start(1f, 0.1f);
    }

    void Update()
    {
        if (Input.location.status != LocationServiceStatus.Running || !_arrowImage || !geoSpawner) return;

        double targetEast = geoSpawner.east;
        double targetNorth = geoSpawner.north;
        ProjNetTransformCH.LV95ToWGS84(targetEast, targetNorth, out var targetLat, out var targetLon);
        var coord = Input.location.lastData;
        _bearingToTarget = GeoDebugHUD_BearingDeg(coord.latitude, coord.longitude, targetLat, targetLon);
        _distanceM = GeoDebugHUD_HaversineMeters(coord.latitude, coord.longitude, targetLat, targetLon);

        // Geräteheading (0° = Norden), clockwise
        float heading = Input.compass.trueHeading; // fallback: .magneticHeading
        float relative = _bearingToTarget - heading;
        // Normalize to [0,360)
        if (relative < 0) relative += 360f;

        // Rotier UI-Pfeil (Z-Rotation)
        _arrowImage.rectTransform.rotation = Quaternion.Euler(0, 0, -relative);

        // Optional: ausblenden, wenn du praktisch "da" bist
        _arrowImage.enabled = _distanceM > hideWhenCloserThanMeters;
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