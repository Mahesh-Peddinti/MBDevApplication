using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using TheResolver.DTOs;
using TheResolver.Services;
using System.Collections.Generic;
using System.Linq;
using System;

namespace TheResolver.Utilities
{
    public class CreateNewBypassRoute
    {
        // ------------------------------------------------------------
        // UNIT CONVERSION
        // Revit internal length unit = feet
        // ------------------------------------------------------------
        private const double MM = 1.0 / 304.8;

        // ============================================================
        // CREATE CABLE TRAY SEGMENTS
        // ============================================================

        public static List<CableTray> CreateNewRoute(
                                             Document doc,
                                             CableTray originalTray,
                                             XYZ breakStart,
                                             XYZ breakEnd,
                                             List<XYZ> detourPoints,
                                             RouteSettingDTOs settings)
        {
            // ---------------------------------------------------------
            // Validate inputs
            // ---------------------------------------------------------
            ValidateNewRoute validateNewRoute = new ValidateNewRoute();
            BuildCleanRoutePoints buildCleanRoutePoints = new BuildCleanRoutePoints();


            var result = new List<CableTray>();

            if (doc == null ||
                originalTray == null ||
                !originalTray.IsValidObject)
            {
                Logger.Log("Invalid document or original cable tray.");
                return result;
            }

            //---------------------------------------------------------
            // GET ORIGINAL LOCATION
            //---------------------------------------------------------

            LocationCurve location =
                originalTray.Location as LocationCurve;

            if (location == null)
            {
                Logger.Log("Original tray has no LocationCurve.");
                return result;
            }

            Line originalLine =
                location.Curve as Line;

            if (originalLine == null)
            {
                Logger.Log(
                    "This implementation requires a straight original tray.");
                return result;
            }

            XYZ originalStart =
                originalLine.GetEndPoint(0);

            XYZ originalEnd =
                originalLine.GetEndPoint(1);

            //---------------------------------------------------------
            // ORDER BREAK POINTS ALONG ORIGINAL TRAY
            //---------------------------------------------------------

            double tStart =
                originalLine.Project(breakStart).Parameter;

            double tEnd =
                originalLine.Project(breakEnd).Parameter;

            if (tStart > tEnd)
            {
                XYZ tmp = breakStart;
                breakStart = breakEnd;
                breakEnd = tmp;
            }

            //---------------------------------------------------------
            // VALIDATE BREAK POINTS
            //---------------------------------------------------------

            IntersectionResult startProjection = originalLine.Project(breakStart);

            IntersectionResult endProjection = originalLine.Project(breakEnd);

            if (startProjection == null ||
                endProjection == null)
            {
                Logger.Log(
                    "Could not project break points onto original tray.");

                return result;
            }

            XYZ projectedStart = startProjection.XYZPoint;

            XYZ projectedEnd = endProjection.XYZPoint;

            double startDistance = breakStart.DistanceTo(projectedStart);

            double endDistance = breakEnd.DistanceTo(projectedEnd);

            // Revit internal units = feet.
            // 5 mm tolerance:
            double tolerance =
                5.0 / 304.8;

            if (startDistance > tolerance ||
                endDistance > tolerance)
            {
                Logger.Log(
                    $"Break points are not sufficiently close " +
                    $"to original tray. " +
                    $"Start={startDistance * 304.8:F2} mm, " +
                    $"End={endDistance * 304.8:F2} mm");

                return result;
            }

            // Use the projected points.
            // This is important because it guarantees that the
            // break points lie exactly on the original tray line.

            breakStart = projectedStart;
            breakEnd = projectedEnd;

            //---------------------------------------------------------
            // ORIGINAL TRAY DATA
            //---------------------------------------------------------

            ElementId typeId =
                originalTray.GetTypeId();

            ElementId levelId =
                originalTray.ReferenceLevel.Id;

            double width =
                GetParameterValue(
                    originalTray,
                    BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM);

            double height =
                GetParameterValue(
                    originalTray,
                    BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM);

            //---------------------------------------------------------
            // SAVE ORIGINAL ORIENTATION
            //---------------------------------------------------------

            XYZ originalDirection =
                (originalEnd - originalStart).Normalize();

            XYZ originalNormal =
                originalTray.CurveNormal;

            if (originalNormal == null ||
                originalNormal.GetLength() < 1e-9)
            {
                originalNormal = XYZ.BasisZ;
            }

            originalNormal =
                originalNormal.Normalize();
            

            List<XYZ> replacementPoints =
                BuildReplacementPoints(
                    breakStart,
                    breakEnd,
                    detourPoints);

            //---------------------------------------------------------
            // REMOVE COLLINEAR / DUPLICATE POINTS
            //---------------------------------------------------------

            replacementPoints =
                buildCleanRoutePoints.CleanRoutePoints(
                    replacementPoints);

            if (replacementPoints.Count < 2)
            {
                Logger.Log(
                    "Replacement route contains insufficient points.");

                return result;
            }

            //---------------------------------------------------------
            // VALIDATE ROUTE
            //---------------------------------------------------------

            if (!validateNewRoute.ValidateRoute(
                    replacementPoints))
            {
                Logger.Log(
                    "Replacement route geometry is invalid.");

                return result;
            }

            //---------------------------------------------------------
            // CREATE NEW REPLACEMENT SEGMENTS FIRST
            //
            // We DO NOT modify original yet.
            //---------------------------------------------------------

            List<CableTray> newSegments =  new List<CableTray>();

            try
            {
                for (int i = 0;
                     i < replacementPoints.Count - 1;
                     i++)
                {
                    XYZ p0 =
                        replacementPoints[i];

                    XYZ p1 =
                        replacementPoints[i + 1];

                    double length =
                        p0.DistanceTo(p1);

                    if (length < 50 * MM)
                    {
                        throw new InvalidOperationException(
                            $"Route segment {i} is too short: " +
                            $"{length * 304.8:F1} mm.");
                    }

                    CableTray tray =
                        CableTray.Create(
                            doc,
                            typeId,
                            p0,
                            p1,
                            levelId);

                    if (tray == null)
                    {
                        throw new InvalidOperationException(
                            $"Failed to create tray segment {i}.");
                    }

                    SetCableTraySize(
                        tray,
                        width,
                        height);

                    //-------------------------------------------------
                    // CRITICAL:
                    // Set orientation before fitting creation.
                    //-------------------------------------------------

                    SetTrayOrientation(
                        tray,
                        p0,
                        p1,
                        originalDirection,
                        originalNormal);

                    newSegments.Add(tray);
                }

                doc.Regenerate();

                //-----------------------------------------------------
                // NOW MODIFY ORIGINAL TRAY
                //
                // Keep:
                //
                // originalStart → breakStart
                //-----------------------------------------------------

                if (originalStart.DistanceTo(breakStart) <
                    50 * MM)
                {
                    throw new InvalidOperationException(
                        "Left retained section is too short.");
                }

                Line leftLine =
                    Line.CreateBound(
                        originalStart,
                        breakStart);

                location.Curve =
                    leftLine;

                //-----------------------------------------------------
                // CREATE RIGHT-HAND CONTINUATION
                //
                // breakEnd → originalEnd
                //-----------------------------------------------------

                CableTray rightTray =
                    null;

                if (breakEnd.DistanceTo(originalEnd) > 50 * MM)
                {
                    rightTray =
                        CableTray.Create(
                            doc,
                            typeId,
                            breakEnd,
                            originalEnd,
                            levelId);

                    if (rightTray == null)
                    {
                        throw new InvalidOperationException(
                            "Failed to create right continuation.");
                    }

                    SetCableTraySize(
                        rightTray,
                        width,
                        height);

                    SetTrayOrientation(
                        rightTray,
                        breakEnd,
                        originalEnd,
                        originalDirection,
                        originalNormal);
                }

                doc.Regenerate();

                //-----------------------------------------------------
                // COMPLETE TOPOLOGY
                //-----------------------------------------------------

                result.Add(originalTray);

                result.AddRange(newSegments);

                if (rightTray != null)
                    result.Add(rightTray);

                //-----------------------------------------------------
                // CREATE FITTINGS BETWEEN REPLACEMENT SEGMENTS
                //-----------------------------------------------------

                CreateRouteFittings(
                    doc,
                    result,
                    replacementPoints,
                    settings);

                doc.Regenerate();

                Logger.Log(
                    $"Cable tray route created successfully. " +
                    $"Original={originalTray.Id}, " +
                    $"NewSegments={newSegments.Count}, " +
                    $"RightTray={(rightTray != null ? rightTray.Id.ToString() : "none")}");

                //-----------------------------------------------------
                // IMPORTANT:
                //
                // DO NOT DELETE ORIGINAL.
                //-----------------------------------------------------

                return result;
            }
            catch (Exception ex)
            {
                Logger.Log(
                    "Replacement route failed: " +
                    ex.Message);

                //-----------------------------------------------------
                // CLEAN UP NEW ELEMENTS
                //-----------------------------------------------------

                foreach (CableTray tray in newSegments)
                {
                    if (tray != null &&
                        tray.IsValidObject)
                    {
                        try
                        {
                            doc.Delete(tray.Id);
                        }
                        catch
                        {
                            // ignore cleanup failure
                        }
                    }
                }

                //-----------------------------------------------------
                // RESTORE ORIGINAL
                //-----------------------------------------------------

                try
                {
                    if (originalTray.IsValidObject)
                    {
                        location.Curve =
                            originalLine;

                        doc.Regenerate();
                    }
                }
                catch
                {
                    // Outer transaction should rollback if necessary.
                }

                return new List<CableTray>();
            }
        }

