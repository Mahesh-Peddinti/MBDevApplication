using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using TheResolver.DTOs;
using static Autodesk.Revit.DB.SpecTypeId;

namespace TheResolver.Utilities
{
    public class GeometricUtilities
    {
        public const double MM = 1.0 / 304.8;
        public LocalSpatialIndex SpatialIndex { get; set; }

        private class LocalObstacleCluster
        {
            public double StationMin { get; set; }
            public double StationMax { get; set; }
            public double ZMin { get; set; }
            public double ZMax { get; set; }
            public double TargetZ { get; set; }
            public double RampRun { get; set; }
            public List<RectInterval> Items { get; set; } = new List<RectInterval>();
        }

        public class LateralInterval
        {
            public double LMin { get; set; }
            public double LMax { get; set; }
            public double SMin { get; set; }
            public double SMax { get; set; }
        }

        // =========================================================================
        // 1. VERTICAL BYPASS ROUTING (ACTIVE BASELINE TRIGGER + CLEARANCE ENVELOPE)
        // =========================================================================
        public bool TryBuildBypassRoutingPoints(ClashInfoDTOs clash, RouteSettingDTOs settings, out List<XYZ> points)
        {
            points = new List<XYZ>();
            if (clash?.Tray == null || clash.ClashElement == null || settings == null)
                return false;

            if (!(clash.Tray.Location is LocationCurve trayLoc) || !(trayLoc.Curve is Line trayLine))
                return false;

            XYZ start = trayLine.GetEndPoint(0);
            XYZ end = trayLine.GetEndPoint(1);
            XYZ dir = (end - start).Normalize();
            double trayLength = start.DistanceTo(end);
            double origZ = start.Z;

            double trayHeight = clash.Tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.AsDouble() ?? (100.0 * MM);
            double trayWidth = clash.Tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.AsDouble() ?? (300.0 * MM);
            double clearance = settings.MinimumClearance;
            double halfCorridor = (trayWidth * 0.5) + settings.MinimumSideOffset;

            // Baseline Tray Vertical Envelope
            double baselineBottomZ = origZ - (trayHeight * 0.5);
            double baselineTopZ = origZ + (trayHeight * 0.5);

            XYZ lateralDir = dir.CrossProduct(XYZ.BasisZ).Normalize();

            // 1. Gather all candidate obstacles in ROI
            double searchSpan = trayLength;
            XYZ centerPt = (start + end) * 0.5;

            BoundingBoxXYZ queryBox = new BoundingBoxXYZ
            {
                Min = new XYZ(centerPt.X - searchSpan, centerPt.Y - searchSpan, origZ - 3000.0 * MM),
                Max = new XYZ(centerPt.X + searchSpan, centerPt.Y + searchSpan, origZ + 3000.0 * MM)
            };

            List<ObstacleBounds> candidates = SpatialIndex != null
                ? SpatialIndex.QueryRoi(queryBox)
                : new List<ObstacleBounds>();

            if (candidates.Count == 0)
            {
                BoundingBoxXYZ cBb = clash.ClashElement.get_BoundingBox(null);
                if (cBb != null)
                    candidates.Add(new ObstacleBounds { Id = clash.ClashElement.UniqueId, Box = cBb, Element = clash.ClashElement });
            }

            // 2. Separate into Active Baseline Clashes vs Secondary Avoidance Objects
            var activeTriggerClashes = new List<RectInterval>();
            var secondaryAvoidance = new List<RectInterval>();

            foreach (var obs in candidates)
            {
                if (obs.Id == clash.Tray.UniqueId) continue;

                double latMin = double.MaxValue, latMax = double.MinValue;
                double sMin = double.MaxValue, sMax = double.MinValue;

                GetCorners(obs.Box, c =>
                {
                    double l = (c - start).DotProduct(lateralDir);
                    latMin = Math.Min(latMin, l);
                    latMax = Math.Max(latMax, l);

                    double s = (c - start).DotProduct(dir);
                    sMin = Math.Min(sMin, s);
                    sMax = Math.Max(sMax, s);
                });

                // Filter out elements outside the lateral corridor or past tray ends
                if (latMax < -halfCorridor || latMin > halfCorridor) continue;
                if (sMax < 0 || sMin > trayLength) continue;

                var interval = new RectInterval
                {
                    StationMin = Math.Max(0, sMin),
                    StationMax = Math.Min(trayLength, sMax),
                    ZMin = obs.Box.Min.Z,
                    ZMax = obs.Box.Max.Z
                };

                // CRITICAL CHECK: Does this obstacle physically collide with the BASELINE tray?
                bool collidesWithBaseline = (interval.ZMin < baselineTopZ && interval.ZMax > baselineBottomZ);

                if (collidesWithBaseline)
                {
                    activeTriggerClashes.Add(interval);
                }
                else
                {
                    // Clearance object only (pipes/beams above or below that don't hit the straight run)
                    secondaryAvoidance.Add(interval);
                }
            }

            // If there are no real baseline clashes on this run, no detour is needed!
            if (activeTriggerClashes.Count == 0)
            {
                return false;
            }

            // 3. Setup Fitting Kinematics
            double angleDeg = settings.BendAngle > 0 ? settings.BendAngle : 30.0;
            double angleRad = angleDeg * Math.PI / 180.0;
            double tangentLength = settings.BendRadius * Math.Tan(angleRad * 0.5);
            double minFittingSpacing = Math.Max(120.0 * MM, tangentLength * 2.0);
            /*
            // 4. Cluster ONLY Around Active Trigger Clashes with Exact Kinematic Gap Verification
            var orderedTriggers = activeTriggerClashes.OrderBy(o => o.StationMin).ToList();
            var clusters = new List<LocalObstacleCluster>();

            var currentCl = new LocalObstacleCluster
            {
                StationMin = orderedTriggers[0].StationMin,
                StationMax = orderedTriggers[0].StationMax,
                ZMin = orderedTriggers[0].ZMin,
                ZMax = orderedTriggers[0].ZMax,
                Items = { orderedTriggers[0] }
            };

            for (int i = 1; i < orderedTriggers.Count; i++)
            {
                var next = orderedTriggers[i];

                // A. Estimate actual vertical displacement ΔZ for both current cluster and next obstacle
                double targetZCurrent;
                SolveElevationPocket(origZ, trayHeight, clearance, settings.PreferredDirection, currentCl.Items, out targetZCurrent);

                double targetZNext;
                SolveElevationPocket(origZ, trayHeight, clearance, settings.PreferredDirection, new List<RectInterval> { next }, out targetZNext);

                double deltaZCurrent = Math.Abs(targetZCurrent - origZ);
                double deltaZNext = Math.Abs(targetZNext - origZ);

                // B. Calculate the exact ramp projections required along Station (S)
                double rampExit1 = deltaZCurrent / Math.Tan(angleRad);
                double rampEntry2 = deltaZNext / Math.Tan(angleRad);

                // C. Exact required clearance gap between obstacle bounding boxes:
                // [Cluster1 SideOffset] + [Ramp 1 Descent] + [Level Baseline Spacing] + [Ramp 2 Ascent] + [Cluster2 SideOffset]
                double sideMargin = Math.Max(settings.MinimumSideOffset, 50.0 * MM);
                double minMergeDistance = sideMargin + rampExit1 + minFittingSpacing + rampEntry2 + sideMargin;

                double actualGap = next.StationMin - currentCl.StationMax;

                if (actualGap < minMergeDistance)
                {
                    // Not enough physical distance to drop to baseline and rise again: Merge clusters!
                    currentCl.StationMax = Math.Max(currentCl.StationMax, next.StationMax);
                    currentCl.ZMin = Math.Min(currentCl.ZMin, next.ZMin);
                    currentCl.ZMax = Math.Max(currentCl.ZMax, next.ZMax);
                    currentCl.Items.Add(next);
                }
                else
                {
                    clusters.Add(currentCl);
                    currentCl = new LocalObstacleCluster
                    {
                        StationMin = next.StationMin,
                        StationMax = next.StationMax,
                        ZMin = next.ZMin,
                        ZMax = next.ZMax,
                        Items = { next }
                    };
                }
            }
            clusters.Add(currentCl);
            */

            // 1. Group obstacles that overlap or touch in Station (S) into Unified Vertical Columns
            var sortedTriggers = activeTriggerClashes.OrderBy(o => o.StationMin).ToList();
            var consolidatedColumns = new List<LocalObstacleCluster>();

            foreach (var obs in sortedTriggers)
            {
                // If this obstacle overlaps in station with the last column, merge vertically first
                if (consolidatedColumns.Count > 0 && obs.StationMin <= consolidatedColumns.Last().StationMax + (20.0 * MM))
                {
                    var lastCol = consolidatedColumns.Last();
                    lastCol.StationMin = Math.Min(lastCol.StationMin, obs.StationMin);
                    lastCol.StationMax = Math.Max(lastCol.StationMax, obs.StationMax);
                    lastCol.ZMin = Math.Min(lastCol.ZMin, obs.ZMin);
                    lastCol.ZMax = Math.Max(lastCol.ZMax, obs.ZMax);
                    lastCol.Items.Add(obs);
                }
                else
                {
                    consolidatedColumns.Add(new LocalObstacleCluster
                    {
                        StationMin = obs.StationMin,
                        StationMax = obs.StationMax,
                        ZMin = obs.ZMin,
                        ZMax = obs.ZMax,
                        Items = new List<RectInterval> { obs }
                    });
                }
            }

            // 2. Pre-Solve the Full Height ΔZ for Every Column
            foreach (var col in consolidatedColumns)
            {
                double targetZ;
                SolveElevationPocket(origZ, trayHeight, clearance, settings.PreferredDirection, col.Items, out targetZ);
                col.TargetZ = targetZ;
                col.RampRun = Math.Abs(targetZ - origZ) / Math.Tan(angleRad);
            }

            // 3. Cluster with Rigorous Ramp Runway Verification
            var clusters = new List<LocalObstacleCluster>();
            var currentCl = consolidatedColumns[0];

            double sideMargin = Math.Max(settings.MinimumSideOffset, 50.0 * MM);

            for (int i = 1; i < consolidatedColumns.Count; i++)
            {
                var nextCol = consolidatedColumns[i];

                // Calculate exact physical endpoints on the baseline
                // Exit of current cluster's descent ramp:
                double currentExitStation = currentCl.StationMax + sideMargin + currentCl.RampRun;

                // Entry of next column's ascent ramp:
                double nextEntryStation = nextCol.StationMin - sideMargin - nextCol.RampRun;

                // Physical gap available along the baseline between the two transitions:
                double baselineClearanceGap = nextEntryStation - currentExitStation;

                if (baselineClearanceGap < minFittingSpacing)
                {
                    // Ramps cross or lack fitting space -> Merge clusters into a single plateau!
                    currentCl.StationMax = Math.Max(currentCl.StationMax, nextCol.StationMax);
                    currentCl.ZMin = Math.Min(currentCl.ZMin, nextCol.ZMin);
                    currentCl.ZMax = Math.Max(currentCl.ZMax, nextCol.ZMax);
                    currentCl.Items.AddRange(nextCol.Items);

                    // Recalculate combined height and ramp run
                    double combinedTargetZ;
                    SolveElevationPocket(origZ, trayHeight, clearance, settings.PreferredDirection, currentCl.Items, out combinedTargetZ);
                    currentCl.TargetZ = combinedTargetZ;
                    currentCl.RampRun = Math.Abs(combinedTargetZ - origZ) / Math.Tan(angleRad);
                }
                else
                {
                    clusters.Add(currentCl);
                    currentCl = nextCol;
                }
            }
            clusters.Add(currentCl);

            // 5. Calculate Target Elevation per Cluster (Incorporating Secondary Obstacles)
            foreach (var cl in clusters)
            {
                // Find any secondary obstacles in this cluster's station zone
                var relevantSecondary = secondaryAvoidance
                    .Where(s => s.StationMax >= cl.StationMin - 200.0 * MM && s.StationMin <= cl.StationMax + 200.0 * MM)
                    .ToList();

                var clusterAllObstacles = new List<RectInterval>(cl.Items);
                clusterAllObstacles.AddRange(relevantSecondary);

                double clusterTargetZ;
                if (!SolveElevationPocket(origZ, trayHeight, clearance, settings.PreferredDirection, clusterAllObstacles, out clusterTargetZ))
                {
                    return false;
                }

                cl.TargetZ = clusterTargetZ;
                double deltaZ = Math.Abs(cl.TargetZ - origZ);
                if (deltaZ < 5.0 * MM) return false;

                cl.RampRun = deltaZ / Math.Tan(angleRad);
            }

            // 6. Build Symmetrical Nodes
            var profileNodes = new List<(double S, double Z)>();
            double minEndRunway = minFittingSpacing + (30.0 * MM);

            for (int i = 0; i < clusters.Count; i++)
            {
                var cl = clusters[i];
                double center = (cl.StationMin + cl.StationMax) * 0.5;
                double halfSpan = Math.Max(settings.MinimumSideOffset, (cl.StationMax - cl.StationMin) * 0.5 + settings.MinimumSideOffset);

                double sPlateauStart = center - halfSpan;
                double sPlateauEnd = center + halfSpan;

                double sEntry = sPlateauStart - cl.RampRun;
                double sExit = sPlateauEnd + cl.RampRun;

                if (sEntry < minEndRunway || sExit > (trayLength - minEndRunway))
                    return false;

                if (i > 0)
                {
                    var prevCl = clusters[i - 1];
                    double prevHalfSpan = Math.Max(settings.MinimumSideOffset, (prevCl.StationMax - prevCl.StationMin) * 0.5 + settings.MinimumSideOffset);
                    double prevExit = ((prevCl.StationMin + prevCl.StationMax) * 0.5) + prevHalfSpan + prevCl.RampRun;

                    if (sEntry > prevExit + minFittingSpacing)
                    {
                        profileNodes.Add((prevExit + (minFittingSpacing * 0.4), origZ));
                        profileNodes.Add((sEntry - (minFittingSpacing * 0.4), origZ));
                    }
                }

                profileNodes.Add((sEntry, origZ));
                profileNodes.Add((sPlateauStart, cl.TargetZ));
                profileNodes.Add((sPlateauEnd, cl.TargetZ));
                profileNodes.Add((sExit, origZ));
            }

            // 7. Clean Adjacent Collinear Points
            var cleanedNodes = new List<(double S, double Z)> { profileNodes[0] };
            for (int i = 1; i < profileNodes.Count; i++)
            {
                var prev = cleanedNodes.Last();
                var curr = profileNodes[i];

                if (Math.Abs(curr.S - prev.S) >= (minFittingSpacing * 0.8) || Math.Abs(curr.Z - prev.Z) > 1e-4)
                {
                    cleanedNodes.Add(curr);
                }
            }

            if (cleanedNodes.Count < 4) return false;

            // 8. Convert to Cartesian Coordinates
            foreach (var node in cleanedNodes)
            {
                XYZ pt = start + (dir * node.S) + (XYZ.BasisZ * (node.Z - origZ));
                points.Add(pt);
            }

            BuildCleanRoutePoints cleaner = new BuildCleanRoutePoints();
            points = cleaner.CleanRoutePoints(points);

            return points.Count >= 4;
        }

