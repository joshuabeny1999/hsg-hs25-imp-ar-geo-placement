using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace Shared.Scripts.Geo
{
    /// <summary>
    /// Helper for querying Swiss GeoPortal elevation service.
    /// Example API: https://www.geoportal.ch/api/elevation/point?lang=de&east=2739782.97&north=1250944.04
    /// </summary>
    public static class GeoInfoAPI
    {
        private const string BaseUrl = "https://www.geoportal.ch/api/";

        /// <summary>
        /// Fetches terrain elevation (and optionally surface) data for a given LV95 (EPSG:2056) coordinate.
        /// </summary>
        /// <param name="east">LV95 east coordinate (meters)</param>
        /// <param name="north">LV95 north coordinate (meters)</param>
        /// <param name="onResult">Callback invoked when response is received (even on error)</param>
        /// <returns>IEnumerator coroutine (use StartCoroutine)</returns>
        public static IEnumerator FetchElevation(double east, double north, System.Action<SwissElevationResponse> onResult)
        {
            string url = $"{BaseUrl}elevation/point?lang=de&east={east:F2}&north={north:F2}";
            using var req = UnityWebRequest.Get(url);
            req.timeout = 10;

            yield return req.SendWebRequest();

            SwissElevationResponse result = null;

            if (req.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    result = JsonUtility.FromJson<SwissElevationResponse>(req.downloadHandler.text);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[GeoInfoAPI] Failed to parse response: {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"[GeoInfoAPI] HTTP error: {req.error}");
            }

            onResult?.Invoke(result);
        }
    }

    /// <summary>
    /// Response model for Swiss GeoPortal elevation API.
    /// </summary>
    [System.Serializable]
    public class SwissElevationResponse
    {
        public double east;
        public double north;
        public double elevation;          // Terrain height (AMSL)
        public double surface;            // Building top height (AMSL)
        public double elevationDifference;
    }
}