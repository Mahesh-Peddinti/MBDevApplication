using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System.Windows.Controls;
using TheResolver.DTOs;
using TheResolver.Utilities;


namespace TheResolver
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ReRouteCableTrayFittingCommand : IExternalCommand
    {
        // 500 mm converted to Revit Internal Units (Feet)
        private const double BREAK_RANGE_MM = 500.0;
        // Example vertical raise: 300 mm (can be passed dynamically via UI/Parameters)
        private const double VERTICAL_RAISE_MM = 300.0;
        // ------------------------------------------------------------
        // UNIT CONVERSION
        // Revit internal length unit = feet
        // ------------------------------------------------------------
        private const double MM = 1.0 / 304.8;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            RouteSettingDTOs settings = new RouteSettingDTOs();

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
                // ----------------------------------------------------
                // CREATE CABLE TRAY SETTINGS
                // ----------------------------------------------------
                settings = new RouteSettingDTOs()
                               {
                                   BendAngle = 45,

                                   BendRadius = 100 * MM,

                                   MinimumClearance = 180 * MM,

                                   MinimumSideOffset = 150 * MM,

                                   BendSafetyfactor = 1.05 * MM

                               };

                using (Transaction trans = new Transaction(doc, "Re-route Cable Tray Fitting"))
                {
                    trans.Start();

                    ReRouteFitting(doc, fitting, breakRange, verticalRaise,settings);

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

        private void ReRouteFitting(
                        Document doc, 
                        FamilyInstance fitting, 
                        double breakRange, 
                        double verticalOffset,
                        RouteSettingDTOs settings)
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
                                ? line.GetEndPoint(0): line.GetEndPoint(1);

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
                CableTray raisedArm = CableTray.Create( doc,
                                                        info.TypeId, 
                                                        raisedBreakPoint, 
                                                        raisedCenter, 
                                                        levelId);
                //Set Cable Tray Orintation
                XYZ originalDirection = (info.FarEndPoint - info.BreakPoint).Normalize();

                XYZ originalNormal = info.OriginalTray.CurveNormal;

                if (originalNormal == null || originalNormal.GetLength() < 1e-6)
                {
                    originalNormal = XYZ.BasisZ;
                }

                info.OriginalDirection = originalDirection;

                info.OriginalNormal = originalNormal;

                if (raisedArm == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to create raised arm for tray " +
                        $"{info.OriginalTray.Id}");
                }

                CopyTrayParameters(
                    info.OriginalTray,
                    raisedArm);

                //--------------------------------------------------
                // SET CROSS-SECTION ORIENTATION
                //--------------------------------------------------

                SetCableTraySegmentOrientation(
                    raisedArm,
                    raisedBreakPoint,
                    raisedCenter,
                    info.OriginalDirection,
                    info.OriginalNormal);

                newRaisedArmTrays.Add(raisedArm);

                // 2. If vertical offset != 0, create vertical riser transition
                if (Math.Abs(verticalOffset) > 1e-4)
                {
                    // Create vertical riser tray segment
                    CableTray verticalRiser =
                                    CableTray.Create(
                                        doc,
                                        info.TypeId,
                                        info.BreakPoint,
                                        raisedBreakPoint,
                                        levelId);

                    if (verticalRiser == null)
                    {
                        throw new InvalidOperationException(
                            $"Failed to create vertical riser for tray " +
                            $"{info.OriginalTray.Id}");
                    }

                    CopyTrayParameters(
                        info.OriginalTray,
                        verticalRiser);

                    //--------------------------------------------------
                    // VERTICAL SEGMENT ORIENTATION
                    //--------------------------------------------------

                    SetCableTraySegmentOrientation(
                        verticalRiser,
                        info.BreakPoint,
                        raisedBreakPoint,
                        info.OriginalDirection,
                        info.OriginalNormal);

                    // Connect Original Tray (at breakPoint) to Vertical Riser with an Elbow
                    Connector origTrayConnAtBreak = GetConnectorClosestTo(info.OriginalTray, info.BreakPoint);
                    Connector riserBottomConn = GetConnectorClosestTo(verticalRiser, info.BreakPoint);
                    try
                    {
                        doc.Create.NewElbowFitting(origTrayConnAtBreak, riserBottomConn);
                        SetFittingParameters(doc, origTrayConnAtBreak, riserBottomConn, settings);

                    }
                    catch (Exception ex )
                    {

                        TaskDialog.Show("Revit",$"{ ex.Message}");
                    }
                    

                    // Connect Vertical Riser to Raised Arm with an Elbow
                    Connector riserTopConn = GetConnectorClosestTo(verticalRiser, raisedBreakPoint);
                    Connector raisedArmOuterConn = GetConnectorClosestTo(raisedArm, raisedBreakPoint);
                    //doc.Create.NewElbowFitting(riserTopConn, raisedArmOuterConn);
                    try
                    {
                        doc.Create.NewElbowFitting(riserTopConn, raisedArmOuterConn);
                        SetFittingParameters(doc, riserTopConn, raisedArmOuterConn, settings);

                    }
                    catch (Exception ex)
                    {

                        TaskDialog.Show("Revit", $"{ex.Message}");
                    }
                }
                else
                {
                    // Direct horizontal reconnect if no vertical offset
                    Connector origTrayConn = GetConnectorClosestTo(info.OriginalTray, info.BreakPoint);
                    Connector raisedArmOuterConn = GetConnectorClosestTo(raisedArm, info.BreakPoint);
                    
                    try
                    {
                        doc.Create.NewElbowFitting(origTrayConn, raisedArmOuterConn);
                        SetFittingParameters(doc, origTrayConn, raisedArmOuterConn, settings);

                    }
                    catch (Exception ex)
                    {

                        TaskDialog.Show("Revit", $"{ex.Message}");
                    }
                }
            }

            // E. Form the New Center Fitting (Elbow or Tee)
            if (newRaisedArmTrays.Count == 2)
            {
                // Elbow Reconstruction
                Connector connA = GetConnectorClosestTo(newRaisedArmTrays[0], raisedCenter);
                Connector connB = GetConnectorClosestTo(newRaisedArmTrays[1], raisedCenter);

                //doc.Create.NewElbowFitting(connA, connB);
                try
                {
                    doc.Create.NewElbowFitting(connA, connB);
                    SetFittingParameters(doc, connA, connB, settings);

                }
                catch (Exception ex)
                {

                    TaskDialog.Show("Revit", $"{ex.Message}");
                }
            }
            else if (newRaisedArmTrays.Count == 3)
            {
                // TEE Reconstruction: Identify Main Run (collinear) vs Branch
                IdentifyTeeRuns(newRaisedArmTrays, raisedCenter, out CableTray main1, out CableTray main2, out CableTray branch);

                Connector mainConn1 = GetConnectorClosestTo(main1, raisedCenter);
                Connector mainConn2 = GetConnectorClosestTo(main2, raisedCenter);
                Connector branchConn = GetConnectorClosestTo(branch, raisedCenter);

                //doc.Create.NewTeeFitting(mainConn1, mainConn2, branchConn);
            }
            else if (newRaisedArmTrays.Count == 4)
            {
                //--------------------------------------------------
                // CROSS
                //--------------------------------------------------

                CreateCrossFitting(
                    doc,
                    newRaisedArmTrays,
                    raisedCenter);
            }
        }

        #region Helper Geometry & Connector Routines

        private static FamilyInstance CreateCrossFitting(
                                            Document doc,
                                            List<CableTray> trays,
                                            XYZ center)
        {
            if (trays == null || trays.Count != 4)
            {
                throw new InvalidOperationException(
                    "Cable Tray Cross requires exactly 4 tray segments.");
            }

            //--------------------------------------------------
            // IMPORTANT:
            // Regenerate before reading final connectors.
            //--------------------------------------------------

            doc.Regenerate();

            List<Connector> connectors =
                new List<Connector>();

            foreach (CableTray tray in trays)
            {
                if (tray == null || !tray.IsValidObject)
                    continue;

                Connector connector =
                    GetConnectorClosestTo(
                        tray,
                        center);

                if (connector == null)
                {
                    throw new InvalidOperationException(
                        $"Could not find center connector " +
                        $"for tray {tray.Id}.");
                }

                connectors.Add(connector);
            }

            if (connectors.Count != 4)
            {
                throw new InvalidOperationException(
                    $"Expected 4 Cross connectors, " +
                    $"but found {connectors.Count}.");
            }

            //--------------------------------------------------
            // DEBUG
            //--------------------------------------------------

            for (int i = 0; i < connectors.Count; i++)
            {
                Connector c = connectors[i];

                Logger.Log(
                    $"Cross Connector {i} : " +
                    $"Origin={c.Origin}, " +
                    $"Direction={c.CoordinateSystem.BasisZ}");
            }

            //--------------------------------------------------
            // ORDER CONNECTORS
            //
            // Find two pairs of opposite connectors.
            //--------------------------------------------------

            List<ConnectorPair> pairs =
                FindOppositeConnectorPairs(connectors);

            if (pairs.Count != 2)
            {
                throw new InvalidOperationException(
                    "Could not determine two opposite connector " +
                    "pairs for Cable Tray Cross.");
            }

            Connector c1 = pairs[0].First;

            Connector c2 = pairs[1].First;

            Connector c3 = pairs[0].Second;

            Connector c4 = pairs[1].Second;

            //--------------------------------------------------
            // CREATE CROSS
            //--------------------------------------------------

            FamilyInstance cross =
                doc.Create.NewCrossFitting(
                                    c1,
                                    c2,
                                    c3,
                                    c4);

            if (cross == null)
            {
                throw new InvalidOperationException(
                    "Revit failed to create Cable Tray Cross fitting.");
            }

            Logger.Log(
                $"Created Cable Tray Cross {cross.Id} " +
                $"at {center}");

            return cross;
        }
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


        private static void SetCableTraySegmentOrientation(
                                    CableTray tray,
                                    XYZ start,
                                    XYZ end,
                                    XYZ referenceDirection,
                                    XYZ referenceNormal)
        {
            if (tray == null || !tray.IsValidObject)
                return;

            XYZ direction = end - start;

            if (direction.GetLength() < 1e-9)
                return;

            direction = direction.Normalize();

            //--------------------------------------------------
            // Project original tray normal onto plane
            // perpendicular to new segment direction.
            //--------------------------------------------------

            XYZ normal =
                referenceNormal -
                direction.Multiply(
                    referenceNormal.DotProduct(direction));

            //--------------------------------------------------
            // If original normal is unsuitable,
            // use original tray direction.
            //--------------------------------------------------

            if (normal.GetLength() < 1e-6)
            {
                normal =
                    referenceDirection -
                    direction.Multiply(
                        referenceDirection.DotProduct(direction));
            }

            //--------------------------------------------------
            // Final fallback
            //--------------------------------------------------

            if (normal.GetLength() < 1e-6)
            {
                XYZ candidate;

                if (Math.Abs(direction.DotProduct(XYZ.BasisZ)) < 0.9)
                    candidate = XYZ.BasisZ;
                else
                    candidate = XYZ.BasisX;

                normal =
                    candidate -
                    direction.Multiply(
                        candidate.DotProduct(direction));
            }

            if (normal.GetLength() < 1e-6)
                return;

            normal = normal.Normalize();

            //--------------------------------------------------
            // IMPORTANT
            //--------------------------------------------------

            tray.CurveNormal = normal;
        }

        private static List<ConnectorPair> FindOppositeConnectorPairs(
                                                    List<Connector> connectors)
        {
            List<ConnectorPair> result = new List<ConnectorPair>();

            if (connectors == null ||
                connectors.Count != 4)
                return result;

            //--------------------------------------------------
            // Calculate all 6 possible pairs.
            //--------------------------------------------------

            List<Tuple<int, int, double>> candidates =
                new List<Tuple<int, int, double>>();

            for (int i = 0; i < connectors.Count; i++)
            {
                XYZ dirI =
                    GetConnectorDirection(
                        connectors[i]);

                for (int j = i + 1;
                     j < connectors.Count;
                     j++)
                {
                    XYZ dirJ =
                        GetConnectorDirection(
                            connectors[j]);

                    double dot =
                        dirI.DotProduct(dirJ);

                    candidates.Add(
                        Tuple.Create(
                            i,
                            j,
                            dot));
                }
            }

            //--------------------------------------------------
            // Sort most opposite first.
            //
            // Perfect opposite = -1
            //--------------------------------------------------

            candidates =
                candidates
                    .OrderBy(x => x.Item3)
                    .ToList();

            //--------------------------------------------------
            // First opposite pair
            //--------------------------------------------------

            var first =
                candidates[0];

            int a = first.Item1;
            int b = first.Item2;

            //--------------------------------------------------
            // Find the best opposite pair using the
            // two remaining connectors.
            //--------------------------------------------------

            List<int> remaining =
                Enumerable
                    .Range(0, 4)
                    .Where(i => i != a && i != b)
                    .ToList();

            if (remaining.Count != 2)
                return result;

            int c =
                remaining[0];

            int d =
                remaining[1];

            XYZ dirC =
                GetConnectorDirection(
                    connectors[c]);

            XYZ dirD =
                GetConnectorDirection(
                    connectors[d]);

            double remainingDot =
                dirC.DotProduct(dirD);

            //--------------------------------------------------
            // Cross should have two opposite pairs.
            //--------------------------------------------------

            if (remainingDot > -0.90)
            {
                Logger.Log(
                    $"Warning: Cross connector pair angle " +
                    $"is not opposite. Dot={remainingDot:F4}");
            }

            result.Add(
                new ConnectorPair
                {
                    First = connectors[a],
                    Second = connectors[b]
                });

            result.Add(
                new ConnectorPair
                {
                    First = connectors[c],
                    Second = connectors[d]
                });

            return result;
        }

        private static XYZ GetConnectorDirection(Connector connector)
        {
            if (connector == null)
                return XYZ.Zero;

            XYZ direction =
                connector.CoordinateSystem.BasisZ;

            if (direction.GetLength() < 1e-6)
                return XYZ.Zero;

            return direction.Normalize();
        }

        private static void SetFittingParameters(Document doc,
                                                    Connector c1,
                                                    Connector c2,
                                                    RouteSettingDTOs settings)
        {
            //-----------------------------------------------------
            // FITTING
            //-----------------------------------------------------
            CreateNewBypassRoute createNewBypassRoute = new CreateNewBypassRoute();

            try
            {
                FamilyInstance fitting =
                    doc.Create.NewElbowFitting(
                        c1,
                        c2);

                if (fitting != null)
                {
                    createNewBypassRoute.TrySetParameter(
                            fitting,
                            "Bend Radius",
                            settings.BendRadius);
                    createNewBypassRoute.TrySetParameter(
                            fitting,
                            "Angle",
                            settings.BendAngle);

                }
                else
                {
                    throw new InvalidOperationException(
                        $"Revit returned null elbow " +
                        $"for joint {fitting}.");
                }

                Logger.Log(
                    $"Created elbow {fitting.Id} " +
                    $"between {c1.Id} and {c2.Id}");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed elbow at joint {0}. " +
                    $"Tray1={c1.Id}, " +
                    $"Tray2={c2.Id}. " +
                    $"Reason={ex.Message}",
                    ex);
            }
        }        
    

        #endregion
    }

    public class ConnectedTrayInfo
    {
        public CableTray OriginalTray { get; set; }

        public ElementId TypeId { get; set; }

        public XYZ BreakPoint { get; set; }

        public XYZ FarEndPoint { get; set; }

        public XYZ OutwardDirection { get; set; }

        public XYZ OriginalDirection { get; set; }

        public XYZ OriginalNormal { get; set; }

        public Connector TrayNearConnector { get; set; }

        public Connector FittingConnector { get; set; }

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

    public class ConnectorPair
    {
        public Connector First { get; set; }

        public Connector Second { get; set; }
        
    }

}