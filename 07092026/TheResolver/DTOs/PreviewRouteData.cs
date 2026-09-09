using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace TheResolver.DTOs
{
    public class PreviewPoint
    {
        public double Station { get; set; }
        public double Elevation { get; set; }
        public XYZ Point { get; set; }
    }

    public class PreviewObstacleBox
    {
        public double StationMinMm { get; set; }
        public double StationMaxMm { get; set; }
        public double ElevationMinMm { get; set; }
        public double ElevationMaxMm { get; set; }
    }

    public class PreviewRouteData
    {
        public string Message { get; set; }
        public double TrayLengthMm { get; set; }
        public double ClashStationMinMm { get; set; }
        public double ClashStationMaxMm { get; set; }
        public double ClashMinElevationMm { get; set; }
        public double ClashMaxElevationMm { get; set; }
        public List<PreviewPoint> RoutePoints { get; set; } = new List<PreviewPoint>();
        public List<PreviewObstacleBox> SecondaryObstacles { get; set; } = new List<PreviewObstacleBox>();
        public bool IsDetourUp { get; set; }
        public double RiseMm { get; set; }
        public double ClearanceMm { get; set; }
        public bool IsValid => RoutePoints != null && RoutePoints.Count >= 2;



    }
}