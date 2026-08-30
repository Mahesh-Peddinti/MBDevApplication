using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using TheResolver.DTOs;

namespace TheResolver.BusinessLogics
{
    public class CalculatingLogics
    {  

        public ClashSide DetermineClashSide(
                                    CableTray tray,
                                    Element conduit,
                                    double tolerance)
        {
            if (tray == null ||
                conduit == null)
                return ClashSide.Unknown;

            BoundingBoxXYZ trayBox =
                tray.get_BoundingBox(null);

            BoundingBoxXYZ conduitBox =
                conduit.get_BoundingBox(null);

            if (trayBox == null ||
                conduitBox == null)
                return ClashSide.Unknown;

            double trayMinZ = trayBox.Min.Z;
            double trayMaxZ = trayBox.Max.Z;

            double conduitMinZ = conduitBox.Min.Z;
            double conduitMaxZ = conduitBox.Max.Z;

            double trayCenterZ =
                (trayMinZ + trayMaxZ) * 0.5;

            double conduitCenterZ =
                (conduitMinZ + conduitMaxZ) * 0.5;

            //------------------------------------------------------
            // Clearly above
            //------------------------------------------------------

            if (conduitMinZ >= trayMaxZ - tolerance)
            {
                return ClashSide.AboveTray;
            }

            //------------------------------------------------------
            // Clearly below
            //------------------------------------------------------

            if (conduitMaxZ <= trayMinZ + tolerance)
            {
                return ClashSide.BelowTray;
            }

            //------------------------------------------------------
            // Actual clash / overlap.
            //
            // Use the centre relationship to determine which
            // side dominates.
            //------------------------------------------------------

            if (conduitCenterZ > trayCenterZ)
            {
                return ClashSide.AboveTray;
            }

            if (conduitCenterZ < trayCenterZ)
            {
                return ClashSide.BelowTray;
            }

            return ClashSide.CrossingTray;
        }


        public double CalculateMinimumBendRise(
                                    double bendRadius,
                                    double bendAngleDegrees)
        {
            if (bendRadius <= 0)
                return 0;

            if (bendAngleDegrees <= 0)
                return 0;

            if (bendAngleDegrees >= 180)
                throw new ArgumentException(
                    "Bend angle must be less than 180 degrees.");

            double theta =
                bendAngleDegrees *
                Math.PI / 180.0;

            double rise =
                2.0 *
                bendRadius *
                (1.0 - Math.Cos(theta));

            return rise;
        }


        public double CalculateRequiredRise(
                                    CableTray tray,
                                    Element conduit,
                                    double existingRequiredRise,
                                    double bendRadius,
                                    double bendAngleDegrees,
                                    double clearance)
        {
            if (tray == null ||
                conduit == null)
                return existingRequiredRise;

            double tolerance =
                2.0 / 304.8; // 2 mm

            ClashSide side =
                DetermineClashSide(
                    tray,
                    conduit,
                    tolerance);

            //------------------------------------------------------
            // Minimum geometric rise required to physically form
            // the two bends.
            //------------------------------------------------------

            double bendMinimumRise =
                CalculateMinimumBendRise(
                    bendRadius,
                    bendAngleDegrees);

            //------------------------------------------------------
            // Existing calculation already works for the
            // normal/top case.
            //------------------------------------------------------

            if (side == ClashSide.AboveTray)
            {
                double rise =
                    Math.Max(
                        existingRequiredRise,
                        bendMinimumRise);

                Logger.Log(
                    $"CLASH ABOVE TRAY | " +
                    $"ExistingRise={existingRequiredRise * 304.8:F1} mm | " +
                    $"BendRise={bendMinimumRise * 304.8:F1} mm | " +
                    $"FinalRise={rise * 304.8:F1} mm");

                return rise;
            }

            //------------------------------------------------------
            // BOTTOM CASE
            //------------------------------------------------------

            if (side == ClashSide.BelowTray)
            {
                BoundingBoxXYZ trayBox =
                    tray.get_BoundingBox(null);

                BoundingBoxXYZ conduitBox =
                    conduit.get_BoundingBox(null);

                if (trayBox == null ||
                    conduitBox == null)
                {
                    return Math.Max(
                        existingRequiredRise,
                        bendMinimumRise);
                }

                //--------------------------------------------------
                // New tray bottom must be above conduit top.
                //--------------------------------------------------

                double clearanceRise =
                    conduitBox.Max.Z
                    - trayBox.Min.Z
                    + clearance;

                clearanceRise =
                    Math.Max(
                        0,
                        clearanceRise);

                //--------------------------------------------------
                // The final rise must satisfy BOTH:
                //
                // 1. physical clearance
                // 2. bend geometry
                //--------------------------------------------------

                double finalRise =
                    Math.Max(
                        clearanceRise,
                        bendMinimumRise);

                Logger.Log(
                    $"CLASH BELOW TRAY | " +
                    $"ClearanceRise={clearanceRise * 304.8:F1} mm | " +
                    $"BendRise={bendMinimumRise * 304.8:F1} mm | " +
                    $"FinalRise={finalRise * 304.8:F1} mm");

                return finalRise;
            }

            //------------------------------------------------------
            // CROSSING / UNKNOWN
            //------------------------------------------------------

            Logger.Log(
                $"Clash side={side}. " +
                $"Using conservative rise.");

            return Math.Max(
                existingRequiredRise,
                bendMinimumRise);
        }


        public double CalculateBendHorizontalOffset(
                                    double bendRadius,
                                    double bendAngleDegrees)
        {
            double theta =
                bendAngleDegrees *
                Math.PI / 180.0;

            return
                2.0 *
                bendRadius *
                Math.Sin(theta);
        }
    }
}
