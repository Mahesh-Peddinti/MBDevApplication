using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMepAutomation.Views;
using RevitMepAutomation.ViewModels;

namespace RevitMepAutomation.Revit
{
    public static class DockablePaneRegistry
    {
        public static readonly DockablePaneId PaneId = new DockablePaneId(new Guid("8A4D2E5B-01C3-4E82-9657-3B29815DFE9C"));
        private static ClashResolutionDockablePage? _pageInstance;

        public static void Register(UIControlledApplication application)
        {
            _pageInstance = new ClashResolutionDockablePage();
            application.RegisterDockablePane(PaneId, "DAR – Clash Resolution Tool", _pageInstance);
        }

        public static ClashResolutionDockablePage? PageInstance => _pageInstance;
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ShowClashResolutionPaneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                DockablePane pane = commandData.Application.GetDockablePane(DockablePaneRegistry.PaneId);
                if (pane != null)
                {
                    if (pane.IsShown())
                        pane.Hide();
                    else
                        pane.Show();
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
