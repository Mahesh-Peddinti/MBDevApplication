using System.Collections.Generic;

namespace TheResolver.DTOs
{
    public enum RoutePlane
    {
        Vertical,
        Horizontal
    }

    public enum LateralSlotType
    {
        InnerTop,
        InnerBottom,
        OuterTop,
        OuterBottom
    }

    public class RouteCandidateDTO
    {
        public string Name { get; set; }
        public RoutePlane Plane { get; set; }
        public LateralSlotType? SlotType { get; set; }
        public double LateralOffsetMm { get; set; }
        public double ElevationDeltaMm { get; set; }
        public double AddedLengthMm { get; set; }
        public double MinimumClearanceMm { get; set; }
        public List<Autodesk.Revit.DB.XYZ> Points { get; set; } = new List<Autodesk.Revit.DB.XYZ>();
    }
}