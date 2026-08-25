using System;

namespace RevitMepAutomation.Models
{
    public class ClashItemModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public ClashStatus Status { get; set; } = ClashStatus.NotResolved;
        
        // Custom Parameters (in metric mm / degrees for intuitive UI display)
        public double BendRadiusMm { get; set; } = 300.0;
        public double BendAngleDeg { get; set; } = 45.0;
        public double OffsetSpanMm { get; set; } = 500.0;
        public double TopClearanceMm { get; set; } = 50.0;
        public RoutingDirection Direction { get; set; } = RoutingDirection.Up;

        // BIM / Revit Context
        public int ElementId { get; set; }
        public int ObstacleId { get; set; }
        public string ElementCategory { get; set; } = "Cable Tray";
        public string ObstacleCategory { get; set; } = "HVAC Duct";
        public string ElementSize { get; set; } = "300 x 100 mm";
        public string ObstacleSize { get; set; } = "600 x 300 mm";
        
        // Coordinates for Revit API view/zoom
        public double ClashX { get; set; }
        public double ClashY { get; set; }
        public double ClashZ { get; set; }
        
        public double BoundingBoxMinX { get; set; }
        public double BoundingBoxMinY { get; set; }
        public double BoundingBoxMinZ { get; set; }
        public double BoundingBoxMaxX { get; set; }
        public double BoundingBoxMaxY { get; set; }
        public double BoundingBoxMaxZ { get; set; }

        public string Details { get; set; } = string.Empty;

        public ClashItemModel Clone()
        {
            return (ClashItemModel)this.MemberwiseClone();
        }
    }
}
