using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using System;
using System.Collections.Generic;
using System.Linq;
using TheResolver.DTOs;

namespace TheResolver.Services
{
    public class ObstacleRegionBuilder
    {
        private const double MM = 1.0 / 304.8;

        public ObstacleRegion Build(
            Document doc,
            ClashInfoDTOs clash,
            RouteSettingDTOs settings)
        {            

            if (clash == null)
                throw new ArgumentNullException(nameof(clash));

            if (clash.Tray == null)
                throw new ArgumentNullException(nameof(clash.Tray));

            CableTray tray = clash.Tray;

            LocationCurve lc =
                tray.Location as LocationCurve;

            if (lc?.Curve is not Line trayLine)
                throw new InvalidOperationException(
                    "Tray must be straight.");

            XYZ trayStart =
                trayLine.GetEndPoint(0);

            XYZ trayEnd =
                trayLine.GetEndPoint(1);

            XYZ trayDirection =
                (trayEnd - trayStart).Normalize();

            List<Element> nearbyObstacles =
                CollectObstacles(
                    doc,
                    tray,
                    clash,
                    trayDirection);

            List<ObstacleProjection> projections =
                BuildProjections(
                    nearbyObstacles,
                    trayStart,
                    trayDirection);

            return BuildRegion(
                            trayStart,
                            trayDirection,
                            projections,
                            settings);
           }



        private List<Element> CollectObstacles(
                                    Document doc,
                                    CableTray tray,
                                    ClashInfoDTOs clash,
                                    XYZ trayDirection)
                {
                    List<Element> result =
                        new List<Element>();

                    BoundingBoxXYZ trayBox =
                        tray.get_BoundingBox(null);

                    if (trayBox == null)
                        return result;

                    double search =
                        5000 * MM;

                    XYZ min =
                        trayBox.Min -
                        new XYZ(search, search, search);

                    XYZ max =
                        trayBox.Max +
                        new XYZ(search, search, search);

                    Outline outline =
                        new Outline(min, max);

                    BoundingBoxIntersectsFilter filter =
                        new BoundingBoxIntersectsFilter(outline);

                    BuiltInCategory[] categories =
                    {
                        BuiltInCategory.OST_Conduit,
                        BuiltInCategory.OST_CableTray,
                        BuiltInCategory.OST_PipeCurves,
                        BuiltInCategory.OST_DuctCurves,
                        BuiltInCategory.OST_StructuralFraming,
                        BuiltInCategory.OST_StructuralColumns
                    };

                    ElementMulticategoryFilter catFilter =
                        new ElementMulticategoryFilter(
                            categories.ToList());

                    result.AddRange(
                        new FilteredElementCollector(doc)
                            .WherePasses(filter)
                            .WherePasses(catFilter)
                            .WhereElementIsNotElementType());

                    return result;
                }

        private IEnumerable<Element> GetLinkedElements(
                                        Document hostDocument,
                                        Outline outline)
        {
            List<Element> result =
                            new();

            var links =
                new FilteredElementCollector(hostDocument)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>();

            foreach (var link in links)
            {
                Document linkDoc =
                    link.GetLinkDocument();

                if (linkDoc == null)
                    continue;

                ElementMulticategoryFilter filter = new ElementMulticategoryFilter(ObstructionCategories);

                foreach (Element e
                         in new FilteredElementCollector(linkDoc)
                            .WherePasses(filter))
                {
                    result.Add(e);
                }
            }

            return result;
        }
                    

        private List<ObstacleProjection> BuildProjections(
                List<Element> obstacles,
                XYZ trayOrigin,
                XYZ trayDirection)
                    {
                        List<ObstacleProjection> results =
                            new();

                        foreach (Element e in obstacles)
                        {
                            BoundingBoxXYZ bb =
                                e.get_BoundingBox(null);

                            if (bb == null)
                                continue;

                            XYZ center =
                                (bb.Min + bb.Max) * 0.5;

                            double station =
                                (center - trayOrigin)
                                .DotProduct(trayDirection);

                            double halfLength =
                                EstimateHalfLengthAlongDirection(
                                    bb,
                                    trayDirection);

                            results.Add(
                                new ObstacleProjection
                                {
                                    Element = e,

                                    BoundingBox = bb,

                                    MinStation =
                                        station - halfLength,

                                    MaxStation =
                                        station + halfLength,

                                    MinElevation =
                                        bb.Min.Z,

                                    MaxElevation =
                                        bb.Max.Z
                                });
                        }

                        return results;
                    }

        private ObstacleRegion BuildRegion(
                            XYZ trayOrigin,
                            XYZ trayDirection,
                            List<ObstacleProjection> projections,
                            RouteSettingDTOs settings)
        {
            if (projections.Count == 0)
                throw new InvalidOperationException(
                    "No obstacles found.");

            double clearance =
                Math.Max(
                    settings.MinimumClearance,
                    100 * MM);

            double minStation =
                projections.Min(x => x.MinStation);

            double maxStation =
                projections.Max(x => x.MaxStation);

            minStation -=
                settings.MinimumSideOffset +
                clearance;

            maxStation +=
                settings.MinimumSideOffset +
                clearance;

            XYZ breakStart =
                trayOrigin +
                trayDirection * minStation;

            XYZ breakEnd =
                trayOrigin +
                trayDirection * maxStation;

            double regionHeight =
                projections.Max(x => x.MaxElevation) -
                projections.Min(x => x.MinElevation);

            XYZ center =
                trayOrigin +
                trayDirection *
                ((minStation + maxStation) / 2.0);

            return new ObstacleRegion
            {
                TrayOrigin = trayOrigin,

                TrayDirection = trayDirection,

                BreakStart = breakStart,

                BreakEnd = breakEnd,

                RegionCenter = center,

                MinStation = minStation,

                MaxStation = maxStation,

                RegionLength =
                    maxStation - minStation,

                RegionHeight =
                    regionHeight,

                Obstacles =
                    projections
                    .Select(x => x.Element)
                    .Distinct()
                    .ToList(),

                Projections =
                    projections
            };
        }

        private double EstimateHalfLengthAlongDirection(
                            BoundingBoxXYZ bb,
                            XYZ direction)
        {
            List<XYZ> corners =
                new()
                {
            new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z),
            new XYZ(bb.Min.X, bb.Min.Y, bb.Max.Z),
            new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z),
            new XYZ(bb.Min.X, bb.Max.Y, bb.Max.Z),

            new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z),
            new XYZ(bb.Max.X, bb.Min.Y, bb.Max.Z),
            new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z),
            new XYZ(bb.Max.X, bb.Max.Y, bb.Max.Z)
                };

            List<double> projections =
                corners
                .Select(c =>
                    c.DotProduct(direction))
                .ToList();

            return
                (projections.Max() -
                 projections.Min()) / 2.0;
        }

        private static readonly BuiltInCategory[] ObstructionCategories =
        {
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_CableTray,
            BuiltInCategory.OST_Conduit,
            BuiltInCategory.OST_PipeCurves
        };



    }


}
