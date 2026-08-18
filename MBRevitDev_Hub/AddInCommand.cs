#region Namespaces
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using MBRevitDev_Hub.UI.ViewModels;
using UIFramework;

#endregion

namespace MBRevitDev_Hub
{
    [Transaction(TransactionMode.Manual)]
    public class AddInCommand : IExternalCommand
    {
        public Result Execute(
          ExternalCommandData commandData,
          ref string message,
          ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc.Document;
            var selection = uidoc.Selection;          


            //UI implimentation
            var vm = new MainViewModel();

            //Show the main window of the application
            var mainWindow = new MainWindow { DataContext = vm };

            mainWindow.Show();

            return Result.Succeeded;            
        }
    }
}
