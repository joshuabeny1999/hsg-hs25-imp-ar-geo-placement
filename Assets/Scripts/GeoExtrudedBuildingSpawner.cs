using System;
using System.Collections.Generic;
using UnityEngine;
using Niantic.Lightship.AR.WorldPositioning; // <-- WPS helper (your sample)

// ---------------------
// Basic data & helpers
// ---------------------
[Serializable]
public struct GeoVertex
{
    public double latitude;   // WGS84
    public double longitude;  // WGS84
}

public static class GeoUtils
{
    public struct Vector2d { public double x, y; public Vector2d(double x, double y){ this.x=x; this.y=y; } public static implicit operator Vector2(Vector2d v)=>new Vector2((float)v.x,(float)v.y); }

    public static Vector2d LatLonToMetersOffset(double lat0, double lon0, double lat, double lon)
    {
        double latRad = lat0 * Math.PI / 180.0;
        const double mPerDegLat = 111_320.0;
        double mPerDegLon = Math.Cos(latRad) * 111_320.0;

        double dLat = lat - lat0;
        double dLon = lon - lon0;
        return new Vector2d(dLon * mPerDegLon, dLat * mPerDegLat); // (East, North)
    }
}

public static class PolygonTriangulator
{
    public static int[] Triangulate(IList<Vector2> poly)
    {
        var indices = new List<int>();
        int n = poly.Count; if (n < 3) return indices.ToArray();

        var V = new List<int>(n);
        if (Area(poly) > 0) { for (int v = 0; v < n; v++) V.Add(v); }
        else { for (int v = 0; v < n; v++) V.Add((n - 1) - v); }

        int nv = n, count = 2 * nv, vtx = nv - 1;
        while (nv > 2)
        {
            if ((count--) <= 0) break;
            int u = vtx; if (nv <= u) u = 0;
            vtx = u + 1; if (nv <= vtx) vtx = 0;
            int w = vtx + 1; if (nv <= w) w = 0;
            if (Snip(poly, u, vtx, w, nv, V))
            {
                int a = V[u], b = V[vtx], c = V[w];
                indices.Add(a); indices.Add(b); indices.Add(c);
                V.RemoveAt(vtx); nv--; count = 2 * nv;
            }
        }
        return indices.ToArray();
    }

    static float Area(IList<Vector2> poly)
    {
        int n = poly.Count; float A = 0;
        for (int p = n - 1, q = 0; q < n; p = q++)
            A += poly[p].x * poly[q].y - poly[q].x * poly[p].y;
        return A * 0.5f;
    }

    static bool Snip(IList<Vector2> poly, int u, int v, int w, int nv, List<int> V)
    {
        Vector2 A = poly[V[u]], B = poly[V[v]], C = poly[V[w]];
        if (Mathf.Epsilon > (((B.x - A.x) * (C.y - A.y)) - ((B.y - A.y) * (C.x - A.x)))) return false;
        for (int p = 0; p < nv; p++)
        {
            if (p == u || p == v || p == w) continue;
            Vector2 P = poly[V[p]];
            if (InsideTriangle(A, B, C, P)) return false;
        }
        return true;
    }

    static bool InsideTriangle(Vector2 A, Vector2 B, Vector2 C, Vector2 P)
    {
        float ax = C.x - B.x, ay = C.y - B.y;
        float bx = A.x - C.x, by = A.y - C.y;
        float cx = B.x - A.x, cy = B.y - A.y;
        float apx = P.x - A.x, apy = P.y - A.y;
        float bpx = P.x - B.x, bpy = P.y - B.y;
        float cpx = P.x - C.x, cpy = P.y - C.y;
        float aCROSSbp = ax * bpy - ay * bpx;
        float cCROSSap = cx * apy - cy * apx;
        float bCROSScp = bx * cpy - by * cpx;
        return (aCROSSbp >= 0f) && (bCROSScp >= 0f) && (cCROSSap >= 0f);
    }
}

// ---------------------
// The MonoBehaviour
// ---------------------
public class GeoExtrudedBuildingSpawner : MonoBehaviour
{
    [Header("WPS helper (assign in Inspector)")]
    [SerializeField] private ARWorldPositioningObjectHelper positioningHelper;

    [Header("Geo-Eckpunkte (Uhrzeigersinn, WGS84)")]
    public List<GeoVertex> vertices = new List<GeoVertex>();

    [Header("WPS placement")]
    [Tooltip("If true, altitude=0 will be camera-relative (easiest for first tests).")]
    public bool cameraRelativeAltitude = true;
    [Tooltip("Absolute altitude for WPS if cameraRelativeAltitude=false.")]
    public double altitudeMeters = 0.0;

    [Header("Visual Offsets")]
    public float heightOffsetMeters = 0.0f;  // lift whole footprint above ground a bit

    [Header("Gebäudehöhe")]
    public float buildingHeightMeters = 3.0f; // default height; can be changed via UI

    [Header("Materialien")]
    public Material roofMaterial;   // top cap
    public Material wallMaterial;   // walls

    [Header("Debug")]
    public bool logInfo = true;

    // internal
    private Transform _anchorRoot;
    private GameObject _roofGO, _wallsGO;
    private List<Vector2> _local2D;
    private bool _ready = false;

    private void Awake()
    {
        // If not assigned, try to find one in scene
        if (positioningHelper == null)
            positioningHelper = FindObjectOfType<ARWorldPositioningObjectHelper>();

        if (positioningHelper == null)
        {
            // Create one if still missing
            var go = new GameObject("ARWorldPositioningObjectHelper (Auto)");
            positioningHelper = go.AddComponent<ARWorldPositioningObjectHelper>();
        }
    }

