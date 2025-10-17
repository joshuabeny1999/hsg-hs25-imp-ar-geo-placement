using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Shared.Scripts.Geo;

/// <summary>
/// Factory that turns Swiss LV95 coordinate strings into extruded building meshes.
/// Geometry is generated in local space; callers are responsible for positioning
/// the returned GameObject in world space.
/// </summary>
public class CreateBuilding : MonoBehaviour
{
    [Header("Rendering")]
    [SerializeField, Tooltip("Material assigned to every generated building mesh.")]
    private Material buildingMaterial;
    [SerializeField, Tooltip("Texture UV scale for the building top surface.")]
    private Vector2 uvScale = new Vector2(0.1f, 0.1f);
    [SerializeField, Tooltip("Extruded thickness applied to spawned buildings (meters).")]
    private float height = 1f;

  
    /// <summary>
    /// Creates a building purely from a coordinate loop ("x,y x,y ...") without additional metadata.
    /// The returned instance contains placement data; the caller must anchor the GameObject.
    /// </summary>
    public BuildingInstance CreateBuildingFromCoordinates(string coordinates, string name = "Manual", float? altitudeOverride = null, bool? clearExistingOverride = null)
    {
        if (!TryParseLv95Loop(coordinates, out var points, out var areaSign))
        {
            Debug.LogWarning("[CreateBuilding] Failed to parse coordinate loop for manual building creation.");
            return null;
        }

        float altitude = altitudeOverride ?? 0f;
        var feature = new BuildingFeature(name, points, areaSign);
        return SpawnBuilding(feature, altitude);
    }

    private BuildingInstance SpawnBuilding(BuildingFeature feature, float altitudeMeters)
    {
        if (feature.Points.Count < 3)
            return null;

        var (eastCentroid, northCentroid) = ComputeCentroid(feature.Points);
        ProjNetTransformCH.LV95ToWGS84(eastCentroid, northCentroid, out var lat, out var lon);

        var local2D = BuildLocalPolygon(feature.Points, eastCentroid, northCentroid);
        if (local2D.Count < 3)
        {
            Debug.LogWarning("[CreateBuilding] Polygon collapsed after localisation, skip.");
            return null;
        }

        var triangles = TriangulatePolygon(local2D);
        if (triangles.Count < 3)
        {
            Debug.LogWarning("[CreateBuilding] Failed to triangulate polygon — using triangle fan fallback.");
            triangles = TriangleFan(local2D.Count);
        }

        if (triangles.Count < 3)
            return null;

        float thickness = Mathf.Abs(height);
        var mesh = BuildThickMesh(local2D, triangles, thickness);

        var goName = string.IsNullOrWhiteSpace(feature.Name)
            ? "ProjectedBuilding"
            : $"ProjectedBuilding_{feature.Name}";

        var buildingGo = new GameObject(goName);
        buildingGo.transform.SetParent(transform, false);
        var meshFilter = buildingGo.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;

        var meshRenderer = buildingGo.AddComponent<MeshRenderer>();
        if (buildingMaterial)
        {
            meshRenderer.sharedMaterial = buildingMaterial;
        }

        var collider = buildingGo.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;

        buildingGo.transform.localScale = Vector3.one;

        Debug.Log($"[CreateBuilding] Spawned building {goName} with {feature.Points.Count} vertices at {lat:F6}, {lon:F6}");
        return new BuildingInstance(buildingGo, lat, lon, altitudeMeters);
    }

    private static List<Vector2> BuildLocalPolygon(IReadOnlyList<Lv95Point> points, double centroidEast, double centroidNorth)
    {
        var result = new List<Vector2>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            result.Add(new Vector2(
                (float)(p.East - centroidEast),
                (float)(p.North - centroidNorth)));
        }

