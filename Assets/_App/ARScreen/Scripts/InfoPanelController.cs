using System;
using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Newtonsoft.Json.Linq;
using Shared.Scripts.App;
using Shared.Scripts.Geo;
using Shared.Scripts.Building;

public class InfoPanelController : MonoBehaviour
{
    private Coroutine _buildRoutine;

    [Header("Wiring")]
    [SerializeField] private GameObject rootPanel;   // InfoPopup panel (visual)
    [SerializeField] private ScrollRect scrollRect;  // Scroll View
    [SerializeField] private Transform contentRoot;  // Content under Scroll View
    [SerializeField] private GameObject rowPrefab;   // uses InfoRow
    [SerializeField] private TMP_Text headerTitle;
    [SerializeField] private Button closeButton;

    [Header("Services in Scene")]
    [SerializeField] private GeoInfoWMSAPI wmsApi;   // optional, for Gebäudestatus (WMS)

    [Header("Options")]
    [SerializeField] private string flaechenblattTyp = "L";  // usually "L"
    [SerializeField] private double eastOverride = 0;        // optional for debugging
    [SerializeField] private double northOverride = 0;

    private void Awake()
    {
        if (closeButton) closeButton.onClick.AddListener(Close);
        if (headerTitle) headerTitle.text = "Information";
        if (rootPanel) rootPanel.SetActive(false);   // hide visually; controller object stays active
    }

    public void Open()
    {
        // Panel sichtbar machen
        if (rootPanel != null)
        {
            // need to be called twice, otherwise on first call the panel is not visible
            rootPanel.SetActive(true);
            rootPanel.SetActive(true);
        }
        else
            gameObject.SetActive(true);

        // alte Coroutine stoppen
        if (_buildRoutine != null)
        {
            StopCoroutine(_buildRoutine);
            _buildRoutine = null;
        }

        // Inhalt neu aufbauen
        _buildRoutine = StartCoroutine(BuildAndShow());
    }

    public void Close()
    {
        if (!rootPanel) return;

        // stop running build, if any
        if (_buildRoutine != null)
        {
            StopCoroutine(_buildRoutine);
            _buildRoutine = null;
        }

        rootPanel.SetActive(false);
        ClearContent();
    }

    private void ClearContent()
    {
        if (!contentRoot) return;
        for (int i = contentRoot.childCount - 1; i >= 0; i--)
            Destroy(contentRoot.GetChild(i).gameObject);
    }