    private void Start()
    {
        if (vertices == null || vertices.Count < 3)
        {
            Debug.LogError("[GeoExtrudedBuildingSpawner] Need at least 3 vertices.");
            return;
        }

        // 1) Create anchor object and let WPS place it at (lat,lon,alt)
        var anchorGO = new GameObject("WPS_Anchor");
        _anchorRoot = anchorGO.transform;

        double alt = cameraRelativeAltitude ? 0.0 : altitudeMeters;
        // rotation: Unity X=East, Y=Up, Z=North is fine → identity
        positioningHelper.AddOrUpdateObject(anchorGO, vertices[0].latitude, vertices[0].longitude, alt, Quaternion.identity);

        // 2) Build local 2D (meter) coords relative to first vertex
        var origin = vertices[0];
        _local2D = new List<Vector2>(vertices.Count);
        foreach (var v in vertices)
        {
            var m = GeoUtils.LatLonToMetersOffset(origin.latitude, origin.longitude, v.latitude, v.longitude);
            _local2D.Add((Vector2)m);
        }

        // 3) Create child renderers (top + walls)
        _roofGO  = new GameObject("BuildingTop");
        _wallsGO = new GameObject("BuildingWalls");
        _roofGO.transform.SetParent(_anchorRoot, false);
        _wallsGO.transform.SetParent(_anchorRoot, false);

        _roofGO.transform.localPosition  = new Vector3(0f, heightOffsetMeters + buildingHeightMeters, 0f);
        _wallsGO.transform.localPosition = new Vector3(0f, heightOffsetMeters, 0f);

        _roofGO.AddComponent<MeshFilter>();
        _roofGO.AddComponent<MeshRenderer>();
        _wallsGO.AddComponent<MeshFilter>();
        _wallsGO.AddComponent<MeshRenderer>();

        if (roofMaterial) _roofGO.GetComponent<MeshRenderer>().sharedMaterial  = roofMaterial;
        if (wallMaterial) _wallsGO.GetComponent<MeshRenderer>().sharedMaterial = wallMaterial;

        RebuildMeshes();
        _ready = true;

        if (logInfo) Debug.Log("[GeoExtrudedBuildingSpawner] WPS active, meshes built.");
    }

    // Public API for a UI slider
    public void SetBuildingHeightMeters(float newHeight)
    {
        buildingHeightMeters = Mathf.Max(0f, newHeight);
        if (_ready) RebuildMeshes();
    }

    private void RebuildMeshes()
    {
        if (_local2D == null || _local2D.Count < 3) return;

        // ----- Top mesh (cap) -----
        var topMesh  = new Mesh();
        var topVerts = new Vector3[_local2D.Count];
        for (int i = 0; i < _local2D.Count; i++)
            topVerts[i] = new Vector3(_local2D[i].x, 0f, _local2D[i].y);

        int[] topTris = PolygonTriangulator.Triangulate(_local2D);

        var topUV = new Vector2[topVerts.Length];
        for (int i = 0; i < topUV.Length; i++)
            topUV[i] = new Vector2(topVerts[i].x, topVerts[i].z) * 0.1f;

        topMesh.vertices  = topVerts;
        topMesh.triangles = topTris;
        topMesh.uv        = topUV;
        topMesh.RecalculateNormals();
        topMesh.RecalculateBounds();

        _roofGO.GetComponent<MeshFilter>().sharedMesh = topMesh;
        _roofGO.transform.localPosition = new Vector3(0f, heightOffsetMeters + buildingHeightMeters, 0f);

        // ----- Wall mesh (quads per edge) -----
        var wallMesh = new Mesh();
        int n = _local2D.Count;

        var wVerts = new List<Vector3>(n * 4);
        var wUV    = new List<Vector2>(n * 4);
        var wTris  = new List<int>(n * 6);

        int baseIndex = 0;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Vector2 A2 = _local2D[i];
            Vector2 B2 = _local2D[j];

            Vector3 A0 = new Vector3(A2.x, 0f, A2.y);
            Vector3 B0 = new Vector3(B2.x, 0f, B2.y);
            Vector3 A1 = new Vector3(A2.x, buildingHeightMeters, A2.y);
            Vector3 B1 = new Vector3(B2.x, buildingHeightMeters, B2.y);

            // outward-facing quad
            wVerts.Add(A0); wVerts.Add(B0); wVerts.Add(B1); wVerts.Add(A1);

            float edgeLen = Vector3.Distance(A0, B0);
            wUV.Add(new Vector2(0, 0));
            wUV.Add(new Vector2(edgeLen, 0));
            wUV.Add(new Vector2(edgeLen, buildingHeightMeters));
            wUV.Add(new Vector2(0, buildingHeightMeters));

            wTris.Add(baseIndex + 0);
            wTris.Add(baseIndex + 1);
            wTris.Add(baseIndex + 2);
            wTris.Add(baseIndex + 0);
            wTris.Add(baseIndex + 2);
            wTris.Add(baseIndex + 3);

            baseIndex += 4;
        }

        wallMesh.SetVertices(wVerts);
        wallMesh.SetTriangles(wTris, 0);
        wallMesh.SetUVs(0, wUV);
        wallMesh.RecalculateNormals();
        wallMesh.RecalculateBounds();

        _wallsGO.GetComponent<MeshFilter>().sharedMesh = wallMesh;

        if (logInfo)
            Debug.Log($"[GeoExtrudedBuildingSpawner] Rebuilt | Top V:{topVerts.Length} Tris:{topTris.Length/3} | Walls:{n} quads");
    }
}