        // ============================================================
        // BUILDING REPLACEMENT ROUTE POINTS
        // ============================================================
        private static List<XYZ> BuildReplacementPoints(
                                    XYZ breakStart,
                                    XYZ breakEnd,
                                    List<XYZ> detourPoints)
        {
            var points =
                new List<XYZ>();

            points.Add(breakStart);

            if (detourPoints != null)
            {
                foreach (XYZ p in detourPoints)
                {
                    if (p == null)
                        continue;

                    //-------------------------------------------------
                    // Ignore points that duplicate the break points
                    //-------------------------------------------------

                    if (p.DistanceTo(breakStart) < 1 * MM)
                        continue;

                    if (p.DistanceTo(breakEnd) < 1 * MM)
                        continue;

                    points.Add(p);
                }
            }

            points.Add(breakEnd);

            return points;
        }


        

        


        // ============================================================
        // READ PARAMETER
        // ============================================================

        private static double GetParameterValue(
                                    Element element,
                                    BuiltInParameter parameter)
        {
            Parameter p =
                element.get_Parameter(parameter);

            if (p == null || !p.HasValue)
            {
                return 0.0;
            }

            return p.AsDouble();
        }

        //============================================================
        // SET CABLE TRAY SIZES
        //============================================================
        private static void SetCableTraySize(
                                    CableTray tray,
                                    double width,
                                    double height)
        {
            if (tray == null ||
                !tray.IsValidObject)
            {
                return;
            }

            Parameter widthParam =
                tray.get_Parameter(
                    BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM);

            Parameter heightParam =
                tray.get_Parameter(
                    BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM);

            if (widthParam != null &&
                !widthParam.IsReadOnly)
            {
                widthParam.Set(width);
            }

            if (heightParam != null &&
                !heightParam.IsReadOnly)
            {
                heightParam.Set(height);
            }
        }


