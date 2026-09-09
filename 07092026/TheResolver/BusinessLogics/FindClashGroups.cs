using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using System;
using System.Collections.Generic;

namespace TheResolver.BusinessLogics
{
    public class FindClashGroups
    {

        // ------------------------------------------------------------
        // UNIT CONVERSION
        // Revit internal length unit = feet
        // ------------------------------------------------------------
        private const double MM = 1.0 / 304.8;

        // ------------------------------------------------------------
        // TEST SETTINGS
        // ------------------------------------------------------------


        // Revit geometry tolerance.
        private const double GeometryTolerance = 1e-9;


        // ============================================================
        // MAIN COMMAND
        // ============================================================

        /*
         public static bool FindClashGroup(ClashInfoDTOs clash, List<ClashInfoDTOs> clashes, RouteSettingDTOs settings, out List<XYZ> points)
        {
            points = new List<XYZ>();

            if (clash == null ||
                clash.Tray == null ||
                clash.Conduit == null)
            {
                Logger.Log("Invalid clash.");
                return false;
            }

            //------------------------------------------------------
            // ORIGINAL TRAY
            //------------------------------------------------------

            LocationCurve trayLocation = clash.Tray.Location as LocationCurve;

            if (trayLocation == null)
            {
                Logger.Log("Tray has no LocationCurve.");
                return false;
            }

            Line trayLine = trayLocation.Curve as Line;

            if (trayLine == null)
            {
                Logger.Log("Only straight cable tray is supported.");
                return false;
            }

            XYZ start = trayLine.GetEndPoint(0);

            XYZ end = trayLine.GetEndPoint(1);

            XYZ dir = (end - start).Normalize();

            double trayLength = start.DistanceTo(end);

            XYZ clashPoint = GetClashPoint(clash.Intersection);

            double clashPointStation = (clashPoint - start).DotProduct(dir);

            var breakPoint1 =
                    Math.Min(
                        breakPoint1,
                        clashPointStation - settings.MinimumSideOffset);

            var breakPoint2 =
                    Math.Max(
                        breakPoint2,
                        clashPointStation + settings.MinimumSideOffset);


            List<XYZ> ClashPoints = new List<XYZ>();
            ClashPoints.Add(clashPoint);  

            foreach (ClashInfoDTOs c in clashes)
            {
                //check if the clash is on the same tray
                if (ReferenceEquals(c, clash))
                    continue;

                if (c.Tray.Id != clash.Tray.Id)
                    continue;

                double tolerance = 10* MM;

                XYZ newClashPoint = GetClashPoint(c.Intersection);
                double newClashPointStation = (newClashPoint - start).DotProduct(dir);
                if (newClashPointStation > breakPoint1-tolerance 
                    && newClashPointStation < breakPoint2+tolerance)
                {
                    ClashPoints.Add(newClashPoint);
                }                                                                                             
                
            }
            double minStation = double.MaxValue;
            double maxStation = double.MinValue;

            foreach (XYZ p in ClashPoints)
            {
                double station =
                    (p - start).DotProduct(dir);

                minStation =
                    Math.Min(minStation, station);

                maxStation =
                    Math.Max(maxStation, station);

                double centerStation =
                            (minStation + maxStation) * 0.5;

                XYZ resultantClashesCenterPoint =
                        start + dir * centerStation;

            }
            //get the center point of the clash points
            if (ClashPoints.Count > 0)
            {
                XYZ centerPoint = new XYZ(0, 0, 0);
                foreach (XYZ p in ClashPoints)
                {
                    centerPoint += p;
                }
                centerPoint /= ClashPoints.Count;             

                //add the center point to the result
                resultantClashesCenterPoint = centerPoint;
            }
            else
            {
                resultantClashesCenterPoint = clashPoint;
            }
            //------------------------------------------------------
            // TRAY UP DIRECTION
            //------------------------------------------------------

            XYZ up = XYZ.BasisZ;                  

            //------------------------------------------------------
            // CLASH STATION
            //------------------------------------------------------

            double resultantClashPoint = (resultantClashesCenterPoint - start).DotProduct(dir);

            //------------------------------------------------------
            // CLASH SIDE
            //------------------------------------------------------

            CalculatingLogics logic = new CalculatingLogics();

            ClashSide clashSide =
                            logic.DetermineClashSide(
                                clash.Tray,
                                clash.Conduit,
                                1.0 * MM);

            Logger.Log( $"Clash side = {clashSide}");

            if (clashSide == ClashSide.Unknown ||
                clashSide == ClashSide.CrossingTray)
            {
                Logger.Log(
                    "Cannot determine whether clash is above or below tray.");

                return false;
            }

            //------------------------------------------------------
            // BEND SETTINGS
            //------------------------------------------------------

            double radius = settings.BendRadius;

            double angleDegrees = settings.BendAngle;

            if (radius <= 0)
            {
                Logger.Log("Invalid bend radius.");
                return false;
            }

            if (angleDegrees <= 0 ||
                angleDegrees >= 90)
            {
                Logger.Log(
                    $"Unsupported bend angle: {angleDegrees}");

                return false;
            }

            double theta =
                angleDegrees *
                Math.PI / 180.0;

            //------------------------------------------------------
            // BEND GEOMETRY
            //
            // Vertical offset generated by TWO equal bends.
            //------------------------------------------------------

            double bendRise =
                2.0 *
                radius *
                (1.0 - Math.Cos(theta));

            bendRise *= settings.BendSafetyfactor;

            //------------------------------------------------------
            // HORIZONTAL TANGENT CONTRIBUTION
            //------------------------------------------------------

            double bendHorizontal =
                2.0 *
                radius *
                Math.Sin(theta);

            //------------------------------------------------------
            // CONDUIT / TRAY BOUNDING BOX
            //------------------------------------------------------

            BoundingBoxXYZ conduitBox =
                clash.Conduit.get_BoundingBox(null);

            BoundingBoxXYZ trayBox =
                clash.Tray.get_BoundingBox(null);

            if (conduitBox == null ||
                trayBox == null)
            {
                Logger.Log("Could not obtain clash bounding boxes.");

                return false;
            }

            //------------------------------------------------------
            // CLEARANCE REQUIREMENT
            //------------------------------------------------------

            double clearanceRise = 0.0;

            if (clashSide == ClashSide.AboveTray)
            {
                //--------------------------------------------------
                // Existing/top calculation can remain here.
                //
                // We need enough vertical movement to get above
                // the obstruction.
                //--------------------------------------------------

                clearanceRise =                    
                    trayBox.Min.Z
                    + settings.MinimumClearance;
            }
            else if (clashSide == ClashSide.BelowTray)
            {
                //--------------------------------------------------
                // BOTTOM CASE:
                //
                // We are lifting the tray away from the conduit.
                //
                // Required rise is based on the relationship
                // between the conduit and the tray.
                //--------------------------------------------------

                clearanceRise =                    
                        trayBox.Min.Z
                        + settings.MinimumClearance;
            }

            clearanceRise =
                Math.Max(0.0,clearanceRise);

            //------------------------------------------------------
            // FINAL RISE
            //
            // NEVER allow rise to be smaller than the geometric
            // requirement of the two bends.
            //------------------------------------------------------

            double rise =
                Math.Max(
                    clearanceRise,
                    bendRise);

            //------------------------------------------------------
            // DIAGONAL TRANSITION
            //
            // P1 -> P2 and P3 -> P4.
            //------------------------------------------------------

            double transition =
                rise /
                Math.Tan(theta);

            //------------------------------------------------------
            // FITTING TANGENT
            //------------------------------------------------------

            double tangentLength =
                radius *
                Math.Tan(theta / 2.0);

            //------------------------------------------------------
            // CONDUIT HORIZONTAL ENVELOPE
            //------------------------------------------------------

            double conduitWidth =
                Math.Max(
                    conduitBox.Max.X - conduitBox.Min.X,
                    conduitBox.Max.Y - conduitBox.Min.Y);

            double hostRadius = conduitWidth * 0.5;

            //------------------------------------------------------
            // REQUIRED SIDE OFFSET
            //
            // IMPORTANT:
            // Do NOT add bendHorizontal here again.
            //
            // transition already defines the diagonal horizontal
            // run.
            //------------------------------------------------------

            double sideOffset =
                  hostRadius
                + settings.MinimumSideOffset
                + tangentLength;

            //------------------------------------------------------
            // TOTAL HALF BYPASS WIDTH
            //------------------------------------------------------

            double xOffset =
                sideOffset * 0.5 +
                transition;

            //------------------------------------------------------
            // STATIONS
            //------------------------------------------------------

            double p1Station = resultantClashPoint - xOffset;

            double p2Station = resultantClashPoint - sideOffset * 0.5;

            double p3Station = resultantClashPoint + sideOffset * 0.5;

            double p4Station = resultantClashPoint + xOffset;

            //------------------------------------------------------
            // FEASIBILITY
            //------------------------------------------------------

            if (p1Station <= 0)
            {
                Logger.Log(
                    $"Insufficient space before clash. " +
                    $"P1={p1Station * 304.8:F1} mm");

                return false;
            }

            if (p4Station >= trayLength)
            {
                Logger.Log(
                    $"Insufficient space after clash. " +
                    $"P4={p4Station * 304.8:F1} mm, " +
                    $"TrayLength={trayLength * 304.8:F1} mm");

                return false;
            }

            if (p2Station <= p1Station)
            {
                Logger.Log(
                    "Invalid P1/P2 spacing.");

                return false;
            }

            if (p3Station <= p2Station)
            {
                Logger.Log(
                    "Invalid P2/P3 spacing.");

                return false;
            }

            if (p4Station <= p3Station)
            {
                Logger.Log(
                    "Invalid P3/P4 spacing.");

                return false;
            }

            //------------------------------------------------------
            // DIRECTION OF DETOUR
            //------------------------------------------------------

            XYZ detourDirection;

            if (clashSide == ClashSide.AboveTray)
            {
                detourDirection = XYZ.BasisZ;
            }
            else
            {
                detourDirection = -XYZ.BasisZ;
            }

            //------------------------------------------------------
            // POINTS
            //------------------------------------------------------

            XYZ P1 =
                start +
                dir * p1Station;

            XYZ P2 =
                start +
                dir * p2Station +
                detourDirection * rise;

            XYZ P3 =
                start +
                dir * p3Station +
                detourDirection * rise;

            XYZ P4 =
                start +
                dir * p4Station;

            //------------------------------------------------------
            // ADD POINTS
            //------------------------------------------------------

            AddPoint(points, P1);
            AddPoint(points, P2);
            AddPoint(points, P3);
            AddPoint(points, P4);

            //------------------------------------------------------
            // FINAL GEOMETRIC VALIDATION
            //------------------------------------------------------

            if (!ValidateRoute(points))
            {
                Logger.Log(
                    "Generated bypass route failed validation.");

                points.Clear();

                return false;
            }

            //------------------------------------------------------
            // DEBUG
            //------------------------------------------------------

            Logger.Log(
                $@"BYPASS ROUTING

                    Tray          : {clash.Tray.Id}
                    Conduit       : {clash.Conduit.Id}

                    Clash Side    : {clashSide}

                    Clash Station : {clashStation * 304.8:F1} mm

                    Bend Angle    : {angleDegrees:F1}°
                    Bend Radius   : {radius * 304.8:F1} mm
                    Theta         : {theta:F1}°

                    ClearanceRise : {clearanceRise * 304.8:F1} mm
                    BendRise      : {bendRise * 304.8:F1} mm
                    FinalRise     : {rise * 304.8:F1} mm

                    Transition    : {transition * 304.8:F1} mm
                    Tangent       : {tangentLength * 304.8:F1} mm
                    dir           : {dir}



                    SideOffset    : {sideOffset * 304.8:F1} mm        
                    MinimumSideOffset    : {settings.MinimumSideOffset * 304.8:F1} mm
                    XOffset       : {xOffset * 304.8:F1} mm

                    P1 Station    : {p1Station * 304.8:F1} mm
                    P2 Station    : {p2Station * 304.8:F1} mm
                    P3 Station    : {p3Station * 304.8:F1} mm
                    P4 Station    : {p4Station * 304.8:F1} mm

                    P1 : {P1}
                    P2 : {P2}
                    P3 : {P3}
                    P4 : {P4}");

            return true;
        }
         
         */