        // =========================================================================
        // 2. HORIZONTAL (LATERAL) CANDIDATE DETOUR GENERATOR
        // =========================================================================
        public List<RouteCandidateDTO> GenerateAllHorizontalCandidates(ClashInfoDTOs clash, RouteSettingDTOs settings)
        {
            var candidates = new List<RouteCandidateDTO>();
            if (clash?.Tray == null || clash.ClashElement == null || settings == null)
                return candidates;

            if (!(clash.Tray.Location is LocationCurve trayLoc) || !(trayLoc.Curve is Line trayLine))
                return candidates;

            XYZ start = trayLine.GetEndPoint(0);
            XYZ end = trayLine.GetEndPoint(1);
            XYZ dir = (end - start).Normalize();
            double trayLength = start.DistanceTo(end);
            double origZ = start.Z;

            double trayHeight = clash.Tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.AsDouble() ?? (100.0 * MM);
            double trayWidth = clash.Tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.AsDouble() ?? (300.0 * MM);
            double clearance = settings.MinimumClearance;

            XYZ lateralDir = dir.CrossProduct(XYZ.BasisZ).Normalize();

            double searchSpan = trayLength;
            XYZ centerPt = (start + end) * 0.5;

            BoundingBoxXYZ queryBox = new BoundingBoxXYZ
            {
                Min = new XYZ(centerPt.X - searchSpan, centerPt.Y - searchSpan, origZ - (trayHeight + clearance)),
                Max = new XYZ(centerPt.X + searchSpan, centerPt.Y + searchSpan, origZ + (trayHeight + clearance))
            };

            List<ObstacleBounds> rawObstacles = SpatialIndex != null
                ? SpatialIndex.QueryRoi(queryBox)
                : new List<ObstacleBounds>();

            if (rawObstacles.Count == 0 && clash.ClashElement != null)
            {
                var bb = clash.ClashElement.get_BoundingBox(null);
                if (bb != null)
                    rawObstacles.Add(new ObstacleBounds { Id = clash.ClashElement.UniqueId, Box = bb, Element = clash.ClashElement });
            }

            var lateralObs = new List<LateralInterval>();
            foreach (var obs in rawObstacles)
            {
                if (obs.Id == clash.Tray.UniqueId) continue;

                if (obs.Box.Max.Z < origZ - (trayHeight * 0.5) || obs.Box.Min.Z > origZ + (trayHeight * 0.5))
                    continue;

                double lMin = double.MaxValue, lMax = double.MinValue;
                double sMin = double.MaxValue, sMax = double.MinValue;

                GetCorners(obs.Box, c =>
                {
                    double l = (c - start).DotProduct(lateralDir);
                    lMin = Math.Min(lMin, l);
                    lMax = Math.Max(lMax, l);

                    double s = (c - start).DotProduct(dir);
                    sMin = Math.Min(sMin, s);
                    sMax = Math.Max(sMax, s);
                });

                lateralObs.Add(new LateralInterval
                {
                    LMin = lMin,
                    LMax = lMax,
                    SMin = Math.Max(0, sMin),
                    SMax = Math.Min(trayLength, sMax)
                });
            }

            var centerObstacle = lateralObs.FirstOrDefault(o => o.LMin <= 0 && o.LMax >= 0)
                                 ?? lateralObs.OrderBy(o => Math.Abs((o.LMin + o.LMax) * 0.5)).FirstOrDefault();

            if (centerObstacle == null) return candidates;

            var topObstacles = lateralObs
                .Where(o => o != centerObstacle && o.LMin >= centerObstacle.LMax - (10.0 * MM))
                .OrderBy(o => o.LMin)
                .ToList();

            var bottomObstacles = lateralObs
                .Where(o => o != centerObstacle && o.LMax <= centerObstacle.LMin + (10.0 * MM))
                .OrderByDescending(o => o.LMax)
                .ToList();

            double requiredChannelWidth = trayWidth + (clearance * 2.0);

            double angleDeg = settings.BendAngle > 0 ? settings.BendAngle : 30.0;
            double angleRad = angleDeg * Math.PI / 180.0;
            double tangentLength = settings.BendRadius * Math.Tan(angleRad * 0.5);
            double minFittingSpacing = Math.Max(120.0 * MM, tangentLength * 2.0);

            // 1. Inner Top Slot
            double topLimitL = topObstacles.Count > 0 ? topObstacles[0].LMin : double.MaxValue;
            double innerTopGap = topLimitL - centerObstacle.LMax;

            if (innerTopGap >= requiredChannelWidth)
            {
                double targetL = centerObstacle.LMax + clearance + (trayWidth * 0.5);
                double sClusterMin = Math.Min(centerObstacle.SMin, topObstacles.Count > 0 ? topObstacles[0].SMin : centerObstacle.SMin);
                double sClusterMax = Math.Max(centerObstacle.SMax, topObstacles.Count > 0 ? topObstacles[0].SMax : centerObstacle.SMax);

                var pts = BuildHorizontalTrapezePoints(start, dir, lateralDir, origZ, trayLength, targetL,
                    sClusterMin, sClusterMax, angleRad, minFittingSpacing, settings.MinimumSideOffset);

                if (pts.Count >= 4)
                {
                    candidates.Add(new RouteCandidateDTO
                    {
                        Name = "Inner Top Slot",
                        Plane = RoutePlane.Horizontal,
                        SlotType = LateralSlotType.InnerTop,
                        LateralOffsetMm = targetL / MM,
                        ElevationDeltaMm = 0,
                        Points = pts
                    });
                }
            }

            // 2. Inner Bottom Slot
            double btmLimitL = bottomObstacles.Count > 0 ? bottomObstacles[0].LMax : double.MinValue;
            double innerBtmGap = centerObstacle.LMin - btmLimitL;

            if (innerBtmGap >= requiredChannelWidth)
            {
                double targetL = centerObstacle.LMin - clearance - (trayWidth * 0.5);
                double sClusterMin = Math.Min(centerObstacle.SMin, bottomObstacles.Count > 0 ? bottomObstacles[0].SMin : centerObstacle.SMin);
                double sClusterMax = Math.Max(centerObstacle.SMax, bottomObstacles.Count > 0 ? bottomObstacles[0].SMax : centerObstacle.SMax);

                var pts = BuildHorizontalTrapezePoints(start, dir, lateralDir, origZ, trayLength, targetL,
                    sClusterMin, sClusterMax, angleRad, minFittingSpacing, settings.MinimumSideOffset);

                if (pts.Count >= 4)
                {
                    candidates.Add(new RouteCandidateDTO
                    {
                        Name = "Inner Bottom Slot",
                        Plane = RoutePlane.Horizontal,
                        SlotType = LateralSlotType.InnerBottom,
                        LateralOffsetMm = targetL / MM,
                        ElevationDeltaMm = 0,
                        Points = pts
                    });
                }
            }

            // 3. Outer Top Sweep
            double outerTopL = topObstacles.Count > 0
                ? topObstacles.Max(o => o.LMax)
                : centerObstacle.LMax;

            double targetOuterTopL = outerTopL + clearance + (trayWidth * 0.5);
            double sOuterTopMin = Math.Min(centerObstacle.SMin, topObstacles.Count > 0 ? topObstacles.Min(o => o.SMin) : centerObstacle.SMin);
            double sOuterTopMax = Math.Max(centerObstacle.SMax, topObstacles.Count > 0 ? topObstacles.Max(o => o.SMax) : centerObstacle.SMax);

            var outerTopPts = BuildHorizontalTrapezePoints(start, dir, lateralDir, origZ, trayLength, targetOuterTopL,
                sOuterTopMin, sOuterTopMax, angleRad, minFittingSpacing, settings.MinimumSideOffset);

            if (outerTopPts.Count >= 4)
            {
                candidates.Add(new RouteCandidateDTO
                {
                    Name = "Outer Top Sweep",
                    Plane = RoutePlane.Horizontal,
                    SlotType = LateralSlotType.OuterTop,
                    LateralOffsetMm = targetOuterTopL / MM,
                    ElevationDeltaMm = 0,
                    Points = outerTopPts
                });
            }

            // 4. Outer Bottom Sweep
            double outerBtmL = bottomObstacles.Count > 0
                ? bottomObstacles.Min(o => o.LMin)
                : centerObstacle.LMin;

            double targetOuterBtmL = outerBtmL - clearance - (trayWidth * 0.5);
            double sOuterBtmMin = Math.Min(centerObstacle.SMin, bottomObstacles.Count > 0 ? bottomObstacles.Min(o => o.SMin) : centerObstacle.SMin);
            double sOuterBtmMax = Math.Max(centerObstacle.SMax, bottomObstacles.Count > 0 ? bottomObstacles.Max(o => o.SMax) : centerObstacle.SMax);

            var outerBtmPts = BuildHorizontalTrapezePoints(start, dir, lateralDir, origZ, trayLength, targetOuterBtmL,
                sOuterBtmMin, sOuterBtmMax, angleRad, minFittingSpacing, settings.MinimumSideOffset);

            if (outerBtmPts.Count >= 4)
            {
                candidates.Add(new RouteCandidateDTO
                {
                    Name = "Outer Bottom Sweep",
                    Plane = RoutePlane.Horizontal,
                    SlotType = LateralSlotType.OuterBottom,
                    LateralOffsetMm = targetOuterBtmL / MM,
                    ElevationDeltaMm = 0,
                    Points = outerBtmPts
                });
            }

            foreach (var cand in candidates)
            {
                double totalLen = 0;
                for (int i = 0; i < cand.Points.Count - 1; i++)
                {
                    totalLen += cand.Points[i].DistanceTo(cand.Points[i + 1]);
                }
                cand.AddedLengthMm = Math.Max(0, (totalLen - trayLength) / MM);
            }

            return candidates;
        }

