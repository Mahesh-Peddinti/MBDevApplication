using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using TheResolver.DTOs;
using TheResolver.Services;
using TheResolver.Utilities;



namespace TheResolver
{
    [Transaction(TransactionMode.Manual)]
    public class ModelService : IExternalEventHandler
    {
        #region
        // ------------------------------------------------------------
        // UNIT CONVERSION
        // Revit internal length unit = feet
        // ------------------------------------------------------------
        private const double MM = 1.0 / 304.8;

        // ------------------------------------------------------------
        // TEST SETTINGS
        // ------------------------------------------------------------
              

        // Revit geometry tolerance.
        private const double GeometryTolerance = 1e-9;


        // ============================================================
        // MAIN COMMAND
        // ============================================================        

        public ModelRequest Request { get; set; }

        public void Execute(UIApplication app)
        {            
            UIDocument uidoc = app.ActiveUIDocument;
            Document doc = uidoc.Document;
            GeometricUtilities geometricUtilities = new GeometricUtilities();
            RouteSettingDTOs settings = new RouteSettingDTOs();
            CreateNewBypassRoute createNewBypassRoute = new CreateNewBypassRoute();

            try
            {
                // ----------------------------------------------------
                // 1. COLLECT ALL CABLE TRAYS
                // ----------------------------------------------------

                List<CableTray> trays = new FilteredElementCollector(doc)
                                        .OfClass(typeof(CableTray))
                                        .WhereElementIsNotElementType()
                                        .Cast<CableTray>()
                                        .ToList();


                // ----------------------------------------------------
                // COLLECT ALL CONDUITS
                // ----------------------------------------------------

                List<Conduit> conduits = new FilteredElementCollector(doc)
                                        .OfClass(typeof(Conduit))
                                        .WhereElementIsNotElementType()
                                        .Cast<Conduit>()
                                        .ToList();


                List<BuiltInCategory> multiCategories = new List<BuiltInCategory>();
                ElementMulticategoryFilter multiCategoryFilter = new ElementMulticategoryFilter(multiCategories);


                multiCategories.Add(BuiltInCategory.OST_DuctCurves);
                multiCategories.Add(BuiltInCategory.OST_CableTray);
                multiCategories.Add(BuiltInCategory.OST_Conduit);

                List<Element> modelElements = new FilteredElementCollector(doc)
                                            .WhereElementIsNotElementType()
                                            .WherePasses(new ElementMulticategoryFilter(multiCategories))
                                            .Cast<Element>()
                                            .ToList();


                if (trays.Count == 0 || conduits.Count == 0)
                {
                    TaskDialog.Show(
                        "Cable Tray Clash Resolver",
                        $"Cable Trays found : {trays.Count}\n" +
                        $"Conduits found    : {conduits.Count}\n\n" +
                        "Nothing to process."
                    );
                    //return Result.Succeeded;
                }

                // ----------------------------------------------------
                // CREATE CABLE TRAY SETTINGS
                // ----------------------------------------------------
                 settings =
                                new RouteSettingDTOs()
                                {
                                    BendAngle = 45,

                                    BendRadius = 100 * MM,

                                    MinimumClearance = 180 * MM,

                                    MinimumSideOffset = 150 * MM,

                                    BendSafetyfactor = 1.05 // unitless multiplier

                                };


                // ----------------------------------------------------
                // FIND REAL CLASHES
                // ----------------------------------------------------


                List<ClashInfoDTOs> clashes = geometricUtilities.FindClashes(trays, modelElements, settings);     


                if (clashes.Count == 0)
                {
                    TaskDialog.Show(
                        "Cable Tray Clash Resolver",
                        $"Cable Trays found : {trays.Count}\n" +
                        $"Conduits found    : {conduits.Count}\n\n" +
                        "No solid clashes detected."
                    );
                    //return Result.Succeeded;
                }

               


                // ----------------------------------------------------
                // 4. CREATE REROUTES
                // ----------------------------------------------------

                int createdSegments = 0;
                int skipped = 0;

                HashSet<long> processedTrays = new HashSet<long>();          


                using (Transaction tx = new Transaction(
                                                doc,
                                                "Cable Tray Clash Test Reroute"))
                {
                    tx.Start();


                    foreach (ClashInfoDTOs clash in clashes)
                    {
                        // ------------------------------------------------
                        // V0.1:
                        // Only one clash is processed per original tray.
                        // ------------------------------------------------

                        if (processedTrays.Contains(
                            clash.Tray.Id.Value))
                        {
                            skipped++;
                            continue;
                        }

                        
                        // ------------------------------------------------
                        // BUILD ROUTE MULTI DEGREE BYPASS
                        // ------------------------------------------------

                        List<XYZ> routePoints;                    


                        //// Try to build a bypass route around the clash.
                        if (!geometricUtilities.TryBuildBypassRoutingPoints(clash, settings, out routePoints))
                        {
                            skipped++;
                            continue;
                        }


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

                        // ------------------------------------------------
                        // COLOR NEW ROUTE PINK
                        // ------------------------------------------------

                        foreach (CableTray newTray in created)
                        {
                            OverrideAsPink(
                                doc.ActiveView,
                                newTray.Id);
                        }

                        createdSegments += created.Count;

                        processedTrays.Add(clash.Tray.Id.Value);
                    }

                    tx.Commit();
                }


                // ----------------------------------------------------
                // RESULT
                // ----------------------------------------------------

                TaskDialog.Show(
                    "Cable Tray Clash Resolver",

                    $"Cable Trays found : {trays.Count}\n" +
                    $"Conduits found    : {conduits.Count}\n" +
                    $"Clashes found     : {clashes.Count}\n" +
                    $"Reroutes created  : {processedTrays.Count}\n" +
                    $"New tray segments : {createdSegments}\n" +
                    $"Skipped            : {skipped}\n\n" +
                    "TEST MODE:\n" +
                    "Run Status------------------------------"
                );


                //return Result.Succeeded;
            }
            catch (Exception ex)
            {
               // message = ex.ToString();

                TaskDialog.Show(
                    "Cable Tray Clash Resolver - Error",
                    ex.ToString());

                //return Result.Failed;
            }
        }       
        // ============================================================
        // PINK GRAPHICS
        // ============================================================

        private static void OverrideAsPink(Autodesk.Revit.DB.View view,ElementId elementId)
        {
            OverrideGraphicSettings ogs = new OverrideGraphicSettings();

            ogs.SetProjectionLineColor(
                new Color(
                    255,
                    0,
                    180));

            ogs.SetProjectionLineWeight(6);

            view.SetElementOverrides(elementId, ogs);
        }

        
        

        public string GetName()
        {
            return "ModelService";
        }

        #endregion

    }
}