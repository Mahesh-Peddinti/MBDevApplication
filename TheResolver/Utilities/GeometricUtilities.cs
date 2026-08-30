using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using TheResolver.BusinessLogics;
using TheResolver.DTOs;
using TheResolver.Services;

namespace TheResolver.Utilities
{
    public class GeometricUtilities
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
        // CLASH DETECTION - BRINGS ALL THE CLASHES IN THE MODEL
        // ============================================================
        #region
        public  List<ClashInfoDTOs> FindClashes(   List<CableTray> trays, 
                                                   List<Element> multiCategoryElements, 
                                                   RouteSettingDTOs settings)
        {
            List<ClashInfoDTOs> result = new List<ClashInfoDTOs>();

            // --------------------------------------------------------
            // CACHE BOUNDING BOXES
            // --------------------------------------------------------

            // Keyed by UniqueId rather than ElementId: the host document and
            // every linked document reuse the same numeric element ids, so a
            // long key silently collides as soon as links are in the mix.
            Dictionary<string, BoundingBoxXYZ> trayBoxes =
                new Dictionary<string, BoundingBoxXYZ>();

            foreach (CableTray tray in trays)
            {
                if (!trayBoxes.ContainsKey(tray.UniqueId))
                {
                    trayBoxes.Add(
                        tray.UniqueId,
                        tray.get_BoundingBox(null));
                }
            }

            Dictionary<string, BoundingBoxXYZ> clashElementBoxes =
                                                new Dictionary<string, BoundingBoxXYZ>();

            foreach (Element e in multiCategoryElements)
            {
                try
                {
                    BoundingBoxXYZ bb = e.get_BoundingBox(null);

                    if (bb == null)
                    {
                        Logger.Log($"NULL BB : {e.Id} | {e.Category?.Name}");

                        continue;
                    }
                    if (!clashElementBoxes.ContainsKey(e.UniqueId))
                    {
                        clashElementBoxes.Add(
                                            e.UniqueId,
                                            bb);
                    }

                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed BB : {e.Id} | {ex.Message}");
                }
            }


            // --------------------------------------------------------
            // BROAD PHASE
            // --------------------------------------------------------

            foreach (CableTray tray in trays)
            {
                if (!trayBoxes.TryGetValue(
                        tray.UniqueId,
                        out BoundingBoxXYZ trayBox)
                    || trayBox == null)
                {
                    continue;
                }

                foreach (Element elem in multiCategoryElements)
                {
                    // Never test an element against itself.
                    if (tray.UniqueId == elem.UniqueId)
                        continue;

                    // A missing entry means the bounding box could not be
                    // read above; an indexer lookup here used to throw.
                    if (!clashElementBoxes.TryGetValue(
                            elem.UniqueId,
                            out BoundingBoxXYZ clashElementBox)
                        || clashElementBox == null)
                    {
                        continue;
                    }

                    // Fast bounding-box test.
                    if (!BoxesIntersect(trayBox, clashElementBox, 1.0 * MM))
                    {
                        continue;
                    }

                    // ------------------------------------------------
                    // NARROW PHASE
                    // Actual Solid vs Solid
                    // ------------------------------------------------

                    List<Solid> traySolids = GetSolids(tray);

                    List<Solid> elemnetSolid = GetSolids(elem);

                    Solid intersection = null;

                    foreach (Solid traySolid in traySolids)
                    {
                        foreach (Solid elementSolid in elemnetSolid)
                        {
                            try
                            {
                                Solid test =
                                    BooleanOperationsUtils
                                        .ExecuteBooleanOperation(
                                            traySolid,
                                            elementSolid,
                                            BooleanOperationsType.Intersect);


                                if (test != null && test.Volume > GeometryTolerance)
                                {
                                    intersection = test;
                                    break;
                                }
                            }
                            catch
                            {
                                // ------------------------------------------------
                                // Some complicated Revit geometry can cause boolean failures.
                                // Ignore that pair and continue.
                                // ------------------------------------------------
                            }
                        }


                        if (intersection != null)
                            break;
                    }


                    // ------------------------------------------------
                    // REAL CLASH FOUND
                    // ------------------------------------------------

                    if (intersection != null)
                    {
                        result.Add(
                            new ClashInfoDTOs
                            {
                                Tray = tray,
                                ClashElement = elem,
                                Intersection = intersection,
                                Name = tray.Id.ToString(),
                            });
                    }
                }
            }

            return result;
        }