        private static List<XYZ> BuildHorizontalTrapezePoints(
            XYZ start,
            XYZ dir,
            XYZ lateralDir,
            double origZ,
            double trayLength,
            double targetL,
            double sMin,
            double sMax,
            double angleRad,
            double minFittingSpacing,
            double sideOffset)
        {
            var points = new List<XYZ>();
            double deltaL = Math.Abs(targetL);
            double rampRun = deltaL / Math.Tan(angleRad);

            double centerS = (sMin + sMax) * 0.5;
            double plateauHalfSpan = Math.Max(sideOffset, (sMax - sMin) * 0.5 + sideOffset);

            double sPlateauStart = centerS - plateauHalfSpan;
            double sPlateauEnd = centerS + plateauHalfSpan;

            double sEntry = sPlateauStart - rampRun;
            double sExit = sPlateauEnd + rampRun;

            double minEndRunway = minFittingSpacing + (30.0 * MM);
            if (sEntry < minEndRunway || sExit > (trayLength - minEndRunway))
                return points;

            points.Add(start + (dir * sEntry));
            points.Add(start + (dir * sPlateauStart) + (lateralDir * targetL));
            points.Add(start + (dir * sPlateauEnd) + (lateralDir * targetL));
            points.Add(start + (dir * sExit));

            var cleaner = new BuildCleanRoutePoints();
            return cleaner.CleanRoutePoints(points);
        }

