using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;

namespace TheResolver.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CableTrayClashResolver 
    {

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
        /*
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

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


                if (trays.Count == 0 || conduits.Count == 0)
                {
                    TaskDialog.Show(
                        "Cable Tray Clash Resolver",
                        $"Cable Trays found : {trays.Count}\n" +
                        $"Conduits found    : {conduits.Count}\n\n" +
                        "Nothing to process."
                    );
                    return Result.Succeeded;
                }

                // ----------------------------------------------------
                // CREATE CABLE TRAY SETTINGS
                // ----------------------------------------------------
                RouteSettings settings =
                                new RouteSettings()
                                {
                                    BendAngle = 45,

                                    BendRadius = 100 * MM,

                                    MinimumClearance = 180 * MM,

                                    MinimumSideOffset = 150 * MM,

                                    BendSafetyfactor = 1.05 * MM

                                };


                // ----------------------------------------------------
                // FIND REAL CLASHES
                // ----------------------------------------------------

                List<ClashInfo> clashes = FindClashes(trays, conduits, settings);


                if (clashes.Count == 0)
                {
                    TaskDialog.Show(
                        "Cable Tray Clash Resolver",
                        $"Cable Trays found : {trays.Count}\n" +
                        $"Conduits found    : {conduits.Count}\n\n" +
                        "No solid clashes detected."
                    );
                    return Result.Succeeded;
                }


                // ----------------------------------------------------
                // 4. CREATE REROUTES
                // ----------------------------------------------------

                int createdSegments = 0;
                int skipped = 0;

                HashSet<long> processedTrays = new HashSet<long>();


                using (Transaction tx =
                    new Transaction(
                        doc,
                        "Cable Tray Clash Test Reroute"))
                {
                    tx.Start();


                    foreach (ClashInfo clash in clashes)
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
                        if (!TryBuildBypassRoutingPoints(clash, settings, out routePoints))
                        {
                            skipped++;
                            continue;
                        }


                        // ------------------------------------------------
                        // CREATE ACTUAL REVIT CABLE TRAYS
                        // ------------------------------------------------

                        List<CableTray> created =
                            CreateNewRoute(
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


                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();

                TaskDialog.Show(
                    "Cable Tray Clash Resolver - Error",
                    ex.ToString());

                return Result.Failed;
            }
        }
        */
        
    }


}
