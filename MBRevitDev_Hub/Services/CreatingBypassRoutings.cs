using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using MBRevitDev_Hub.DTOs;
using MBRevitDev_Hub.LogDetails;
using MBRevitDev_Hub.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;


namespace MBRevitDev_Hub.Services
{
    public class CreatingBypassRoutings
    {
        // ================================================================
        // CONFIGURATION
        // ================================================================

        private const double MmToFt = 1.0 / 304.8;
        // ================================================================
        // 4-POINT ROUTE SOLVER
        // ================================================================       

        /// <summary>
        /// Builds a 4-point symmetric bypass route (P0→P1→P2→P3).
        /// P0, P3 are at original tray height.
        /// P1, P2 are at elevated height above all clashes.
        /// Topology: P0 --45°--> P1 (peak start)
        ///           P1 -------> P2 (peak horizontal)
        ///           P2 --45°--> P3 (descent end)
        /// </summary>
        public bool TryBuildRoutePoints(
                                ClashGroupDTOs group,
                                CableTray tray,
                                out RouteDefinitionDTOs route)
        {
            RouteSettingDTOs routeSettingDTOs = new RouteSettingDTOs();
            route = null;

            LocationCurve location = tray.Location as LocationCurve;

            if (location == null || !(location.Curve is Line))
                return false;

            Line trayLine = location.Curve as Line;
            XYZ trayStart = trayLine.GetEndPoint(0);
            XYZ trayEnd = trayLine.GetEndPoint(1);
            XYZ trayDirection = (trayEnd - trayStart).Normalize();

            // Validate horizontal tray.
            if (Math.Abs(trayDirection.Z) > routeSettingDTOs.HorizontalTrayZTolerance)
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

            foreach (ClashInfoDTOs clash in group.Clashes)
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
                             routeSettingDTOs.ClearanceMm * MmToFt +
                             trayHeight / 2.0;

            targetZ = Math.Max(
                targetZ,
                currentZ + routeSettingDTOs.MinimumRiseMm * MmToFt);

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

            route = new RouteDefinitionDTOs
            {
                Points = points,
                Rise = rise,
                Transition = transition,
                EntryStation = entryStation,
                ExitStation = exitStation
            };
            ClashResolveToolLogDetails.Info($"Route Path station points Details)" +
                $"P0: {p0}" + "\n" +
                $"P1: {p1}" + "\n" +
                $"P2: {p2}" + "\n" +
                $"P3: {p3}");

            return true;
        }

        // ================================================================
        // CREATE BYPASS SEGMENTS (3 segments for 4 points)
        // ================================================================

        public List<CableTray> CreateBypassSegments(
                                        Document doc,
                                        CableTray sourceTray,
                                        IList<XYZ> routePoints)
        {
            List<CableTray> segments = new List<CableTray>();
            RouteSettingDTOs routeSettingDTOs = new RouteSettingDTOs();

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

                if (start.DistanceTo(end) < routeSettingDTOs.PointToleranceMm * MmToFt)
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

        public List<ElementId> ConnectBypassSegments(
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

        public Connector GetEndpointConnector(
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
        public bool TrySplitOriginalTray(
            Document doc,
            CableTray originalTray,
            RouteDefinitionDTOs route)
        {
            RouteSettingDTOs routeSettingDTOs = new RouteSettingDTOs();
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
                if (originalStart.DistanceTo(p0) > routeSettingDTOs.PointToleranceMm * MmToFt)
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
                if (p3.DistanceTo(originalEnd) > routeSettingDTOs.PointToleranceMm * MmToFt)
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

        public bool RouteStillClashes(
                            IList<CableTray> segments,
                            IList<ElementId> fittingIds,
                            Conduit conduit)
        {
            GeometricUtilities geometricUtilities = new GeometricUtilities();
            ClashDetectionServices clashDetectionServices = new ClashDetectionServices();
            List<Solid> conduitSolids = geometricUtilities.GetSolids(conduit);

            if (conduitSolids.Count == 0)
                return true;

            foreach (CableTray segment in segments)
            {
                List<Solid> segmentSolids = geometricUtilities.GetSolids(segment);

                if (clashDetectionServices.FindSolidIntersection(segmentSolids, conduitSolids) != null)
                    return true;
            }

            // Check fittings.
            Document doc = conduit.Document;

            foreach (ElementId fittingId in fittingIds)
            {
                Element fitting = doc.GetElement(fittingId);

                if (fitting == null)
                    continue;

                List<Solid> fittingSolids = geometricUtilities.GetSolids(fitting);

                if (clashDetectionServices.FindSolidIntersection(fittingSolids, conduitSolids) != null)
                    return true;
            }

            return false;
        }

        // ================================================================
        // STATION / GEOMETRY
        // ================================================================

        public bool TryGetStationRange(
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


        private static double GetParameterValue(
                                       Element element,
                                       BuiltInParameter parameter)
        {
            Parameter parameterValue = element.get_Parameter(parameter);

            if (parameterValue == null || !parameterValue.HasValue)
                return 0.0;

            return parameterValue.AsDouble();
        }

    }
}
