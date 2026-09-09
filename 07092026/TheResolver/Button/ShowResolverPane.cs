using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TheResolver.ViewModel;

namespace TheResolver.Button
{
    [Transaction(TransactionMode.Manual)]
    public class ShowResolverPane : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            DockablePane pane =
                commandData.Application
                           .GetDockablePane(
                               AddInApplication.ResolverPaneId);

            if (pane == null)
            {
                message = "The Resolver pane is not registered.";
                return Result.Failed;
            }

            pane.Show();

            // The pane is built during OnStartup, before any document exists,
            // so the model list can only be filled from a command like this.
            ClashViewModel viewModel =
                AddInApplication.PaneProvider?.ViewModel;

            if (viewModel != null)
            {
                Document doc = commandData.Application
                                          .ActiveUIDocument
                                          ?.Document;

                if (doc != null)
                    viewModel.Initialize(doc);
            }

            return Result.Succeeded;
        }
    }
}