        public List<ClashGroupInfoDTOs> FindClashGroup(
                                             List<ClashInfoDTOs> clashes,
                                             RouteSettingDTOs settings)
        {
            List<ClashGroupInfoDTOs> clashGroups =
                new List<ClashGroupInfoDTOs>();

            if (clashes == null ||
                clashes.Count == 0)
            {
                Logger.Log("No clashes found.");
                return clashGroups;
            }

            //------------------------------------------------------
            // VALID CLASHES
            //------------------------------------------------------

            var validClashes =
                clashes
                .Where(x =>
                    x != null &&
                    x.Tray != null &&
                    x.Intersection != null)
                .ToList();

            if (!validClashes.Any())
            {
                Logger.Log(
                    "No valid clash intersections found.");

                return clashGroups;
            }

            //------------------------------------------------------
            // GROUP BY TRAY
            //------------------------------------------------------

            var trayGroups =
                validClashes
                .GroupBy(x => x.Tray.Id.Value);

            foreach (var group in trayGroups)
            {
                List<ClashInfoDTOs> trayClashes =
                    group.ToList();

                if (!trayClashes.Any())
                    continue;

                CableTray tray =
                    trayClashes.First().Tray;

                if (tray == null ||
                    !tray.IsValidObject)
                    continue;

                //--------------------------------------------------
                // TRAY GEOMETRY
                //--------------------------------------------------

                LocationCurve location =
                    tray.Location as LocationCurve;

                Line trayLine =
                    location?.Curve as Line;

                if (trayLine == null)
                {
                    Logger.Log(
                        $"Tray {tray.Id} is not straight.");

                    continue;
                }

                XYZ trayStart =
                    trayLine.GetEndPoint(0);

                XYZ trayDirection =
                    (trayLine.GetEndPoint(1) - trayStart)
                    .Normalize();

                //--------------------------------------------------
                // COMPUTE CLASH STATIONS
                //--------------------------------------------------

                List<(XYZ Point, double Station)>
                    clashData =
                    new List<(XYZ, double)>();

                foreach (var clash in trayClashes)
                {
                    try
                    {
                        XYZ clashPoint =
                            clash.Intersection
                                 .ComputeCentroid();

                        if (clashPoint == null)
                            continue;

                        double station =
                            (clashPoint - trayStart)
                            .DotProduct(trayDirection);

                        clashData.Add(
                            (clashPoint, station));
                    }
                    catch
                    {
                        Logger.Log(
                            $"Failed centroid for Tray {tray.Id}");
                    }
                }

                if (!clashData.Any())
                {
                    Logger.Log(
                        $"Tray {tray.Id}: no valid clash points.");

                    continue;
                }

                //--------------------------------------------------
                // MIN/MAX STATION
                //--------------------------------------------------

                double minStation =
                    clashData.Min(x => x.Station);

                double maxStation =
                    clashData.Max(x => x.Station);

                double centerStation =
                    (minStation + maxStation) * 0.5;

                //--------------------------------------------------
                // POINTS FROM TRAY STATION
                //--------------------------------------------------

                XYZ minPoint =
                    trayStart +
                    trayDirection * minStation;

                XYZ maxPoint =
                    trayStart +
                    trayDirection * maxStation;

                XYZ centerPoint =
                    trayStart +
                    trayDirection * centerStation;

                //--------------------------------------------------
                // BOUNDING BOX
                //--------------------------------------------------

                double bbMinX =
                    clashData.Min(x => x.Point.X);

                double bbMinY =
                    clashData.Min(x => x.Point.Y);

                double bbMinZ =
                    clashData.Min(x => x.Point.Z);

                double bbMaxX =
                    clashData.Max(x => x.Point.X);

                double bbMaxY =
                    clashData.Max(x => x.Point.Y);

                double bbMaxZ =
                    clashData.Max(x => x.Point.Z);

                BoundingBoxXYZ bbox =
                    new BoundingBoxXYZ
                    {
                        Min = new XYZ(
                            bbMinX,
                            bbMinY,
                            bbMinZ),

                        Max = new XYZ(
                            bbMaxX,
                            bbMaxY,
                            bbMaxZ)
                    };

                //--------------------------------------------------
                // MAX Z
                //--------------------------------------------------

                double zMax =
                    clashData.Max(x => x.Point.Z);

                //--------------------------------------------------
                // RESULT
                //--------------------------------------------------

                clashGroups.Add(
                    new ClashGroupInfoDTOs
                    {
                        Tray = tray,

                        Conduit = trayClashes.First().Conduit,

                        CenetrPoint =
                            centerPoint,

                        AvaragePoint =
                            centerStation,

                        XOffsetMin =
                            minStation,

                        XOffsetMax =
                            maxStation,

                        ZMax =
                            zMax,

                        ClashBoundingBox =
                            bbox
                    });

                //--------------------------------------------------
                // DEBUG
                //--------------------------------------------------

                Logger.Log(
                            $@"CLASH GROUP

                                Tray            : {tray.Id}

                                Clash Count     : {clashData.Count}

                                Min Station     : {minStation * 304.8:F1} mm

                                Max Station     : {maxStation * 304.8:F1} mm

                                Center Station  : {centerStation * 304.8:F1} mm

                                Center Point    : {centerPoint}");
            }

            Logger.Log(
                $"Clash Groups Created = {clashGroups.Count}");

            return clashGroups;
        }

        #endregion

        // ============================================================
        // BUIDING ROUTING POINTS -
        // THIS OUT THE ROUTING POINTS FOR THE BYPASS ROUTE
        // ============================================================
        #region Bypass Routing

        /// <summary>
        /// Applies the user's routing-direction override.
        /// <see cref="RouteDirection.Auto"/> keeps the side that was derived
        /// from the geometry, so existing behaviour is unchanged by default.
        /// </summary>
        private static ClashSide ApplyPreferredDirection(
            ClashSide geometricSide,
            RouteSettingDTOs settings)
        {
            switch (settings?.PreferredDirection ?? RouteDirection.Auto)
            {
                case RouteDirection.Up:
                    return ClashSide.AboveTray;

                case RouteDirection.Down:
                    return ClashSide.BelowTray;

                default:
                    return geometricSide;
            }
        }

        public bool TryBuildBypassRoutingPoints(
            ClashInfoDTOs clash,
            RouteSettingDTOs settings,
            out List<XYZ> points)
        {
            points = new List<XYZ>();

            if (clash?.Tray == null || 
                clash.ClashElement == null || 
                settings == null)
            {
                Logger.Log("Invalid clash or settings parameter.");
                return false;
            }

            LocationCurve trayLocation = clash.Tray.Location as LocationCurve;

            Line trayLine = trayLocation?.Curve as Line;

            if (trayLine == null)
            {
                Logger.Log("Only straight cable trays are supported.");
                return false;
            }

            XYZ start = trayLine.GetEndPoint(0);
            XYZ end = trayLine.GetEndPoint(1);
            XYZ trayDirection = (end - start).Normalize();
            XYZ up = XYZ.BasisZ;
            XYZ side = trayDirection.CrossProduct(up).Normalize();
            double trayLength = start.DistanceTo(end);

            XYZ clashPoint = clash.Intersection?.ComputeCentroid();
            if (clashPoint == null)
            {
                Logger.Log("Could not determine clash centroid.");
                return false;
            }

            double clashStation = (clashPoint - start).DotProduct(trayDirection);

            CalculatingLogics logic = new CalculatingLogics();

            ClashSide clashSide = logic.DetermineClashSide( clash.Tray, 
                                                            clash.ClashElement, 
                                                            1.0 * MM);
            BuiltInCategory cat =
                            (BuiltInCategory)
                            clash.ClashElement.Category.Id.Value;

            //switch (cat)
            //{
            //    case BuiltInCategory.OST_StructuralColumns:
            //    case BuiltInCategory.OST_Columns:
            //    case BuiltInCategory.OST_Walls:
            //        PreferLeftRight();
            //        break;

            //    case BuiltInCategory.OST_PipeCurves:
            //    case BuiltInCategory.OST_DuctCurves:
            //    case BuiltInCategory.OST_Conduit:
            //        PreferUpDown();
            //        break;

            //    default:
            //        UseMaximumClearanceDirection();
            //        break;
            //}


            if (clashSide == ClashSide.Unknown ||
                clashSide == ClashSide.CrossingTray)
            {
                Logger.Log($"Cannot determine bypass direction for clash side: {clashSide}");
                return false;
            }

            clashSide = ApplyPreferredDirection(clashSide, settings);

            double radius = settings.BendRadius;
            double angleDegrees = settings.BendAngle;

            if (radius <= 0 || 
                angleDegrees <= 0 || 
                angleDegrees >= 90)
            {
                Logger.Log($"Invalid bend angle ({angleDegrees}°) or radius ({radius * 304.8:F1} mm).");
                return false;
            }

            double theta = angleDegrees * Math.PI / 180.0;

            // Vertical offset generated by TWO equal bends
            double bendRise = 2.0 
                              * radius * (1.0 - Math.Cos(theta)) 
                              * settings.BendSafetyfactor;

            BoundingBoxXYZ clashElementBox = clash.ClashElement.get_BoundingBox(null);

            BoundingBoxXYZ trayBox = clash.Tray.get_BoundingBox(null);

            if (clashElementBox == null || trayBox == null)
            {
                Logger.Log("Could not obtain element bounding boxes.");
                return false;
            }

            // Clearance calculation based on detour direction
            double clearanceRise;
            XYZ detourDirection;

            if (clashSide == ClashSide.AboveTray)
            {
                // Detour over the obstruction (UP)
                detourDirection = XYZ.BasisZ;
                clearanceRise = clashElementBox.Max.Z 
                                - trayBox.Min.Z 
                                + settings.MinimumClearance;
            }
            else
            {
                // Detour under the obstruction (DOWN)
                detourDirection = -XYZ.BasisZ;
                clearanceRise = trayBox.Max.Z 
                                - clashElementBox.Min.Z 
                                + settings.MinimumClearance;
            }

            clearanceRise = Math.Max(0.0, clearanceRise);
            double rise = Math.Max(clearanceRise, bendRise);

            // Diagonal run
            double transition = rise / Math.Tan(theta);
            double tangentLength = radius * Math.Tan(theta / 2.0);

            double clashElementWidth = Math.Max(
                clashElementBox.Max.X - clashElementBox.Min.X,
                clashElementBox.Max.Y - clashElementBox.Min.Y);

            //better approach
            double widthAlongTray =
                ProjectBoundingBoxSize(
                    clashElementBox,
                    trayDirection);

            double widthPerpendicular =
                ProjectBoundingBoxSize(
                    clashElementBox,
                    side);

            double height =
                ProjectBoundingBoxSize(
                    clashElementBox,
                    XYZ.BasisZ);

            //double hostRadius = widthAlongTray * 0.5;
            double sideOffset = widthAlongTray * 0.5 + settings.MinimumSideOffset + tangentLength;
            double xOffset = (sideOffset * 0.5) + transition;

            double p1Station = clashStation - xOffset;
            double p2Station = clashStation - sideOffset;
            double p3Station = clashStation + sideOffset;
            double p4Station = clashStation + xOffset;

            // Feasibility Checks
            double minEndMargin = 50.0 * MM;
            if (p1Station < minEndMargin || p4Station > (trayLength - minEndMargin))
            {
                Logger.Log($"Insufficient tray length for bypass: " +
                    $"TrayLen={trayLength * 304.8:F1}mm, Required=[{p1Station * 304.8:F1}mm " +
                    $"to {p4Station * 304.8:F1}mm]");
                return false;
            }

            if (p2Station <= p1Station 
                || p3Station <= p2Station 
                || p4Station <= p3Station)
            {
                Logger.Log("Invalid station sequencing for bypass route.");
                return false;
            }
            double sizeX = clashElementBox.Max.X - clashElementBox.Min.X;

            double sizeY = clashElementBox.Max.Y - clashElementBox.Min.Y;

            double sizeZ = clashElementBox.Max.Z - clashElementBox.Min.Z;


            XYZ p1 = start + trayDirection * p1Station;
            XYZ p2 = start + trayDirection * p2Station + detourDirection * rise;
            XYZ p3 = start + trayDirection * p3Station + detourDirection * rise;
            XYZ p4 = start + trayDirection * p4Station;

            AddPoint(points, p1);
            AddPoint(points, p2);
            AddPoint(points, p3);
            AddPoint(points, p4);

            if (!ValidateRouteGeometry(points))
            {
                Logger.Log("Generated bypass points failed geometric validation.");
                points.Clear();
                return false;
            }


            //------------------------------------------------------
            // DEBUG
            //------------------------------------------------------

            Logger.Log(
                $@"BYPASS ROUTING

                    Tray                    : {clash.Tray.Id}
                    Conduit                 : {clash.Tray.Id}
                    Clash Side              : {clashSide}
                    Clash Station           : {clashStation * 304.8:F1} mm

                    Bend Angle              : {angleDegrees:F1}°
                    Bend Radius             : {radius * 304.8:F1} mm
                    Theta                   : {theta:F1}°
                    bounding Box Size       : {sizeX * 304.8:F1},{sizeY * 304.8:F1},{sizeZ * 304.8:F1}
                    Clash Elemnt BB Width   : {widthAlongTray * 304.8:F1}                    
                    Clash Elemnt BB Length  : {widthPerpendicular * 304.8:F1}                    
                    Clash Elemnt BB Height  : {height * 304.8:F1}


                    ClearanceRise           : {clearanceRise * 304.8:F1} mm
                    BendRise                : {bendRise * 304.8:F1} mm
                    FinalRise               : {rise * 304.8:F1} mm

                    Transition              : {transition * 304.8:F1} mm
                    Tangent Length          : {tangentLength * 304.8:F1} mm
                    TrayDirection           : {trayDirection}

                    SideOffset              : {sideOffset * 304.8:F1} mm        
                    MinimumSideOffset       : {settings.MinimumSideOffset * 304.8:F1} mm
                    XOffset                 : {xOffset * 304.8:F1} mm

                    P1 Station    : {p1Station * 304.8:F1} mm
                    P2 Station    : {p2Station * 304.8:F1} mm
                    P3 Station    : {p3Station * 304.8:F1} mm
                    P4 Station    : {p4Station * 304.8:F1} mm  ");


            return true;
        }
        #endregion

        public bool TryBuildBypassRoutingClashGroupPoints(
                                            ClashGroupInfoDTOs clash,
                                            RouteSettingDTOs settings,
                                            out List<XYZ> points)
        {
            // ------------------------------------------------------------
            //VALIDATE NEW ROUTE
            //------------------------------------------------------------
            ValidateNewRoute validateNewRoute = new ValidateNewRoute();

            points = new List<XYZ>();

            if (clash == null ||
                clash.Tray == null )
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

            Line trayLine =
                trayLocation.Curve as Line;

            if (trayLine == null)
            {
                Logger.Log(
                    "Only straight cable tray is supported.");
                return false;
            }

            XYZ start =trayLine.GetEndPoint(0);

            XYZ end = trayLine.GetEndPoint(1);

            XYZ dir = (end - start).Normalize();

            double trayLength = start.DistanceTo(end);


            //------------------------------------------------------
            // TRAY UP DIRECTION
            //------------------------------------------------------

            XYZ up = XYZ.BasisZ;

            //------------------------------------------------------
            // CLASH POINT
            //------------------------------------------------------

            XYZ clashPoint = clash.CenetrPoint;

            if (clashPoint == null)
            {
                Logger.Log("Could not determine clash point.");
                return false;
            }

            //------------------------------------------------------
            // CLASH STATION
            //------------------------------------------------------

            double clashStation = (clashPoint - start).DotProduct(dir);

            //------------------------------------------------------
            // CLASH SIDE
            //------------------------------------------------------

            CalculatingLogics logic = new CalculatingLogics();

            ClashSide clashSide =
                logic.DetermineClashSide(
                    clash.Tray,
                    clash.Conduit,
                    1.0 * MM);

            Logger.Log($"Clash side = {clashSide}");

            if (clashSide == ClashSide.Unknown ||
                clashSide == ClashSide.CrossingTray)
            {
                Logger.Log("Cannot determine whether clash is above or below tray.");

                return false;
            }

            clashSide = ApplyPreferredDirection(clashSide, settings);

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

            BoundingBoxXYZ conduitBox = clash.ClashBoundingBox;
                //clash.Conduit.get_BoundingBox(null);

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
                    conduitBox.Max.Z
                    - trayBox.Min.Z
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
                    conduitBox.Max.Z
                    - trayBox.Min.Z
                    + settings.MinimumClearance;
            }

            clearanceRise =
                Math.Max(
                    0.0,
                    clearanceRise);

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

            double p1Station = clashStation - xOffset;

            double p2Station = clashStation - sideOffset * 0.5;

            double p3Station = clashStation + sideOffset * 0.5;

            double p4Station = clashStation + xOffset;

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

            if (!validateNewRoute.ValidateRoute(points))
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
                    Conduit       : {clash.Tray.Id}

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



        public static bool ValidateRouteGeometry(List<XYZ> points)
        {
            if (points == null || points.Count < 2)
                return false;

            for (int i = 0; i < points.Count - 1; i++)
            {
                double dist = points[i].DistanceTo(points[i + 1]);
                if (dist < 30.0 * MM) // Minimum segment length in Revit
                    return false;
            }

            return true;
        }


        // ============================================================
        // ADD POINT METHOD - ADDS VALIDATED POINTS TO THE ROUTING LIST 
        // ============================================================
        #region
        private static void AddPoint(
           List<XYZ> points,
           XYZ point)
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


        private static void CollectSolids(GeometryElement geometry, List<Solid> solids)
        {
            foreach (GeometryObject obj in geometry)
            {
                // -----------------------------------------------
                // DIRECT SOLID
                // -----------------------------------------------

                Solid solid = obj as Solid;

                if (solid != null &&
                    solid.Volume >
                    GeometryTolerance)
                {
                    solids.Add(solid);

                    continue;
                }


                // -----------------------------------------------
                // GEOMETRY INSTANCE
                // -----------------------------------------------

                GeometryInstance instance = obj as GeometryInstance;

                if (instance != null)
                {
                    GeometryElement instanceGeometry = instance.GetInstanceGeometry();

                    if (instanceGeometry != null)
                    {
                        CollectSolids(
                            instanceGeometry,
                            solids);
                    }
                }
            }
        }


        // ============================================================
        // BOUNDING BOX INTERSECTION
        // ============================================================

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



        // ============================================================
        // GET CLASH POINT
        // ============================================================

        private static XYZ GetClashPoint(Solid solid)
        {
            if (solid == null)
                return null;

            return solid.ComputeCentroid();
        }
        #endregion

        // ============================================================
        // EXTRACT SOLIDS - HELPER METHOD TO GET SOLIDS FROM ELEMENTS
        // ============================================================
        #region
        //private static List<Solid> GetSolids(Element element)
        //{
        //    List<Solid> solids = new List<Solid>();

        //    Options options =
        //        new Options
        //        {
        //            ComputeReferences = false,
        //            DetailLevel = ViewDetailLevel.Fine,
        //            IncludeNonVisibleObjects = false
        //        };

        //    GeometryElement geometry = element.get_Geometry(options);

        //    if (geometry == null)
        //        return solids;

        //    CollectSolids(geometry, solids);

        //    return solids;
        //}

        private static List<Solid> GetSolids(Element element)
        {
            var solids = new List<Solid>();

            Options opt = new Options
            {
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true,
                ComputeReferences = true
            };

            GeometryElement geo = element.get_Geometry(opt);

            if (geo == null)
                return solids;

            ExtractSolidsRecursive(geo, Transform.Identity, solids);

            return solids;
        }

        private static void ExtractSolidsRecursive(
                                GeometryElement geo,
                                Transform transform,
                                List<Solid> solids)
        {
            foreach (GeometryObject obj in geo)
            {
                if (obj is Solid solid &&
                    solid.Volume > 1e-6)
                {
                    Solid transformed =
                        SolidUtils.CreateTransformed(
                            solid,
                            transform);

                    solids.Add(transformed);
                }
                else if (obj is GeometryInstance gi)
                {
                    ExtractSolidsRecursive(
                        gi.GetInstanceGeometry(),
                        transform.Multiply(gi.Transform),
                        solids);
                }
            }
        }


        private static double ProjectBoundingBoxSize(
                                    BoundingBoxXYZ bbox,
                                    XYZ direction)
        {
            if (bbox == null)
                return 0.0;

            direction = direction.Normalize();

            List<XYZ> corners =
                new List<XYZ>
                {
                    new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Min.Z),
                    new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Max.Z),
                    new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Min.Z),
                    new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Max.Z),

                    new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Min.Z),
                    new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Max.Z),
                    new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Min.Z),
                    new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Max.Z)
                };

            double minProjection =
                double.MaxValue;

            double maxProjection =
                double.MinValue;

            foreach (XYZ corner in corners)
            {
                double projection =
                    corner.DotProduct(direction);

                minProjection =
                    Math.Min(
                        minProjection,
                        projection);

                maxProjection =
                    Math.Max(
                        maxProjection,
                        projection);
            }

            return maxProjection - minProjection;
        }
        #endregion


    }
}
