using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using System.Windows.Data;
using TheResolver.BusinessLogics;
using TheResolver.DTOs;
using TheResolver.Utilities;
using TheResolver.ViewModel;

namespace TheResolver.Services
{
    public class ClashResolveTool : IExternalEventHandler
    {
        // ------------------------------------------------------------
        // UNIT CONVERSION
        // Revit internal length unit = feet
        // ------------------------------------------------------------
        private const double MM = 1.0 / 304.8;
        public ModelRequest Request { get; set; }

        private readonly ClashViewModel _clashViewModel;

        public List<ClashGridItemViewModel> SelectedClashes { get; set; }

        public ClashResolveTool(ClashViewModel viewModel)
        {
            _clashViewModel = viewModel;
        }


        public void Execute(UIApplication app)
        {

            switch (Request)
            {
                case ModelRequest.RunClashDetection:
                    RunClashDetection(app);
                    break;

                case ModelRequest.PreviewRoute:
                    PreviewRoute(app);
                    break;

                case ModelRequest.ResolveSelected:
                    ResolveSelected(app);
                    break;

                case ModelRequest.ResolveAll:
                    ResolveAll(app);
                    break;
            }

            Request = ModelRequest.None;
        }

        public string GetName()
        {
            return "The Resolver Model Service";
        }

        private void RunClashDetection(UIApplication app)
        {
            //getting Document
            UIDocument uidoc = app.ActiveUIDocument;
            Document doc = uidoc.Document;

            //settings for routing
            RouteSettingDTOs routeSettingDTOs = new RouteSettingDTOs();

            routeSettingDTOs = new RouteSettingDTOs()
            {
                BendAngle = 30,

                BendRadius = 100 * MM,

                MinimumClearance = 100 * MM,

                MinimumSideOffset = 150 * MM,

                BendSafetyfactor = 1.05 * MM
            };

            //Pre-Load the Elmenets from Host Model
            List<Element> hostElements = new List<Element>();
            List<CableTray> trays = new List<CableTray>();

            trays = new FilteredElementCollector(doc)
                                        .OfClass(typeof(CableTray))
                                        .WhereElementIsNotElementType()
                                        .Cast<CableTray>()
                                        .ToList();


            List<Element> linkElements = new List<Element>();

            List<BuiltInCategory> multiCategories =
                                        new List<BuiltInCategory>
                                        {
                                            BuiltInCategory.OST_DuctCurves,
                                            BuiltInCategory.OST_CableTray,
                                            BuiltInCategory.OST_Conduit,
                                            BuiltInCategory.OST_PipeCurves
                                        };

            ElementMulticategoryFilter multiCategoryFilter =
                new ElementMulticategoryFilter(multiCategories);

            List<RevitLinkInstance> revitLinkInstances =
                                        new FilteredElementCollector(doc)
                                            .OfClass(typeof(RevitLinkInstance))
                                            .Cast<RevitLinkInstance>()
                                            .ToList();

            //Working with Cable trays and conduite from same project
            List<Element> multiCategoriesElements =
                                        new FilteredElementCollector(doc)
                                            .WhereElementIsNotElementType()
                                            .WherePasses(multiCategoryFilter)
                                            .ToList();
            


            //Working with Linked Elements
            foreach (RevitLinkInstance linkInstance in revitLinkInstances)
            {
                Document linkDocument =
                    linkInstance.GetLinkDocument();

                if (linkDocument == null)
                    continue;

                List<Element> elementsInLink =
                    new FilteredElementCollector(linkDocument)
                        .WhereElementIsNotElementType()
                        .WherePasses(multiCategoryFilter)
                        .ToList();

                linkElements.AddRange(elementsInLink);
            }           

            // Main clash detection logic
            List<ClashInfoDTOs> clashInfoDTOs = new List<ClashInfoDTOs>();
            GeometricUtilities geometricUtilities = new GeometricUtilities();

            clashInfoDTOs = geometricUtilities.FindClashes(trays, linkElements, routeSettingDTOs);

            System.Windows.Application.Current.Dispatcher.Invoke(() =>{


                _clashViewModel.LoadClashes(clashInfoDTOs);

                CollectionViewSource.GetDefaultView(_clashViewModel.Clashes).Refresh();
            });
            if ((clashInfoDTOs.Count>=1))
            {
                TaskDialog.Show("Clash Detection", $"Total : {clashInfoDTOs.Count} Clashe(s) Found");
            }
            else
            {
                TaskDialog.Show("Clash Detection", $"No Clash Found!!!");
            }
            
        }


        private void PreviewRoute(UIApplication app)
        {
            if (_clashViewModel.SelectedClash == null)
                return;

            ClashInfoDTOs clash =
                _clashViewModel.SelectedClash.ClashInfo;

            CreatePreviewView(app, clash);
        }

        private void ResolveSelected(UIApplication app)
        {
            ClashResolver resolver = new ClashResolver();

            foreach (ClashGridItemViewModel selectedClash in SelectedClashes)
            {
                resolver.ResolveSelectedClash(app, selectedClash.ClashInfo);
            }         
            

            TaskDialog.Show("Clash Resolve", "Selected Clash resolved Successfully! ");
        }

        private void ResolveAll(UIApplication app)
        {
            // Batch resolve logic
        }
        
        private void DeleteOldPreviewViews(Document doc)
        {
            var oldViews =
                new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D))
                    .Cast<View3D>()
                    .Where(v => v.Name.StartsWith("Resolver Preview"))
                    .ToList();

            foreach (var view in oldViews)
            {
                doc.Delete(view.Id);
            }
        }


        private void CreatePreviewView(
                        UIApplication app,
                        ClashInfoDTOs clash)
        {
            UIDocument uiDoc = app.ActiveUIDocument;
            Document doc = uiDoc.Document;

            using (Transaction tx =
                new Transaction(doc, "Resolver Preview"))
            {
                tx.Start();

                ViewFamilyType viewType =
                    new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .First(x =>
                        x.ViewFamily ==
                        ViewFamily.ThreeDimensional);

                DeleteOldPreviewViews(doc);

                View3D view =
                    View3D.CreateIsometric(
                        doc,
                        viewType.Id);                

                Solid solid = clash.Intersection;

                XYZ point = solid.ComputeCentroid();

                BoundingBoxXYZ sectionBox =
                    new BoundingBoxXYZ
                    {
                        Min = point - new XYZ(5, 5, 5),
                        Max = point + new XYZ(5, 5, 5)
                    };

                view.SetSectionBox(sectionBox);

                view.IsolateElementsTemporary(
                [
                    clash.Tray.Id,
                    clash.ClashElement.Id
                ]);

                //Override Elements graphics
                OverrideGraphicSettings trayOgs =
                        new OverrideGraphicSettings();

                trayOgs.SetProjectionLineColor(
                    new Color(0, 255, 0));

                view.SetElementOverrides(
                    clash.Tray.Id,
                    trayOgs);


                OverrideGraphicSettings conduitOgs =
                        new OverrideGraphicSettings();

                conduitOgs.SetProjectionLineColor(
                    new Color(255, 0, 0));

                view.SetElementOverrides(
                    clash.ClashElement.Id,
                    conduitOgs);              

                tx.Commit();

                uiDoc.ActiveView = view;
            }
        }
    }
}