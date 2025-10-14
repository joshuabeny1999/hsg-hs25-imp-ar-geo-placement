// Assets/Shared/Geo/ProjNetTransformCH.cs
// LV95 (EPSG:2056) <-> WGS84 (EPSG:4326) using NetTopologySuite.ProjNet 2.x
// Builds CRSs from WKT (CreateFromWkt), then transforms with MathTransform.

using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace Shared.Scripts.Geo
{
    public static class ProjNetTransformCH
    {
        private static readonly CoordinateSystemFactory CSF = new();
        private static readonly CoordinateTransformationFactory CTF = new();

        // --- WKT strings (from EPSG.io) ---
        // EPSG:2056 (CH1903+ / LV95) — ESRI/OGC WKT version
        // Source: https://epsg.io/2056  (WKT section)
        private const string LV95_WKT =
            "PROJCS[\"CH1903+ / LV95\",GEOGCS[\"CH1903+\",DATUM[\"CH1903+\",SPHEROID[\"Bessel 1841\",6377397.155,299.1528128],TOWGS84[674.374,15.056,405.346,0,0,0,0]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4150\"]],PROJECTION[\"Hotine_Oblique_Mercator_Azimuth_Center\"],PARAMETER[\"latitude_of_center\",46.9524055555556],PARAMETER[\"longitude_of_center\",7.43958333333333],PARAMETER[\"azimuth\",90],PARAMETER[\"rectified_grid_angle\",90],PARAMETER[\"scale_factor\",1],PARAMETER[\"false_easting\",2600000],PARAMETER[\"false_northing\",1200000],UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],AXIS[\"Easting\",EAST],AXIS[\"Northing\",NORTH],AUTHORITY[\"EPSG\",\"2056\"]]";
        
        // Build CRS objects from WKT (use 'var' to avoid type name differences between versions)
        private static readonly CoordinateSystem LV95 = CSF.CreateFromWkt(LV95_WKT);
        private static readonly CoordinateSystem WGS84 = GeographicCoordinateSystem.WGS84;

        // Pre-create transformations
        private static readonly ICoordinateTransformation ToWgs84 =
            CTF.CreateFromCoordinateSystems(LV95, WGS84);
        private static readonly ICoordinateTransformation ToLv95 =
            CTF.CreateFromCoordinateSystems(WGS84, LV95);

        /// <summary>
        /// LV95 (E,N meters) -> WGS84 (lat, lon degrees)
        /// </summary>
        public static void LV95ToWGS84(double east, double north, out double lat, out double lon)
        {
            double[] result = ToWgs84.MathTransform.Transform(new[] { east, north });
            lon = result[0];
            lat = result[1];
        }

        /// <summary>
        /// WGS84 (lat, lon degrees) -> LV95 (E, N meters)
        /// </summary>
        public static void WGS84ToLV95(double lat, double lon, out double east, out double north)
        {
            double[] result = ToLv95.MathTransform.Transform(new[] { lon, lat });
            east = result[0];
            north = result[1];
        }    
    }
}