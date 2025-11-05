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
    [Header("Wiring")]
    [SerializeField] private GameObject rootPanel;           // InfoPopup panel
    [SerializeField] private ScrollRect scrollRect;          // Scroll View
    [SerializeField] private Transform contentRoot;          // Content under Scroll View
    [SerializeField] private GameObject accordionSectionPrefab;
    [SerializeField] private GameObject rowPrefab;
    [SerializeField] private TMP_Text headerTitle;
    [SerializeField] private Button closeButton;

    [Header("Services in Scene")]
    [SerializeField] private GeoInfoWMSAPI wmsApi;           // optional, for Gebäudestatus (WMS)

    [Header("Options")]
    [SerializeField] private bool autoFetchOnOpen = true;
    [SerializeField] private string flaechenblattTyp = "L";  // usually "L"
    [SerializeField] private double eastOverride = 0;        // optional for debugging
    [SerializeField] private double northOverride = 0;

    private void Awake()
    {
        if (closeButton) closeButton.onClick.AddListener(Close);
        if (headerTitle) headerTitle.text = "Information";
        if (rootPanel) rootPanel.SetActive(false);
    }

    public void Open()
    {
        if (!rootPanel) return;
        rootPanel.SetActive(true);
        if (autoFetchOnOpen) StartCoroutine(BuildAndShow());
    }

    public void Close()
    {
        if (!rootPanel) return;
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

        // -------- 1) Allgemein (immer)
        var secGeneral = CreateSection("Allgemein", expanded: true);
        AddRow(secGeneral, "Name", Safe(SelectedTargetContext.Name));
        AddRow(secGeneral, "EGID", Safe(SelectedTargetContext.Egid));
        AddRow(secGeneral, "Latitude", SelectedTargetContext.Latitude.ToString("F6", CultureInfo.InvariantCulture));
        AddRow(secGeneral, "Longitude", SelectedTargetContext.Longitude.ToString("F6", CultureInfo.InvariantCulture));

        // LV95 aus Polygoncentroid oder WGS84 ableiten
        double east = 0, north = 0;
        bool haveEN = false;

        if (!string.IsNullOrWhiteSpace(SelectedTargetContext.RawCoordinates) &&
            BuildingGeometryUtils.TryCentroidLV95(SelectedTargetContext.RawCoordinates, out var eC, out var nC))
        {
            east = eC; north = nC; haveEN = true;
        }
        else if (SelectedTargetContext.Latitude != 0 || SelectedTargetContext.Longitude != 0)
        {
            ProjNetTransformCH.WGS84ToLV95(SelectedTargetContext.Latitude, SelectedTargetContext.Longitude, out east, out north);
            haveEN = true;
        }

        if (eastOverride != 0 || northOverride != 0) { east = eastOverride; north = northOverride; haveEN = true; }

        if (haveEN)
        {
            AddRow(secGeneral, "East (LV95)", east.ToString("F2", CultureInfo.InvariantCulture));
            AddRow(secGeneral, "North (LV95)", north.ToString("F2", CultureInfo.InvariantCulture));
        }

        if (SelectedTargetContext.ElevationMeters.HasValue)
            AddRow(secGeneral, "Geländehöhe (m.ü.M.)", SelectedTargetContext.ElevationMeters.Value.ToString("F2", CultureInfo.InvariantCulture));

        // -------- 2) Stammdaten (liefert BFS/LiegNr/EGRID für Flächenblatt)
        GeoPortalStammdatenResponse stammdaten = null;
        if (haveEN)
        {
            bool sdDone = false;
            yield return GeoInfoAPI.FetchStammdaten(east, north, r => { stammdaten = r; sdDone = true; }, "de");
            while (!sdDone) yield return null;

            var secSd = CreateSection("Liegenschaft (Stammdaten)", expanded: true);
            if (stammdaten == null || stammdaten.IsEmpty)
            {
                AddRow(secSd, "Hinweis", "Keine Stammdaten verfügbar.");
            }
            else
            {
                RenderStammdaten(secSd, stammdaten);
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

            var secFb = CreateSection("Liegenschaft (Flächenblatt)", expanded: true);
            if (fb == null || fb.error)
            {
                AddRow(secFb, "Hinweis", "Keine Flächenblatt-Daten.");
            }
            else
            {
                RenderFlaechenblatt(secFb, fb);
            }
        }
        else
        {
            var secFb = CreateSection("Liegenschaft (Flächenblatt)", expanded: false);
            AddRow(secFb, "Hinweis", "Stammdaten unvollständig (BFS/LiegNr/EGRID fehlen).");
        }

        // -------- 4) Gebäudestatus (WMS) – optional / kann fehlen
        if (wmsApi != null && haveEN)
        {
            WmsBuildingResult wms = null;
            bool done = false;
            wmsApi.FetchByCentroid(east, north, res => { wms = res; done = true; });
            while (!done) yield return null;

            if (wms != null)
            {
                var secWms = CreateSection("Gebäudestatus (WMS)", expanded: true);
                RenderWmsPropertiesGrouped(secWms, wms);
            }
            else
            {
                var secWms = CreateSection("Gebäudestatus (WMS)", expanded: false);
                AddRow(secWms, "Hinweis", "Keine Daten gefunden.");
            }
        }

        // scroll to top
        if (scrollRect)
        {
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 1f;
        }
    }

    // ---------------- render helpers ----------------

    private void RenderStammdaten(AccordionSection section, GeoPortalStammdatenResponse sd)
    {
        var s1 = CreateSubTitle(section, "Allgemein");
        if (sd.gemeinde != null)
        {
            AddRow(s1, "Gemeinde", Safe(sd.gemeinde.name));
            AddRow(s1, "BFS-Nr.", Safe(sd.gemeinde.bfsnr));
            AddRow(s1, "Kanton", Safe(sd.gemeinde.kanton));
        }
        if (sd.liegenschaft != null)
        {
            AddRow(s1, "Grundstücksnummer", Safe(sd.liegenschaft.nummer));
            AddRow(s1, "EGRID", Safe(sd.liegenschaft.egrid));
        }

        if (sd.adressen != null && sd.adressen.Length > 0)
        {
            var s2 = CreateSubTitle(section, "Adresse(n)");
            foreach (var a in sd.adressen)
            {
                string line = $"{Safe(a.street, "")} {Safe(a.number, "")}".Trim();
                if (!string.IsNullOrEmpty(a.zip)) line = $"{line}, {a.zip}";
                AddRow(s2, "Adresse", Safe(line));
            }
        }

        if (!string.IsNullOrEmpty(sd.oerebUrl) || !string.IsNullOrEmpty(sd.oerebPortalUrl))
        {
            var s3 = CreateSubTitle(section, "ÖREB");
            if (!string.IsNullOrEmpty(sd.oerebUrl)) AddRow(s3, "PDF", sd.oerebUrl);
            if (!string.IsNullOrEmpty(sd.oerebPortalUrl)) AddRow(s3, "Portal", sd.oerebPortalUrl);
        }
    }

    private void RenderWmsPropertiesGrouped(AccordionSection section, WmsBuildingResult wms)
    {
        AccordionSection current = section;

        foreach (var p in wms.Properties)
        {
            if (IsEmpty(p.Value)) continue;

            if (string.Equals(p.Type, "title", StringComparison.OrdinalIgnoreCase))
            {
                current = CreateSubTitle(section, p.Label);
            }
            else
            {
                AddRow(current, p.Label, WmsValueToDisplay(p.Value));
            }
        }
    }

    private void RenderFlaechenblatt(AccordionSection section, GeoPortalFlaechenblattResponse fb)
    {
        var s1 = CreateSubTitle(section, "Liegenschaft");
        if (fb.Liegenschaft != null)
        {
            AddRow(s1, "Parzelle", Safe(fb.Liegenschaft.parznr));
            AddRow(s1, "EGRID", Safe(fb.Liegenschaft.egrid));
            AddRow(s1, "Gemeinde", Safe(fb.Liegenschaft.gemeinde));
            AddRow(s1, "Lokalname", Safe(fb.Liegenschaft.lokalname));
            AddRow(s1, "Adresse", Safe(fb.Liegenschaft.strasse));
            AddRow(s1, "Fläche (m²)", Safe(fb.Liegenschaft.flaeche));
            AddRow(s1, "Plan-Nr.", Safe(fb.Liegenschaft.plannr));
            AddRow(s1, "Kanton", Safe(fb.Liegenschaft.kanton));
            AddRow(s1, "Eigentumsform", Safe(fb.Liegenschaft.eigentumsform));
            AddRow(s1, "EigForm", Safe(fb.Liegenschaft.eigform));
            AddRow(s1, "Typ", Safe(fb.Liegenschaft.typ));
        }

        if (fb.zoneAreas != null && fb.zoneAreas.Length > 0)
        {
            var s2 = CreateSubTitle(section, "Zonen");
            foreach (var z in fb.zoneAreas)
                AddRow(s2, z.label, z.value.ToString(CultureInfo.InvariantCulture));
        }

        if (fb.slopeAreas != null && fb.slopeAreas.Length > 0)
        {
            var s3 = CreateSubTitle(section, "Hangneigung");
            foreach (var s in fb.slopeAreas)
                AddRow(s3, s.label, s.value.ToString(CultureInfo.InvariantCulture));
        }

        if (fb.Areal != null && fb.Areal.Length > 0)
        {
            var s4 = CreateSubTitle(section, "Areal / Teilflächen");
            foreach (var a in fb.Areal)
            {
                var label = string.IsNullOrEmpty(a.art) ? "Objekt" : a.art;
                var val = string.IsNullOrEmpty(a.flaeche) ? "" : $"{a.flaeche} m²";
                AddRow(s4, label, val);
            }
        }
    }

    // ---------------- section/row creation ----------------

    private AccordionSection CreateSection(string title, bool expanded)
    {
        var go = Instantiate(accordionSectionPrefab, contentRoot);
        var sec = go.GetComponent<AccordionSection>();
        sec.SetTitle(title);
        sec.SetExpanded(expanded);
        return sec;
    }

    private AccordionSection CreateSubTitle(AccordionSection parent, string subtitle)
    {
        var go = Instantiate(accordionSectionPrefab, parent.ContentRoot);
        var sec = go.GetComponent<AccordionSection>();
        sec.SubStyle = true; // optional: smaller style
        sec.SetTitle(subtitle);
        sec.SetExpanded(true);
        return sec;
    }

    private void AddRow(AccordionSection target, string label, string value)
    {
        var row = Instantiate(rowPrefab, target.ContentRoot);
        var ir = row.GetComponent<InfoRow>();
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