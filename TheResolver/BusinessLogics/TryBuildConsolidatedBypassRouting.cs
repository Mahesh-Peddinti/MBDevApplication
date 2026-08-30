using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using TheResolver.DTOs;
using TheResolver.Services;
using TheResolver.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using static TheResolver.ModelService;

namespace TheResolver.BusinessLogics
{
    public class TryBuildConsolidatedBypassRouting
    {
        // ------------------------------------------------------------
        // UNIT CONVERSION
        // Revit internal length unit = feet
        // ------------------------------------------------------------
        private const double MM = 1.0 / 304.8;
        public  bool TryBuildConsolidatedBypassPoints(
                                        CableTray tray,
                                        List<ClashInfoDTOs> trayClashes,
                                        RouteSettingDTOs settings,
                                        out List<XYZ> points)
        {
            CalculatingLogics calculatingLogics = new CalculatingLogics();
            ValidateNewRoute validateNewRoute = new ValidateNewRoute();
            BuildCleanRoutePoints buildCleanRoutePoints = new BuildCleanRoutePoints();

            points = new List<XYZ>();

            if (tray == null ||!tray.IsValidObject)
                return false;

            if (trayClashes == null || trayClashes.Count == 0)
                return false;

            //------------------------------------------------------
            // GET TRAY LINE
            //------------------------------------------------------

            LocationCurve location = tray.Location as LocationCurve;

            if (location == null)
                return false;

            Line trayLine = location.Curve as Line;

            if (trayLine == null)
                return false;

            XYZ trayStart = trayLine.GetEndPoint(0);

            XYZ trayEnd = trayLine.GetEndPoint(1);

            XYZ dir = (trayEnd - trayStart).Normalize();

            double trayLength = trayStart.DistanceTo(trayEnd);
            //------------------------------------------------------
            // CLASH GROUPS
            //------------------------------------------------------
            //
            XYZ consolidatedCenter;
            if (!GroupClashesByTray(tray, 
                                    trayStart, 
                                    trayEnd, 
                                    dir, 
                                    trayClashes,
                                    settings, 
                                    out consolidatedCenter))
            {
                Logger.Log($"Tray {tray.Id}: could not group clashes.");
                return false;
            }


            //------------------------------------------------------
            // CONSOLIDATE ALL CLASHES
            //------------------------------------------------------

            ConsolidatedClashRegion region;

            if (!TryGetConsolidatedClashRegion(
                    tray,
                    trayClashes,
                    settings,
                    out region))
            {
                return false;
            }


            double centerClashStation = region.CenterStation;

            XYZ centerClashPoint = region.CenterPoint;

            double clashStation =(centerClashPoint- trayStart).DotProduct(dir);


            //------------------------------------------------------
            // ANGLE
            //------------------------------------------------------

            double theta =
                settings.BendAngle *
                Math.PI / 180.0;

            if (theta <= 0.001)
                return false;

            //------------------------------------------------------
            // BEND GEOMETRY
            //------------------------------------------------------

            double tangentLength =
                settings.BendRadius *
                Math.Tan(theta / 2.0);

            //------------------------------------------------------
            // REQUIRED RISE
            //------------------------------------------------------
            
            double geometricRise =
                calculatingLogics.CalculateMinimumBendRise(
                    settings.BendRadius,
                    settings.BendAngle);

            geometricRise *= settings.BendSafetyfactor;

            //------------------------------------------------------
            // PHYSICAL CLEARANCE
            //------------------------------------------------------

            double requiredRise =
                CalculateRequiredRiseForClashes(
                    tray,
                    trayClashes,
                    settings);

            double finalRise =
                        Math.Max(
                            requiredRise,
                            geometricRise);

            //------------------------------------------------------
            // CONSOLIDATED CLASH RANGE
            //------------------------------------------------------

            double minClashStation =  region.MinStation;

            double maxClashStation =  region.MaxStation;
           

            //------------------------------------------------------
            // ADD SAFETY SIDE OFFSET
            //------------------------------------------------------

            double sideOffset =
                settings.MinimumSideOffset;

            //------------------------------------------------------
            // TRANSITION DISTANCE
            //------------------------------------------------------

            double transition =
                finalRise /
                Math.Tan(theta);

            //------------------------------------------------------
            // BEND START / END
            //------------------------------------------------------

            double p1Station = clashStation +                
                sideOffset -
                transition -
                tangentLength;

            double p4Station = clashStation+
                sideOffset +
                transition +
                tangentLength;

            //------------------------------------------------------
            // CHECK AVAILABLE TRAY LENGTH
            //------------------------------------------------------

            if (p1Station <= 0)
            {
                Logger.Log(
                    "Insufficient tray length before clash.");

                return false;
            }

            if (p4Station >= trayLength)
            {
                Logger.Log(
                    "Insufficient tray length after clash.");

                return false;
            }

            //------------------------------------------------------
            // INNER ELEVATED SECTION
            //------------------------------------------------------

            double p2Station =
                minClashStation -
                sideOffset;

            double p3Station =
                maxClashStation +
                sideOffset;

            if (p2Station <= p1Station)
            {
                Logger.Log(
                    "P2 is not after P1.");

                return false;
            }

            if (p3Station <= p2Station)
            {
                Logger.Log(
                    "Invalid elevated section.");

                return false;
            }

            if (p4Station <= p3Station)
            {
                Logger.Log(
                    "P4 is not after P3.");

                return false;
            }

            //------------------------------------------------------
            // CREATE POINTS
            //------------------------------------------------------

            XYZ P1 =
                trayStart +
                dir * p1Station;

            XYZ P2 =
                trayStart +
                dir * p2Station +
                XYZ.BasisZ * finalRise;

            XYZ P3 =
                trayStart +
                dir * p3Station +
                XYZ.BasisZ * finalRise;

            XYZ P4 =
                trayStart +
                dir * p4Station;

            //------------------------------------------------------
            // ADD ROUTE
            //------------------------------------------------------

            points.Add(P1);
            points.Add(P2);
            points.Add(P3);
            points.Add(P4);

            //------------------------------------------------------
            // CLEAN
            //------------------------------------------------------

            points = buildCleanRoutePoints.CleanRoutePoints(points);

            if (!validateNewRoute.ValidateRoute(points))
                return false;

            //------------------------------------------------------
            // DEBUG
            //------------------------------------------------------

            Logger.Log(
                $@"
                    ========================================
                    CONSOLIDATED BYPASS
                    ========================================

                    Tray:{tray.Id}

                    Number of clashes: {trayClashes.Count}

                    Clash Min:{minClashStation * 304.8:F1} mm

                    Clash Max: {maxClashStation * 304.8:F1} mm

                    Clash Center: {region.CenterStation * 304.8:F1} mm

                    Rise: {finalRise * 304.8:F1} mm

                    Side Offset: {sideOffset * 304.8:F1} mm

                    Transition:{transition * 304.8:F1} mm

                    Tangent: {tangentLength * 304.8:F1} mm

                    P1: {P1}

                    P2: {P2}

                    P3: {P3}

                    P4: {P4}

                    ======================================== ");

            return true;
        }


        public bool GroupClashesByTray(
                                    CableTray cableTray,
                                    XYZ trayStartPoint,
                                    XYZ trayEndPoint,
                                    XYZ trayDirection,
                                    List<ClashInfoDTOs> allClashes,
                                    RouteSettingDTOs settings,
                                    out XYZ ConsolidatedCenter)
        {
            ConsolidatedCenter = null;
            List<ClashInfoGroup> clashGroups = new List<ClashInfoGroup>();
            foreach (var clash in allClashes)
            {
                if (cableTray.Id != clash.Tray.Id)
                    continue;
                List<XYZ> clashPoints = new List<XYZ>();

                XYZ clashPoint = GetClashPoint(clash.Intersection);

                double start = trayStartPoint.DotProduct(trayDirection);
                double end = trayEndPoint.DotProduct(trayDirection);

                double tempStation = (clashPoint - trayStartPoint).DotProduct(trayDirection);

                if (!(tempStation >= start && tempStation <= end))
                    continue;

                clashPoints.Add(clashPoint);

                if (clashPoints.Count > 1)
                {
                    // Consolidate all clash points into a single point (average)
                    XYZ consolidatedPoint = clashPoints.Aggregate((p1, p2) => p1 + p2) / clashPoints.Count;                

                    ConsolidatedCenter = consolidatedPoint;
                }

                clashGroups.Add(new ClashInfoGroup
                {
                    Tray = cableTray,
                    ConsolidatedClashCenter = clashPoint
                });

                // remove the clash from the list to avoid processing it again
                allClashes.Remove(clash);                               
            }
            return true;
        }
        public class ClashInfoGroup
        {
            public CableTray Tray { get; set; }
            public List<ClashInfoDTOs> Clashes { get; set; }
            public XYZ ConsolidatedClashCenter { get; set; }
        }


        private static bool TryGetConsolidatedClashRegion(
                                    CableTray tray,
                                    List<ClashInfoDTOs> trayClashes,
                                    RouteSettingDTOs settings,
                                    out ConsolidatedClashRegion region)
        {
            region = null;

            if (tray == null ||
                !tray.IsValidObject)
                return false;

            if (trayClashes == null ||
                trayClashes.Count == 0)
                return false;

            LocationCurve location = tray.Location as LocationCurve;

            if (location == null)
                return false;

            Line trayLine = location.Curve as Line;

            if (trayLine == null)
            {
                Logger.Log(
                    $"Tray {tray.Id}: only straight tray supported.");

                return false;
            }

            XYZ trayStart = trayLine.GetEndPoint(0);

            XYZ trayEnd = trayLine.GetEndPoint(1);

            XYZ trayDirection = (trayEnd - trayStart).Normalize();

            double trayLength = trayStart.DistanceTo(trayEnd);

            //------------------------------------------------------
            // COLLECT CLASH STATIONS
            //------------------------------------------------------

            var stations = new List<double>();

            var clashPoints = new List<XYZ>();

            foreach (ClashInfoDTOs clash in trayClashes)
            {
                XYZ clashPoint = GetClashPoint(clash.Intersection);

                if (clashPoint == null)
                {
                    Logger.Log( $"Tray {tray.Id}: could not determine clash point.");

                    continue;
                }

                //--------------------------------------------------
                // Project clash point onto tray centerline
                //--------------------------------------------------

                IntersectionResult projection = trayLine.Project(clashPoint);

                if (projection == null)
                    continue;

                double station = (projection.XYZPoint - trayStart)
                            .DotProduct(trayDirection);

                //--------------------------------------------------
                // Ignore clash outside tray
                //--------------------------------------------------

                if (station < 0 ||
                    station > trayLength)
                {
                    Logger.Log(
                        $"Tray {tray.Id}: clash outside tray range. " +
                        $"Station={station * 304.8:F1} mm");

                    continue;
                }

                stations.Add(station);

                XYZ projectedPoint =
                    trayStart +
                    trayDirection * station;

                clashPoints.Add(projectedPoint);
                

                Logger.Log(
                    $"Tray {tray.Id}: clash station = " +
                    $"{station * 304.8:F1} mm");
            }

            if (stations.Count == 0)
                return false;

            //------------------------------------------------------
            // CONSOLIDATED CLASH RANGE
            //------------------------------------------------------

            double expansion = settings.MinimumSideOffset + settings.BendRadius;

            double minStation =
                Math.Max(
                    0.0,
                    stations.Min() - expansion);

            double maxStation =
                Math.Min(
                    trayLength,
                    stations.Max() + expansion);

            if (maxStation <= minStation)
            {
                Logger.Log(
                    "Invalid consolidated clash region.");

                return false;
            }
            double length =   maxStation - minStation;

            if (length < 100 * MM)
            {
                Logger.Log(
                    "Consolidated clash region too small.");

                return false;
            }


            //------------------------------------------------------
            // CENTER OF THE CLASH ENVELOPE
            //------------------------------------------------------

            double centerStation =  stations.Average();

            XYZ centerPoint =
                trayStart +
                trayDirection * centerStation;

            XYZ minPoint =
                trayStart +
                trayDirection * minStation;

            XYZ maxPoint =
                trayStart +
                trayDirection * maxStation;

            //------------------------------------------------------
            // RESULT
            //------------------------------------------------------

            region =
                new ConsolidatedClashRegion
                {
                    CenterPoint = centerPoint,

                    MinStation = minStation,

                    MaxStation = maxStation,

                    CenterStation = centerStation,

                    MinPoint = minPoint,

                    MaxPoint = maxPoint,

                    Clashes = trayClashes,

                    ClashPoints = clashPoints
                };

            //------------------------------------------------------
            // DEBUG
            //------------------------------------------------------

            Logger.Log(
                $@"
                    ========================================
                    CONSOLIDATED CLASH REGION
                    ========================================

                    Tray       : {tray.Id}

                    Clash Count:
                        {trayClashes.Count}

                    Min Station:
                        {minStation * 304.8:F1} mm

                    Max Station:
                        {maxStation * 304.8:F1} mm

                    Center Station:
                        {centerStation * 304.8:F1} mm

                    Center Point:
                        {centerPoint}

                    ========================================
                    ");

            return true;
        }

        private static XYZ GetClashPoint(Solid solid)
        {
            if (solid == null)
                return null;

            return solid.ComputeCentroid();
        }

        private static List<ClashInfoDTOs> GetClashesForTray(
                                            CableTray tray,
                                            List<ClashInfoDTOs> allClashes)
        {
            var result = new List<ClashInfoDTOs>();

            if (tray == null ||
                !tray.IsValidObject ||
                allClashes == null)
                return result;

            ElementId trayId = tray.Id;

            foreach (ClashInfoDTOs clash in allClashes)
            {
                if (clash == null)
                    continue;

                if (clash.Tray == null ||
                    !clash.Tray.IsValidObject)
                    continue;

                if (clash.Tray.Id != trayId)
                    continue;

                if (clash.Conduit == null ||
                    !clash.Conduit.IsValidObject)
                    continue;

                result.Add(clash);
            }

            return result;
        }


        private static double CalculateRequiredRiseForClashes(
                                            CableTray tray,
                                            List<ClashInfoDTOs> clashes,
                                            RouteSettingDTOs settings)
        {
            CalculatingLogics calculatingLogics = new CalculatingLogics();
            double maximumRise = 0.0;

            foreach (ClashInfoDTOs clash in clashes)
            {
                double rise =
                    calculatingLogics.CalculateRequiredRise(
                        tray,
                        clash.Conduit,
                        0.0,
                        settings.BendRadius,
                        settings.BendAngle,
                        0.0);

                maximumRise =
                    Math.Max(
                        maximumRise,
                        rise);
            }

            return maximumRise;
        }        

        private class ConsolidatedClashRegion
        {
            public XYZ CenterPoint { get; set; }

            public double MinStation { get; set; }

            public double MaxStation { get; set; }

            public double CenterStation { get; set; }

            public List<ClashInfoDTOs> Clashes { get; set; }

            public XYZ MinPoint { get; set; }

            public XYZ MaxPoint { get; set; }

            public List<XYZ> ClashPoints { get; set; }
        }

    }
}
