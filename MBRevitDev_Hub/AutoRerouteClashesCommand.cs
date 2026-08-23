using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using MBRevitDev_Hub.Core;
using MBRevitDev_Hub.Models;
using MBRevitDev_Hub.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MBRevitDev_Hub
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoRerouteClashesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // Configuration: 500mm reference distance to P1/P4, 50mm clearance above obstacle
                RerouteConfig config = new RerouteConfig
                {
                    ClashOffsetRangeFeet = 500.0 / 304.8,     // 500mm from clash reference point
                    Clearance50mmFeet = 50.0 / 304.8,        // 50mm clearance above obstacle
                    TransitionRunFeet = 200.0 / 304.8        // 200mm transition incline
                };

                // Collect all Cable Trays in active view or entire document
                FilteredElementCollector collector = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .OfCategory(BuiltInCategory.OST_CableTray)
                    .WhereElementIsNotElementType();

                List<CableTray> trays = collector.Cast<CableTray>().ToList();

                // If active view has none, query whole model
                if (trays.Count == 0)
                {
                    trays = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_CableTray)
                        .WhereElementIsNotElementType()
                        .Cast<CableTray>()
                        .ToList();
                }

                if (trays.Count == 0)
                {
                    TaskDialog.Show("Auto Reroute", "No Cable Trays found in the project.");
                    return Result.Succeeded;
                }

                int traysRerouted = 0;
                int totalClashesResolved = 0;

                ClashDetector detector = new ClashDetector(doc, config);
                ClashClusterEngine clusterEngine = new ClashClusterEngine(config);
                MepPointBuilder pointBuilder = new MepPointBuilder(config);
                MepRebuilder rebuilder = new MepRebuilder(doc, config);

                using (TransactionGroup tg = new TransactionGroup(doc, "Cable Tray 500mm Clash Auto-Reroute"))
                {
                    tg.Start();

                    foreach (CableTray tray in trays)
                    {
                        LocationCurve lc = tray.Location as LocationCurve;
                        Line centerLine = lc?.Curve as Line;
                        if (centerLine == null) continue;

                        // 1. Identify Clashes and exact Clash Points
                        List<CableTrayClashInfo> rawClashes = detector.DetectClashes(tray);
                        if (rawClashes.Count == 0) continue;

                        XYZ p0 = centerLine.GetEndPoint(0);
                        XYZ p1 = centerLine.GetEndPoint(1);
                        XYZ dir = (p1 - p0).Normalize();
                        double totalLength = centerLine.Length;

                        // 2. Cluster clashes using 500mm range rule
                        List<CableTrayClashCluster> clusters = clusterEngine.ClusterClashes(rawClashes, totalLength);
                        if (clusters.Count == 0) continue;

                        // 3. Build Point Sets (P1, P2, P3, P4)
                        List<TrayPointSet> pointSets = pointBuilder.BuildPointSets(tray, clusters, p0, dir, totalLength);
                        if (pointSets.Count == 0) continue;

                        // 4. Reconstruct Cable Tray with 50mm clearance and Elbow fittings
                        using (Transaction tx = new Transaction(doc, $"Reroute Tray {tray.Id}"))
                        {
                            FailureHandlingOptions failOpt = tx.GetFailureHandlingOptions();
                            failOpt.SetFailuresPreprocessor(new WarningSwallower());
                            tx.SetFailureHandlingOptions(failOpt);

                            tx.Start();
                            rebuilder.ExecuteRebuild(tray, pointSets);
                            tx.Commit();
                        }

                        traysRerouted++;
                        totalClashesResolved += rawClashes.Count;
                    }

                    tg.Assimilate();
                }

                TaskDialog.Show("Cable Tray Auto-Reroute Complete",
                    $"Automatic Clash Rerouting Summary:\n\n" +
                    $"• Cable Trays Analyzed: {trays.Count}\n" +
                    $"• Cable Trays Rerouted: {traysRerouted}\n" +
                    $"• Clashes Resolved: {totalClashesResolved}\n\n" +
                    $"• Strategy Applied: Clash Point as Reference -> 500mm to P1 / P4\n" +
                    $"• Top Clearance: 50mm above obstacle\n" +
                    $"• Point Architecture: (P1, P2, P3, P4) reconstructed successfully.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message + "\n" + ex.StackTrace;
                return Result.Failed;
            }
        }
    }
}
