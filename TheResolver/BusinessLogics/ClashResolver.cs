using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using TheResolver.DTOs;
using TheResolver.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace TheResolver.BusinessLogics
{
    public class ClashResolver
    {
        // ------------------------------------------------------------
        // UNIT CONVERSION
        // Revit internal length unit = feet
        // ------------------------------------------------------------
        private const double MM = 1.0 / 304.8;

        private static readonly BuiltInCategory[] ObstructionCategories =
        {
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_CableTray,
            BuiltInCategory.OST_Conduit,
            BuiltInCategory.OST_PipeCurves
        };

        /// <summary>
        /// Resolves a clash using the built-in defaults. Kept so existing
        /// callers keep compiling; the pane uses the overload that takes the
        /// settings the user typed in.
        /// </summary>
        public bool ResolveSelectedClash(UIApplication app, ClashInfoDTOs clash)
        {
            return ResolveSelectedClash(app, clash, null);
        }

        /// <summary>
        /// Resolves a single clash with explicit route settings.
        /// </summary>
        /// <param name="settings">
        /// Route settings in Revit internal units (feet). Null falls back to
        /// the built-in defaults.
        /// </param>
        public bool ResolveSelectedClash(
                        UIApplication app,
                        ClashInfoDTOs clash,
                        RouteSettingDTOs settings)
        {
            if (app?.ActiveUIDocument?.Document == null || clash?.Tray == null)
            {
                Logger.Log("Invalid application state or clash data.");
                return false;
            }

            Document doc = app.ActiveUIDocument.Document;

            if (settings == null)
            {
                settings = new RouteSettingDTOs
                {
                    BendAngle = 30.0,
                    BendRadius = 100.0 * MM,
                    MinimumClearance = 50.0 * MM,
                    MinimumSideOffset = 250.0 * MM,
                    BendSafetyfactor = 1.05
                };
            }

            // ------------------------------------------------
            // ITERATIVE RESOLUTION
            // Generate a candidate route, validate it against surrounding
            // obstructions, and repeat with adjusted parameters until the
            // new route is clean or the iteration limit is reached.
            // ------------------------------------------------
            var obstructions = CollectObstructions(doc, clash.Tray);

            var (success, created) =
                TryResolveWithIterations(
                    doc, clash, settings, obstructions);

            return success;
        }

        /// <summary>
        /// Attempts to create and validate a bypass route, iterating on
        /// parameters when the new route re-clashes with inline elements.
        /// </summary>
        private (bool success, List<CableTray> created) TryResolveWithIterations(
            Document doc,
            ClashInfoDTOs clash,
            RouteSettingDTOs settings,
            List<Element> obstructions)
        {
            const int maxIterations = 5;

            var currentSettings = CloneSettings(settings);

            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                var geometricUtilities = new GeometricUtilities();

                if (!geometricUtilities.TryBuildBypassRoutingPoints(
                                    clash, currentSettings, out List<XYZ> routePoints)
                                    || routePoints == null
                                    || routePoints.Count < 4)
                {
                    Logger.Log($"Iteration {iteration}: Failed to compute bypass points.");
                    MutateSettingsForNextIteration(currentSettings, iteration);
                    continue;
                }

                using (SubTransaction tx = new SubTransaction(doc))
                {
                    tx.Start();

                    List<CableTray> created =
                        CreateNewBypassRoute.CreateNewRoute(
                            doc,
                            clash.Tray,
                            routePoints[0],
                            routePoints[3],
                            routePoints,
                            currentSettings);

                    if (created == null || created.Count == 0)
                    {
                        Logger.Log($"Iteration {iteration}: Route creation failed, rolling back.");
                        tx.RollBack();
                        MutateSettingsForNextIteration(currentSettings, iteration);
                        continue;
                    }

                    doc.Regenerate();

                    if (created.Any(elem => ReClashesWithObstructions(elem, obstructions, clash.Tray)))
                    {
                        Logger.Log($"Iteration {iteration}: New route clashes with inline elements, rolling back.");
                        tx.RollBack();
                        MutateSettingsForNextIteration(currentSettings, iteration);
                        continue;
                    }

                    tx.Commit();

                    Logger.Log($"Clash resolved successfully for Tray {clash.Tray.Id} " +
                        $"after {iteration + 1} iteration(s). Created {created.Count} segments/fittings.");

                    return (true, created);
                }
            }

            Logger.Log($"Failed to resolve clash for Tray {clash.Tray.Id} after {maxIterations} iterations.");
            return (false, new List<CableTray>());
        }

        /// <summary>
        /// Checks whether the newly created element clashes with any
        /// surrounding obstruction element (excluding the original tray).
        /// Uses bounding-box broad-phase followed by solid intersection.
        /// </summary>
        private static bool ReClashesWithObstructions(
            Element createdElement,
            List<Element> obstructions,
            Element originalTray)
        {
            if (createdElement == null)
                return false;

            BoundingBoxXYZ createdBox = createdElement.get_BoundingBox(null);

            if (createdBox == null)
                return false;

            foreach (Element obs in obstructions)
            {
                if (obs == null)
                    continue;

                if (obs.Id == createdElement.Id)
                    continue;

                if (obs.UniqueId == originalTray?.UniqueId)
                    continue;

                BoundingBoxXYZ obsBox = obs.get_BoundingBox(null);

                if (obsBox == null)
                    continue;

                if (!BoundingBoxesIntersect(createdBox, obsBox))
                    continue;

                List<Solid> createdSolids = GetSolids(createdElement);
                List<Solid> obsSolids = GetSolids(obs);

                foreach (Solid cs in createdSolids)
                {
                    foreach (Solid os in obsSolids)
                    {
                        try
                        {
                            Solid intersection =
                                BooleanOperationsUtils.ExecuteBooleanOperation(
                                    cs, os, BooleanOperationsType.Intersect);

                            if (intersection != null && intersection.Volume > 1e-9)
                                return true;
                        }
                        catch
                        {
                            continue;
                        }
                    }
                }
            }

            return false;
        }

        private static bool BoundingBoxesIntersect(
            BoundingBoxXYZ a,
            BoundingBoxXYZ b)
        {
            return
                a.Min.X <= b.Max.X && a.Max.X >= b.Min.X &&
                a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y &&
                a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;
        }

        private static List<Solid> GetSolids(Element element)
        {
            var solids = new List<Solid>();

            if (element == null)
                return solids;

            Options opt = new Options
            {
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true,
                ComputeReferences = true
            };

            GeometryElement geo = element.get_Geometry(opt);

            if (geo == null)
                return solids;

            ExtractSolidsRecursive(geo, Transform.Identity, solids);

            return solids;
        }

        private static void ExtractSolidsRecursive(
            GeometryElement geo,
            Transform transform,
            List<Solid> solids)
        {
            foreach (GeometryObject obj in geo)
            {
                if (obj is Solid solid && solid.Volume > 1e-6)
                {
                    Solid transformed =
                        SolidUtils.CreateTransformed(solid, transform);
                    solids.Add(transformed);
                }
                else if (obj is GeometryInstance gi)
                {
                    ExtractSolidsRecursive(
                        gi.GetInstanceGeometry(),
                        transform.Multiply(gi.Transform),
                        solids);
                }
            }
        }

        /// <summary>
        /// Collects all MEP elements that cable trays are checked against,
        /// excluding the original clash tray itself.
        /// </summary>
        private static List<Element> CollectObstructions(
            Document doc,
            Element originalTray)
        {
            var obstructions = new List<Element>();

            var multiCategoryFilter =
                new ElementMulticategoryFilter(ObstructionCategories.ToList());

            var collector =
                new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(multiCategoryFilter);

            foreach (Element elem in collector)
            {
                if (elem.UniqueId == originalTray?.UniqueId)
                    continue;

                obstructions.Add(elem);
            }

            return obstructions;
        }

        /// <summary>
        /// Deep-clones settings so each iteration can mutate them
        /// without corrupting the user's input.
        /// </summary>
        private static RouteSettingDTOs CloneSettings(RouteSettingDTOs src)
        {
            if (src == null)
                return new RouteSettingDTOs();

            return new RouteSettingDTOs
            {
                BendAngle = src.BendAngle,
                BendRadius = src.BendRadius,
                MinimumClearance = src.MinimumClearance,
                MinimumSideOffset = src.MinimumSideOffset,
                BendSafetyfactor = src.BendSafetyfactor,
                PreferredDirection = src.PreferredDirection
            };
        }

        /// <summary>
        /// Mutates settings between iterations to escape a re-clash:
        /// widens the side offset, raises clearance, or flips the
        /// detour direction to find an alternative route.
        /// </summary>
        private static void MutateSettingsForNextIteration(
            RouteSettingDTOs settings,
            int iteration)
        {
            if (settings == null)
                return;

            switch (iteration)
            {
                case 0:
                    // Widen horizontal clearance.
                    settings.MinimumSideOffset *= 1.5;
                    break;

                case 1:
                    // Try the opposite vertical side.
                    settings.PreferredDirection =
                        settings.PreferredDirection == RouteDirection.Up
                            ? RouteDirection.Down
                            : RouteDirection.Up;
                    break;

                case 2:
                    // Increase bend radius for a wider arc.
                    settings.BendRadius *= 1.5;
                    break;

                case 3:
                    // Increase vertical clearance.
                    settings.MinimumClearance *= 1.5;
                    break;

                case 4:
                    // Last resort: auto direction with everything widened.
                    settings.PreferredDirection = RouteDirection.Auto;
                    settings.MinimumSideOffset *= 2.0;
                    settings.MinimumClearance *= 2.0;
                    break;
            }
        }
    }
}