        // =========================================================================
        // 3. UTILITY METHODS & EXACT SOLID BOOLEAN CLASH DETECTION
        // =========================================================================
        private static bool SolveElevationPocket(
            double origZ,
            double trayHeight,
            double clearance,
            RouteDirection preferredDir,
            List<RectInterval> obstacles,
            out double targetZ)
        {
            targetZ = origZ;
            if (obstacles == null || obstacles.Count == 0) return false; 

            double stackZMin = obstacles.Min(o => o.ZMin); 
            double stackZMax = obstacles.Max(o => o.ZMax); 

            var sorted = obstacles.OrderBy(o => o.ZMin).ToList(); 
            var distinctLayers = new List<(double Bottom, double Top)>(); 
            (double Bottom, double Top) cur = (sorted[0].ZMin, sorted[0].ZMax); 

            for (int i = 1; i < sorted.Count; i++) 
            {
                if (sorted[i].ZMin <= cur.Top + (15.0 * MM)) 
                {
                    cur = (cur.Bottom, Math.Max(cur.Top, sorted[i].ZMax)); 
                }
                else
                {
                    distinctLayers.Add(cur); 
                    cur = (sorted[i].ZMin, sorted[i].ZMax); 
                }
            }
            distinctLayers.Add(cur); 

            var candidates = new List<(double Z, double Delta, bool IsUp)>(); 

            // Below
            double belowZ = stackZMin - clearance - (trayHeight * 0.5); 
            candidates.Add((belowZ, Math.Abs(belowZ - origZ), false)); 

            // Above
            double aboveZ = stackZMax + clearance + (trayHeight * 0.5); 
            candidates.Add((aboveZ, Math.Abs(aboveZ - origZ), true)); 

            // Interstices
            for (int i = 0; i < distinctLayers.Count - 1; i++) 
            {
                double gap = distinctLayers[i + 1].Bottom - distinctLayers[i].Top; 
                if (gap >= (trayHeight + (2.0 * clearance))) 
                {
                    double pocketZ = distinctLayers[i].Top + clearance + (trayHeight * 0.5); 
                    candidates.Add((pocketZ, Math.Abs(pocketZ - origZ), pocketZ >= origZ)); 
                }
            }

            if (preferredDir == RouteDirection.Up) 
                candidates = candidates.Where(c => c.IsUp).ToList(); 
            else if (preferredDir == RouteDirection.Down) 
                candidates = candidates.Where(c => !c.IsUp).ToList(); 

            if (candidates.Count == 0) return false; 

            targetZ = candidates.OrderBy(c => c.Delta).First().Z; 
            return true; 
        }

