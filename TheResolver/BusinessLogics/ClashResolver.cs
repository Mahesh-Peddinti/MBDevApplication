using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using TheResolver.DTOs;
using TheResolver.Utilities;
using System;
using System.Collections.Generic;
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
            GeometricUtilities geometricUtilities = new GeometricUtilities();

            if (settings == null)
            {
                settings = new RouteSettingDTOs
                {
                    BendAngle = 30.0,
                    BendRadius = 100.0 * MM,
                    MinimumClearance = 50.0 * MM,
                    MinimumSideOffset = 250.0 * MM,
                    BendSafetyfactor = 1.05 // Unitless multiplier
                };
            }

            // ------------------------------------------------
            // BUILD ROUTE MULTI-DEGREE BYPASS
            // ------------------------------------------------
            if (!geometricUtilities.TryBuildBypassRoutingPoints(
                                clash, settings, out List<XYZ> routePoints) 
                                || routePoints == null 
                                || routePoints.Count < 4)
            {
                Logger.Log($"Failed to compute valid bypass routing points for Tray {clash.Tray.Id}.");
                return false;
            }

            // ------------------------------------------------
            // CREATE ACTUAL REVIT CABLE TRAYS & FITTINGS
            // ------------------------------------------------
            using (Transaction tx = new Transaction( doc,"cable Tray Clash Test Reroute"))
            {
                try
                {
                    tx.Start();

                    // ------------------------------------------------
                    // CREATE ACTUAL REVIT CABLE TRAYS
                    // ------------------------------------------------

                    List<CableTray> created =
                        CreateNewBypassRoute.CreateNewRoute(
                                                doc,
                                                clash.Tray,
                                                routePoints[0],
                                                routePoints[3],
                                                routePoints,
                                                settings);

                    if (created == null || created.Count == 0)
                    {
                        Logger.Log($"Reroute creation failed for Tray {clash.Tray.Id}." +
                            $" Rolling back transaction.");
                        tx.RollBack();
                        return false;
                    }

                    // ------------------------------------------------
                    // COLOR NEW ROUTE PINK
                    // ------------------------------------------------

                    //foreach (CableTray newTray in created)
                    //{
                    //    OverrideAsPink(
                    //        doc.ActiveView,
                    //        newTray.Id);
                    //}   

                    tx.Commit();
                    Logger.Log($"Clash resolved successfully for Tray {clash.Tray.Id}. " +
                        $"Created {created.Count} segments/fittings.");
                    return true;                    

                }
                catch (Exception ex)
                {
                    Logger.Log($"Exception during clash resolution transaction: {ex.Message}");
                    if (tx.HasStarted() && !tx.HasEnded())
                    {
                        tx.RollBack();
                    }
                    return false;
                }                
            }
        }
    }
}
