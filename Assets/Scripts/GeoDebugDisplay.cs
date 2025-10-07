using UnityEngine;
using TMPro;
using System;

/// <summary>
/// Simple debug display showing device GPS location and distance to cube
/// Assign to a TextMeshPro - Text (TMP) UI element
/// </summary>
public class GeoDebugDisplay : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The GeoObjectSpawner to compare against")]
    public GeoObjectSpawner geoSpawner;

    [Header("Settings")]
    [Tooltip("Update interval in seconds")]
    public float updateInterval = 0.5f;

    private TextMeshProUGUI _text;
    private float _updateTimer = 0f;
    private bool _gpsStarted = false;

    private void Start()
    {
        // Get TextMeshPro component
        _text = GetComponent<TextMeshProUGUI>();
        if (_text == null)
        {
            Debug.LogError("[GeoDebugDisplay] No TextMeshProUGUI component found! Add this script to a TMP Text element.");
            enabled = false;
            return;
        }

        // Auto-find spawner if not assigned
        if (geoSpawner == null)
        {
            geoSpawner = FindFirstObjectByType<GeoObjectSpawner>();
        }

        // Start GPS
        StartGPS();
        
        _text.text = "Starting GPS...";
    }

    private void StartGPS()
    {
        if (!Input.location.isEnabledByUser)
        {
            _text.text = "GPS disabled by user";
            return;
        }

        Input.location.Start(1f, 0.5f); // 1m accuracy, 0.5m update distance
        _gpsStarted = true;
    }

    private void Update()
    {
        if (!_gpsStarted) return;

        // Update at specified interval
        _updateTimer += Time.deltaTime;
        if (_updateTimer < updateInterval) return;
        _updateTimer = 0f;

        UpdateDebugText();
    }

    private void UpdateDebugText()
    {
        if (Input.location.status == LocationServiceStatus.Stopped)
        {
            _text.text = "GPS: STOPPED";
            return;
        }

        if (Input.location.status == LocationServiceStatus.Initializing)
        {
            _text.text = "GPS: Initializing...";
            return;
        }

        if (Input.location.status == LocationServiceStatus.Failed)
        {
            _text.text = "GPS: FAILED";
            return;
        }

        // GPS is running
        var location = Input.location.lastData;
        double deviceLat = location.latitude;
        double deviceLon = location.longitude;
        float deviceAlt = location.altitude;
        float accuracy = location.horizontalAccuracy;

        // Calculate distance to cube if spawner exists
        string distanceInfo = "";
        if (geoSpawner != null)
        {
            double cubeLat = geoSpawner.latitude;
            double cubeLon = geoSpawner.longitude;
            float distanceM = CalculateDistance(deviceLat, deviceLon, cubeLat, cubeLon);
            
            distanceInfo = $"\n<color=yellow>Distance to Cube: {distanceM:F1}m</color>";
            
            // Add visibility hint
            if (distanceM < 5f)
            {
                distanceInfo += "\n<color=lime>★ VERY CLOSE! ★</color>";
            }
            else if (distanceM < 20f)
            {
                distanceInfo += "\n<color=green>Should be visible</color>";
            }
            else if (distanceM < 100f)
            {
                distanceInfo += "\n<color=orange>Getting close...</color>";
            }
            else
            {
                distanceInfo += "\n<color=red>Too far away</color>";
            }
        }

        // Build debug text
        _text.text = $"<b>DEVICE GPS</b>\n" +
                     $"Lat: {deviceLat:F8}\n" +
                     $"Lon: {deviceLon:F8}\n" +
                     $"Alt: {deviceAlt:F1}m\n" +
                     $"Accuracy: ±{accuracy:F1}m\n" +
                     distanceInfo;

        // Add cube location info
        if (geoSpawner != null)
        {
            _text.text += $"\n\n<b>CUBE GPS</b>\n" +
                          $"Lat: {geoSpawner.latitude:F8}\n" +
                          $"Lon: {geoSpawner.longitude:F8}\n" +
                          $"Alt: {geoSpawner.AltitudeMeters:F1}m (API)";
        }
    }

    /// <summary>
    /// Calculate distance between two GPS points using Haversine formula
    /// </summary>
    private float CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000.0; // Earth radius in meters
        double dLat = (lat2 - lat1) * Math.PI / 180.0;
        double dLon = (lon2 - lon1) * Math.PI / 180.0;
        
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return (float)(R * c);
    }

    private void OnDestroy()
    {
        if (_gpsStarted)
        {
            Input.location.Stop();
        }
    }
}
