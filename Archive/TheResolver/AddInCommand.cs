using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TheResolver.View;
using TheResolver.ViewModel;


namespace TheResolver
{    
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AddInCommand : IExternalCommand
    {        
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc.Document;
            var selection = uidoc.Selection;         

            //UI implimentation
            var vm = new MainViewModel(doc);

            //Show the main window of the application
            var mainWindow = new MainWindow { DataContext = vm };
            mainWindow.Show();
            return Result.Succeeded;
        }
    }
}