using System.Collections.Generic;

namespace Shared.Scripts.App
{

    public static class ReceivedProjections
    {
        public static List<SelectedTargetContext> Buildings = new List<SelectedTargetContext>();
    }

    public static class CurrentSelectedProjection
    {
        public static SelectedTargetContext Building = new SelectedTargetContext();
    }

    public class SelectedTargetContext
    {
        public string Egid;
        public string Name;
        public string RawCoordinates; // LV95 string (optional)
        public double? ElevationMeters;
        public double Latitude;       // WGS84
        public double Longitude;      // WGS84

        public void Clear()
        {
            Egid = Name = RawCoordinates = null;
            Latitude = Longitude = 0;
            ElevationMeters = null;
        }
    }
}