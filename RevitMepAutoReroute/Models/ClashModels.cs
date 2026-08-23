using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitMepAutomation.Models
{
    public class ClashInfo
    {
        public Element Obstacle { get; set; }
        
        /// <summary>
        /// Distance from the start of the target centerline curve (in internal feet).
        /// </summary>
        public double ParamStart { get; set; }

        /// <summary>
        /// Distance to the end of the clash volume along the target curve (in internal feet).
        /// </summary>
        public double ParamEnd { get; set; }

        /// <summary>
        /// Required vertical displacement (delta Z) to clear this obstacle.
        /// </summary>
        public double RequiredDeltaZ { get; set; }
    }

    public class ClashCluster
    {
        /// <summary>
        /// Start of transition (including slant length and straight safety buffer).
        /// </summary>
        public double ParamStart { get; set; }

        /// <summary>
        /// End of transition (including downward slant length and straight safety buffer).
        /// </summary>
        public double ParamEnd { get; set; }

        /// <summary>
        /// Maximum required vertical displacement across all grouped clashes.
        /// </summary>
        public double DeltaZ { get; set; }

        /// <summary>
        /// Individual clashes grouped inside this continuous bridge cluster.
        /// </summary>
        public List<ClashInfo> Clashes { get; set; } = new List<ClashInfo>();
    }

    public class MepNetworkNode
    {
        public MEPCurve MainCurve { get; set; }
        public FamilyInstance End0Fitting { get; set; }
        public FamilyInstance End1Fitting { get; set; }
        public List<MEPCurve> BranchCurves { get; set; } = new List<MEPCurve>();

        public static MepNetworkNode Build(Document doc, MEPCurve curve)
        {
            MepNetworkNode node = new MepNetworkNode { MainCurve = curve };

            LocationCurve lc = curve.Location as LocationCurve;
            if (lc == null) return node;

            XYZ p0 = lc.Curve.GetEndPoint(0);
            XYZ p1 = lc.Curve.GetEndPoint(1);

            foreach (Connector c in curve.ConnectorManager.Connectors)
            {
                if (c.IsConnected)
                {
                    foreach (Connector linked in c.AllRefs)
                    {
                        if (linked.Owner is FamilyInstance fi)
                        {
                            if (c.Origin.DistanceTo(p0) < 0.1) node.End0Fitting = fi;
                            if (c.Origin.DistanceTo(p1) < 0.1) node.End1Fitting = fi;

                            // Inspect other branches connected to this fitting (Tee / Cross)
                            if (fi.MEPModel != null && fi.MEPModel.ConnectorManager != null)
                            {
                                foreach (Connector fitConn in fi.MEPModel.ConnectorManager.Connectors)
                                {
                                    if (fitConn.IsConnected)
                                    {
                                        foreach (Connector refConn in fitConn.AllRefs)
                                        {
                                            if (refConn.Owner is MEPCurve bCurve && bCurve.Id != curve.Id)
                                            {
                                                if (!node.BranchCurves.Exists(x => x.Id == bCurve.Id))
                                                {
                                                    node.BranchCurves.Add(bCurve);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return node;
        }
    }
}
