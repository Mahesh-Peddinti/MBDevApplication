using System;
using System.Reflection;
using Autodesk.Revit.UI;
using RevitMepAutomation.Commands;
using RevitMepAutomation.Revit;

namespace RevitMepAutomation
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                // 1. Register the Dockable Pane Provider
                DockablePaneRegistry.Register(application);

                // 2. Create Ribbon Tab & Panel
                string tabName = "DAR Automation";
                try { application.CreateRibbonTab(tabName); } catch { /* Tab may already exist */ }

                RibbonPanel panel = application.CreateRibbonPanel(tabName, "Clash Resolution");
                string assemblyPath = Assembly.GetExecutingAssembly().Location;

                // Button 1: Open Dockable Panel
                PushButtonData btnDockableData = new PushButtonData(
                    "BtnShowClashPanel",
                    "Clash Tool\nDockable",
                    assemblyPath,
                    typeof(ShowClashResolutionPaneCommand).FullName)
                {
                    ToolTip = "Opens the DAR Clash Resolution Tool Dockable Panel."
                };
                panel.AddItem(btnDockableData);

                // Button 2: Auto Reroute Command
                PushButtonData btnAutoData = new PushButtonData(
                    "BtnAutoReroute",
                    "Auto Reroute\nBatch",
                    assemblyPath,
                    typeof(AutoRerouteClashesCommand).FullName)
                {
                    ToolTip = "Automatically scans and reroutes all MEP clashes in active view."
                };
                panel.AddItem(btnAutoData);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("DAR Startup Error", ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
