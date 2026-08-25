using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitMepAutomation.Models
{
    public class CableTrayClashInfo
    {
        public Element Obstacle { get; set; }
        public XYZ ClashPoint3D { get; set; }
        public double ParamCenter { get; set; }
        public double ObstacleWidth { get; set; }
        public double ObstacleTopZ { get; set; }
        public double RequiredDeltaZ { get; set; }
    }

    public class CableTrayClashCluster
    {
        public double FirstClashParam { get; set; }
        public double LastClashParam { get; set; }
        public double ObstacleTotalWidth { get; set; }
        public double MaxDeltaZ { get; set; }
        public List<CableTrayClashInfo> Clashes { get; set; } = new List<CableTrayClashInfo>();
    }

    public class TrayPointSet
    {
        public XYZ P1 { get; set; } // Inside Bend vertex
        public XYZ P2 { get; set; } // Outside Bend vertex (Elevated start)
        public XYZ P3 { get; set; } // Outside Bend vertex (Elevated end)
        public XYZ P4 { get; set; } // Inside Bend vertex

        public double DeltaZ { get; set; }
        public double CalculatedAngleDeg { get; set; }
        public double FittingTakeoffFeet { get; set; }
        public double SlantStraightGapMm { get; set; }
        public double BridgeStraightGapMm { get; set; }
    }

    public class AttachedFittingInfo
    {
        public FamilyInstance Fitting { get; set; }
        public Connector FittingConnector { get; set; }
        public int TrayEndIndex { get; set; }
    }
}
