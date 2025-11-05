using System;
using System.Collections;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Shared.Scripts.Geo
{
    [Serializable]
    public class WmsField
    {
        [JsonProperty("label")] public string Label;
        [JsonProperty("value")] public JToken Value;   // Zahl, String oder null
        [JsonProperty("type")]  public string Type;
    }

    [Serializable]
    public class WmsBuildingResult
    {
        public string FeatureId;
        public int?   LayerId;
        public string LayerLabel;
        public string Origin;
        public string BfsNr;

        public WmsField[] Properties; // alle Felder inkl. leer/null
        public string RawJson;        // Debug/Logging
    }

    /// <summary>
    /// OGC WMS GetFeatureInfo – Metadaten für genau EIN Gebäude an einer LV95-Koordinate (ohne Geometrie).
    /// </summary>
    public class GeoInfoWMSAPI : MonoBehaviour
    {
        [Header("WMS Settings")]
        [SerializeField] private string baseUrlWms = "https://www.geoportal.ch/services/wms";
        [SerializeField] private string layer = "v12006";   // Gebäude- und Wohnungsregister
        [SerializeField] private string lang = "de";
        [SerializeField] private string crs = "EPSG:2056";

        [Tooltip("Halbe Kantenlänge der Such-BBOX um den Punkt (Meter). Wenn kein Treffer, erhöhen (z.B. 30–50).")]
        [SerializeField] private double searchHalfMeters = 20.0;

        [Header("Debug")]
        [SerializeField] private bool logCurl = true;

        public event Action<WmsBuildingResult> BuildingFetched;

        /// <summary>
        /// Öffentliche API: Ein Gebäude an LV95-Koordinate abfragen.
        /// </summary>
        public void FetchByCentroid(double east, double north, Action<WmsBuildingResult> onCompleted = null)
        {
            StartCoroutine(FetchByCentroidRoutine(east, north, onCompleted));
        }

        private IEnumerator FetchByCentroidRoutine(double east, double north, Action<WmsBuildingResult> onCompleted)
        {
            if (string.IsNullOrWhiteSpace(baseUrlWms) || string.IsNullOrWhiteSpace(layer))
            {
                Debug.LogError("[GeoInfoWMSAPI] baseUrlWms/layer not configured.");
                onCompleted?.Invoke(null);
                yield break;
            }

            // kleines Rasterfenster um den Punkt (zentriert I=J=50 auf 101x101)
            double minx = east  - searchHalfMeters;
            double miny = north - searchHalfMeters;
            double maxx = east  + searchHalfMeters;
            double maxy = north + searchHalfMeters;

            var url = new StringBuilder();
            url.Append(baseUrlWms)
               .Append("?lang=").Append(lang)
               .Append("&primaryAreaForStatistic=ch")
               .Append("&REQUEST=GetFeatureInfo")
               .Append("&QUERY_LAYERS=").Append(layer)
               .Append("&SERVICE=WMS")
               .Append("&VERSION=1.3.0")
               .Append("&FORMAT=image/png8")
               .Append("&STYLES=")
               .Append("&TRANSPARENT=true")
               .Append("&LAYERS=").Append(layer)
               .Append("&FILTERIDS=include")
               .Append("&CRS=").Append(crs)
               .Append("&INFO_FORMAT=application/json")
               .Append("&TYPE=default")
               .Append("&getShortInformation=false")
               .Append("&I=50&J=50&WIDTH=101&HEIGHT=101")
               .Append("&BBOX=")
               .Append(minx.ToString(CultureInfo.InvariantCulture)).Append(",")
               .Append(miny.ToString(CultureInfo.InvariantCulture)).Append(",")
               .Append(maxx.ToString(CultureInfo.InvariantCulture)).Append(",")
               .Append(maxy.ToString(CultureInfo.InvariantCulture));

            string finalUrl = url.ToString();
            if (logCurl) Debug.Log($"[GeoInfoWMSAPI] curl -X GET \"{finalUrl}\"");

            using var req = UnityWebRequest.Get(finalUrl);
            req.timeout = 10;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[GeoInfoWMSAPI] HTTP error: {req.error}");
                onCompleted?.Invoke(null);
                BuildingFetched?.Invoke(null);
                yield break;
            }

            var raw = req.downloadHandler.text;

            try
            {
                var root = JObject.Parse(raw);
                var features = (JArray)root["features"];
                if (features == null || features.Count == 0)
                {
                    onCompleted?.Invoke(null);
                    BuildingFetched?.Invoke(null);
                    yield break;
                }

                var f = (JObject)features[0];

                var propsArr = (JArray)f["properties"];
                var props = propsArr != null
                    ? propsArr.ToObject<WmsField[]>()
                    : Array.Empty<WmsField>();

                var res = new WmsBuildingResult
                {
                    FeatureId  = f.Value<string>("id"),
                    LayerId    = f.Value<int?>("layerId"),
                    LayerLabel = f.Value<string>("label"),
                    Origin     = f.Value<string>("origin"),
                    BfsNr      = f.Value<string>("bfsnr"),
                    Properties = props,
                    RawJson    = raw
                };


                onCompleted?.Invoke(res);
                BuildingFetched?.Invoke(res);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GeoInfoWMSAPI] Parse error: {ex.Message}");
                onCompleted?.Invoke(null);
                BuildingFetched?.Invoke(null);
            }
        }

        // Helper: Für UI hübsch formatieren (optional)
        public static string FormatValue(JToken v, string dash = "—")
        {
            if (v == null || v.Type == JTokenType.Null) return dash;
            if (v.Type == JTokenType.String) return v.ToString();    // leerer String bleibt leer
            return v.ToString();
        }
    }
}