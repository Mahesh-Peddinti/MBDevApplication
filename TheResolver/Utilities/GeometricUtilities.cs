using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using System;
using System.Collections.Generic;
using System.Linq;
using TheResolver.BusinessLogics;
using TheResolver.DTOs;
using TheResolver.Services;

namespace TheResolver.Utilities
{
    public class GeometricUtilities
    {
        private const double MM = 1.0 / 304.8;
        private const double GeometryTolerance = 1e-9;

        public LocalSpatialIndex SpatialIndex { get; set; }

        public List<ClashInfoDTOs> FindClashes(List<CableTray> trays, List<Element> multiCategoryElements, RouteSettingDTOs settings)
        {
            List<ClashInfoDTOs> result = new List<ClashInfoDTOs>();
            Dictionary<string, BoundingBoxXYZ> trayBoxes = new Dictionary<string, BoundingBoxXYZ>();

            foreach (CableTray tray in trays)
            {
                if (!trayBoxes.ContainsKey(tray.UniqueId))
                    trayBoxes.Add(tray.UniqueId, tray.get_BoundingBox(null));
            }

            Dictionary<string, BoundingBoxXYZ> clashElementBoxes = new Dictionary<string, BoundingBoxXYZ>();
            foreach (Element e in multiCategoryElements)
            {
                try
                {
                    BoundingBoxXYZ bb = e.get_BoundingBox(null);
                    if (bb != null && !clashElementBoxes.ContainsKey(e.UniqueId))
                        clashElementBoxes.Add(e.UniqueId, bb);
                }
                catch { }
            }

            foreach (CableTray tray in trays)
            {
                if (!trayBoxes.TryGetValue(tray.UniqueId, out BoundingBoxXYZ trayBox) || trayBox == null)
                    continue;

                foreach (Element elem in multiCategoryElements)
                {
                    if (tray.UniqueId == elem.UniqueId) continue;
                    if (!clashElementBoxes.TryGetValue(elem.UniqueId, out BoundingBoxXYZ clashElementBox) || clashElementBox == null)
                        continue;

                    if (!BoxesIntersect(trayBox, clashElementBox, 1.0 * MM))
                        continue;

                    List<Solid> traySolids = GetSolids(tray);
                    List<Solid> elementSolids = GetSolids(elem);
                    Solid intersection = null;

                    foreach (Solid ts in traySolids)
                    {
                        foreach (Solid es in elementSolids)
                        {
                            try
                            {
                                Solid test = BooleanOperationsUtils.ExecuteBooleanOperation(ts, es, BooleanOperationsType.Intersect);
                                if (test != null && test.Volume > GeometryTolerance)
                                {
                                    intersection = test;
                                    break;
                                }
                            }
                            catch { }
                        }
                        if (intersection != null) break;
                    }

                    if (intersection != null)
                    {
                        result.Add(new ClashInfoDTOs
                        {
                            Tray = tray,
                            ClashElement = elem,
                            Intersection = intersection,
                            Name = tray.Id.ToString()
                        });
                    }
                }
            }
            return result;
        }