        // ============================================================




        private static XYZ GetClashPoint(Solid solid)
        {
            if (solid == null)
                return null;

            return solid.ComputeCentroid();
        }

        private static void AddPoint(List<XYZ> points,XYZ point)
        {
            if (points.Count == 0 ||
                points[
                    points.Count - 1]
                    .DistanceTo(point)
                    > 1.0 * MM)
            {
                points.Add(point);
            }
        }

        private static bool ValidateRoute(List<XYZ> points)
        {
            if (points == null ||
                points.Count < 2)
                return false;

            for (int i = 0;
                 i < points.Count - 1;
                 i++)
            {
                XYZ a = points[i];
                XYZ b = points[i + 1];

                double length = a.DistanceTo(b);
                double minSegment = length * 3;

                if (length < 50 * MM)
                {
                    Logger.Log(
                        $"Segment {i} too short.");
                    return false;
                }

                if (i > 0)
                {
                    XYZ previous =
                        (points[i] - points[i - 1]).Normalize();

                    XYZ next =
                        (points[i + 1] - points[i]).Normalize();

                    double dot =
                        previous.DotProduct(next);

                    //-------------------------------------------------
                    // 180-degree reversal is invalid.
                    //-------------------------------------------------

                    if (dot < -0.999)
                    {
                        Logger.Log($"Route reverses direction at point {i}.");

                        return false;
                    }

                    //-------------------------------------------------
                    // Straight continuation.
                    // CleanRoutePoints should already remove it.
                    //-------------------------------------------------

                    if (Math.Abs(dot - 1.0) < 1e-6)
                    {
                        Logger.Log($"Unnecessary collinear point at {i}.");

                        return false;
                    }
                }
            }

            return true;
        }


        // ============================================================
        // CLASH DATA
        // ============================================================

        public class ClashGroupInfo
        {
            public CableTray Tray { get; set; }                      

            public XYZ ClashCenterPoint { get; set; }

            public double Station { get; set; }
        }

    }
}
