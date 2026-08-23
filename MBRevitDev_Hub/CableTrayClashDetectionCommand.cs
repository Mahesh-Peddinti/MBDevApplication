using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using MBRevitDev_Hub.DTOs;
using MBRevitDev_Hub.Services;
using System;
using System.Collections.Generic;
using System.Linq;


namespace MBRevitDev_Hub
{
    [Transaction(TransactionMode.Manual)]
    public class CableTrayClashDetectionCommand : IExternalCommand
    {
        // ================================================================
        // CONFIGURATION
        // ================================================================

        private const double MmToFt = 1.0 / 304.8;           

        // ================================================================
        // MAIN COMMAND
        // ================================================================

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;
            // Initialize clash detection service
            ClashDetectionServices clashDetectionServices = new ClashDetectionServices();
            RouteSettingDTOs routeSettings = new RouteSettingDTOs
            {
                HorizontalMinimumClearanceMm = 100.0,
                ClearanceMm = 50.0,
                MinimumRiseMm = 100.0
            };
            try
            {
                List<CableTray> trays = CollectCableTrays(doc);
                List<Conduit> conduits = CollectConduits(doc);

                if (trays.Count == 0 || conduits.Count == 0)
                {
                    ShowSummary(
                        "Cable Tray Clash Resolver",
                        $"Cable Trays : {trays.Count}\n" +
                        $"Conduits    : {conduits.Count}\n\n" +
                        "Nothing to process.");

                    return Result.Succeeded;
                }

                
                List<ClashInfoDTOs> clashes = clashDetectionServices.FindClashes(trays, conduits);

                if (clashes.Count == 0)
                {
                    ShowSummary(
                        "Cable Tray Clash Resolver",
                        $"Cable Trays : {trays.Count}\n" +
                        $"Conduits    : {conduits.Count}\n\n" +
                        "No solid clashes detected.");

                    return Result.Succeeded;
                }

                // ============================================================
                // GROUP CLASHES BY PROXIMITY
                // ============================================================
                List<ClashGroupDTOs> clashGroups = clashDetectionServices.GroupClashesByProximity(
                    clashes,
                    routeSettings.HorizontalMinimumClearanceMm * MmToFt);

                if (clashGroups.Count == 0)
                {
                    ShowSummary(
                        "Cable Tray Clash Resolver",
                        "Clash grouping failed or produced no valid groups.");
                    return Result.Succeeded;
                }

                int successfulGroups = 0;
                int skippedGroups = 0;
                int createdSegments = 0;
                int createdFittings = 0;
                int traysSplit = 0;

                // ============================================================
                // PROCESS EACH CLASH GROUP
                // ============================================================
                using (Transaction tx =
                    new Transaction(doc, "Auto Resolve Cable Tray Clashes"))
                {
                    tx.Start();

                    foreach (ClashGroupDTOs group in clashGroups)
                    {
                        CableTray originalTray =
                            doc.GetElement(group.TrayId) as CableTray;

                        if (originalTray == null)
                        {
                            skippedGroups++;
                            continue;
                        }

                        RouteDefinitionDTOs route;

                        CreatingBypassRoutings creatingBypassRoutings = new CreatingBypassRoutings();

                        if (!creatingBypassRoutings.TryBuildRoutePoints(group, originalTray, out route))
                        {
                            skippedGroups++;
                            continue;
                        }

                        using (SubTransaction st = new SubTransaction(doc))
                        {
                            st.Start();

                            try
                            {
                                // Create the 4-point bypass route (P0→P1→P2→P3).
                                List<CableTray> bypassSegments =
                                    creatingBypassRoutings.CreateBypassSegments(
                                        doc,
                                        originalTray,
                                        route.Points);

                                if (bypassSegments.Count != 3)
                                {
                                    st.RollBack();
                                    skippedGroups++;
                                    continue;
                                }

                                doc.Regenerate();

                                // Connect the bypass segments at junction points.
                                List<ElementId> fittingIds =
                                    creatingBypassRoutings.ConnectBypassSegments(
                                        doc,
                                        bypassSegments,
                                        route.Points);

                                if (fittingIds.Count < 2)
                                {
                                    st.RollBack();
                                    skippedGroups++;
                                    continue;
                                }

                                doc.Regenerate();

                                // Validate the bypass route against all original conduits.
                                bool stillClashes = false;

                                foreach (ClashInfoDTOs clash in group.Clashes)
                                {
                                    if (creatingBypassRoutings.RouteStillClashes(
                                        bypassSegments,
                                        fittingIds,
                                        clash.Conduit))
                                    {
                                        stillClashes = true;
                                        break;
                                    }
                                }

                                if (stillClashes)
                                {
                                    st.RollBack();
                                    skippedGroups++;
                                    continue;
                                }

                                // Split the original tray:
                                // Keep pre-bypass and post-bypass segments,
                                // delete the section that conflicts.
                                bool splitSuccess = creatingBypassRoutings.TrySplitOriginalTray(
                                    doc,
                                    originalTray,
                                    route);

                                if (!splitSuccess)
                                {
                                    st.RollBack();
                                    skippedGroups++;
                                    continue;
                                }

                                traysSplit++;

                                // Visualize the bypass route.
                                foreach (CableTray segment in bypassSegments)
                                {
                                    OverrideAsPink(
                                        doc.ActiveView,
                                        segment.Id);
                                }

                                st.Commit();

                                successfulGroups++;
                                createdSegments += bypassSegments.Count;
                                createdFittings += fittingIds.Count;
                            }
                            catch (Exception ex)
                            {
                                st.RollBack();
                                skippedGroups++;
                            }
                        }
                    }

                    tx.Commit();
                }

                ShowSummary(
                    "Cable Tray Clash Resolver - Multi-Clash",
                    $"Cable Trays found     : {trays.Count}\n" +
                    $"Conduits found        : {conduits.Count}\n" +
                    $"Clashes found         : {clashes.Count}\n" +
                    $"Clash groups formed   : {clashGroups.Count}\n\n" +
                    $"Successful groups     : {successfulGroups}\n" +
                    $"Trays split           : {traysSplit}\n" +
                    $"Bypass segments       : {createdSegments}\n" +
                    $"Fittings created      : {createdFittings}\n" +
                    $"Skipped groups        : {skippedGroups}\n\n" +
                    "PRODUCTION MODE:\n" +
                    "Original Cable Trays were split and modified.\n" +
                    "Bypass routes shown in pink.");

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

        // ================================================================
        // COLLECTION
        // ================================================================

        private static List<CableTray> CollectCableTrays(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(CableTray))
                .WhereElementIsNotElementType()
                .Cast<CableTray>()
                .ToList();
        }

        private static List<Conduit> CollectConduits(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Conduit))
                .WhereElementIsNotElementType()
                .Cast<Conduit>()
                .ToList();
        }       
        
        

        // ================================================================
        // PARAMETER / GRAPHICS
        // ================================================================       

        private static void OverrideAsPink(
            View view,
            ElementId elementId)
        {
            OverrideGraphicSettings settings =
                new OverrideGraphicSettings();

            settings.SetProjectionLineColor(
                new Color(255, 0, 180));

            settings.SetProjectionLineWeight(6);

            view.SetElementOverrides(elementId, settings);
        }

        // ================================================================
        // UI
        // ================================================================

        private static void ShowSummary(
            string title,
            string message)
        {
            TaskDialog.Show(title, message);
        }      

       
    }
}