        private static void GetCorners(BoundingBoxXYZ box, Action<XYZ> onCorner)
        {
            onCorner(new XYZ(box.Min.X, box.Min.Y, box.Min.Z)); 
            onCorner(new XYZ(box.Max.X, box.Min.Y, box.Min.Z)); 
            onCorner(new XYZ(box.Min.X, box.Max.Y, box.Min.Z)); 
            onCorner(new XYZ(box.Max.X, box.Max.Y, box.Min.Z)); 
            onCorner(new XYZ(box.Min.X, box.Min.Y, box.Max.Z)); 
            onCorner(new XYZ(box.Max.X, box.Min.Y, box.Max.Z)); 
            onCorner(new XYZ(box.Min.X, box.Max.Y, box.Max.Z)); 
            onCorner(new XYZ(box.Max.X, box.Max.Y, box.Max.Z)); 
        }

        public List<ClashInfoDTOs> FindClashes(List<CableTray> trays, List<Element> obstacles, RouteSettingDTOs settings)
        {
            var results = new List<ClashInfoDTOs>(); 
            var geomOptions = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false }; 

            foreach (var tray in trays) 
            {
                BoundingBoxXYZ tBox = tray.get_BoundingBox(null); 
                if (tBox == null) continue; 

                Solid traySolid = null; 

                foreach (var obs in obstacles) 
                {
                    if (obs.Id == tray.Id) continue; 
                    BoundingBoxXYZ oBox = obs.get_BoundingBox(null); 
                    if (oBox == null) continue; 

                    // 1. Broad Phase: Fast AABB Rejection
                    bool aabbOverlap = tBox.Min.X <= oBox.Max.X && tBox.Max.X >= oBox.Min.X &&
                                       tBox.Min.Y <= oBox.Max.Y && tBox.Max.Y >= oBox.Min.Y &&
                                       tBox.Min.Z <= oBox.Max.Z && tBox.Max.Z >= oBox.Min.Z; 

                    if (!aabbOverlap) continue; 

                    // 2. Narrow Phase: Exact Revit Solid-Solid Intersection
                    if (traySolid == null) 
                    {
                        traySolid = GetElementSolid(tray, geomOptions); 
                        if (traySolid == null || traySolid.Volume < 1e-6) break; 
                    }

                    Solid obsSolid = GetElementSolid(obs, geomOptions); 
                    if (obsSolid == null || obsSolid.Volume < 1e-6) continue; 

                    try
                    {
                        Solid intersection = BooleanOperationsUtils.ExecuteBooleanOperation(
                            traySolid,
                            obsSolid,
                            BooleanOperationsType.Intersect); 

                        if (intersection != null && intersection.Volume > 1e-6) 
                        {
                            results.Add(new ClashInfoDTOs
                            {
                                Tray = tray,
                                ClashElement = obs
                            });
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.InvalidOperationException) 
                    {
                        if (CheckCurveClearance(tray, obs, settings?.MinimumClearance ?? (25.0 * MM))) 
                        {
                            results.Add(new ClashInfoDTOs { Tray = tray, ClashElement = obs }); 
                        }
                    }
                    }
                }
                return results; 
        }

