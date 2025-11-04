namespace Shared.Scripts.App
{
    public static class SelectedTargetContext
    {
        public static string Egid;
        public static string Name;
        public static string RawCoordinates; // LV95 string (optional)
        public static double? ElevationMeters;
        public static double Latitude;       // WGS84
        public static double Longitude;      // WGS84

        public static void Clear()
        {
            Egid = Name = RawCoordinates = null;
            Latitude = Longitude = 0;
            ElevationMeters = null;
        }
    }
}