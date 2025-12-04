using UnityEngine;
using Shared.Scripts.Building;
using System.Collections.Generic;

public class ManualLv95Spawner : MonoBehaviour
{
    [SerializeField, TextArea(4, 12)] private string buildingCoordinatesLv95;
    [SerializeField] private CreateBuilding buildingFactory;
    [SerializeField] private Material floorMaterial;

    private void Awake()
    {
        if (!buildingFactory) buildingFactory = GetComponent<CreateBuilding>();
        if (!buildingFactory) buildingFactory = FindFirstObjectByType<CreateBuilding>();
    }

    private void Start()
    {
        if (string.IsNullOrWhiteSpace(buildingCoordinatesLv95) || !buildingFactory) return;

        buildingFactory.SetExtrusionHeight(5f);
        var building = buildingFactory.CreateBuildingFromCoordinates(buildingCoordinatesLv95, "Building", 0f);
        if (building != null && building.GameObject != null)
        {
            var go = building.GameObject;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
        }

        CreateFloorPlane();
    }

    private void CreateFloorPlane()
    {
        if (!BuildingGeometryUtils.TryParseLv95Loop(buildingCoordinatesLv95, out var points, out var areaSign))
            return;

        var (eastCentroid, northCentroid) = BuildingGeometryUtils.ComputeCentroid(points);

        var local2D = new List<Vector2>();
        foreach (var p in points)
        {
            local2D.Add(new Vector2(
                (float)(p.East - eastCentroid),
                (float)(p.North - northCentroid)));
        }

        var triangles = TriangulatePolygon(local2D);
        if (triangles.Count < 3)
        {
            triangles = new List<int>();
            for (int i = 1; i < local2D.Count - 1; i++)
            {
                triangles.Add(0);
                triangles.Add(i);
                triangles.Add(i + 1);
            }
        }

        var mesh = new Mesh { name = "FloorPlane" };
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();

        foreach (var v in local2D)
        {
            vertices.Add(new Vector3(v.x, 0.02f, v.y));
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(v.x, v.y)); 
        }

        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();

        var floorGo = new GameObject("FloorPlane");
        floorGo.transform.SetParent(transform, false);
        floorGo.transform.localPosition = Vector3.zero;

        var meshFilter = floorGo.AddComponent<MeshFilter>();
        meshFilter.mesh = mesh;

        var meshRenderer = floorGo.AddComponent<MeshRenderer>();
        meshRenderer.material = floorMaterial;
    }

    private static List<int> TriangulatePolygon(IReadOnlyList<Vector2> polygon)
    {
        var triangles = new List<int>();
        int n = polygon.Count;
        if (n < 3) return triangles;

        var indices = new List<int>(n);
        if (SignedArea(polygon) > 0f)
        {
            for (int i = 0; i < n; i++) indices.Add(i);
        }
        else
        {
            for (int i = n - 1; i >= 0; i--) indices.Add(i);
        }

        int guard = 0;
        while (indices.Count > 2 && guard < 4096)
        {
            bool earFound = false;
            for (int i = 0; i < indices.Count; i++)
            {
                int prevIndex = indices[(i - 1 + indices.Count) % indices.Count];
                int currIndex = indices[i];
                int nextIndex = indices[(i + 1) % indices.Count];

                if (IsEar(prevIndex, currIndex, nextIndex, polygon, indices))
                {
                    triangles.Add(prevIndex);
                    triangles.Add(currIndex);
                    triangles.Add(nextIndex);
                    indices.RemoveAt(i);
                    earFound = true;
                    break;
                }
            }

            if (!earFound) break;
            guard++;
        }

        return triangles;
    }

    private static bool IsEar(int prev, int current, int next, IReadOnlyList<Vector2> polygon, List<int> available)
    {
        Vector2 a = polygon[prev];
        Vector2 b = polygon[current];
        Vector2 c = polygon[next];

        if (Cross(b - a, c - b) <= 0f) return false;

        foreach (var idx in available)
        {
            if (idx == prev || idx == current || idx == next) continue;
            if (PointInTriangle(polygon[idx], a, b, c)) return false;
        }

        return true;
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        const float eps = 1e-5f;
        float area2 = Cross(b - a, c - a);
        if (Mathf.Abs(area2) < eps) return false;

        float d1 = Cross(b - a, p - a);
        float d2 = Cross(c - b, p - b);
        float d3 = Cross(a - c, p - c);

        bool hasNeg = (d1 < -eps) || (d2 < -eps) || (d3 < -eps);
        bool hasPos = (d1 > eps) || (d2 > eps) || (d3 > eps);

        return !(hasNeg && hasPos);
    }

    private static float SignedArea(IReadOnlyList<Vector2> polygon)
    {
        float area = 0f;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 p0 = polygon[i];
            Vector2 p1 = polygon[(i + 1) % polygon.Count];
            area += (p0.x * p1.y) - (p1.x * p0.y);
        }
        return area * 0.5f;
    }
    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
}
