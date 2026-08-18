using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;


namespace MBRevitDev_Hub
{
    [Transaction(TransactionMode.Manual)]
    public class CableTrayClashDetectionCommand : IExternalCommand
    {
        // ================================================================
        // CONFIGURATION
        // ================================================================

        private const double MmToFt = 1.0 / 304.8;

        // Required free space above the conduit.
        private const double ClearanceMm = 50.0;

        // Minimum vertical rise.
        private const double MinimumRiseMm = 100.0;

        // Minimum clash/geometry tolerance.
        private const double PointToleranceMm = 1.0;

        // Minimum accepted clash volume.
        private const double MinimumIntersectionVolume = 1e-9;

        // First-stage routing supports horizontal trays in any XY direction.
        private const double HorizontalTrayZTolerance = 0.01;

        // Horizontal minimum clearance for grouping clashes.
        // Clashes within this distance along the tray direction are grouped.
        private const double HorizontalMinimumClearanceMm = 300.0;

        // ================================================================
        // MAIN COMMAND
        // ================================================================

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;

            try
            {
                List<CableTray> trays = CollectCableTrays(doc);
                List<Conduit> conduits = CollectConduits(doc);

                if (trays.Count == 0 || conduits.Count == 0)
                {
                    ShowSummary(
                        "Cable Tray Clash Resolver",
                        $"Cable Trays : {trays.Count}\n" +
                        $"Conduits    : {conduits.Count}\n\n" +
                        "Nothing to process.");

                    return Result.Succeeded;
                }

                List<ClashInfo> clashes = FindClashes(trays, conduits);

                if (clashes.Count == 0)
                {
                    ShowSummary(
                        "Cable Tray Clash Resolver",
                        $"Cable Trays : {trays.Count}\n" +
                        $"Conduits    : {conduits.Count}\n\n" +
                        "No solid clashes detected.");

                    return Result.Succeeded;
                }

                // ============================================================
                // GROUP CLASHES BY PROXIMITY
                // ============================================================
                List<ClashGroup> clashGroups = GroupClashesByProximity(
                    clashes,
                    HorizontalMinimumClearanceMm * MmToFt);

                if (clashGroups.Count == 0)
                {
                    ShowSummary(
                        "Cable Tray Clash Resolver",
                        "Clash grouping failed or produced no valid groups.");
                    return Result.Succeeded;
                }

                int successfulGroups = 0;
                int skippedGroups = 0;
                int createdSegments = 0;
                int createdFittings = 0;
                int traysSplit = 0;

                // ============================================================
                // PROCESS EACH CLASH GROUP
                // ============================================================
                using (Transaction tx =
                    new Transaction(doc, "Auto Resolve Cable Tray Clashes"))
                {
                    tx.Start();

                    foreach (ClashGroup group in clashGroups)
                    {
                        CableTray originalTray =
                            doc.GetElement(group.TrayId) as CableTray;

                        if (originalTray == null)
                        {
                            skippedGroups++;
                            continue;
                        }

                        RouteDefinition route;

                        if (!TryBuild4PointRoute(group, originalTray, out route))
                        {
                            skippedGroups++;
                            continue;
                        }

                        using (SubTransaction st = new SubTransaction(doc))
                        {
                            st.Start();

                            try
                            {
                                // Create the 4-point bypass route (P0→P1→P2→P3).
                                List<CableTray> bypassSegments =
                                    CreateBypassSegments(
                                        doc,
                                        originalTray,
                                        route.Points);

                                if (bypassSegments.Count != 3)
                                {
                                    st.RollBack();
                                    skippedGroups++;
                                    continue;
                                }

                                doc.Regenerate();

                                // Connect the bypass segments at junction points.
                                List<ElementId> fittingIds =
                                    ConnectBypassSegments(
                                        doc,
                                        bypassSegments,
                                        route.Points);

                                if (fittingIds.Count < 2)
                                {
                                    st.RollBack();
                                    skippedGroups++;
                                    continue;
                                }

                                doc.Regenerate();

                                // Validate the bypass route against all original conduits.
                                bool stillClashes = false;

                                foreach (ClashInfo clash in group.Clashes)
                                {
                                    if (RouteStillClashes(
                                        bypassSegments,
                                        fittingIds,
                                        clash.Conduit))
                                    {
                                        stillClashes = true;
                                        break;
                                    }
                                }

                                if (stillClashes)
                                {
                                    st.RollBack();
                                    skippedGroups++;
                                    continue;
                                }

                                // Split the original tray:
                                // Keep pre-bypass and post-bypass segments,
                                // delete the section that conflicts.
                                bool splitSuccess = TrySplitOriginalTray(
                                    doc,
                                    originalTray,
                                    route);

                                if (!splitSuccess)
                                {
                                    st.RollBack();
                                    skippedGroups++;
                                    continue;
                                }

                                traysSplit++;

                                // Visualize the bypass route.
                                foreach (CableTray segment in bypassSegments)
                                {
                                    OverrideAsPink(
                                        doc.ActiveView,
                                        segment.Id);
                                }

                                st.Commit();

                                successfulGroups++;
                                createdSegments += bypassSegments.Count;
                                createdFittings += fittingIds.Count;
                            }
                            catch (Exception ex)
                            {
                                st.RollBack();
                                skippedGroups++;
                            }
                        }
                    }

                    tx.Commit();
                }

                ShowSummary(
                    "Cable Tray Clash Resolver - Multi-Clash",
                    $"Cable Trays found     : {trays.Count}\n" +
                    $"Conduits found        : {conduits.Count}\n" +
                    $"Clashes found         : {clashes.Count}\n" +
                    $"Clash groups formed   : {clashGroups.Count}\n\n" +
                    $"Successful groups     : {successfulGroups}\n" +
                    $"Trays split           : {traysSplit}\n" +
                    $"Bypass segments       : {createdSegments}\n" +
                    $"Fittings created      : {createdFittings}\n" +
                    $"Skipped groups        : {skippedGroups}\n\n" +
                    "PRODUCTION MODE:\n" +
                    "Original Cable Trays were split and modified.\n" +
                    "Bypass routes shown in pink.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show(
                    "Cable Tray Clash Resolver - Error",
                    ex.ToString());
                return Result.Failed;
            }
        }

