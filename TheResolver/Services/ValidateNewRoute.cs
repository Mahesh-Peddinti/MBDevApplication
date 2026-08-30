using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TheResolver.Services
{
    internal class ValidateNewRoute
    {
        // ------------------------------------------------------------
        // UNIT CONVERSION
        // Revit internal length unit = feet
        // ------------------------------------------------------------
        private const double MM = 1.0 / 304.8;


        public bool ValidateRoute(List<XYZ> points)
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

    }
}
