using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

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

            pane.Show();

            return Result.Succeeded;
        }
    }
}