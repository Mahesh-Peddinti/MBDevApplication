using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using TheResolver.DTOs;
using TheResolver.Services;
using TheResolver.Utilities;
using TheResolver.ViewModel;

namespace TheResolver.Commands
{
    public class ClashResolveTool : IExternalEventHandler
    {
        private readonly ClashViewModel _viewModel;
        private readonly Queue<ModelRequest> _pendingRequests = new Queue<ModelRequest>();
        private LocalSpatialIndex _spatialIndex;

        public List<ClashGridItemViewModel> SelectedClashes { get; set; } = new List<ClashGridItemViewModel>();

        public ClashResolveTool(ClashViewModel viewModel)
        {
            _viewModel = viewModel;
        }

        public string GetName() => "ClashResolveTool";

        public void EnqueueRequest(ModelRequest request)
        {
            _pendingRequests.Enqueue(request);
        }



        public void Execute(UIApplication app)
        {
            while (_pendingRequests.Count > 0)
            {
                var request = _pendingRequests.Dequeue();
                switch (request)
                {
                    case ModelRequest.LoadModels:
                        _viewModel.Initialize(app.ActiveUIDocument?.Document);
                        break;
                    case ModelRequest.LoadCategories:
                        LoadCategories(app.ActiveUIDocument?.Document);
                        break;
                    case ModelRequest.RunClashDetection:
                        RunClashDetection(app);
                        break;
                    case ModelRequest.PreviewRoute:
                    case ModelRequest.UpdateBypassPreview:
                        UpdatePreview(app);
                        if (_viewModel.SelectedClash?.ClashInfo != null)
                        {
                            FocusOnClash(app, _viewModel.SelectedClash.ClashInfo);
                        }
                        break;
                    case ModelRequest.ResolveSelected:
                        ResolveSelected(app);
                        break;
                    case ModelRequest.RefreshFeasibility:
                        _viewModel.RefreshFeasibility();
                        break;
                    case ModelRequest.DiscardPreview:
                        break;
                    case ModelRequest.FinishSession:
                        break;


                }
            }
        }

        private void LoadCategories(Document doc)
        {
            if (doc == null) return;

            var hostCats = new HashSet<BuiltInCategory>();
            var linkCats = new HashSet<BuiltInCategory>();

            // 1. Harvest categories from host
            foreach (var model in _viewModel.HostModels)
            {
                if (model.Document != null)
                {
                    model.Categories.Clear();
                    var cats = _viewModel.ModelSelectionService.GetAvailableCategories(model.Document);
                    foreach (var c in cats)
                    {
                        model.Categories.Add(c);
                        hostCats.Add(c.Category);
                    }
                }
            }

            // 2. Harvest categories from linked files
            foreach (var model in _viewModel.LinkModels)
            {
                if (model.Document != null)
                {
                    model.Categories.Clear();
                    var cats = _viewModel.ModelSelectionService.GetAvailableCategories(model.Document);
                    foreach (var c in cats)
                    {
                        model.Categories.Add(c);
                        linkCats.Add(c.Category);
                    }
                }
            }

            // 3. Populate ViewModel collections for UI ComboBox binding
            _viewModel.PopulateCategoryCombos(hostCats, linkCats);
        }

        private void RunClashDetection(UIApplication app)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            Document doc = uidoc.Document;

            var selectedCategories = _viewModel.GetSelectedCategories();
            var selectedLinkIds = _viewModel.GetSelectedLinkInstanceIds();

            List<Element> hostObstructions = new List<Element>();
            List<(RevitLinkInstance Instance, Element Element)> linkedObstructions =
                new List<(RevitLinkInstance Instance, Element Element)>();

            // Harvest Host Elements
            if (_viewModel.IsHostModelSelected)
            {
                foreach (var cat in selectedCategories)
                {
                    var elems = new FilteredElementCollector(doc)
                        .OfCategory(cat)
                        .WhereElementIsNotElementType()
                        .ToElements();
                    hostObstructions.AddRange(elems);
                }
            }

            // Harvest Linked Elements with Parent Instance Transforms
            var linkInstances = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>();

            foreach (var link in linkInstances)
            {
                if (selectedLinkIds != null && !selectedLinkIds.Contains(link.Id))
                    continue;

                Document linkDoc = link.GetLinkDocument();
                if (linkDoc == null) continue;

                foreach (var cat in selectedCategories)
                {
                    var elems = new FilteredElementCollector(linkDoc)
                        .OfCategory(cat)
                        .WhereElementIsNotElementType()
                        .ToElements();

                    foreach (var elem in elems)
                    {
                        linkedObstructions.Add((link, elem));
                    }
                }
            }

