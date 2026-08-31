using System.Collections.Generic;

namespace TheResolver.DTOs
{
    /// <summary>
    /// Pure (non-Revit) 2D schematic data describing a proposed bypass route,
    /// computed from a clash and the current route settings. Consumed by the
    /// pane to draw a side-view preview so the user can see the solution
    /// before accepting.
    /// </summary>
    public class PreviewRouteData
    {
        public bool IsValid { get; set; }

        public string Message { get; set; }

        public double TrayLengthMm { get; set; }

        public double ClashStationMinMm { get; set; }

        public double ClashStationMaxMm { get; set; }

        public double ClashMinElevationMm { get; set; }

        public double ClashMaxElevationMm { get; set; }

        public List<(double Station, double Elevation)> RoutePoints { get; set; }
            = new List<(double, double)>();

        public bool IsDetourUp { get; set; }

        public double RiseMm { get; set; }

        public double ClearanceMm { get; set; }
    }
}
