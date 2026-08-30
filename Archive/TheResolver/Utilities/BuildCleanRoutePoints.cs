using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TheResolver.Utilities
{
    public class BuildCleanRoutePoints
    {
        // ------------------------------------------------------------
        // UNIT CONVERSION
        // Revit internal length unit = feet
        // ------------------------------------------------------------
        private const double MM = 1.0 / 304.8;
        public  List<XYZ> CleanRoutePoints(List<XYZ> input)
        {
            var output =
                new List<XYZ>();

            foreach (XYZ p in input)
            {
                if (p == null)
                    continue;

                //-----------------------------------------------------
                // Remove duplicate points
                //-----------------------------------------------------

                if (output.Count > 0 &&
                    p.DistanceTo(
                        output[output.Count - 1]) < 1 * MM)
                {
                    continue;
                }

                //-----------------------------------------------------
                // Remove unnecessary collinear point
                //-----------------------------------------------------

                if (output.Count >= 2)
                {
                    XYZ a =
                        output[output.Count - 2];

                    XYZ b =
                        output[output.Count - 1];

                    XYZ ab =
                        (b - a).Normalize();

                    XYZ bc =
                        (p - b).Normalize();

                    double cross =
                        ab.CrossProduct(bc).GetLength();

                    double dot =
                        ab.DotProduct(bc);

                    if (cross < 1e-6 &&
                        dot > 0)
                    {
                        output.RemoveAt(
                            output.Count - 1);
                    }
                }

                output.Add(p);
            }

            return output;
        }
    }
}
