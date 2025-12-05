using System;
using Shared.Scripts.App;
using Shared.Scripts.Geo;
using Shared.Scripts.Building;
using UnityEngine;
using UnityEngine.UI;

public class ArrowToTarget : MonoBehaviour
{
    [Header("Settings")]
    public float hideWhenCloserThanMeters = 30f;

    private Image _arrowImage;
    private double _bearingToTarget = 0f;
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

        Input.compass.enabled = true;
        if (Input.location.isEnabledByUser) Input.location.Start(0.1f, 0.1f);
    }

    void Update()
    {
        if (Input.location.status != LocationServiceStatus.Running || !_arrowImage) return;

        // 1) Preferred: target from SelectedTargetContext (lat/lon)
        double targetLat = 0, targetLon = 0;
        bool hasContextTarget = CurrentSelectedProjection.Building.Latitude != 0 || CurrentSelectedProjection.Building.Longitude != 0;
        if (hasContextTarget)
        {
            targetLat = CurrentSelectedProjection.Building.Latitude;
            targetLon = CurrentSelectedProjection.Building.Longitude;
        }
        else
        {
            return;
        }

        var coord = Input.location.lastData;
        _bearingToTarget = BuildingGeometryUtils.BearingDegrees(coord.latitude, coord.longitude, targetLat, targetLon);
        _distanceM = BuildingGeometryUtils.HaversineMeters(coord.latitude, coord.longitude, targetLat, targetLon);

        float heading = Input.compass.trueHeading;
        float relative = (float)_bearingToTarget - heading;
        if (relative < 0) relative += 360f;

        _arrowImage.rectTransform.rotation = Quaternion.Euler(0, 0, -relative);
        _arrowImage.enabled = _distanceM > hideWhenCloserThanMeters;
    }
}