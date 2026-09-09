using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using System;
using System.Collections.Generic;
using System.Linq;
using TheResolver.DTOs;

namespace TheResolver.Utilities
{
    public class GeometricUtilities
    {
        public const double MM = 1.0 / 304.8;
        public LocalSpatialIndex SpatialIndex { get; set; }

        private class ObstacleCluster
        {
            public double StationMin { get; set; }
            public double StationMax { get; set; }
            public double TargetZ { get; set; }
            public List<RectInterval> Items { get; set; } = new List<RectInterval>();
        }

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

            double trayHeight = clash.Tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.AsDouble() ?? (100 * MM);
            double trayWidth = clash.Tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.AsDouble() ?? (300 * MM);
            double clearance = settings.MinimumClearance;
            double halfCorridor = (trayWidth * 0.5) + settings.MinimumSideOffset;

            XYZ lateralDir = dir.CrossProduct(XYZ.BasisZ).Normalize();

            // 1. Gather all obstacles along the tray run in host coordinates
            BoundingBoxXYZ trayBb = clash.Tray.get_BoundingBox(null);
            double searchSpan = trayLength;
            XYZ centerPt = (start + end) * 0.5;

            BoundingBoxXYZ queryBox = new BoundingBoxXYZ
            {
                Min = new XYZ(centerPt.X - searchSpan, centerPt.Y - searchSpan, origZ - 3000 * MM),
                Max = new XYZ(centerPt.X + searchSpan, centerPt.Y + searchSpan, origZ + 3000 * MM)
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

            // 2. Project obstacles into 2D [Station, Z] inside the tray width corridor
            List<RectInterval> corridorObstacles = new List<RectInterval>();
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

                // Filter out obstacles that do not cross the tray horizontal envelope
                if (latMax < -halfCorridor || latMin > halfCorridor) continue;
                if (sMax < 0 || sMin > trayLength) continue;

                corridorObstacles.Add(new RectInterval
                {
                    StationMin = Math.Max(0, sMin),
                    StationMax = Math.Min(trayLength, sMax),
                    ZMin = obs.Box.Min.Z,
                    ZMax = obs.Box.Max.Z
                });
            }

            if (corridorObstacles.Count == 0) return false;

            // 3. Solve Elevation and Target Pocket
            double targetElevation;
            if (!SolveElevationPocket(origZ, trayHeight, clearance, settings.PreferredDirection, corridorObstacles, out targetElevation))
            {
                return false;
            }

            double deltaZ = targetElevation - origZ;
            double absDeltaZ = Math.Abs(deltaZ);
            if (absDeltaZ < 5.0 * MM) return false;

            // 4. Cluster obstacles with minimum transition space awareness
            double angleDeg = settings.BendAngle > 0 ? settings.BendAngle : 30.0;
            double angleRad = angleDeg * Math.PI / 180.0;
            double tangentLength = settings.BendRadius * Math.Tan(angleRad * 0.5);
            double idealRampRun = (absDeltaZ / Math.Tan(angleRad)) + tangentLength;
            double minFittingSpacing = Math.Max(120.0 * MM, tangentLength * 2.0);

            // Minimum gap required to plunge down and come back up
            double recoveryGapRequired = (idealRampRun * 2.0) + minFittingSpacing;

            var ordered = corridorObstacles.OrderBy(o => o.StationMin).ToList();
            List<ObstacleCluster> clusters = new List<ObstacleCluster>();
            ObstacleCluster current = new ObstacleCluster
            {
                StationMin = ordered[0].StationMin,
                StationMax = ordered[0].StationMax,
                Items = { ordered[0] }
            };

            for (int i = 1; i < ordered.Count; i++)
            {
                var next = ordered[i];
                if (next.StationMin - current.StationMax < recoveryGapRequired)
                {
                    // Merge into single continuous cluster to avoid impossible intermediate drops
                    current.StationMax = Math.Max(current.StationMax, next.StationMax);
                    current.Items.Add(next);
                }
                else
                {
                    clusters.Add(current);
                    current = new ObstacleCluster
                    {
                        StationMin = next.StationMin,
                        StationMax = next.StationMax,
                        Items = { next }
                    };
                }
            }
            clusters.Add(current);

