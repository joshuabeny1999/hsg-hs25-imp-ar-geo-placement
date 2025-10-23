using System;
using Shared.Scripts.App;
using Shared.Scripts.Geo;
using UnityEngine;
using UnityEngine.UI;

public class ArrowToTarget : MonoBehaviour
{
    [Header("References")]
    public GeoObjectSpawner geoSpawner; // optional fallback

    [Header("Settings")]
    public float hideWhenCloserThanMeters = 30f;

    private Image _arrowImage;
    private float _bearingToTarget = 0f;
    private float _distanceM = Mathf.Infinity;

    void Start()
    {
        _arrowImage = GetComponent<Image>();
        if (!_arrowImage)
        {
            Debug.LogError("[ArrowToTarget] No Image found on this GameObject.");
            enabled = false;
            return;
        }

        if (!geoSpawner) geoSpawner = FindFirstObjectByType<GeoObjectSpawner>();

        Input.compass.enabled = true;
        if (Input.location.isEnabledByUser) Input.location.Start(1f, 0.1f);
    }

    void Update()
    {
        if (Input.location.status != LocationServiceStatus.Running || !_arrowImage) return;

        // 1) Preferred: target from SelectedTargetContext (lat/lon)
        double targetLat = 0, targetLon = 0;
        bool hasContextTarget = SelectedTargetContext.Latitude != 0 || SelectedTargetContext.Longitude != 0;
        if (hasContextTarget)
        {
            targetLat = SelectedTargetContext.Latitude;
            targetLon = SelectedTargetContext.Longitude;
        }
        else if (geoSpawner != null)
        {
            // 2) Fallback to spawner’s LV95
            ProjNetTransformCH.LV95ToWGS84(geoSpawner.east, geoSpawner.north, out targetLat, out targetLon);
        }
        else
        {
            return;
        }

        var coord = Input.location.lastData;
        _bearingToTarget = GeoDebugHUD_BearingDeg(coord.latitude, coord.longitude, targetLat, targetLon);
        _distanceM = GeoDebugHUD_HaversineMeters(coord.latitude, coord.longitude, targetLat, targetLon);

        float heading = Input.compass.trueHeading;
        float relative = _bearingToTarget - heading;
        if (relative < 0) relative += 360f;

        _arrowImage.rectTransform.rotation = Quaternion.Euler(0, 0, -relative);
        _arrowImage.enabled = _distanceM > hideWhenCloserThanMeters;
    }

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