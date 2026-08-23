using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using MBRevitDev_Hub.Models;

namespace MBRevitDev_Hub.Core
{
    public class ClashDetector
    {
        private readonly Document _doc;
        private readonly RerouteConfig _config;

        public ClashDetector(Document doc, RerouteConfig config)
        {
            _doc = doc;
            _config = config;
        }

        public List<CableTrayClashInfo> DetectClashes(CableTray tray)
        {
            List<CableTrayClashInfo> result = new List<CableTrayClashInfo>();
            LocationCurve locCurve = tray.Location as LocationCurve;
            Line centerLine = locCurve?.Curve as Line;
            if (centerLine == null) return result;

            XYZ p0 = centerLine.GetEndPoint(0);
            XYZ p1 = centerLine.GetEndPoint(1);
            XYZ dir = (p1 - p0).Normalize();
            double totalLength = centerLine.Length;

            Parameter hParam = tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM);
            double trayHeight = hParam != null ? hParam.AsDouble() : (100.0 / 304.8);
            double trayHalfHeight = trayHeight / 2.0;

            BoundingBoxXYZ trayBBox = tray.get_BoundingBox(null);
            if (trayBBox == null) return result;

            List<BuiltInCategory> obstacleCategories = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_Conduit,
                BuiltInCategory.OST_CableTray,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_DuctFitting,
                BuiltInCategory.OST_CableTrayFitting,
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_GenericModel
            };

            FilteredElementCollector collector = new FilteredElementCollector(_doc)
                .WherePasses(new ElementMulticategoryFilter(obstacleCategories))
                .WhereElementIsNotElementType()
                .Excluding(new List<ElementId> { tray.Id });

            ElementIntersectsElementFilter elemIntFilter = new ElementIntersectsElementFilter(tray);
            List<Element> candidateList = collector.WherePasses(elemIntFilter).ToList();

            if (candidateList.Count == 0)
            {
                FilteredElementCollector broadCollector = new FilteredElementCollector(_doc)
                    .WherePasses(new ElementMulticategoryFilter(obstacleCategories))
                    .WhereElementIsNotElementType()
                    .Excluding(new List<ElementId> { tray.Id });

                foreach (Element elem in broadCollector)
                {
                    BoundingBoxXYZ obsBox = elem.get_BoundingBox(null);
                    if (obsBox == null) continue;

                    if (DoBoundingBoxesIntersect(trayBBox, obsBox))
                    {
                        candidateList.Add(elem);
                    }
                    else if (elem is MEPCurve mepObstacle)
                    {
                        LocationCurve obsLoc = mepObstacle.Location as LocationCurve;
                        if (obsLoc?.Curve is Line obsLine)
                        {
                            if (AreLinesCrossingIn3D(centerLine, obsLine, 2.0))
                            {
                                candidateList.Add(elem);
                            }
                        }
                    }
                }
            }

            foreach (Element obstacle in candidateList)
            {
                BoundingBoxXYZ obsBox = obstacle.get_BoundingBox(null);
                if (obsBox == null) continue;

                XYZ clashPoint = null;
                double sParam = -1;
                double obsWidth = 0;

                if (obstacle is MEPCurve mepObs && (mepObs.Location as LocationCurve)?.Curve is Line obsLine)
                {
                    clashPoint = ComputeLineIntersectionPoint(centerLine, obsLine);
                    Parameter dParam = mepObs.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)
                                    ?? mepObs.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)
                                    ?? mepObs.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
                    obsWidth = dParam != null ? dParam.AsDouble() : (obsBox.Max.X - obsBox.Min.X);
                }

                if (clashPoint == null)
                {
                    XYZ obsCenter = (obsBox.Min + obsBox.Max) / 2.0;
                    sParam = (obsCenter - p0).DotProduct(dir);
                    clashPoint = p0 + dir * Math.Max(0.1, Math.Min(totalLength - 0.1, sParam));
                    obsWidth = Math.Max(obsBox.Max.X - obsBox.Min.X, obsBox.Max.Y - obsBox.Min.Y);
                }
                else
                {
                    sParam = (clashPoint - p0).DotProduct(dir);
                }

                if (sParam >= 0 && sParam <= totalLength)
                {
                    double obsTopZ = obsBox.Max.Z;
                    double trayCenterZ = p0.Z;

                    // 50mm clearance above top of obstacle
                    double requiredLift = (obsTopZ + _config.Clearance50mmFeet + trayHalfHeight) - trayCenterZ;
                    if (requiredLift < (50.0 / 304.8)) requiredLift = 150.0 / 304.8;

                    result.Add(new CableTrayClashInfo
                    {
                        Obstacle = obstacle,
                        ClashPoint3D = clashPoint,
                        ParamCenter = sParam,
                        ObstacleWidth = obsWidth,
                        ObstacleTopZ = obsTopZ,
                        RequiredDeltaZ = requiredLift
                    });
                }
            }

            return result.OrderBy(c => c.ParamCenter).ToList();
        }

        private bool DoBoundingBoxesIntersect(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            return (a.Min.X <= b.Max.X && a.Max.X >= b.Min.X) &&
                   (a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y) &&
                   (a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z);
        }

        private bool AreLinesCrossingIn3D(Line l1, Line l2, double tolerance)
        {
            XYZ p1 = l1.GetEndPoint(0);
            XYZ p2 = l1.GetEndPoint(1);
            XYZ p3 = l2.GetEndPoint(0);
            XYZ p4 = l2.GetEndPoint(1);

            double x1 = p1.X, y1 = p1.Y, x2 = p2.X, y2 = p2.Y;
            double x3 = p3.X, y3 = p3.Y, x4 = p4.X, y4 = p4.Y;

            double denom = (y4 - y3) * (x2 - x1) - (x4 - x3) * (y2 - y1);
            if (Math.Abs(denom) < 1e-6) return false;

            double ua = ((x4 - x3) * (y1 - y3) - (y4 - y3) * (x1 - x3)) / denom;
            double ub = ((x2 - x1) * (y1 - y3) - (y2 - y1) * (x1 - x3)) / denom;

            if (ua >= 0 && ua <= 1 && ub >= 0 && ub <= 1)
            {
                double z1 = p1.Z + ua * (p2.Z - p1.Z);
                double z2 = p3.Z + ub * (p4.Z - p3.Z);
                return Math.Abs(z1 - z2) <= tolerance;
            }
            return false;
        }

        private XYZ ComputeLineIntersectionPoint(Line l1, Line l2)
        {
            XYZ p1 = l1.GetEndPoint(0);
            XYZ p2 = l1.GetEndPoint(1);
            XYZ p3 = l2.GetEndPoint(0);
            XYZ p4 = l2.GetEndPoint(1);

            double x1 = p1.X, y1 = p1.Y, x2 = p2.X, y2 = p2.Y;
            double x3 = p3.X, y3 = p3.Y, x4 = p4.X, y4 = p4.Y;

            double denom = (y4 - y3) * (x2 - x1) - (x4 - x3) * (y2 - y1);
            if (Math.Abs(denom) < 1e-6) return (p1 + p2) / 2.0;

            double ua = ((x4 - x3) * (y1 - y3) - (y4 - y3) * (x1 - x3)) / denom;
            ua = Math.Max(0.0, Math.Min(1.0, ua));

            return p1 + ua * (p2 - p1);
        }
    }
}