        // ================================================================
        // COLLECTION
        // ================================================================

        private static List<CableTray> CollectCableTrays(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(CableTray))
                .WhereElementIsNotElementType()
                .Cast<CableTray>()
                .ToList();
        }

        private static List<Conduit> CollectConduits(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Conduit))
                .WhereElementIsNotElementType()
                .Cast<Conduit>()
                .ToList();
        }

        // ================================================================
        // CLASH DETECTION
        // ================================================================

        private static List<ClashInfo> FindClashes(
            List<CableTray> trays,
            List<Conduit> conduits)
        {
            List<ClashInfo> result = new List<ClashInfo>();

            Dictionary<ElementId, BoundingBoxXYZ> trayBoxes =
                trays.ToDictionary(t => t.Id, t => t.get_BoundingBox(null));

            Dictionary<ElementId, BoundingBoxXYZ> conduitBoxes =
                conduits.ToDictionary(c => c.Id, c => c.get_BoundingBox(null));

            Dictionary<ElementId, List<Solid>> traySolids =
                trays.ToDictionary(t => t.Id, GetSolids);

            Dictionary<ElementId, List<Solid>> conduitSolids =
                conduits.ToDictionary(c => c.Id, GetSolids);

            foreach (CableTray tray in trays)
            {
                BoundingBoxXYZ trayBox = trayBoxes[tray.Id];

                if (trayBox == null)
                    continue;

                foreach (Conduit conduit in conduits)
                {
                    BoundingBoxXYZ conduitBox = conduitBoxes[conduit.Id];

                    if (conduitBox == null)
                        continue;

                    // Broad phase: bounding box intersection.
                    if (!BoxesIntersect(trayBox, conduitBox, 1.0 * MmToFt))
                        continue;

                    // Narrow phase: solid intersection.
                    Solid intersection = FindSolidIntersection(
                        traySolids[tray.Id],
                        conduitSolids[conduit.Id]);

                    if (intersection == null)
                        continue;

                    result.Add(
                        new ClashInfo
                        {
                            Tray = tray,
                            Conduit = conduit,
                            Intersection = intersection
                        });
                }
            }

            return result;
        }

        private static Solid FindSolidIntersection(
            List<Solid> firstSolids,
            List<Solid> secondSolids)
        {
            foreach (Solid first in firstSolids)
            {
                foreach (Solid second in secondSolids)
                {
                    try
                    {
                        Solid intersection =
                            BooleanOperationsUtils.ExecuteBooleanOperation(
                                first,
                                second,
                                BooleanOperationsType.Intersect);

                        if (intersection != null &&
                            intersection.Volume > MinimumIntersectionVolume)
                        {
                            return intersection;
                        }
                    }
                    catch
                    {
                        // Some geometry combinations fail BooleanOperations.
                    }
                }
            }

            return null;
        }

        // ================================================================
        // GROUPING & CONSOLIDATION
        // ================================================================

        /// <summary>
        /// Groups clashes by tray, then by proximity along the tray direction.
        /// Clashes within horizontalMinimumClearance are merged into single groups.
        /// </summary>
        private static List<ClashGroup> GroupClashesByProximity(
            List<ClashInfo> clashes,
            double horizontalMinimumClearanceFt)
        {
            List<ClashGroup> groups = new List<ClashGroup>();

            // Group clashes by tray ID.
            var clashesByTray = clashes.GroupBy(c => c.Tray.Id).ToList();

            foreach (var trayClashGroup in clashesByTray)
            {
                ElementId trayId = trayClashGroup.Key;
                List<ClashInfo> trayClashes = trayClashGroup.ToList();

                if (trayClashes.Count == 0)
                    continue;

                // Get tray geometry.
                LocationCurve location =
                    trayClashes[0].Tray.Location as LocationCurve;

                if (location == null || !(location.Curve is Line))
                    continue;

                Line trayLine = location.Curve as Line;
                XYZ trayStart = trayLine.GetEndPoint(0);
                XYZ trayEnd = trayLine.GetEndPoint(1);
                XYZ trayDirection = (trayEnd - trayStart).Normalize();

                // Calculate station (projection) for each clash's intersection.
                Dictionary<ClashInfo, double> clashStations =
                    new Dictionary<ClashInfo, double>();

                foreach (ClashInfo clash in trayClashes)
                {
                    if (!TryGetStationRange(
                        clash.Intersection,
                        trayStart,
                        trayDirection,
                        out double minSta,
                        out double maxSta))
                    {
                        continue;
                    }

                    // Use midpoint of clash range for grouping.
                    double midStation = (minSta + maxSta) / 2.0;
                    clashStations[clash] = midStation;
                }

                if (clashStations.Count == 0)
                    continue;

                // Sort clashes by station.
                List<ClashInfo> sortedClashes = clashStations
                    .OrderBy(kvp => kvp.Value)
                    .Select(kvp => kvp.Key)
                    .ToList();

                // Merge clashes within horizontalMinimumClearance.
                List<List<ClashInfo>> mergedGroups = MergeProximalClashes(
                    sortedClashes,
                    clashStations,
                    horizontalMinimumClearanceFt);

                // Create ClashGroup for each merged set.
                foreach (List<ClashInfo> groupClashes in mergedGroups)
                {
                    if (groupClashes.Count == 0)
                        continue;

                    double minStation = double.MaxValue;
                    double maxStation = double.MinValue;

                    foreach (ClashInfo clash in groupClashes)
                    {
                        if (TryGetStationRange(
                            clash.Intersection,
                            trayStart,
                            trayDirection,
                            out double minSta,
                            out double maxSta))
                        {
                            minStation = Math.Min(minStation, minSta);
                            maxStation = Math.Max(maxStation, maxSta);
                        }
                    }

                    // Calculate consolidated centroid along tray direction.
                    XYZ consolidatedCentroid = CalculateConsolidatedCentroid(
                        groupClashes,
                        trayStart,
                        trayDirection);

                    groups.Add(
                        new ClashGroup
                        {
                            TrayId = trayId,
                            Clashes = groupClashes,
                            MinStation = minStation,
                            MaxStation = maxStation,
                            ConsolidatedCentroid = consolidatedCentroid
                        });
                }
            }

            return groups;
        }

        /// <summary>
        /// Merges clashes that are within horizontalMinimumClearance along tray direction.
        /// </summary>
        private static List<List<ClashInfo>> MergeProximalClashes(
            List<ClashInfo> sortedClashes,
            Dictionary<ClashInfo, double> stationMap,
            double clearanceFt)
        {
            List<List<ClashInfo>> merged = new List<List<ClashInfo>>();

            if (sortedClashes.Count == 0)
                return merged;

            List<ClashInfo> currentGroup = new List<ClashInfo> { sortedClashes[0] };

            for (int i = 1; i < sortedClashes.Count; i++)
            {
                double currentStation = stationMap[sortedClashes[i]];
                double lastStation = stationMap[sortedClashes[i - 1]];

                if (currentStation - lastStation <= clearanceFt)
                {
                    // Within clearance, add to current group.
                    currentGroup.Add(sortedClashes[i]);
                }
                else
                {
                    // Gap exceeds clearance, start new group.
                    merged.Add(new List<ClashInfo>(currentGroup));
                    currentGroup = new List<ClashInfo> { sortedClashes[i] };
                }
            }

            merged.Add(currentGroup);

            return merged;
        }

        /// <summary>
        /// Calculates the average centroid of all intersection solids,
        /// projected onto the tray direction.
        /// </summary>
        private static XYZ CalculateConsolidatedCentroid(
            List<ClashInfo> groupClashes,
            XYZ trayStart,
            XYZ trayDirection)
        {
            List<XYZ> centroids = new List<XYZ>();

            foreach (ClashInfo clash in groupClashes)
            {
                XYZ centroid = clash.Intersection.ComputeCentroid();
                
                centroids.Add(centroid);
            }

            if (centroids.Count == 0)
                return trayStart;

            // Average centroid position.
            XYZ averageCentroid = new XYZ(
                centroids.Average(pt => pt.X),
                centroids.Average(pt => pt.Y),
                centroids.Average(pt => pt.Z));

            // Project onto tray direction to get station.
            double station = (averageCentroid - trayStart).DotProduct(trayDirection);

            // Reconstruct position at this station on tray line.
            XYZ projectedPoint = trayStart + trayDirection * station;

            return projectedPoint;
        }

        // ================================================================
        // 4-POINT ROUTE SOLVER
        // ================================================================

        private sealed class RouteDefinition
        {
            public List<XYZ> Points { get; set; }  // P0, P1, P2, P3
            public double Rise { get; set; }
            public double Transition { get; set; }
            public double EntryStation { get; set; }
            public double ExitStation { get; set; }
        }

        /// <summary>
        /// Builds a 4-point symmetric bypass route (P0→P1→P2→P3).
        /// P0, P3 are at original tray height.
        /// P1, P2 are at elevated height above all clashes.
        /// Topology: P0 --45°--> P1 (peak start)
        ///           P1 -------> P2 (peak horizontal)
        ///           P2 --45°--> P3 (descent end)
        /// </summary>
        private static bool TryBuild4PointRoute(
            ClashGroup group,
            CableTray tray,
            out RouteDefinition route)
        {
            route = null;

            LocationCurve location = tray.Location as LocationCurve;

            if (location == null || !(location.Curve is Line))
                return false;

            Line trayLine = location.Curve as Line;
            XYZ trayStart = trayLine.GetEndPoint(0);
            XYZ trayEnd = trayLine.GetEndPoint(1);
            XYZ trayDirection = (trayEnd - trayStart).Normalize();

            // Validate horizontal tray.
            if (Math.Abs(trayDirection.Z) > HorizontalTrayZTolerance)
                return false;

            XYZ Z = XYZ.BasisZ;
            double trayHeight = GetParameterValue(
                tray,
                BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM);

            if (trayHeight <= 0)
                return false;

            double currentZ = trayStart.Z;
            double trayLength = trayStart.DistanceTo(trayEnd);

            // Calculate required elevation above all clashes in the group.
            double maxConduitTopZ = double.MinValue;

            foreach (ClashInfo clash in group.Clashes)
            {
                BoundingBoxXYZ conduitBox =
                    clash.Conduit.get_BoundingBox(null);

                if (conduitBox != null)
                {
                    maxConduitTopZ = Math.Max(maxConduitTopZ, conduitBox.Max.Z);
                }
            }

            if (maxConduitTopZ == double.MinValue)
                return false;

            // Target height: above top of all conduits + clearance + tray height/2.
            double targetZ = maxConduitTopZ +
                             ClearanceMm * MmToFt +
                             trayHeight / 2.0;

            targetZ = Math.Max(
                targetZ,
                currentZ + MinimumRiseMm * MmToFt);

            double rise = targetZ - currentZ;

            // 45-degree condition: horizontal transition = vertical rise.
            double transition = rise;

            double minStation = group.MinStation;
            double maxStation = group.MaxStation;

            // Entry and exit stations for the bypass.
            double entryStation = minStation - transition;
            double exitStation = maxStation + transition;

            // Validate: bypass must fit within tray extents.
            if (entryStation <= 0 || exitStation >= trayLength)
                return false;

            if (exitStation <= entryStation)
                return false;

            // Build 4-point route.
            // P0: entry point (original height, tray direction).
            XYZ p0 = trayStart + trayDirection * entryStation;

            // P1: peak start (45° up, horizontal distance = rise).
            XYZ p1 = p0 + trayDirection * transition + Z * rise;

            // P2: peak end (back along tray direction).
            XYZ p2 = trayStart + trayDirection * (exitStation - transition) + Z * rise;

            // P3: exit point (original height).
            XYZ p3 = trayStart + trayDirection * exitStation;

            List<XYZ> points = new List<XYZ> { p0, p1, p2, p3 };

            route = new RouteDefinition
            {
                Points = points,
                Rise = rise,
                Transition = transition,
                EntryStation = entryStation,
                ExitStation = exitStation
            };

            return true;
        }

        // ================================================================
        // CREATE BYPASS SEGMENTS (3 segments for 4 points)
        // ================================================================

        private static List<CableTray> CreateBypassSegments(
            Document doc,
            CableTray sourceTray,
            IList<XYZ> routePoints)
        {
            List<CableTray> segments = new List<CableTray>();

            if (routePoints.Count != 4)
                return segments;

            ElementId typeId = sourceTray.GetTypeId();
            ElementId levelId = sourceTray.LevelId;

            double width = GetParameterValue(
                sourceTray,
                BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM);

            double height = GetParameterValue(
                sourceTray,
                BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM);

            // Create 3 segments: P0→P1, P1→P2, P2→P3.
            for (int i = 0; i < routePoints.Count - 1; i++)
            {
                XYZ start = routePoints[i];
                XYZ end = routePoints[i + 1];

                if (start.DistanceTo(end) < PointToleranceMm * MmToFt)
                    continue;

                CableTray segment = CableTray.Create(
                    doc,
                    typeId,
                    start,
                    end,
                    levelId);

                SetCableTraySize(segment, width, height);
                segments.Add(segment);
            }

            return segments;
        }

        private static void SetCableTraySize(
            CableTray tray,
            double width,
            double height)
        {
            Parameter widthParameter = tray.get_Parameter(
                BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM);

            if (widthParameter != null &&
                !widthParameter.IsReadOnly &&
                width > 0)
            {
                widthParameter.Set(width);
            }

            Parameter heightParameter = tray.get_Parameter(
                BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM);

            if (heightParameter != null &&
                !heightParameter.IsReadOnly &&
                height > 0)
            {
                heightParameter.Set(height);
            }
        }

        // ================================================================
        // FITTING / CONNECTION ENGINE
        // ================================================================

        private static List<ElementId> ConnectBypassSegments(
            Document doc,
            IList<CableTray> segments,
            IList<XYZ> routePoints)
        {
            List<ElementId> fittingIds = new List<ElementId>();

            if (segments.Count < 2)
                return fittingIds;

            // 4-point route produces 3 segments, so 2 connection points.
            for (int i = 0; i < segments.Count - 1; i++)
            {
                XYZ connectionPoint = routePoints[i + 1];

                Connector first = GetEndpointConnector(
                    segments[i],
                    connectionPoint);

                Connector second = GetEndpointConnector(
                    segments[i + 1],
                    connectionPoint);

                if (first == null || second == null)
                {
                    throw new InvalidOperationException(
                        $"Could not find route connectors at point {i + 1}.");
                }

                if (first.Domain != second.Domain)
                {
                    throw new InvalidOperationException(
                        "Cable Tray connector domains do not match.");
                }

                double distance = first.Origin.DistanceTo(second.Origin);

                if (distance > 5.0 * MmToFt)
                {
                    throw new InvalidOperationException(
                        $"Connector gap is too large: {distance / MmToFt:F1} mm.");
                }

                HashSet<ElementId> before = GetCableTrayFittingIds(doc);

                try
                {
                    first.ConnectTo(second);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to connect bypass segments at {connectionPoint}.\n{ex.Message}",
                        ex);
                }

                if (!first.IsConnected || !second.IsConnected)
                {
                    throw new InvalidOperationException(
                        "ConnectTo completed but connectors are not physically connected.");
                }

                doc.Regenerate();

                HashSet<ElementId> after = GetCableTrayFittingIds(doc);

                List<ElementId> newFittings = after.Except(before).ToList();

                if (newFittings.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"No Cable Tray fitting was generated at bypass vertex {i + 1}.");
                }

                fittingIds.AddRange(newFittings);
            }

            return fittingIds.Distinct().ToList();
        }

        private static Connector GetEndpointConnector(
            MEPCurve curve,
            XYZ point)
        {
            Connector best = null;
            double bestDistance = double.MaxValue;

            foreach (Connector connector in curve.ConnectorManager.Connectors)
            {
                if (connector.ConnectorType != ConnectorType.End)
                    continue;

                double distance = connector.Origin.DistanceTo(point);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = connector;
                }
            }

            if (best == null || bestDistance > 5.0 * MmToFt)
                return null;

            return best;
        }

        private static HashSet<ElementId> GetCableTrayFittingIds(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_CableTrayFitting)
                .WhereElementIsNotElementType()
                .ToElementIds()
                .ToHashSet();
        }

        // ================================================================
        // SPLIT ORIGINAL TRAY
        // ================================================================

        /// <summary>
        /// Splits the original tray:
        /// 1. Modify the original tray segment to end at entry station.
        /// 2. Create new tray segment from exit station to end.
        /// The bypass replaces the segment between entry and exit.
        /// </summary>
        private static bool TrySplitOriginalTray(
            Document doc,
            CableTray originalTray,
            RouteDefinition route)
        {
            try
            {
                LocationCurve location = originalTray.Location as LocationCurve;

                if (location == null || !(location.Curve is Line))
                    return false;

                Line originalLine = location.Curve as Line;
                XYZ originalStart = originalLine.GetEndPoint(0);
                XYZ originalEnd = originalLine.GetEndPoint(1);
                XYZ trayDirection = (originalEnd - originalStart).Normalize();

                // P0 and P3 from route.
                XYZ p0 = route.Points[0];
                XYZ p3 = route.Points[3];

                ElementId typeId = originalTray.GetTypeId();
                ElementId levelId = originalTray.LevelId;

                double width = GetParameterValue(
                    originalTray,
                    BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM);

                double height = GetParameterValue(
                    originalTray,
                    BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM);

                // Segment 1: original start to P0.
                if (originalStart.DistanceTo(p0) > PointToleranceMm * MmToFt)
                {
                    CableTray preSegment = CableTray.Create(
                        doc,
                        typeId,
                        originalStart,
                        p0,
                        levelId);

                    SetCableTraySize(preSegment, width, height);
                }

                // Segment 2: P3 to original end.
                if (p3.DistanceTo(originalEnd) > PointToleranceMm * MmToFt)
                {
                    CableTray postSegment = CableTray.Create(
                        doc,
                        typeId,
                        p3,
                        originalEnd,
                        levelId);

                    SetCableTraySize(postSegment, width, height);
                }

                // Delete the original tray.
                doc.Delete(originalTray.Id);

                return true;
            }
            catch
            {
                return false;
            }
        }

        // ================================================================
        // ROUTE VALIDATION
        // ================================================================

        private static bool RouteStillClashes(
            IList<CableTray> segments,
            IList<ElementId> fittingIds,
            Conduit conduit)
        {
            List<Solid> conduitSolids = GetSolids(conduit);

            if (conduitSolids.Count == 0)
                return true;

            foreach (CableTray segment in segments)
            {
                List<Solid> segmentSolids = GetSolids(segment);

                if (FindSolidIntersection(segmentSolids, conduitSolids) != null)
                    return true;
            }

            // Check fittings.
            Document doc = conduit.Document;

            foreach (ElementId fittingId in fittingIds)
            {
                Element fitting = doc.GetElement(fittingId);

                if (fitting == null)
                    continue;

                List<Solid> fittingSolids = GetSolids(fitting);

                if (FindSolidIntersection(fittingSolids, conduitSolids) != null)
                    return true;
            }

            return false;
        }

        // ================================================================
        // STATION / GEOMETRY
        // ================================================================

        private static bool TryGetStationRange(
            Solid solid,
            XYZ origin,
            XYZ direction,
            out double minStation,
            out double maxStation)
        {
            minStation = double.MaxValue;
            maxStation = double.MinValue;

            if (solid == null)
                return false;

            foreach (Edge edge in solid.Edges)
            {
                IList<XYZ> points = edge.Tessellate();

                foreach (XYZ point in points)
                {
                    double station = (point - origin).DotProduct(direction);

                    minStation = Math.Min(minStation, station);
                    maxStation = Math.Max(maxStation, station);
                }
            }

            return minStation != double.MaxValue &&
                   maxStation != double.MinValue;
        }

        // ================================================================
        // SOLID EXTRACTION
        // ================================================================

        private static List<Solid> GetSolids(Element element)
        {
            List<Solid> solids = new List<Solid>();

            Options options = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = false
            };

            GeometryElement geometry = element.get_Geometry(options);

            if (geometry == null)
                return solids;

            CollectSolids(geometry, solids);

            return solids;
        }

        private static void CollectSolids(
            GeometryElement geometry,
            IList<Solid> solids)
        {
            foreach (GeometryObject obj in geometry)
            {
                Solid solid = obj as Solid;

                if (solid != null && solid.Volume > MinimumIntersectionVolume)
                {
                    solids.Add(solid);
                    continue;
                }

                GeometryInstance instance = obj as GeometryInstance;

                if (instance == null)
                    continue;

                GeometryElement instanceGeometry =
                    instance.GetInstanceGeometry();

                if (instanceGeometry != null)
                    CollectSolids(instanceGeometry, solids);
            }
        }

        // ================================================================
        // BOUNDING BOX
        // ================================================================

        private static bool BoxesIntersect(
            BoundingBoxXYZ a,
            BoundingBoxXYZ b,
            double tolerance)
        {
            return
                a.Min.X <= b.Max.X + tolerance &&
                a.Max.X + tolerance >= b.Min.X &&
                a.Min.Y <= b.Max.Y + tolerance &&
                a.Max.Y + tolerance >= b.Min.Y &&
                a.Min.Z <= b.Max.Z + tolerance &&
                a.Max.Z + tolerance >= b.Min.Z;
        }

        // ================================================================
        // PARAMETER / GRAPHICS
        // ================================================================

        private static double GetParameterValue(
            Element element,
            BuiltInParameter parameter)
        {
            Parameter parameterValue = element.get_Parameter(parameter);

            if (parameterValue == null || !parameterValue.HasValue)
                return 0.0;

            return parameterValue.AsDouble();
        }

        private static void OverrideAsPink(
            View view,
            ElementId elementId)
        {
            OverrideGraphicSettings settings =
                new OverrideGraphicSettings();

            settings.SetProjectionLineColor(
                new Color(255, 0, 180));

            settings.SetProjectionLineWeight(6);

            view.SetElementOverrides(elementId, settings);
        }

        // ================================================================
        // UI
        // ================================================================

        private static void ShowSummary(
            string title,
            string message)
        {
            TaskDialog.Show(title, message);
        }

        // ================================================================
        // DATA STRUCTURES
        // ================================================================

        private sealed class ClashInfo
        {
            public CableTray Tray { get; set; }
            public Conduit Conduit { get; set; }
            public Solid Intersection { get; set; }
        }

        private sealed class ClashGroup
        {
            public ElementId TrayId { get; set; }
            public List<ClashInfo> Clashes { get; set; }
            public double MinStation { get; set; }
            public double MaxStation { get; set; }
            public XYZ ConsolidatedCentroid { get; set; }
        }
    }
}