    private IEnumerator BuildAndShow()
    {
        ClearContent();
        Debug.Log("[InfoPanel] Build and show");

        // -------- 1) Titel: Allgemein
        AddTitle("Allgemein");

        // rows
        AddRow("Name", Safe(CurrentSelectedProjection.Building.Name));
        AddRow("EGID", Safe(CurrentSelectedProjection.Building.Egid));
        AddRow("Latitude", CurrentSelectedProjection.Building.Latitude.ToString("F6", CultureInfo.InvariantCulture));
        AddRow("Longitude", CurrentSelectedProjection.Building.Longitude.ToString("F6", CultureInfo.InvariantCulture));

        // LV95 aus Polygoncentroid oder WGS84 ableiten
        double east = 0, north = 0;
        bool haveEN = false;

        if (!string.IsNullOrWhiteSpace(CurrentSelectedProjection.Building.RawCoordinates) &&
            BuildingGeometryUtils.TryCentroidLV95(CurrentSelectedProjection.Building.RawCoordinates, out var eC, out var nC))
        {
            east = eC; north = nC; haveEN = true;
        }
        else if (CurrentSelectedProjection.Building.Latitude != 0 || CurrentSelectedProjection.Building.Longitude != 0)
        {
            ProjNetTransformCH.WGS84ToLV95(CurrentSelectedProjection.Building.Latitude, CurrentSelectedProjection.Building.Longitude, out east, out north);
            haveEN = true;
        }

        if (eastOverride != 0 || northOverride != 0) { east = eastOverride; north = northOverride; haveEN = true; }

        if (haveEN)
        {
            AddRow("East (LV95)", east.ToString("F2", CultureInfo.InvariantCulture));
            AddRow("North (LV95)", north.ToString("F2", CultureInfo.InvariantCulture));
        }

        if (CurrentSelectedProjection.Building.ElevationMeters.HasValue)
            AddRow("Geländehöhe (m.ü.M.)", CurrentSelectedProjection.Building.ElevationMeters.Value.ToString("F2", CultureInfo.InvariantCulture));

        // -------- 2) Stammdaten (liefert BFS/LiegNr/EGRID für Flächenblatt)
        GeoPortalStammdatenResponse stammdaten = null;
        if (haveEN)
        {
            bool sdDone = false;
            yield return GeoInfoAPI.FetchStammdaten(east, north, r => { stammdaten = r; sdDone = true; }, "de");
            while (!sdDone) yield return null;

            AddTitle("Liegenschaft (Stammdaten)");
            if (stammdaten == null || stammdaten.IsEmpty)
            {
                Debug.Log("[InfoPanel] Stammdaten: empty");
                AddRow("Hinweis", "Keine Stammdaten verfügbar.");
            }
            else
            {
                Debug.Log($"[InfoPanel] Stammdaten: {stammdaten.liegenschaft.nummer} / {stammdaten.liegenschaft.egrid}");
                RenderStammdaten(stammdaten);
            }
        }

        // -------- 3) Flächenblatt (nutzt Schlüssel aus Stammdaten)
        if (stammdaten != null && !stammdaten.IsEmpty &&
            stammdaten.gemeinde != null &&
            stammdaten.liegenschaft != null &&
            !string.IsNullOrEmpty(stammdaten.gemeinde.bfsnr) &&
            !string.IsNullOrEmpty(stammdaten.liegenschaft.nummer) &&
            !string.IsNullOrEmpty(stammdaten.liegenschaft.egrid))
        {
            string bfs = stammdaten.gemeinde.bfsnr;
            string liegnr = stammdaten.liegenschaft.nummer;
            string egrid = stammdaten.liegenschaft.egrid;

            bool fbDone = false;
            GeoPortalFlaechenblattResponse fb = null;
            yield return GeoInfoAPI.FetchFlaechenblattByIds(bfs, liegnr, flaechenblattTyp, egrid, r => { fb = r; fbDone = true; }, "de");
            while (!fbDone) yield return null;

            AddTitle("Liegenschaft (Flächenblatt)");
            if (fb == null || fb.error)
            {
                Debug.Log("[InfoPanel] Flächenblatt: empty");
                AddRow("Hinweis", "Keine Flächenblatt-Daten.");
            }
            else
            {
                Debug.Log("[Info Panel] Flächenblatt: found");
                RenderFlaechenblatt(fb);
            }
        }
        else
        {
            AddTitle("Liegenschaft (Flächenblatt)");
            AddRow("Hinweis", "Stammdaten unvollständig (BFS/LiegNr/EGRID fehlen).");
        }

        // -------- 4) Gebäudestatus (WMS) – optional
        if (wmsApi != null && haveEN)
        {
            WmsBuildingResult wms = null;
            bool done = false;
            wmsApi.FetchByCentroid(east, north, res => { wms = res; done = true; });
            while (!done) yield return null;

            AddTitle("Gebäudestatus (WMS)");
            if (wms != null)
            {
                Debug.Log("[Info Panel] Gebäudestatus (WMS): found");
                RenderWmsProperties(wms);
            }
            else
            {
                Debug.Log("[Info Panel] Gebäudestatus (WMS): empty");
                AddRow("Hinweis", "Keine Daten gefunden.");
            }
        }

        // scroll to top
        if (scrollRect)
        {
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 1f;
        }
    }

    // ---------------- render helpers (no accordion) ----------------

    private void RenderStammdaten(GeoPortalStammdatenResponse sd)
    {
        // Allgemein
        AddSubtitle("Allgemein");
        if (sd.gemeinde != null)
        {
            AddRow("Gemeinde", Safe(sd.gemeinde.name));
            AddRow("BFS-Nr.", Safe(sd.gemeinde.bfsnr));
            AddRow("Kanton", Safe(sd.gemeinde.kanton));
        }
        if (sd.liegenschaft != null)
        {
            AddRow("Grundstücksnummer", Safe(sd.liegenschaft.nummer));
            AddRow("EGRID", Safe(sd.liegenschaft.egrid));
        }

        // Adressen
        if (sd.adressen != null && sd.adressen.Length > 0)
        {
            AddSubtitle("Adresse(n)");
            foreach (var a in sd.adressen)
            {
                string line = $"{Safe(a.street, "")} {Safe(a.number, "")}".Trim();
                if (!string.IsNullOrEmpty(a.zip)) line = $"{line}, {a.zip}";
                AddRow("Adresse", Safe(line));
            }
        }

    }

