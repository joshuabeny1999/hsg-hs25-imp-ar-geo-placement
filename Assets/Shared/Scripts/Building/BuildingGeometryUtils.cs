using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Shared.Scripts.Geo;
using UnityEngine;

namespace Shared.Scripts.Building
{
    public static class BuildingGeometryUtils
    {
        // ---- Public helpers you (and other code) can call ----

        /// <summary>
        /// From raw "east,north east,north ..." string → centroid in LV95 (east, north).
        /// </summary>
        public static bool TryCentroidLV95(string coordinates, out double east, out double north)
        {
            east = north = 0;

            if (string.IsNullOrWhiteSpace(coordinates))
            {
                Debug.LogWarning("[BuildingGeometryUtils] TryCentroidLV95: coordinates string is null/empty.");
                return false;
            }

            if (!TryParseLv95Loop(coordinates, out var points, out _))
            {
                Debug.LogWarning("[BuildingGeometryUtils] TryCentroidLV95: failed to parse LV95 loop.");
                return false;
            }

            var (e, n) = ComputeCentroid(points);

            if (double.IsNaN(e) || double.IsNaN(n))
            {
                Debug.LogWarning("[BuildingGeometryUtils] TryCentroidLV95: centroid computation returned NaN.");
                return false;
            }

            east = e;
            north = n;

            Debug.Log($"[BuildingGeometryUtils] TryCentroidLV95: points={points.Count} -> E={east:F2}, N={north:F2}");
            return true;
        }

        /// <summary>
        /// From raw "east,north ..." string → centroid in WGS84 (lat, lon).
        /// </summary>
        public static bool TryCentroidWGS84(string coordinates, out double lat, out double lon)
        {
            lat = lon = 0;

            if (!TryCentroidLV95(coordinates, out var east, out var north))
            {
                Debug.LogWarning("[BuildingGeometryUtils] TryCentroidWGS84: LV95 centroid failed.");
                return false;
            }

            // ProjNetTransformCH.LV95ToWGS84 signature in your project is (east, north, out lat, out lon)
            ProjNetTransformCH.LV95ToWGS84(east, north, out lat, out lon);

            Debug.Log($"[BuildingGeometryUtils] TryCentroidWGS84: E={east:F2}, N={north:F2} -> lat={lat:F6}, lon={lon:F6}");
            return true;
        }

        /// <summary>
        /// Parse LV95 loop string into points and compute signed area sign.
        /// </summary>
        public static bool TryParseLv95Loop(string coordinates, out List<Lv95Point> points, out float areaSign)
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

        /// <summary>
        /// Compute polygon centroid (LV95) from parsed points.
        /// </summary>
        public static (double East, double North) ComputeCentroid(IReadOnlyList<Lv95Point> polygon)
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

        /// <summary>
        /// Compute signed area (LV95) for orientation tests.
        /// </summary>
        public static double ComputeSignedArea(IReadOnlyList<Lv95Point> polygon)
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

        // ---- Types you’ll move from CreateBuilding ----
        public readonly struct Lv95Point
        {
            public readonly double East;
            public readonly double North;

            public Lv95Point(double east, double north)
            {
                East = east;
                North = north;
            }
        }
    }
}