using System;
using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.Networking;

namespace Shared.Scripts.Geo
{
    /// <summary>
    /// Helper for querying Swiss GeoPortal services:
    /// - Elevation (/api/elevation/point)
    /// - Stammdaten (/search/stammdaten)
    /// - Flächenblatt (/search/flaechenblatt)
    /// All coordinates are LV95 (EPSG:2056).
    /// </summary>
    public static class GeoInfoAPI
    {
        // -------- Base URLs (can be customized in code if needed) ----------
        public static string ApiBaseUrl    = "https://www.geoportal.ch/api/";
        public static string SearchBaseUrl = "https://www.geoportal.ch/search/";

        // -------------------------------------------------------------------
        #region Elevation

        /// <summary>
        /// Fetches terrain and surface elevation data for a given LV95 (EPSG:2056) coordinate.
        /// </summary>
        public static IEnumerator FetchElevation(double east, double north, Action<GeoPortalElevationResponse> onResult)
        {
            string url = $"{ApiBaseUrl}elevation/point?lang=de&east={east.ToString("F2", CultureInfo.InvariantCulture)}&north={north.ToString("F2", CultureInfo.InvariantCulture)}";
            using var req = UnityWebRequest.Get(url);
            req.timeout = 10;

            yield return req.SendWebRequest();

            GeoPortalElevationResponse result = null;

            if (req.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    result = JsonUtility.FromJson<GeoPortalElevationResponse>(req.downloadHandler.text);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[GeoInfoAPI] Failed to parse elevation response: {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"[GeoInfoAPI] HTTP error (elevation): {req.error}");
            }

            onResult?.Invoke(result);
        }

        #endregion
        // -------------------------------------------------------------------
        #region Stammdaten

        /// <summary>
        /// Fetches property information (stammdaten) for a given LV95 coordinate.
        /// Example: /search/stammdaten/?search=coor&coor=2704033.87,1232743.82&lang=de
        /// </summary>
        public static IEnumerator FetchStammdaten(
            double east,
            double north,
            Action<GeoPortalStammdatenResponse> onResult,
            string lang = "de")
        {
            string url =
                $"{SearchBaseUrl}stammdaten/?search=coor" +
                $"&coor={east.ToString("F6", CultureInfo.InvariantCulture)},{north.ToString("F6", CultureInfo.InvariantCulture)}" +
                $"&noOwners=" +
                $"&lang={lang}";

            using var req = UnityWebRequest.Get(url);
            req.timeout = 10;

            yield return req.SendWebRequest();

            GeoPortalStammdatenResponse result = null;

            if (req.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    result = JsonUtility.FromJson<GeoPortalStammdatenResponse>(req.downloadHandler.text);

                    if (result != null && result.liegenschaft != null && !string.IsNullOrEmpty(result.liegenschaft.egrid))
                    {
                        var egrid = result.liegenschaft.egrid;
                        if (!string.IsNullOrEmpty(result.oerebUrl))
                            result.oerebUrl = result.oerebUrl.Replace("{{ egrid }}", egrid);
                        if (!string.IsNullOrEmpty(result.oerebPortalUrl))
                            result.oerebPortalUrl = result.oerebPortalUrl.Replace("{{ egrid }}", egrid);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[GeoInfoAPI] Failed to parse stammdaten response: {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"[GeoInfoAPI] HTTP error (stammdaten): {req.error}");
            }

            onResult?.Invoke(result);
        }

        #endregion
        // -------------------------------------------------------------------
        #region Flächenblatt

        /// <summary>
        /// Holt das "Flächenblatt" typisiert via festen IDs.
        /// Beispiel:
        /// /search/flaechenblatt/?bfs=3340&liegnr=4553J&typ=L&egrid=CH579177147966&lang=de
        /// </summary>
        /// <param name="bfs">Gemeinde BFS-Nummer, z.B. "3340"</param>
        /// <param name="liegnr">Liegenschafts-/Parzellen-Nr., z.B. "4553J"</param>
        /// <param name="typ">Typ (z.B. "L")</param>
        /// <param name="egrid">EGRID, z.B. "CH579177147966"</param>
        /// <param name="onResult">Callback mit parsebarem Ergebnis (oder null)</param>
        /// <param name="lang">"de" (Default) oder andere unterstützte Sprache</param>
        public static IEnumerator FetchFlaechenblattByIds(
            string bfs,
            string liegnr,
            string typ,
            string egrid,
            Action<GeoPortalFlaechenblattResponse> onResult,
            string lang = "de")
        {
            if (string.IsNullOrWhiteSpace(bfs) ||
                string.IsNullOrWhiteSpace(liegnr) ||
                string.IsNullOrWhiteSpace(typ) ||
                string.IsNullOrWhiteSpace(egrid))
            {
                Debug.LogWarning("[GeoInfoAPI] FetchFlaechenblattByIds: required parameter missing.");
                onResult?.Invoke(null);
                yield break;
            }

            string url =
                $"{SearchBaseUrl}flaechenblatt/?" +
                $"bfs={UnityWebRequest.EscapeURL(bfs)}" +
                $"&liegnr={UnityWebRequest.EscapeURL(liegnr)}" +
                $"&typ={UnityWebRequest.EscapeURL(typ)}" +
                $"&egrid={UnityWebRequest.EscapeURL(egrid)}" +
                $"&lang={UnityWebRequest.EscapeURL(lang)}";

            using var req = UnityWebRequest.Get(url);
            req.timeout = 12;

            yield return req.SendWebRequest();

            GeoPortalFlaechenblattResponse result = null;

            if (req.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    result = JsonUtility.FromJson<GeoPortalFlaechenblattResponse>(req.downloadHandler.text);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[GeoInfoAPI] Failed to parse flaechenblatt response: {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"[GeoInfoAPI] HTTP error (flaechenblatt): {req.error}");
            }

            onResult?.Invoke(result);
        }

        #endregion
    }

