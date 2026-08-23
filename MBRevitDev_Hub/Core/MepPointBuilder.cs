using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using MBRevitDev_Hub.Models;

namespace MBRevitDev_Hub.Core
{
    public class MepPointBuilder
    {
        private readonly RerouteConfig _config;

        public MepPointBuilder(RerouteConfig config)
        {
            _config = config;
        }

        public List<TrayPointSet> BuildPointSets(
            CableTray tray,
            List<CableTrayClashCluster> clusters,
            XYZ p0,
            XYZ dir,
            double totalLength)
        {
            List<TrayPointSet> pointSets = new List<TrayPointSet>();
            XYZ upVector = XYZ.BasisZ;

            double bendRadius = GetTrayBendRadius(tray);
            double lStraight = 10.0 / 304.8; // Exactly 10mm straight spool (0.0328084 ft)

            for (int i = 0; i < clusters.Count; i++)
            {
                CableTrayClashCluster cluster = clusters[i];
                double deltaZ = cluster.MaxDeltaZ;

                double sFirst = cluster.FirstClashParam;
                double sLast = cluster.LastClashParam;
                double sClashCenter = (sFirst + sLast) / 2.0;

                // 1. Solve exact quadratic equation for Theta given R, deltaZ, and L_straight = 10mm
                // Equation: (4R - deltaZ) * t^2 + (2 * L_straight) * t - deltaZ = 0
                double a = 4.0 * bendRadius - deltaZ;
                double b = 2.0 * lStraight;
                double c = -deltaZ;

                double discriminant = b * b - 4.0 * a * c;
                if (discriminant < 0) discriminant = 0;

                double t = (-b + Math.Sqrt(discriminant)) / (2.0 * a);
                double thetaRad = 2.0 * Math.Atan(Math.Max(0.01, t));
                double angleDeg = thetaRad * (180.0 / Math.PI);

                // 2. Exact Fitting Tangent Takeoff Cutback: T = R * tan(theta / 2) = R * t
                double takeoff = bendRadius * t;

                // 3. Exact Centerline Distances between vertices
                // Slant distance ensures exactly 10mm straight spool remains between Inside and Outside Bends
                double distP1P2 = 2.0 * takeoff + lStraight;

                // Top bridge distance ensures at least 10mm straight spool at apex
                double obsWidth = cluster.ObstacleTotalWidth;
                double lBridgeStraight = Math.Max(lStraight, obsWidth + 2.0 * lStraight);
                double distP2P3 = 2.0 * takeoff + lBridgeStraight;

                // 4. Horizontal Half-Span: DeltaX = distP1P2 * cos(theta) + distP2P3 / 2
                double deltaX = distP1P2 * Math.Cos(thetaRad) + (distP2P3 / 2.0);

                // 5. Construct 3D Coordinates
                XYZ pClash = p0 + dir * sClashCenter;

                XYZ p1 = pClash - dir * deltaX;
                XYZ p2 = p1 + dir * (distP1P2 * Math.Cos(thetaRad)) + upVector * deltaZ;
                XYZ p3 = p2 + dir * distP2P3;
                XYZ p4 = p3 + dir * (distP1P2 * Math.Cos(thetaRad)) - upVector * deltaZ;

                // Prevent boundary underflow
                double sP1 = (p1 - p0).DotProduct(dir);
                if (sP1 < 0)
                {
                    double shift = -sP1;
                    p1 += dir * shift;
                    p2 += dir * shift;
                    p3 += dir * shift;
                    p4 += dir * shift;
                }

                // Prevent overlap with previous cluster
                if (pointSets.Count > 0)
                {
                    XYZ prevP4 = pointSets[pointSets.Count - 1].P4;
                    double prevP4Param = (prevP4 - p0).DotProduct(dir);
                    double currentP1Param = (p1 - p0).DotProduct(dir);
                    if (currentP1Param < prevP4Param + lStraight)
                    {
                        double shift = (prevP4Param + lStraight) - currentP1Param;
                        p1 += dir * shift;
                        p2 += dir * shift;
                        p3 += dir * shift;
                        p4 += dir * shift;
                    }
                }

                pointSets.Add(new TrayPointSet
                {
                    P1 = p1,
                    P2 = p2,
                    P3 = p3,
                    P4 = p4,
                    DeltaZ = deltaZ,
                    CalculatedAngleDeg = angleDeg,
                    FittingTakeoffFeet = takeoff,
                    SlantStraightGapMm = lStraight * 304.8,      // 10mm
                    BridgeStraightGapMm = lBridgeStraight * 304.8 // >= 10mm
                });
            }

            return pointSets;
        }

        private double GetTrayBendRadius(CableTray tray)
        {
            ElementId typeId = tray.GetTypeId();
            CableTrayType trayType = tray.Document.GetElement(typeId) as CableTrayType;
            if (trayType != null)
            {
                Parameter rParam = trayType.get_Parameter(BuiltInParameter.RBS_CABLETRAY_BENDRADIUS);
                if (rParam != null && rParam.AsDouble() > 0.01)
                {
                    return rParam.AsDouble();
                }
            }
            return _config.DefaultBendRadiusFeet; // 300mm default
        }
    }
}