    private void RenderWmsProperties(WmsBuildingResult wms)
    {
        foreach (var p in wms.Properties)
        {
            if (IsEmpty(p.Value)) continue;

            if (string.Equals(p.Type, "title", StringComparison.OrdinalIgnoreCase))
            {
                AddSubtitle(p.Label);
            }
            else
            {
                AddRow(p.Label, WmsValueToDisplay(p.Value));
            }
        }
    }

    private void RenderFlaechenblatt(GeoPortalFlaechenblattResponse fb)
    {
        // Liegenschaft
        AddSubtitle("Liegenschaft");
        if (fb.Liegenschaft != null)
        {
            AddRow("Parzelle", Safe(fb.Liegenschaft.parznr));
            AddRow("EGRID", Safe(fb.Liegenschaft.egrid));
            AddRow("Gemeinde", Safe(fb.Liegenschaft.gemeinde));
            AddRow("Lokalname", Safe(fb.Liegenschaft.lokalname));
            AddRow("Adresse", Safe(fb.Liegenschaft.strasse));
            AddRow("Fläche (m²)", Safe(fb.Liegenschaft.flaeche));
            AddRow("Plan-Nr.", Safe(fb.Liegenschaft.plannr));
            AddRow("Kanton", Safe(fb.Liegenschaft.kanton));
            AddRow("Eigentumsform", Safe(fb.Liegenschaft.eigentumsform));
            AddRow("EigForm", Safe(fb.Liegenschaft.eigform));
            AddRow("Typ", Safe(fb.Liegenschaft.typ));
        }

        // Zonen
        if (fb.zoneAreas != null && fb.zoneAreas.Length > 0)
        {
            AddSubtitle("Zonen");
            foreach (var z in fb.zoneAreas) {
                var label = string.IsNullOrEmpty(z.label) ? "" : z.label;
                var val = $"{z.value.ToString(CultureInfo.InvariantCulture)} m²";
                AddRow(label, val);
            }
        }

        // Hangneigung
        if (fb.slopeAreas != null && fb.slopeAreas.Length > 0)
        {
            AddSubtitle("Hangneigung");
            foreach (var s in fb.slopeAreas)
            {
                var label = string.IsNullOrEmpty(s.label) ? "" : s.label;
                var val = $"{s.value.ToString(CultureInfo.InvariantCulture)} m²";
                AddRow(label, val);
            }
        }

        // Areal
        if (fb.Areal != null && fb.Areal.Length > 0)
        {
            AddSubtitle("Areal / Teilflächen");
            foreach (var a in fb.Areal)
            {
                var label = string.IsNullOrEmpty(a.art) ? "Objekt" : a.art;
                var val = string.IsNullOrEmpty(a.flaeche) ? "" : $"{a.flaeche} m²";
                AddRow(label, val);
            }
        }
    }

    // ---------------- row creation helpers ----------------

    private void AddTitle(string text)
    {
        var go = Instantiate(rowPrefab, contentRoot);
        var ir = go.GetComponent<InfoRow>();
        ir.SetTitle(text); // big, bold, no value
    }

    private void AddSubtitle(string text)
    {
        var go = Instantiate(rowPrefab, contentRoot);
        var ir = go.GetComponent<InfoRow>();
        ir.SetSubtitle(text); // bold (slightly smaller than title)
    }

    private void AddRow(string label, string value)
    {
        var go = Instantiate(rowPrefab, contentRoot);
        var ir = go.GetComponent<InfoRow>();
        ir.Set(label, value);
    }

    // ---------------- utils ----------------

    private static string Safe(string s, string dash = "—") => string.IsNullOrEmpty(s) ? dash : s;

    private static bool IsEmpty(JToken t)
    {
        if (t == null || t.Type == JTokenType.Null) return true;
        if (t.Type == JTokenType.String) return string.IsNullOrWhiteSpace(t.ToString());
        return false;
    }

    private static string WmsValueToDisplay(JToken v)
    {
        if (v == null || v.Type == JTokenType.Null) return "—";
        if (v.Type == JTokenType.String)
        {
            var s = v.ToString();
            return string.IsNullOrEmpty(s) ? "" : s;
        }
        return v.ToString();
    }
}