        // ============================================================
        // SETTING CABLE TRAY ORIENTATION
        // ============================================================
        private static void SetTrayOrientation(
                                    CableTray tray,
                                    XYZ start,
                                    XYZ end,
                                    XYZ referenceDirection,
                                    XYZ referenceNormal)
        {
            XYZ direction = (end - start).Normalize();

            //---------------------------------------------------------
            // Try to preserve the original tray's up direction.
            //---------------------------------------------------------

            XYZ normal =
                referenceNormal -
                direction.Multiply(
                    referenceNormal.DotProduct(direction));

            //---------------------------------------------------------
            // If reference normal is parallel to the new tray,
            // use original tray direction instead.
            //
            // This is especially important for VERTICAL tray.
            //---------------------------------------------------------

            if (normal.GetLength() < 1e-6)
            {
                normal =
                    referenceDirection -
                    direction.Multiply(
                        referenceDirection.DotProduct(direction));
            }

            //---------------------------------------------------------
            // Final fallback
            //---------------------------------------------------------

            if (normal.GetLength() < 1e-6)
            {
                XYZ candidate =
                    Math.Abs(direction.DotProduct(XYZ.BasisZ))
                        < 0.9
                        ? XYZ.BasisZ
                        : XYZ.BasisX;

                normal =
                    candidate -
                    direction.Multiply(
                        candidate.DotProduct(direction));
            }

            normal = normal.Normalize();

            //---------------------------------------------------------
            // This is the Revit API property that controls the
            // cable tray up orientation.
            //---------------------------------------------------------

            tray.CurveNormal = normal;
        }


