using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitMepAutomation.Models;

namespace RevitMepAutomation.Core
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

        public List<ClashInfo> DetectClashes(MEPCurve target)
        {
            List<ClashInfo> result = new List<ClashInfo>();
            LocationCurve locCurve = target.Location as LocationCurve;
            Line centerLine = locCurve?.Curve as Line;
            if (centerLine == null) return result;

            XYZ p0 = centerLine.GetEndPoint(0);
            XYZ p1 = centerLine.GetEndPoint(1);
            XYZ dir = (p1 - p0).Normalize();
            double totalLength = centerLine.Length;

            double targetHalfHeight = GetTargetHalfHeight(target);

            // Bounding box filter for quick candidate collection
            BoundingBoxXYZ targetBBox = target.get_BoundingBox(null);
            if (targetBBox == null) return result;

            Outline outline = new Outline(targetBBox.Min - new XYZ(1, 1, 1), targetBBox.Max + new XYZ(1, 1, 1));
            BoundingBoxIntersectsFilter bboxFilter = new BoundingBoxIntersectsFilter(outline);

            FilteredElementCollector collector = new FilteredElementCollector(_doc)
                .WherePasses(bboxFilter)
                .WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>
                {
                    BuiltInCategory.OST_PipeCurves,
                    BuiltInCategory.OST_DuctCurves,
                    BuiltInCategory.OST_CableTray,
                    BuiltInCategory.OST_Conduit,
                    BuiltInCategory.OST_StructuralFraming,
                    BuiltInCategory.OST_PipeFitting,
                    BuiltInCategory.OST_DuctFitting
                }))
                .Excluding(new List<ElementId> { target.Id });

            Solid targetSolid = GetElementSolid(target);
            if (targetSolid == null) return result;

            foreach (Element obstacle in collector)
            {
                Solid obsSolid = GetElementSolid(obstacle);
                if (obsSolid == null || obsSolid.Volume < 1e-6) continue;

                try
                {
                    Solid intersection = BooleanOperationsUtils.ExecuteBooleanOperation(
                        targetSolid, obsSolid, BooleanOperationsType.Intersect);

                    if (intersection != null && intersection.Volume > 1e-7)
                    {
                        BoundingBoxXYZ intBox = intersection.GetBoundingBox();

                        double s1 = (intBox.Min - p0).DotProduct(dir);
                        double s2 = (intBox.Max - p0).DotProduct(dir);
                        double clashMin = Math.Max(0.0, Math.Min(s1, s2));
                        double clashMax = Math.Min(totalLength, Math.Max(s1, s2));

                        BoundingBoxXYZ obsBox = obstacle.get_BoundingBox(null);
                        double obsTop = obsBox.Max.Z;
                        double targetCenterZ = (p0.Z + p1.Z) / 2.0;

                        // Calculate minimum lift
                        double deltaZ = (obsTop + _config.ClearanceFeet + targetHalfHeight) - targetCenterZ;
                        if (deltaZ < 0.2) deltaZ = 0.5; // Minimum realistic offset threshold

                        result.Add(new ClashInfo
                        {
                            Obstacle = obstacle,
                            ParamStart = clashMin,
                            ParamEnd = clashMax,
                            RequiredDeltaZ = deltaZ
                        });
                    }
                }
                catch
                {
                    // Catch solid Boolean operation failures on non-manifold geometries
                }
            }

            return result.OrderBy(c => c.ParamStart).ToList();
        }

        private double GetTargetHalfHeight(MEPCurve mep)
        {
            Parameter hParam = mep.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)
                            ?? mep.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM);
            return hParam != null ? hParam.AsDouble() / 2.0 : 0.25;
        }

        private Solid GetElementSolid(Element elem)
        {
            Options opt = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine };
            GeometryElement geomElem = elem.get_Geometry(opt);
            if (geomElem == null) return null;

            foreach (GeometryObject obj in geomElem)
            {
                if (obj is Solid s && s.Volume > 1e-6) return s;
                if (obj is GeometryInstance gi)
                {
                    foreach (GeometryObject instObj in gi.GetInstanceGeometry())
                    {
                        if (instObj is Solid instSolid && instSolid.Volume > 1e-6) return instSolid;
                    }
                }
            }
            return null;
        }
    }
}
