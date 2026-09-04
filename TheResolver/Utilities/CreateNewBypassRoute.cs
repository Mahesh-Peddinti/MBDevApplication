using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using TheResolver.DTOs;
using TheResolver.Services;
using System.Collections.Generic;
using System;

namespace TheResolver.Utilities
{
    public class CreateNewBypassRoute
    {
        private const double MM = 1.0 / 304.8;

        public static List<CableTray> CreateNewRoute(
            Document doc,
            CableTray originalTray,
            XYZ breakStart,
            XYZ breakEnd,
            List<XYZ> detourPoints,
            RouteSettingDTOs settings)
        {
            var result = new List<CableTray>();
            if (doc == null || originalTray == null || !originalTray.IsValidObject)
                return result;

            if (!(originalTray.Location is LocationCurve location) || !(location.Curve is Line originalLine))
                return result;

            XYZ originalStart = originalLine.GetEndPoint(0);
            XYZ originalEnd = originalLine.GetEndPoint(1);

            // Clean replacement points
            var cleaner = new BuildCleanRoutePoints();
            List<XYZ> replacementPoints = cleaner.CleanRoutePoints(detourPoints);
            if (replacementPoints.Count < 2) return result;

            breakStart = replacementPoints[0];
            breakEnd = replacementPoints[replacementPoints.Count - 1];

            ElementId typeId = originalTray.GetTypeId();
            ElementId levelId = originalTray.ReferenceLevel.Id;
            double width = originalTray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.AsDouble() ?? 0.0;
            double height = originalTray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.AsDouble() ?? 0.0;

            XYZ originalDirection = (originalEnd - originalStart).Normalize();
            XYZ originalNormal = originalTray.CurveNormal?.Normalize() ?? XYZ.BasisZ;

            List<CableTray> newSegments = new List<CableTray>();
            try
            {
                // 1. Create intermediate replacement segments
                for (int i = 0; i < replacementPoints.Count - 1; i++)
                {
                    XYZ p0 = replacementPoints[i];
                    XYZ p1 = replacementPoints[i + 1];
                    if (p0.DistanceTo(p1) < 40 * MM)
                        throw new InvalidOperationException($"Segment {i} is too short.");

                    CableTray seg = CableTray.Create(doc, typeId, p0, p1, levelId);
                    SetCableTraySize(seg, width, height);
                    SetTrayOrientation(seg, p0, p1, originalDirection, originalNormal);
                    newSegments.Add(seg);
                }
                doc.Regenerate();

                // 2. Modify original tray to keep start -> breakStart
                if (originalStart.DistanceTo(breakStart) < 40 * MM)
                    throw new InvalidOperationException("Left retained section too short.");

                location.Curve = Line.CreateBound(originalStart, breakStart);

                // 3. Create right continuation segment
                CableTray rightTray = null;
                if (breakEnd.DistanceTo(originalEnd) > 40 * MM)
                {
                    rightTray = CableTray.Create(doc, typeId, breakEnd, originalEnd, levelId);
                    SetCableTraySize(rightTray, width, height);
                    SetTrayOrientation(rightTray, breakEnd, originalEnd, originalDirection, originalNormal);
                }
                doc.Regenerate();

                result.Add(originalTray);
                result.AddRange(newSegments);
                if (rightTray != null) result.Add(rightTray);

                // 4. Stitch elbow fittings along all joints
                CreateRouteFittings(doc, result, settings);
                doc.Regenerate();

                return result;
            }
            catch (Exception ex)
            {
                Logger.Log("Replacement route generation failed: " + ex.Message);
                foreach (CableTray tray in newSegments)
                {
                    if (tray != null && tray.IsValidObject)
                    {
                        try { doc.Delete(tray.Id); } catch { }
                    }
                }
                if (originalTray.IsValidObject)
                {
                    try { location.Curve = originalLine; doc.Regenerate(); } catch { }
                }
                return new List<CableTray>();
            }
        }

        private static void CreateRouteFittings(Document doc, List<CableTray> route, RouteSettingDTOs settings)
        {
            for (int i = 0; i < route.Count - 1; i++)
            {
                CableTray t1 = route[i];
                CableTray t2 = route[i + 1];

                XYZ joint = FindCommonPoint(t1, t2);
                if (joint == null) continue;

                Connector c1 = FindConnectorAtPoint(t1, joint);
                Connector c2 = FindConnectorAtPoint(t2, joint);
                if (c1 != null && c2 != null && !c1.IsConnected && !c2.IsConnected)
                {
                    try { doc.Create.NewElbowFitting(c1, c2); }
                    catch (Exception ex) { Logger.Log($"Elbow creation failed between {t1.Id} and {t2.Id}: {ex.Message}"); }
                }
            }
        }

        private static Connector FindConnectorAtPoint(CableTray tray, XYZ point)
        {
            foreach (Connector c in tray.ConnectorManager.Connectors)
            {
                if (c.Origin.DistanceTo(point) < 2.0 * MM) return c;
            }
            return null;
        }

        private static XYZ FindCommonPoint(CableTray a, CableTray b)
        {
            if (!(a.Location is LocationCurve la) || !(b.Location is LocationCurve lb)) return null;
            XYZ[] aPts = { la.Curve.GetEndPoint(0), la.Curve.GetEndPoint(1) };
            XYZ[] bPts = { lb.Curve.GetEndPoint(0), lb.Curve.GetEndPoint(1) };

            foreach (XYZ pa in aPts)
            {
                foreach (XYZ pb in bPts)
                {
                    if (pa.DistanceTo(pb) < 2.0 * MM) return pa;
                }
            }
            return null;
        }

        private static void SetCableTraySize(CableTray tray, double width, double height)
        {
            tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.Set(width);
            tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.Set(height);
        }

        private static void SetTrayOrientation(CableTray tray, XYZ start, XYZ end, XYZ refDir, XYZ refNorm)
        {
            XYZ dir = (end - start).Normalize();
            XYZ normal = refNorm - dir.Multiply(refNorm.DotProduct(dir));
            if (normal.GetLength() < 1e-6)
                normal = refDir - dir.Multiply(refDir.DotProduct(dir));
            if (normal.GetLength() < 1e-6)
                normal = Math.Abs(dir.DotProduct(XYZ.BasisZ)) < 0.9 ? XYZ.BasisZ : XYZ.BasisX;

            tray.CurveNormal = normal.Normalize();
        }
    }
}