            // Ingest into Spatial Hash
            _spatialIndex = new LocalSpatialIndex(cellSizeMm: 1000.0);
            _spatialIndex.IngestElements(doc, hostObstructions, linkedObstructions);

            GeometricUtilities geomUtils = new GeometricUtilities { SpatialIndex = _spatialIndex };

            List<CableTray> trays = new FilteredElementCollector(doc)
                .OfClass(typeof(CableTray))
                .WhereElementIsNotElementType()
                .Cast<CableTray>()
                .ToList();

            List<Element> allObstructions = new List<Element>(hostObstructions);
            allObstructions.AddRange(linkedObstructions.Select(x => x.Element));

            RouteSettingDTOs settings = _viewModel.BuildRouteSettings();
            List<ClashInfoDTOs> clashes = geomUtils.FindClashes(trays, allObstructions, settings);

            _viewModel.LoadClashes(clashes);
        }

        private void ResolveSelected(UIApplication app)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            Document doc = uidoc.Document;
            RouteSettingDTOs settings = _viewModel.BuildRouteSettings();

            GeometricUtilities geomUtils = new GeometricUtilities { SpatialIndex = _spatialIndex };

            using (Transaction tx = new Transaction(doc, "Resolve Cable Tray Clashes"))
            {
                tx.Start();

                foreach (var item in SelectedClashes)
                {
                    if (item.IsResolved) continue;

                    if (geomUtils.TryBuildBypassRoutingPoints(item.ClashInfo, settings, out List<XYZ> routePoints))
                    {
                        var created = CreateNewBypassRoute.CreateNewRoute(
                            doc,
                            item.ClashInfo.Tray,
                            routePoints[0],
                            routePoints[routePoints.Count - 1],
                            routePoints,
                            settings);

                        if (created != null && created.Count > 0)
                        {
                            _viewModel.ReportResolveResult(item, true);
                        }
                        else
                        {
                            _viewModel.ReportResolveResult(item, false);
                        }
                    }
                    else
                    {
                        _viewModel.ReportResolveResult(item, false);
                    }
                }

                tx.Commit();
            }
        }

        private void UpdatePreview(UIApplication app)
        {
            if (_viewModel.SelectedClash?.ClashInfo == null) return;
            var settings = _viewModel.BuildRouteSettings();
            var preview = BuildPreviewData(_viewModel.SelectedClash.ClashInfo, settings);
            _viewModel.UpdatePreview(preview);
        }

        public PreviewRouteData BuildPreviewData(ClashInfoDTOs clash, RouteSettingDTOs settings)
        {
            var data = new PreviewRouteData();
            if (clash?.Tray == null || clash.ClashElement == null)
            {
                data.Message = "No valid clash data.";
                return data;
            }

            if (!(clash.Tray.Location is LocationCurve lc) || !(lc.Curve is Line trayLine))
            {
                data.Message = "Selected tray is not straight.";
                return data;
            }

            XYZ start = trayLine.GetEndPoint(0);
            XYZ end = trayLine.GetEndPoint(1);
            XYZ trayDir = (end - start).Normalize();
            double trayLength = start.DistanceTo(end);

            data.TrayLengthMm = trayLength * 304.8;

            var utils = new GeometricUtilities { SpatialIndex = _spatialIndex };

            List<XYZ> points;
            bool routeOk = utils.TryBuildBypassRoutingPoints(clash, settings, out points);

            if (routeOk && points != null && points.Count >= 2)
            {
                data.RoutePoints = new List<PreviewPoint>();
                foreach (var pt in points)
                {
                    double station = (pt - start).DotProduct(trayDir) * 304.8;
                    double elevation = (pt.Z - start.Z) * 304.8;
                    data.RoutePoints.Add(new PreviewPoint { Station = station, Elevation = elevation, Point = pt });
                }

                double maxDeltaZ = points.Max(p => Math.Abs(p.Z - start.Z));
                data.RiseMm = maxDeltaZ * 304.8;
                data.IsDetourUp = points.Any(p => p.Z > start.Z + 1e-4);
                data.ClearanceMm = settings.MinimumClearance * 304.8;
            }
            else
            {
                data.Message = "Cannot resolve route with current clearance parameters.";
            }

            // Harvest Extended Environmental Obstacles for UI Preview Canvas
            data.SecondaryObstacles.Clear();
            double clashStation = 0.0;
            try
            {
                XYZ cPt = clash.Intersection?.ComputeCentroid();
                if (cPt != null) clashStation = (cPt - start).DotProduct(trayDir);
            }
            catch { }

            if (_spatialIndex != null)
            {
                double extSpan = 4000.0 / 304.8; // 4m envelope
                XYZ roiCenter = start + trayDir * clashStation;
                BoundingBoxXYZ extBox = new BoundingBoxXYZ
                {
                    Min = new XYZ(roiCenter.X - extSpan, roiCenter.Y - extSpan, roiCenter.Z - (2500.0 / 304.8)),
                    Max = new XYZ(roiCenter.X + extSpan, roiCenter.Y + extSpan, roiCenter.Z + (2500.0 / 304.8))
                };

                List<ObstacleBounds> nearby = _spatialIndex.QueryRoi(extBox);
                foreach (var obs in nearby)
                {
                    if (obs.Id == clash.Tray.UniqueId) continue;

                    XYZ[] corners = new XYZ[]
                    {
                        new XYZ(obs.Box.Min.X, obs.Box.Min.Y, obs.Box.Min.Z),
                        new XYZ(obs.Box.Max.X, obs.Box.Min.Y, obs.Box.Min.Z),
                        new XYZ(obs.Box.Min.X, obs.Box.Max.Y, obs.Box.Min.Z),
                        new XYZ(obs.Box.Max.X, obs.Box.Max.Y, obs.Box.Min.Z),
                        new XYZ(obs.Box.Min.X, obs.Box.Min.Y, obs.Box.Max.Z),
                        new XYZ(obs.Box.Max.X, obs.Box.Min.Y, obs.Box.Max.Z),
                        new XYZ(obs.Box.Min.X, obs.Box.Max.Y, obs.Box.Max.Z),
                        new XYZ(obs.Box.Max.X, obs.Box.Max.Y, obs.Box.Max.Z)
                    };

                    double sMin = corners.Min(c => (c - start).DotProduct(trayDir)) * 304.8;
                    double sMax = corners.Max(c => (c - start).DotProduct(trayDir)) * 304.8;
                    double eMin = corners.Min(c => c.Z - start.Z) * 304.8;
                    double eMax = corners.Max(c => c.Z - start.Z) * 304.8;

                    if (obs.Id == clash.ClashElement.UniqueId)
                    {
                        data.ClashStationMinMm = sMin;
                        data.ClashStationMaxMm = sMax;
                        data.ClashMinElevationMm = eMin;
                        data.ClashMaxElevationMm = eMax;
                    }
                    else
                    {
                        data.SecondaryObstacles.Add(new PreviewObstacleBox
                        {
                            StationMinMm = sMin,
                            StationMaxMm = sMax,
                            ElevationMinMm = eMin,
                            ElevationMaxMm = eMax
                        });
                    }
                }
            }

            return data;
        }
        private void FocusOnClash(UIApplication app, ClashInfoDTOs clash)
        {
            if (clash?.Tray == null) return;

            UIDocument uidoc = app.ActiveUIDocument;
            Document doc = uidoc.Document;
            Autodesk.Revit.DB.View activeView = doc.ActiveView;

            // 1. Highlight the clashing tray in Revit's selection set
            var selectionIds = new List<ElementId> { clash.Tray.Id };
            if (!clash.ClashElement.Document.IsLinked)
            {
                selectionIds.Add(clash.ClashElement.Id);
            }
            uidoc.Selection.SetElementIds(selectionIds);

            // 2. Derive the 3D target bounding box for the clash region
            BoundingBoxXYZ trayBox = clash.Tray.get_BoundingBox(null);
            if (trayBox == null) return;

            XYZ center;
            try
            {
                center = clash.Intersection?.ComputeCentroid();
            }
            catch { center = null; }

            if (center == null)
            {
                center = (trayBox.Min + trayBox.Max) * 0.5;
            }

            // Expand bounding box by 1.5 meters around the clash for context
            double offset = 1500.0 / 304.8;
            XYZ min = new XYZ(center.X - offset, center.Y - offset, center.Z - offset);
            XYZ max = new XYZ(center.X + offset, center.Y + offset, center.Z + offset);

            // 3. Zoom and center the active UIView to the clash bounding area
            UIView activeUIView = uidoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == activeView.Id);
            if (activeUIView != null)
            {
                activeUIView.ZoomAndCenterRectangle(min, max);
            }

            // 4. If in a 3D View, update the Section Box inside a Transaction
            if (activeView is View3D view3d && view3d.IsSectionBoxActive)
            {
                using (Transaction t = new Transaction(doc, "Focus Clash Section Box"))
                {
                    t.Start();
                    BoundingBoxXYZ sectionBox = new BoundingBoxXYZ { Min = min, Max = max };
                    view3d.SetSectionBox(sectionBox);
                    t.Commit();
                }
            }
        }
        public static bool IsRouteFeasible(ClashInfoDTOs clash, RouteSettingDTOs settings)
        {
            if (clash?.Tray == null || clash.ClashElement == null || settings == null)
                return false;

            var utils = new GeometricUtilities();
            return utils.TryBuildBypassRoutingPoints(clash, settings, out var points);
        }
    }
}