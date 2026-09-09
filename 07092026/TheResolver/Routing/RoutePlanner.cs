using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using TheResolver.DTOs;

namespace TheResolver.Routing
{
    public class RoutePlanner
    {
        private const double MM = 1.0 / 304.8;

        public List<RouteCandidate> GenerateCandidates(
            ObstacleRegion region,
            RouteSettingDTOs settings)
        {
            var routes = new List<RouteCandidate>();

            routes.AddRange(
                GenerateOverRoutes(region, settings));

            routes.AddRange(
                GenerateUnderRoutes(region, settings));

            routes.AddRange(
                GenerateLeftRoutes(region, settings));

            routes.AddRange(
                GenerateRightRoutes(region, settings));

            routes.AddRange(
                GenerateMixedRoutes(region, settings));

            return routes;
        }

        #region OVER

        private IEnumerable<RouteCandidate> GenerateOverRoutes(
            ObstacleRegion region,
            RouteSettingDTOs settings)
        {
            var routes =
                new List<RouteCandidate>();

            double[] factors =
            {
                1.0,
                1.5,
                2.0
            };

            foreach (double factor in factors)
            {
                routes.Add(
                    BuildOverCandidate(
                        region,
                        settings,
                        factor));
            }

            return routes;
        }

        private RouteCandidate BuildOverCandidate(
            ObstacleRegion region,
            RouteSettingDTOs settings,
            double riseFactor)
        {
            double rise =
                region.RegionHeight +
                settings.MinimumClearance +
                (100 * MM);

            rise *= riseFactor;

            XYZ up =
                XYZ.BasisZ * rise;

            XYZ p1 =
                region.BreakStart;

            XYZ p2 =
                region.BreakStart + up;

            XYZ p3 =
                region.BreakEnd + up;

            XYZ p4 =
                region.BreakEnd;

            RouteCandidate route =
                RouteCandidateFactory.Create(
                    RouteType.Over,
                    region.BreakStart,
                    region.BreakEnd);

            route.Name =
                $"Over - x{riseFactor:0.0} Rise";

            route.Description =
                $"Vertical bypass over obstacle region.";

            route.DetourPoints.Add(p2);
            route.DetourPoints.Add(p3);

            PopulateMetrics(
                route,
                rise,
                lateralOffset: 0);

            return route;
        }

        #endregion

        #region UNDER

        private IEnumerable<RouteCandidate> GenerateUnderRoutes(
            ObstacleRegion region,
            RouteSettingDTOs settings)
        {
            var routes =
                new List<RouteCandidate>();

            double[] factors =
            {
                1.0,
                1.5,
                2.0
            };

            foreach (double factor in factors)
            {
                routes.Add(
                    BuildUnderCandidate(
                        region,
                        settings,
                        factor));
            }

            return routes;
        }

        private RouteCandidate BuildUnderCandidate(
            ObstacleRegion region,
            RouteSettingDTOs settings,
            double dropFactor)
        {
            double drop =
                region.RegionHeight +
                settings.MinimumClearance +
                (100 * MM);

            drop *= dropFactor;

            XYZ down =
                -XYZ.BasisZ * drop;

            XYZ p1 =
                region.BreakStart;

            XYZ p2 =
                p1 + down;

            XYZ p3 =
                region.BreakEnd + down;

            XYZ p4 =
                region.BreakEnd;

            RouteCandidate route =
                RouteCandidateFactory.Create(
                    RouteType.Under,
                    region.BreakStart,
                    region.BreakEnd);

            route.Name =
                $"Under - x{dropFactor:0.0} Drop";

            route.Description =
                $"Route below obstacle envelope.";

            route.DetourPoints.Add(p2);
            route.DetourPoints.Add(p3);

            PopulateMetrics(
                route,
                drop,
                lateralOffset: 0);

            route.ElevationDrop =
                drop;

            return route;
        }

        #endregion

        #region LEFT

