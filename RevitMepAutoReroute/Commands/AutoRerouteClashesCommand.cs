using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using RevitMepAutomation.Core;
using RevitMepAutomation.Models;
using RevitMepAutomation.Utils;

namespace RevitMepAutomation.Commands
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
                // 1. Pick target Cable Tray or Conduit
                Reference pickedRef = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new MepCurveSelectionFilter(),
                    "Select a Cable Tray or Conduit to analyze and auto-reroute");

                MEPCurve targetCurve = doc.GetElement(pickedRef) as MEPCurve;
                if (targetCurve == null) return Result.Cancelled;

                // 2. Default Configuration
                RerouteConfig config = new RerouteConfig
                {
                    ClearanceFeet = 50.0 / 304.8,     // 50 mm clearance
                    PreferredAngleDeg = 30.0,          // 30 degree vertical offset
                    MinStraightSpoolFeet = 100.0 / 304.8, // 100 mm spool
                    PreferOver = true
                };

                // 3. Detect and Cluster Clashes
                ClashDetector detector = new ClashDetector(doc, config);
                List<ClashInfo> rawClashes = detector.DetectClashes(targetCurve);

                if (rawClashes.Count == 0)
                {
                    TaskDialog.Show("No Clashes", "No interfering elements were detected along the selected MEP curve.");
                    return Result.Succeeded;
                }

                ClashClusterEngine clusterEngine = new ClashClusterEngine(config);
                List<ClashCluster> clusters = clusterEngine.ClusterClashes(rawClashes);

                // 4. Build topological network node
                MepNetworkNode networkNode = MepNetworkNode.Build(doc, targetCurve);

                // 5. Execute transaction
                using (Transaction tx = new Transaction(doc, "Auto-Reroute MEP Clashes"))
                {
                    FailureHandlingOptions failOpt = tx.GetFailureHandlingOptions();
                    failOpt.SetFailuresPreprocessor(new WarningSwallower());
                    tx.SetFailureHandlingOptions(failOpt);

                    tx.Start();

                    MepRebuilder rebuilder = new MepRebuilder(doc, config);
                    rebuilder.ExecuteRebuild(networkNode, clusters);

                    tx.Commit();
                }

                TaskDialog.Show("Success", $"Successfully rerouted {clusters.Count} clash cluster(s) and reconnected MEP network.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message + "\n" + ex.StackTrace;
                return Result.Failed;
            }
        }
    }
}
