using Autodesk.Revit.DB;
using MBRevitDev_Hub.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MBRevitDev_Hub.Utilities
{
    public class GeometricUtilities
    {
        // ================================================================
        // CONFIGURATION
        // ================================================================

        private const double MmToFt = 1.0 / 304.8;

        // ================================================================
        // SOLID EXTRACTION
        // ================================================================

        public  List<Solid> GetSolids(Element element)
        {
            RouteSettingDTOs routeSettingDTOs = new RouteSettingDTOs();
            List<Solid> solids = new List<Solid>();

            Options options = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = false
            };

            GeometryElement geometry = element.get_Geometry(options);

            if (geometry == null)
                return solids;

            CollectSolids(geometry, solids);

            return solids;
        }

        public void CollectSolids(
                                GeometryElement geometry,
                                IList<Solid> solids)
        {
            RouteSettingDTOs routeSettingDTOs = new RouteSettingDTOs();
            foreach (GeometryObject obj in geometry)
            {
                Solid solid = obj as Solid;

                if (solid != null && solid.Volume > routeSettingDTOs.MinimumIntersectionVolume)
                {
                    solids.Add(solid);
                    continue;
                }

                GeometryInstance instance = obj as GeometryInstance;

                if (instance == null)
                    continue;

                GeometryElement instanceGeometry =
                    instance.GetInstanceGeometry();

                if (instanceGeometry != null)
                    CollectSolids(instanceGeometry, solids);
            }
        }


        // ================================================================
        // BOUNDING BOX
        // ================================================================

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

    }
}