        public bool TryBuildBypassRoutingPoints(ClashInfoDTOs clash, RouteSettingDTOs settings, out List<XYZ> points)
        {
            points = new List<XYZ>();
            if (clash?.Tray == null || clash.ClashElement == null || settings == null)
                return false;

            if (!(clash.Tray.Location is LocationCurve trayLocation) || !(trayLocation.Curve is Line trayLine))
                return false;

            XYZ start = trayLine.GetEndPoint(0);
            XYZ end = trayLine.GetEndPoint(1);
            XYZ trayDirection = (end - start).Normalize();
            double trayLength = start.DistanceTo(end);

            XYZ clashCentroid = null;
            try { clashCentroid = clash.Intersection?.ComputeCentroid(); } catch { }

            if (clashCentroid == null)
            {
                BoundingBoxXYZ tBox = clash.Tray.get_BoundingBox(null);
                BoundingBoxXYZ cBox = clash.ClashElement.get_BoundingBox(null);
                if (tBox != null && cBox != null)
                {
                    clashCentroid = new XYZ(
                        (Math.Max(tBox.Min.X, cBox.Min.X) + Math.Min(tBox.Max.X, cBox.Max.X)) * 0.5,
                        (Math.Max(tBox.Min.Y, cBox.Min.Y) + Math.Min(tBox.Max.Y, cBox.Max.Y)) * 0.5,
                        (Math.Max(tBox.Min.Z, cBox.Min.Z) + Math.Min(tBox.Max.Z, cBox.Max.Z)) * 0.5
                    );
                }
            }

            if (clashCentroid == null)
                return false;

            double clashStation = (clashCentroid - start).DotProduct(trayDirection);
            double searchSpan = 3500.0 * MM;

            XYZ roiCenter = start + trayDirection * clashStation;
            BoundingBoxXYZ roiBox = new BoundingBoxXYZ
            {
                Min = new XYZ(roiCenter.X - searchSpan, roiCenter.Y - searchSpan, roiCenter.Z - 2000 * MM),
                Max = new XYZ(roiCenter.X + searchSpan, roiCenter.Y + searchSpan, roiCenter.Z + 2000 * MM)
            };

            List<ObstacleBounds> localObstacles = SpatialIndex != null
                ? SpatialIndex.QueryRoi(roiBox)
                : new List<ObstacleBounds>();

            if (localObstacles.Count == 0)
            {
                var pBox = clash.ClashElement.get_BoundingBox(null);
                if (pBox != null)
                    localObstacles.Add(new ObstacleBounds { Id = clash.ClashElement.UniqueId, Box = pBox, Element = clash.ClashElement });
            }

            double trayHeight = clash.Tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.AsDouble() ?? (100 * MM);
            double trayWidth = clash.Tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.AsDouble() ?? (300 * MM);
            double requiredClearance = settings.MinimumClearance;
            double halfWidth = (trayWidth * 0.5) + settings.MinimumSideOffset;

            XYZ lateralDir = trayDirection.CrossProduct(XYZ.BasisZ).Normalize();

            List<RectInterval> allCorridorObstacles = new List<RectInterval>();
            foreach (var obs in localObstacles)
            {
                if (obs.Id == clash.Tray.UniqueId) continue;

                double latProjMin = double.MaxValue, latProjMax = double.MinValue;
                double stMin = double.MaxValue, stMax = double.MinValue;

                GetBoundingBoxCorners(obs.Box, corner =>
                {
                    double l = (corner - start).DotProduct(lateralDir);
                    latProjMin = Math.Min(latProjMin, l);
                    latProjMax = Math.Max(latProjMax, l);

                    double s = (corner - start).DotProduct(trayDirection);
                    stMin = Math.Min(stMin, s);
                    stMax = Math.Max(stMax, s);
                });

                if (latProjMax < -halfWidth || latProjMin > halfWidth)
                    continue;

                if (stMax < clashStation - searchSpan || stMin > clashStation + searchSpan)
                    continue;

                allCorridorObstacles.Add(new RectInterval
                {
                    StationMin = stMin - settings.MinimumSideOffset,
                    StationMax = stMax + settings.MinimumSideOffset,
                    ZMin = obs.Box.Min.Z,
                    ZMax = obs.Box.Max.Z
                });
            }

            if (allCorridorObstacles.Count == 0)
                return false;

            // Target Elevation Selection (Interval Window Slicing with Bottom Clearance Rule)
            double origZ = start.Z;
            double targetElevation;
            if (!TryFindOptimalElevationSlot(
                    origZ,
                    trayHeight,
                    requiredClearance,
                    settings.PreferredDirection,
                    allCorridorObstacles,
                    clashStation,
                    out targetElevation))
            {
                return false;
            }

            // Contextual Filtering: Keep ONLY obstacles that occupy the vertical elevation band of this route
            double routeZMin = Math.Min(origZ, targetElevation) - (trayHeight * 0.5 + requiredClearance);
            double routeZMax = Math.Max(origZ, targetElevation) + (trayHeight * 0.5 + requiredClearance);

            var activeObstacles = allCorridorObstacles
                .Where(o => o.ZMax >= routeZMin && o.ZMin <= routeZMax)
                .ToList();

            if (activeObstacles.Count == 0)
                activeObstacles = allCorridorObstacles;

            // Horizontal Symmetry centered around active clash cluster
            double clusterStMin = activeObstacles.Min(o => o.StationMin);
            double clusterStMax = activeObstacles.Max(o => o.StationMax);
            double clusterCenter = (clusterStMin + clusterStMax) * 0.5;
            double plateauHalfLength = Math.Max(settings.MinimumSideOffset, (clusterStMax - clusterStMin) * 0.5);

            double angleRad = settings.BendAngle * Math.PI / 180.0;
            double deltaZ = targetElevation - origZ;
            double absDeltaZ = Math.Abs(deltaZ);

            if (absDeltaZ < 10.0 * MM)
                return false;

            double rampRun = absDeltaZ / Math.Tan(angleRad);
            double tangentLength = settings.BendRadius * Math.Tan(angleRad * 0.5);
            rampRun = Math.Max(rampRun, 60.0 * MM) + tangentLength;

            double s2 = clusterCenter - plateauHalfLength;
            double s3 = clusterCenter + plateauHalfLength;
            double s1 = s2 - rampRun;
            double s4 = s3 + rampRun;

            double minMargin = 50.0 * MM;
            if (s1 < minMargin || s4 > (trayLength - minMargin))
                return false;

            XYZ p1 = start + trayDirection * s1;
            XYZ p2 = start + trayDirection * s2 + XYZ.BasisZ * deltaZ;
            XYZ p3 = start + trayDirection * s3 + XYZ.BasisZ * deltaZ;
            XYZ p4 = start + trayDirection * s4;

            points.Add(p1);
            points.Add(p2);
            points.Add(p3);
            points.Add(p4);

            BuildCleanRoutePoints cleaner = new BuildCleanRoutePoints();
            points = cleaner.CleanRoutePoints(points);

            return points.Count >= 4;
        }

