using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using MBRevitDev_Hub.DTOs;
using MBRevitDev_Hub.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MBRevitDev_Hub.Services
{
    public class ClashDetectionServices
    {
        // ================================================================
        // CONFIGURATION
        // ================================================================

        private const double MmToFt = 1.0 / 304.8;

        // ================================================================
        // CLASH DETECTION
        // ================================================================

        public List<ClashInfoDTOs> FindClashes(
                                        List<CableTray> trays,
                                        List<Conduit> conduits)
        {
            List<ClashInfoDTOs> result = new List<ClashInfoDTOs>();
            RouteSettingDTOs routeSettings = new RouteSettingDTOs();
            GeometricUtilities geometricUtilities = new GeometricUtilities();

            Dictionary<ElementId, BoundingBoxXYZ> trayBoxes =
                trays.ToDictionary(t => t.Id, t => t.get_BoundingBox(null));

            Dictionary<ElementId, BoundingBoxXYZ> conduitBoxes =
                conduits.ToDictionary(c => c.Id, c => c.get_BoundingBox(null));

            Dictionary<ElementId, List<Solid>> traySolids =
                trays.ToDictionary(t => t.Id, t => geometricUtilities.GetSolids(t));

            Dictionary<ElementId, List<Solid>> conduitSolids =
                conduits.ToDictionary(c => c.Id, c => geometricUtilities.GetSolids(c));

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
                        new ClashInfoDTOs
                        {
                            Tray = tray,
                            Conduit = conduit,
                            Intersection = intersection
                        });
                }
            }

            return result;
        }

        public Solid FindSolidIntersection(
                                List<Solid> firstSolids,
                                List<Solid> secondSolids)
        {
            RouteSettingDTOs routeSettingsDTOs = new RouteSettingDTOs();
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
                            intersection.Volume > routeSettingsDTOs.MinimumIntersectionVolume)
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
        public List<ClashGroupDTOs> GroupClashesByProximity(
                                            List<ClashInfoDTOs> clashes,
                                            double horizontalMinimumClearanceFt)
        {
            List<ClashGroupDTOs> groups = new List<ClashGroupDTOs>();
            CreatingBypassRoutings creatingBypassRoutings = new CreatingBypassRoutings();

            // Group clashes by tray ID.
            var clashesByTray = clashes.GroupBy(c => c.Tray.Id).ToList();

            foreach (var trayClashGroup in clashesByTray)
            {
                ElementId trayId = trayClashGroup.Key;
                List<ClashInfoDTOs> trayClashes = trayClashGroup.ToList();

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
                Dictionary<ClashInfoDTOs, double> clashStations =
                                    new Dictionary<ClashInfoDTOs, double>();

                foreach (ClashInfoDTOs clash in trayClashes)
                {
                    if (!creatingBypassRoutings.TryGetStationRange(
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
                List<ClashInfoDTOs> sortedClashes = clashStations
                    .OrderBy(kvp => kvp.Value)
                    .Select(kvp => kvp.Key)
                    .ToList();

                // Merge clashes within horizontalMinimumClearance.
                List<List<ClashInfoDTOs>> mergedGroups = MergeProximalClashes(
                    sortedClashes,
                    clashStations,
                    horizontalMinimumClearanceFt);

                // Create ClashGroup for each merged set.
                foreach (List<ClashInfoDTOs> groupClashes in mergedGroups)
                {
                    if (groupClashes.Count == 0)
                        continue;

                    double minStation = double.MaxValue;
                    double maxStation = double.MinValue;

                    foreach (ClashInfoDTOs clash in groupClashes)
                    {
                        if (creatingBypassRoutings.TryGetStationRange(
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
                        new ClashGroupDTOs
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
        private static List<List<ClashInfoDTOs>> MergeProximalClashes(
            List<ClashInfoDTOs> sortedClashes,
            Dictionary<ClashInfoDTOs, double> stationMap,
            double clearanceFt)
        {
            List<List<ClashInfoDTOs>> merged = new List<List<ClashInfoDTOs>>();

            if (sortedClashes.Count == 0)
                return merged;

            List<ClashInfoDTOs> currentGroup = new List<ClashInfoDTOs> { sortedClashes[0] };

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
                    merged.Add(new List<ClashInfoDTOs>(currentGroup));
                    currentGroup = new List<ClashInfoDTOs> { sortedClashes[i] };
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
                                    List<ClashInfoDTOs> groupClashes,
                                    XYZ trayStart,
                                    XYZ trayDirection)
        {
            List<XYZ> centroids = new List<XYZ>();

            foreach (ClashInfoDTOs clash in groupClashes)
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

        private static bool BoxesIntersect(BoundingBoxXYZ a, BoundingBoxXYZ b, double tolerance)
        {
            if (a == null || b == null)
                return false;

            XYZ aMin = a.Min;
            XYZ aMax = a.Max;
            XYZ bMin = b.Min;
            XYZ bMax = b.Max;

            if (aMin == null || aMax == null || bMin == null || bMax == null)
                return false;

            // Expand a's bounds by tolerance
            aMin = new XYZ(aMin.X - tolerance, aMin.Y - tolerance, aMin.Z - tolerance);
            aMax = new XYZ(aMax.X + tolerance, aMax.Y + tolerance, aMax.Z + tolerance);

            // Check separation on each axis
            if (aMax.X < bMin.X || aMin.X > bMax.X) return false;
            if (aMax.Y < bMin.Y || aMin.Y > bMax.Y) return false;
            if (aMax.Z < bMin.Z || aMin.Z > bMax.Z) return false;

            return true;
        }
    }

        
}