            // 5. Build Symmetrical Trapeze Vertices for each cluster
            var profileNodes = new List<(double S, double Z)>();
            double hardEndMargin = 30.0 * MM;

            for (int i = 0; i < clusters.Count; i++)
            {
                var cl = clusters[i];
                double center = (cl.StationMin + cl.StationMax) * 0.5;
                double halfSpan = Math.Max(settings.MinimumSideOffset, (cl.StationMax - cl.StationMin) * 0.5 + settings.MinimumSideOffset);

                double sPlateauStart = center - halfSpan;
                double sPlateauEnd = center + halfSpan;

                // Adaptive ramp run (steepen angle if near tray ends)
                double availableStart = Math.Max(10.0 * MM, sPlateauStart - hardEndMargin);
                double availableEnd = Math.Max(10.0 * MM, (trayLength - hardEndMargin) - sPlateauEnd);
                double effectiveRamp = Math.Min(idealRampRun, Math.Min(availableStart, availableEnd));
                effectiveRamp = Math.Max(effectiveRamp, 50.0 * MM);

                double sEntry = sPlateauStart - effectiveRamp;
                double sExit = sPlateauEnd + effectiveRamp;

                // Clamp within bounds
                sEntry = Math.Max(hardEndMargin, sEntry);
                sExit = Math.Min(trayLength - hardEndMargin, sExit);

                if (i == 0)
                {
                    profileNodes.Add((sEntry, origZ));
                    profileNodes.Add((sPlateauStart, targetElevation));
                }
                else
                {
                    profileNodes.Add((sEntry, origZ));
                    profileNodes.Add((sPlateauStart, targetElevation));
                }

                profileNodes.Add((sPlateauEnd, targetElevation));

                if (i == clusters.Count - 1)
                {
                    profileNodes.Add((sExit, origZ));
                }
            }

            // 6. Enforce Minimum Segment Tangent Spacing for Fittings
            var cleanedNodes = new List<(double S, double Z)> { profileNodes[0] };
            for (int i = 1; i < profileNodes.Count; i++)
            {
                var prev = cleanedNodes.Last();
                var curr = profileNodes[i];
                if (Math.Abs(curr.S - prev.S) >= minFittingSpacing || Math.Abs(curr.Z - prev.Z) > 1e-4)
                {
                    cleanedNodes.Add(curr);
                }
            }

            if (cleanedNodes.Count < 4) return false;

            // 7. Map to 3D Cartesian Coordinates
            foreach (var node in cleanedNodes)
            {
                XYZ pt = start + dir * node.S + XYZ.BasisZ * (node.Z - origZ);
                points.Add(pt);
            }

            BuildCleanRoutePoints cleaner = new BuildCleanRoutePoints();
            points = cleaner.CleanRoutePoints(points);

            return points.Count >= 4;
        }

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
                if (sorted[i].ZMin <= cur.Top + 15 * MM)
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
                if (gap >= (trayHeight + 2 * clearance))
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
            foreach (var tray in trays)
            {
                BoundingBoxXYZ tBox = tray.get_BoundingBox(null);
                if (tBox == null) continue;

                foreach (var obs in obstacles)
                {
                    if (obs.Id == tray.Id) continue;
                    BoundingBoxXYZ oBox = obs.get_BoundingBox(null);
                    if (oBox == null) continue;

                    // AABB Collision Check
                    if (tBox.Min.X <= oBox.Max.X && tBox.Max.X >= oBox.Min.X &&
                        tBox.Min.Y <= oBox.Max.Y && tBox.Max.Y >= oBox.Min.Y &&
                        tBox.Min.Z <= oBox.Max.Z && tBox.Max.Z >= oBox.Min.Z)
                    {
                        results.Add(new ClashInfoDTOs
                        {
                            Tray = tray,
                            ClashElement = obs
                        });
                    }
                }
            }
            return results;
        }
    }
}