        return result;
    }

    private Mesh BuildThickMesh(List<Vector2> polygon, List<int> triangles, float thickness)
    {
        int count = polygon.Count;
        float clampedThickness = Mathf.Abs(thickness);
        float halfThickness = clampedThickness * 0.5f;
        bool includeSides = clampedThickness > 0.0001f;
        float polygonArea = SignedArea(polygon);

        var vertices = new List<Vector3>(count * (includeSides ? 4 : 2));
        var normals = new List<Vector3>(vertices.Capacity);
        var uvs = new List<Vector2>(vertices.Capacity);
        var meshTriangles = new List<int>(triangles.Count * 2 + (includeSides ? count * 6 : 0));

        // Top surface (facing up)
        for (int i = 0; i < count; i++)
        {
            Vector2 p = polygon[i];
            vertices.Add(new Vector3(p.x, halfThickness, p.y));
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(p.x * uvScale.x, p.y * uvScale.y));
        }

        for (int i = 0; i < triangles.Count; i += 3)
        {
            int first = triangles[i];
            int second = triangles[i + 1];
            int third = triangles[i + 2];

            int topSecond = second;
            int topThird = third;

            Vector3 va = vertices[first];
            Vector3 vb = vertices[topSecond];
            Vector3 vc = vertices[topThird];
            if (Vector3.Cross(vb - va, vc - va).y <= 0f)
            {
                (topSecond, topThird) = (topThird, topSecond);
            }

            meshTriangles.Add(first);
            meshTriangles.Add(topSecond);
            meshTriangles.Add(topThird);
        }

        // Bottom surface (facing down)
        int bottomStart = vertices.Count;
        for (int i = 0; i < count; i++)
        {
            Vector2 p = polygon[i];
            vertices.Add(new Vector3(p.x, -halfThickness, p.y));
            normals.Add(Vector3.down);
            uvs.Add(new Vector2(p.x * uvScale.x, p.y * uvScale.y));
        }

        for (int i = 0; i < triangles.Count; i += 3)
        {
            int first = triangles[i];
            int second = triangles[i + 1];
            int third = triangles[i + 2];

            int bottomFirst = bottomStart + first;
            int bottomSecond = bottomStart + third;
            int bottomThird = bottomStart + second;

            Vector3 vba = vertices[bottomFirst];
            Vector3 vbb = vertices[bottomSecond];
            Vector3 vbc = vertices[bottomThird];
            if (Vector3.Cross(vbb - vba, vbc - vba).y >= 0f)
            {
                (bottomSecond, bottomThird) = (bottomThird, bottomSecond);
            }

            meshTriangles.Add(bottomFirst);
            meshTriangles.Add(bottomSecond);
            meshTriangles.Add(bottomThird);
        }

        if (includeSides)
        {
            float normalSign = polygonArea >= 0f ? 1f : -1f;

            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                Vector2 p0 = polygon[i];
                Vector2 p1 = polygon[next];
                Vector3 edge = new Vector3(p1.x - p0.x, 0f, p1.y - p0.y);
                if (edge.sqrMagnitude < 1e-8f)
                    continue;

                Vector3 normal = new Vector3(edge.z, 0f, -edge.x);
                if (normalSign < 0f)
                    normal = -normal;
                normal.Normalize();

                Vector3 v0Top = new Vector3(p0.x, halfThickness, p0.y);
                Vector3 v0Bottom = new Vector3(p0.x, -halfThickness, p0.y);
                Vector3 v1Top = new Vector3(p1.x, halfThickness, p1.y);
                Vector3 v1Bottom = new Vector3(p1.x, -halfThickness, p1.y);

                int baseIndex = vertices.Count;

                vertices.Add(v0Top);
                normals.Add(normal);
                uvs.Add(new Vector2(0f, 1f));

                vertices.Add(v0Bottom);
                normals.Add(normal);
                uvs.Add(new Vector2(0f, 0f));

                vertices.Add(v1Top);
                normals.Add(normal);
                uvs.Add(new Vector2(1f, 1f));

                vertices.Add(v1Bottom);
                normals.Add(normal);
                uvs.Add(new Vector2(1f, 0f));

                meshTriangles.Add(baseIndex);
                meshTriangles.Add(baseIndex + 2);
                meshTriangles.Add(baseIndex + 1);

                meshTriangles.Add(baseIndex + 2);
                meshTriangles.Add(baseIndex + 3);
                meshTriangles.Add(baseIndex + 1);
            }
        }

        var mesh = new Mesh { name = "ProjectedBuildingMesh" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(meshTriangles, 0);
        mesh.RecalculateBounds();

        return mesh;
    }

    private static List<int> TriangleFan(int vertexCount)
    {
        var list = new List<int>((vertexCount - 2) * 3);
        for (int i = 1; i < vertexCount - 1; i++)
        {
            list.Add(0);
            list.Add(i);
            list.Add(i + 1);
        }

        return list;
    }

    private static List<int> TriangulatePolygon(IReadOnlyList<Vector2> polygon)
    {
        var triangles = new List<int>();
        int n = polygon.Count;
        if (n < 3)
            return triangles;

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

            if (!earFound)
                break;

            guard++;
        }

        if (triangles.Count != (n - 2) * 3)
        {
            triangles.Clear();
        }

        return triangles;
    }

    private static bool IsEar(int prev, int current, int next, IReadOnlyList<Vector2> polygon, List<int> available)
    {
        Vector2 a = polygon[prev];
        Vector2 b = polygon[current];
        Vector2 c = polygon[next];

        if (Cross(b - a, c - b) <= 0f)
            return false; // reflex corner

        foreach (var idx in available)
        {
            if (idx == prev || idx == current || idx == next)
                continue;

            if (PointInTriangle(polygon[idx], a, b, c))
                return false;
        }

        return true;
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float denominator = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
        if (Mathf.Abs(denominator) < 1e-6f)
            return false;

        float w1 = ((b.y - c.y) * (p.x - c.x) + (c.x - b.x) * (p.y - c.y)) / denominator;
        float w2 = ((c.y - a.y) * (p.x - c.x) + (a.x - c.x) * (p.y - c.y)) / denominator;
        float w3 = 1f - w1 - w2;

        const float epsilon = -1e-5f;
        return w1 >= epsilon && w2 >= epsilon && w3 >= epsilon;
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

    private static (double East, double North) ComputeCentroid(IReadOnlyList<Lv95Point> polygon)
    {
        double area = 0d;
        double cx = 0d;
        double cy = 0d;

        for (int i = 0; i < polygon.Count; i++)
        {
            var p0 = polygon[i];
            var p1 = polygon[(i + 1) % polygon.Count];
            double cross = p0.East * p1.North - p1.East * p0.North;
            area += cross;
            cx += (p0.East + p1.East) * cross;
            cy += (p0.North + p1.North) * cross;
        }

        area *= 0.5d;
        if (Math.Abs(area) < 1e-6)
        {
            double meanEast = polygon.Average(p => p.East);
            double meanNorth = polygon.Average(p => p.North);
            return (meanEast, meanNorth);
        }

        double factor = 1.0 / (6.0 * area);
        return (cx * factor, cy * factor);
    }

    private static bool TryParseLv95Loop(string coordinates, out List<Lv95Point> points, out float areaSign)
    {
        points = new List<Lv95Point>();
        areaSign = 0f;

        if (string.IsNullOrWhiteSpace(coordinates))
            return false;

        var tokens = coordinates
            .Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

        foreach (var token in tokens)
        {
            var pair = token.Split(',');
            if (pair.Length != 2)
                continue;

            if (!double.TryParse(pair[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double east))
                continue;
            if (!double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double north))
                continue;

            points.Add(new Lv95Point(east, north));
        }

        if (points.Count < 3)
        {
            points.Clear();
            return false;
        }

        // Remove duplicate closing vertex if present
        var first = points[0];
        var last = points[^1];
        if (Math.Abs(first.East - last.East) < 0.001 && Math.Abs(first.North - last.North) < 0.001)
            points.RemoveAt(points.Count - 1);

        if (points.Count < 3)
        {
            points.Clear();
            return false;
        }

        areaSign = (float)ComputeSignedArea(points);

        return true;
    }

    private readonly struct Lv95Point
    {
        public readonly double East;
        public readonly double North;

        public Lv95Point(double east, double north)
        {
            East = east;
            North = north;
        }
    }

    private static double ComputeSignedArea(IReadOnlyList<Lv95Point> polygon)
    {
        double area = 0d;
        for (int i = 0; i < polygon.Count; i++)
        {
            var p0 = polygon[i];
            var p1 = polygon[(i + 1) % polygon.Count];
            area += p0.East * p1.North - p1.East * p0.North;
        }

        return area * 0.5d;
    }

    private readonly struct BuildingFeature
    {
        public readonly string Name;
        public readonly List<Lv95Point> Points;
        public readonly float PointsAreaSign;

        public BuildingFeature(string name, List<Lv95Point> points, float areaSign)
        {
            Name = name;
            Points = points;
            PointsAreaSign = areaSign;
        }
    }

    /// <summary>
    /// Result data for a generated building mesh. Contains the spawned GameObject and
    /// its geo-reference so that callers can anchor it in world space.
    /// </summary>
    public sealed class BuildingInstance
    {
        public GameObject GameObject { get; }
        public double Latitude { get; }
        public double Longitude { get; }
        public float AltitudeMeters { get; }
        public BuildingInstance(GameObject gameObject, double latitude, double longitude, float altitudeMeters)
        {
            GameObject = gameObject;
            Latitude = latitude;
            Longitude = longitude;
            AltitudeMeters = altitudeMeters;
        }
    }
}