        private static bool TryFindOptimalElevationSlot(
            double origZ,
            double trayHeight,
            double clearance,
            RouteDirection preferredDir,
            List<RectInterval> obstacles,
            double clashStation,
            out double selectedZ)
        {
            selectedZ = origZ;

            var localObstacles = obstacles
                .Where(o => o.StationMin <= clashStation + 1500 * MM && o.StationMax >= clashStation - 1500 * MM)
                .OrderBy(o => o.ZMin)
                .ToList();

            if (localObstacles.Count == 0)
                localObstacles = obstacles.OrderBy(o => o.ZMin).ToList();

            // Distinct physical obstacle layers sorted vertically
            var distinctLayers = new List<(double Bottom, double Top)>();
            var cur = (localObstacles[0].ZMin, localObstacles[0].ZMax);

            for (int i = 1; i < localObstacles.Count; i++)
            {
                if (localObstacles[i].ZMin <= cur.Item2 + 20 * MM)
                {
                    cur = (cur.Item1, Math.Max(cur.Item2, localObstacles[i].ZMax));
                }
                else
                {
                    distinctLayers.Add(cur);
                    cur = (localObstacles[i].ZMin, localObstacles[i].ZMax);
                }
            }
            distinctLayers.Add(cur);

            var candidateSlots = new List<(double TargetZ, double DeltaFromOrig, bool IsUp)>();

            // Option 1: Route Below the lowest layer
            double belowCenterZ = distinctLayers.First().Bottom - clearance - (trayHeight * 0.5);
            candidateSlots.Add((belowCenterZ, Math.Abs(belowCenterZ - origZ), false));

            // Option 2: Route Above the highest layer
            double aboveCenterZ = distinctLayers.Last().Top + clearance + (trayHeight * 0.5);
            candidateSlots.Add((aboveCenterZ, Math.Abs(aboveCenterZ - origZ), true));

            // Option 3: Pocket spaces (Interstices) with Bottom-of-Tray locked to exact Clearance
            for (int i = 0; i < distinctLayers.Count - 1; i++)
            {
                double obstacleBelowTop = distinctLayers[i].Top;
                double obstacleAboveBottom = distinctLayers[i + 1].Bottom;
                double totalPocketGap = obstacleAboveBottom - obstacleBelowTop;

                // Test if the required envelope (Tray + bottom clearance + top clearance) fits
                if (totalPocketGap >= (trayHeight + 2 * clearance))
                {
                    // Centerline position guaranteeing exact clearance at the bottom
                    double pocketCenterZ = obstacleBelowTop + clearance + (trayHeight * 0.5);
                    bool isUp = pocketCenterZ >= origZ;
                    candidateSlots.Add((pocketCenterZ, Math.Abs(pocketCenterZ - origZ), isUp));
                }
            }

            if (preferredDir == RouteDirection.Up)
                candidateSlots = candidateSlots.Where(s => s.IsUp).ToList();
            else if (preferredDir == RouteDirection.Down)
                candidateSlots = candidateSlots.Where(s => !s.IsUp).ToList();

            if (candidateSlots.Count == 0)
                return false;

            selectedZ = candidateSlots.OrderBy(s => s.DeltaFromOrig).First().TargetZ;
            return true;
        }

