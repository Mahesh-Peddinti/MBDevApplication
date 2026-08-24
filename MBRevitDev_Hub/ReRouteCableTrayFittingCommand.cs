using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;


namespace MBRevitDev_Hub
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ReRouteCableTrayFittingCommand : IExternalCommand
    {
        // 500 mm converted to Revit Internal Units (Feet)
        private const double BREAK_RANGE_MM = 500.0;
        // Example vertical raise: 300 mm (can be passed dynamically via UI/Parameters)
        private const double VERTICAL_RAISE_MM = 300.0;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // 1. Prompt user to select an Elbow or Tee fitting
                Reference pickedRef = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new FittingSelectionFilter(),
                    "Select a Cable Tray Elbow or Tee fitting to re-route:");

                FamilyInstance fitting = doc.GetElement(pickedRef) as FamilyInstance;
                if (fitting == null)
                {
                    message = "Selected element is not a valid FamilyInstance.";
                    return Result.Failed;
                }

                double breakRange = UnitUtils.ConvertToInternalUnits(BREAK_RANGE_MM, UnitTypeId.Millimeters);
                double verticalRaise = UnitUtils.ConvertToInternalUnits(VERTICAL_RAISE_MM, UnitTypeId.Millimeters);

                using (Transaction trans = new Transaction(doc, "Re-route Cable Tray Fitting"))
                {
                    trans.Start();

                    ReRouteFitting(doc, fitting, breakRange, verticalRaise);

                    trans.Commit();
                }

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message + "\n" + ex.StackTrace;
                return Result.Failed;
            }
        }

        private void ReRouteFitting(Document doc, FamilyInstance fitting, double breakRange, double verticalOffset)
        {
            // A. Inspect Connectors and Connected Trays
            List<Connector> fittingConnectors = GetActiveConnectors(fitting);
            if (fittingConnectors.Count < 2 || fittingConnectors.Count > 3)
            {
                throw modernException("Fitting must be a 2-way (Elbow) or 3-way (Tee) fitting.");
            }

            XYZ fittingCenter = GetFittingCenterPoint(fitting, fittingConnectors);
            ElementId levelId = fitting.LevelId;

            // Map connected trays with their directions and endpoints
            List<ConnectedTrayInfo> trayInfoList = new List<ConnectedTrayInfo>();

            foreach (Connector fConn in fittingConnectors)
            {
                Connector connectedTrayConn = GetConnectedTrayConnector(fConn);
                if (connectedTrayConn == null)
                    continue;

                CableTray tray = connectedTrayConn.Owner as CableTray;
                LocationCurve locCurve = tray.Location as LocationCurve;
                Line line = locCurve.Curve as Line;

                // Determine unit vector pointing AWAY from the fitting along the tray line
                XYZ trayFarEnd = (line.GetEndPoint(0).DistanceTo(fConn.Origin) > line.GetEndPoint(1).DistanceTo(fConn.Origin))
                    ? line.GetEndPoint(0)
                    : line.GetEndPoint(1);

                XYZ outwardDir = (trayFarEnd - fConn.Origin).Normalize();
                XYZ breakPoint = fittingCenter + outwardDir * breakRange;

                trayInfoList.Add(new ConnectedTrayInfo
                {
                    OriginalTray = tray,
                    FittingConnector = fConn,
                    TrayNearConnector = connectedTrayConn,
                    OutwardDirection = outwardDir,
                    BreakPoint = breakPoint,
                    FarEndPoint = trayFarEnd,
                    TypeId = tray.GetTypeId(),
                    Width = tray.Width,
                    Height = tray.Height
                });
            }

            if (trayInfoList.Count < 2)
            {
                throw new InvalidOperationException("Could not resolve at least 2 connected cable tray runs.");
            }

            // B. Disconnect and Trim/Shorten Original Cable Trays to BreakPoint
            foreach (var info in trayInfoList)
            {
                // Disconnect from fitting
                if (info.FittingConnector.IsConnectedTo(info.TrayNearConnector))
                {
                    info.FittingConnector.DisconnectFrom(info.TrayNearConnector);
                }

                // Trim the existing tray from FarEndPoint to BreakPoint
                LocationCurve locCurve = info.OriginalTray.Location as LocationCurve;
                locCurve.Curve = Line.CreateBound(info.BreakPoint, info.FarEndPoint);
            }

            // C. Delete the old fitting
            ElementId oldFittingId = fitting.Id;
            doc.Delete(oldFittingId);

            // D. Build Raised Geometry & Segments
            XYZ raisedCenter = fittingCenter + new XYZ(0, 0, verticalOffset);
            List<CableTray> newRaisedArmTrays = new List<CableTray>();
            ElementId defaultTypeId = trayInfoList[0].TypeId;

            foreach (var info in trayInfoList)
            {
                XYZ raisedBreakPoint = info.BreakPoint + new XYZ(0, 0, verticalOffset);

                // 1. Create horizontal raised segment: from raisedBreakPoint towards raisedCenter
                CableTray raisedArm = CableTray.Create(doc, info.TypeId, raisedBreakPoint, raisedCenter, levelId);
                CopyTrayParameters(info.OriginalTray, raisedArm);
                newRaisedArmTrays.Add(raisedArm);

                // 2. If vertical offset != 0, create vertical riser transition
                if (Math.Abs(verticalOffset) > 1e-4)
                {
                    // Create vertical riser tray segment
                    CableTray verticalRiser = CableTray.Create(doc, info.TypeId, info.BreakPoint, raisedBreakPoint, levelId);
                    CopyTrayParameters(info.OriginalTray, verticalRiser);

                    // Connect Original Tray (at breakPoint) to Vertical Riser with an Elbow
                    Connector origTrayConnAtBreak = GetConnectorClosestTo(info.OriginalTray, info.BreakPoint);
                    Connector riserBottomConn = GetConnectorClosestTo(verticalRiser, info.BreakPoint);
                    doc.Create.NewElbowFitting(origTrayConnAtBreak, riserBottomConn);

                    // Connect Vertical Riser to Raised Arm with an Elbow
                    Connector riserTopConn = GetConnectorClosestTo(verticalRiser, raisedBreakPoint);
                    Connector raisedArmOuterConn = GetConnectorClosestTo(raisedArm, raisedBreakPoint);
                    doc.Create.NewElbowFitting(riserTopConn, raisedArmOuterConn);
                }
                else
                {
                    // Direct horizontal reconnect if no vertical offset
                    Connector origTrayConn = GetConnectorClosestTo(info.OriginalTray, info.BreakPoint);
                    Connector raisedArmOuterConn = GetConnectorClosestTo(raisedArm, info.BreakPoint);
                    doc.Create.NewElbowFitting(origTrayConn, raisedArmOuterConn);
                }
            }

            // E. Form the New Center Fitting (Elbow or Tee)
            if (newRaisedArmTrays.Count == 2)
            {
                // Elbow Reconstruction
                Connector connA = GetConnectorClosestTo(newRaisedArmTrays[0], raisedCenter);
                Connector connB = GetConnectorClosestTo(newRaisedArmTrays[1], raisedCenter);

                doc.Create.NewElbowFitting(connA, connB);
            }
            else if (newRaisedArmTrays.Count == 3)
            {
                // TEE Reconstruction: Identify Main Run (collinear) vs Branch
                IdentifyTeeRuns(newRaisedArmTrays, raisedCenter, out CableTray main1, out CableTray main2, out CableTray branch);

                Connector mainConn1 = GetConnectorClosestTo(main1, raisedCenter);
                Connector mainConn2 = GetConnectorClosestTo(main2, raisedCenter);
                Connector branchConn = GetConnectorClosestTo(branch, raisedCenter);

                doc.Create.NewTeeFitting(mainConn1, mainConn2, branchConn);
            }
        }

        #region Helper Geometry & Connector Routines

        private static XYZ GetFittingCenterPoint(FamilyInstance fitting, List<Connector> connectors)
        {
            // If location point exists and matches connector centroid, use location point
            if (fitting.Location is LocationPoint locPoint)
            {
                return locPoint.Point;
            }

            // Fallback: Average of connector origins
            XYZ sum = XYZ.Zero;
            foreach (var c in connectors) sum += c.Origin;
            return sum / connectors.Count;
        }

        private static List<Connector> GetActiveConnectors(FamilyInstance fitting)
        {
            var list = new List<Connector>();
            if (fitting.MEPModel?.ConnectorManager?.Connectors == null) return list;

            foreach (Connector c in fitting.MEPModel.ConnectorManager.Connectors)
            {
                if (c.ConnectorType == ConnectorType.End || c.ConnectorType == ConnectorType.Curve)
                {
                    list.Add(c);
                }
            }
            return list;
        }

        private static Connector GetConnectedTrayConnector(Connector fittingConnector)
        {
            if (!fittingConnector.IsConnected) return null;

            foreach (Connector other in fittingConnector.AllRefs)
            {
                if (other.Owner is CableTray && other.Owner.Id != fittingConnector.Owner.Id)
                {
                    return other;
                }
            }
            return null;
        }

        private static Connector GetConnectorClosestTo(CableTray tray, XYZ targetPt)
        {
            Connector closest = null;
            double minDist = double.MaxValue;

            foreach (Connector c in tray.ConnectorManager.Connectors)
            {
                double d = c.Origin.DistanceTo(targetPt);
                if (d < minDist)
                {
                    minDist = d;
                    closest = c;
                }
            }
            return closest;
        }

        private static void IdentifyTeeRuns(List<CableTray> trays, XYZ center,
            out CableTray main1, out CableTray main2, out CableTray branch)
        {
            // The two trays whose vectors into the center are opposite (Dot product close to -1) form the Main Run.
            double bestDot = 1.0;
            int idx1 = 0, idx2 = 1, branchIdx = 2;

            for (int i = 0; i < trays.Count; i++)
            {
                for (int j = i + 1; j < trays.Count; j++)
                {
                    XYZ dirI = GetDirectionTowardsCenter(trays[i], center);
                    XYZ dirJ = GetDirectionTowardsCenter(trays[j], center);

                    double dot = dirI.DotProduct(dirJ);
                    if (dot < bestDot) // Closest to -1.0
                    {
                        bestDot = dot;
                        idx1 = i;
                        idx2 = j;
                        branchIdx = 3 - (i + j); // Remaining index (0+1+2 = 3)
                    }
                }
            }

            main1 = trays[idx1];
            main2 = trays[idx2];
            branch = trays[branchIdx];
        }

        private static XYZ GetDirectionTowardsCenter(CableTray tray, XYZ center)
        {
            Line line = (tray.Location as LocationCurve).Curve as Line;
            XYZ p0 = line.GetEndPoint(0);
            XYZ p1 = line.GetEndPoint(1);

            XYZ nearPt = (p0.DistanceTo(center) < p1.DistanceTo(center)) ? p0 : p1;
            XYZ farPt = (nearPt == p0) ? p1 : p0;

            return (nearPt - farPt).Normalize();
        }

        private static void CopyTrayParameters(CableTray source, CableTray target)
        {
            target.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.Set(source.Width);
            target.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.Set(source.Height);

            Parameter srcService = source.get_Parameter(BuiltInParameter.RBS_SERVICE_TYPE_PARAM);
            Parameter tgtService = target.get_Parameter(BuiltInParameter.RBS_SERVICE_TYPE_PARAM);
            if (srcService != null && tgtService != null && !tgtService.IsReadOnly)
            {
                tgtService.Set(srcService.AsString());
            }
        }

        private static Exception modernException(string msg) => new InvalidOperationException(msg);

        #endregion
    }

    public class ConnectedTrayInfo
    {
        public CableTray OriginalTray { get; set; }
        public Connector FittingConnector { get; set; }
        public Connector TrayNearConnector { get; set; }
        public XYZ OutwardDirection { get; set; }
        public XYZ BreakPoint { get; set; }
        public XYZ FarEndPoint { get; set; }
        public ElementId TypeId { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    public class FittingSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem is FamilyInstance fi && fi.Category != null)
            {
                return fi.Category.Id.Value == (int)BuiltInCategory.OST_CableTrayFitting;
            }
            return false;
        }

        public bool AllowReference(Reference reference, XYZ position) => true;
    }
}