        private IEnumerable<RouteCandidate> GenerateLeftRoutes(
            ObstacleRegion region,
            RouteSettingDTOs settings)
        {
            var routes =
                new List<RouteCandidate>();

            XYZ leftDir =
                GetLeftDirection(
                    region.TrayDirection);

            double[] multipliers =
            {
                1.0,
                1.5,
                2.0
            };

            foreach (double factor in multipliers)
            {
                double offset =
                    region.RegionWidth +
                    settings.MinimumClearance +
                    (100 * MM);

                offset *= factor;

                XYZ shift =
                    leftDir * offset;

                RouteCandidate route =
                    RouteCandidateFactory.Create(
                        RouteType.Left,
                        region.BreakStart,
                        region.BreakEnd);

                route.Name =
                    $"Left - x{factor:0.0}";

                XYZ p1 =
                    region.BreakStart + shift;

                XYZ p2 =
                    region.BreakEnd + shift;

                route.DetourPoints.Add(p1);
                route.DetourPoints.Add(p2);

                PopulateMetrics(
                    route,
                    elevation: 0,
                    lateralOffset: offset);

                routes.Add(route);
            }

            return routes;
        }

        #endregion

        #region RIGHT

        private IEnumerable<RouteCandidate> GenerateRightRoutes(
            ObstacleRegion region,
            RouteSettingDTOs settings)
        {
            var routes =
                new List<RouteCandidate>();

            XYZ rightDir =
                -GetLeftDirection(
                    region.TrayDirection);

            double[] multipliers =
            {
                1.0,
                1.5,
                2.0
            };

            foreach (double factor in multipliers)
            {
                double offset =
                    region.RegionWidth +
                    settings.MinimumClearance +
                    (100 * MM);

                offset *= factor;

                XYZ shift =
                    rightDir * offset;

                RouteCandidate route =
                    RouteCandidateFactory.Create(
                        RouteType.Right,
                        region.BreakStart,
                        region.BreakEnd);

                route.Name =
                    $"Right - x{factor:0.0}";

                XYZ p1 =
                    region.BreakStart + shift;

                XYZ p2 =
                    region.BreakEnd + shift;

                route.DetourPoints.Add(p1);
                route.DetourPoints.Add(p2);

                PopulateMetrics(
                    route,
                    elevation: 0,
                    lateralOffset: offset);

                routes.Add(route);
            }

            return routes;
        }

        #endregion

        #region MIXED

        private IEnumerable<RouteCandidate> GenerateMixedRoutes(
            ObstacleRegion region,
            RouteSettingDTOs settings)
        {
            var routes =
                new List<RouteCandidate>();

            XYZ left =
                GetLeftDirection(
                    region.TrayDirection);

            double rise =
                region.RegionHeight +
                settings.MinimumClearance +
                (100 * MM);

            double offset =
                region.RegionWidth +
                settings.MinimumClearance +
                (100 * MM);

            RouteCandidate route =
                RouteCandidateFactory.Create(
                    RouteType.Mixed,
                    region.BreakStart,
                    region.BreakEnd);

            route.Name =
                "Mixed Rise + Left";

            XYZ p1 =
                region.BreakStart +
                left * offset * 0.5;

            XYZ p2 =
                p1 +
                XYZ.BasisZ * rise;

            XYZ p3 =
                region.BreakEnd +
                left * offset * 0.5 +
                XYZ.BasisZ * rise;

            XYZ p4 =
                region.BreakEnd +
                left * offset * 0.5;

            route.DetourPoints.Add(p1);
            route.DetourPoints.Add(p2);
            route.DetourPoints.Add(p3);
            route.DetourPoints.Add(p4);

            PopulateMetrics(
                route,
                rise,
                offset);

            routes.Add(route);

            return routes;
        }

        #endregion

        #region HELPERS

        private XYZ GetLeftDirection(
            XYZ trayDirection)
        {
            XYZ up =
                XYZ.BasisZ;

            XYZ left =
                up.CrossProduct(
                    trayDirection);

            if (left.GetLength() < 1e-9)
            {
                return XYZ.BasisY;
            }

            return left.Normalize();
        }

        private void PopulateMetrics(
            RouteCandidate route,
            double elevation,
            double lateralOffset)
        {
            route.ElevationGain =
                elevation;

            route.LateralOffset =
                lateralOffset;

            //route.BendCount = EstimateBends(route);
        }
        #endregion
    }
}