        private static void GetBoundingBoxCorners(BoundingBoxXYZ box, Action<XYZ> action)
        {
            action(new XYZ(box.Min.X, box.Min.Y, box.Min.Z));
            action(new XYZ(box.Max.X, box.Min.Y, box.Min.Z));
            action(new XYZ(box.Min.X, box.Max.Y, box.Min.Z));
            action(new XYZ(box.Max.X, box.Max.Y, box.Min.Z));
            action(new XYZ(box.Min.X, box.Min.Y, box.Max.Z));
            action(new XYZ(box.Max.X, box.Min.Y, box.Max.Z));
            action(new XYZ(box.Min.X, box.Max.Y, box.Max.Z));
            action(new XYZ(box.Max.X, box.Max.Y, box.Max.Z));
        }

        private static bool BoxesIntersect(BoundingBoxXYZ a, BoundingBoxXYZ b, double tolerance)
        {
            return a.Min.X <= b.Max.X + tolerance && a.Max.X + tolerance >= b.Min.X &&
                   a.Min.Y <= b.Max.Y + tolerance && a.Max.Y + tolerance >= b.Min.Y &&
                   a.Min.Z <= b.Max.Z + tolerance && a.Max.Z + tolerance >= b.Min.Z;
        }

        private static List<Solid> GetSolids(Element element)
        {
            var solids = new List<Solid>();
            Options opt = new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = true, ComputeReferences = true };
            GeometryElement geo = element.get_Geometry(opt);
            if (geo == null) return solids;
            ExtractSolidsRecursive(geo, Transform.Identity, solids);
            return solids;
        }

        private static void ExtractSolidsRecursive(GeometryElement geo, Transform transform, List<Solid> solids)
        {
            foreach (GeometryObject obj in geo)
            {
                if (obj is Solid solid && solid.Volume > 1e-6)
                {
                    solids.Add(SolidUtils.CreateTransformed(solid, transform));
                }
                else if (obj is GeometryInstance gi)
                {
                    ExtractSolidsRecursive(gi.GetInstanceGeometry(), transform.Multiply(gi.Transform), solids);
                }
            }
        }

        public class RectInterval
        {
            public double StationMin { get; set; }
            public double StationMax { get; set; }
            public double ZMin { get; set; }
            public double ZMax { get; set; }
        }
    }
}