        private static Solid GetElementSolid(Element elem, Options opt)
        {
            if (elem == null) return null; 
            GeometryElement geomElem = elem.get_Geometry(opt); 
            if (geomElem == null) return null; 

            return ExtractLargestSolid(geomElem); 
        }

        private static Solid ExtractLargestSolid(GeometryElement geomElem)
        {
            Solid maxSolid = null; 
            double maxVol = 0; 

            foreach (GeometryObject obj in geomElem) 
            {
                if (obj is Solid solid && solid.Faces.Size > 0 && solid.Volume > maxVol) 
                {
                    maxSolid = solid; 
                    maxVol = solid.Volume; 
                }
                else if (obj is GeometryInstance inst) 
                {
                    GeometryElement instGeom = inst.GetInstanceGeometry(); 
                    if (instGeom != null) 
                    {
                        Solid s = ExtractLargestSolid(instGeom); 
                        if (s != null && s.Volume > maxVol) 
                        {
                            maxSolid = s; 
                            maxVol = s.Volume; 
                        }
                    }
                }
            }
            return maxSolid; 
        }

        private static bool CheckCurveClearance(CableTray tray, Element obs, double requiredClearance)
        {
            if (!(tray.Location is LocationCurve lc1) || !(lc1.Curve is Line line1)) return false; 

            if (obs.Location is LocationCurve lc2 && lc2.Curve is Line line2) 
            {
                return line1.Distance(line2.GetEndPoint(0)) < requiredClearance || 
                       line1.Distance(line2.GetEndPoint(1)) < requiredClearance; 
            }

            return false; 
        }
    }
}