        // ---------------------------------------------------
        // CONNECT SEGMENTS WITH FITTINGS
        // ---------------------------------------------------  
        private static void CreateRouteFittings(
                                Document doc,
                                List<CableTray> route,
                                List<XYZ> routePoints,
                                RouteSettingDTOs settings)
        {
            if (route == null ||
                route.Count < 2)
                return;

            //---------------------------------------------------------
            // We connect consecutive route elements by their
            // common point.
            //
            // IMPORTANT:
            // NewElbowFitting creates the fitting and connects the
            // two cable-tray connectors.
            //---------------------------------------------------------

            for (int i = 0;
                 i < route.Count - 1;
                 i++)
            {
                CableTray tray1 =
                    route[i];

                CableTray tray2 =
                    route[i + 1];

                XYZ joint =
                    FindCommonPoint(
                        tray1,
                        tray2);

                if (joint == null)
                {
                    throw new InvalidOperationException(
                        $"No common point between " +
                        $"{tray1.Id} and {tray2.Id}.");

                }

                Connector c1 =
                    FindConnectorAtPoint(
                        tray1,
                        joint);

                Connector c2 =
                    FindConnectorAtPoint(
                        tray2,
                        joint);

                if (c1 == null ||
                    c2 == null)
                {
                    throw new InvalidOperationException(
                        $"Could not find connectors at joint {joint}.");
                }

                //-----------------------------------------------------
                // DEBUG INFORMATION
                //-----------------------------------------------------

                LogConnectorInfo(
                    $"Tray {tray1.Id}",
                    c1);

                LogConnectorInfo(
                    $"Tray {tray2.Id}",
                    c2);

                //-----------------------------------------------------
                // FITTING
                //-----------------------------------------------------

                try
                {
                    FamilyInstance fitting =
                        doc.Create.NewElbowFitting(
                            c1,
                            c2);

                    if (fitting != null)
                    {
                        //TrySetParameter(
                        //        fitting,
                        //        "Bend Radius",
                        //        settings.BendRadius);
                        //TrySetParameter(
                        //        fitting,
                        //        "Angle",
                        //        settings.BendAngle);

                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"Revit returned null elbow " +
                            $"for joint {joint}.");
                    }

                    Logger.Log(
                        $"Created elbow {fitting.Id} " +
                        $"between {tray1.Id} and {tray2.Id}");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed elbow at joint {joint}. " +
                        $"Tray1={tray1.Id}, " +
                        $"Tray2={tray2.Id}. " +
                        $"Reason={ex.Message}",
                        ex);
                }
            }
        }

        public bool TrySetParameter(
                                Element element,
                                string parameterName,
                                double value)
        {
            if (element == null)
                return false;

            Parameter p = element.LookupParameter(parameterName);

            if (p == null)
                return false;

            if (p.IsReadOnly)
                return false;

            p.Set(value);

            return true;
        }


        private static Connector FindConnectorAtPoint(
                                    CableTray tray,
                                    XYZ point)
        {
            ConnectorSet connectors =
                tray.ConnectorManager.Connectors;

            foreach (Connector connector in connectors)
            {
                if (connector == null)
                    continue;

                if (connector.Origin.DistanceTo(point)
                    < 1 * MM)
                {
                    return connector;
                }
            }

            return null;
        }

        private static XYZ FindCommonPoint(
                                CableTray a,
                                CableTray b)
        {
            LocationCurve la = a.Location as LocationCurve;

            LocationCurve lb = b.Location as LocationCurve;

            if (la == null ||
                lb == null)
                return null;

            XYZ[] aPoints =
            {
                la.Curve.GetEndPoint(0),
                la.Curve.GetEndPoint(1)
            };

            XYZ[] bPoints =
            {
                lb.Curve.GetEndPoint(0),
                lb.Curve.GetEndPoint(1)
            };


            foreach (XYZ pa in aPoints)
            {
                foreach (XYZ pb in bPoints)
                {
                    if (pa.DistanceTo(pb) < 1 * MM)
                        return pa;
                }
            }

            return null;
        }


        private static void LogConnectorInfo(
                            string name,
                            Connector connector)
        {
            if (connector == null)
            {
                Logger.Log(
                    $"{name}: NULL CONNECTOR");
                return;
            }

            Transform cs =
                connector.CoordinateSystem;

            Logger.Log(
                $"{name} " +
                $"Origin={connector.Origin} " +
                $"Direction={cs.BasisZ} " +
                $"X={cs.BasisX} " +
                $"Y={cs.BasisY} " +
                $"Connected={connector.IsConnected}");
        }


    }
}