    // -----------------------------------------------------------------------
    #region Models

    [Serializable]
    public class GeoPortalElevationResponse
    {
        public double east;
        public double north;
        public double elevation;          // Terrain height (AMSL)
        public double surface;            // Building top height (AMSL)
        public double elevationDifference;
    }

    [Serializable]
    public class GeoPortalStammdatenResponse
    {
        public GeoPortalGemeinde gemeinde;
        public GeoPortalLiegenschaft liegenschaft;

        public bool eigentuemerChallenge;
        public GeoPortalAdresse[] adressen;

        public string oerebUrl;
        public string oerebPortalUrl;

        /// <summary>
        /// True if no liegenschaft (e.g. outside valid area)
        /// </summary>
        public bool IsEmpty => liegenschaft == null || string.IsNullOrEmpty(liegenschaft.egrid);
    }

    [Serializable]
    public class GeoPortalGemeinde
    {
        public string name;
        public string bfsnr;
        public string kanton;
    }

    [Serializable]
    public class GeoPortalLiegenschaft
    {
        public string nummer; // Grundstücksnummer
        public string egrid;  // EGRID
    }

    [Serializable]
    public class GeoPortalAdresse
    {
        public string street;
        public string number;
        public string zip;
    }

        [Serializable]
    public class GeoPortalFlaechenblattResponse
    {
        public bool error;
        public GeoPortalFlaechenblattLiegenschaft Liegenschaft;

        public GeoPortalLabelValue[] slopeAreas;   // [{label, value}]
        public int slopeAreaDifference;

        public GeoPortalZoneArea[] zoneAreas;      // [{label, shortLabel, value}]
        public int zoneAreaDifference;

        public GeoPortalAreal[] Areal;            // Liste Teilflächen/Objekte

        public int coverAreaDifference;
    }

    [Serializable]
    public class GeoPortalFlaechenblattLiegenschaft
    {
        public string parznr;
        public string egrid;
        public string gemeinde;
        public string mutnr;
        public string lokalname;
        public string strasse;   // z. B. "Zürcherstrasse 137"
        public string flaeche;   // z. B. "513" (kommt als String)
        public string plannr;
        public string kanton;
        public string eigentumsform;  // kann null sein
        public string eigform;
        public string typ;       // z. B. "L"
        public string geom;      // MULTIPOLYGON(...), POLYGON(...)
    }

    [Serializable]
    public class GeoPortalLabelValue
    {
        public string label;
        public int value;
    }

    [Serializable]
    public class GeoPortalZoneArea
    {
        public string label;
        public string shortLabel;
        public int value;
    }

    [Serializable]
    public class GeoPortalAreal
    {
        public string art;       // z. B. "Gebäude", "Gartenanlage"
        public string egid;      // kann null sein
        public string asseknr;   // z. B. "5028J"
        public string flaeche;   // z. B. "173" (als String)
        public string typ;       // z. B. "O"
        public string artgroup;  // kann null sein
        public string adresse;   // kann null sein
        public string geom;      // POLYGON(...) | MULTIPOLYGON(...)
    }


    #endregion
}