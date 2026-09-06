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
                        // 1. Reset ViewModel collections and preview data
                        _viewModel.ResetSession();

                        // 2. Tear down spatial index to free memory
                        _spatialIndex = null;

                        // 3. Clear Revit viewport selection
                        try
                        {
                            app.ActiveUIDocument?.Selection.SetElementIds(new List<ElementId>());
                        }
                        catch { }

                        // 4. Hide the dockable pane
                        try
                        {
                            DockablePaneId paneId = new DockablePaneId(new Guid("D53C1A1E-8B7C-4C9C-A898-1C71279DF4A1")); // Match your registered GUID
                            DockablePane pane = app.GetDockablePane(paneId);
                            if (pane != null && pane.IsShown())
                            {
                                pane.Hide();
                            }
                        }
                        catch { }
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

        private void ExecuteBatchResolution(UIApplication app)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            Document doc = uidoc.Document;

            var targetClashes = _viewModel.Clashes.Where(c => c.IsSelected && !c.IsResolved).ToList();
            if (targetClashes.Count == 0)
            {
                return;
            }

            // Build RouteSettingDTOs directly from the ViewModel properties
            var routeSettings = new RouteSettingDTOs
            {
                BendRadius = _viewModel.BendRadiusMm / 304.8,
                BendAngle = _viewModel.BendAngleDegrees,
                MinimumClearance = _viewModel.TopClearanceMm / 304.8,
                MinimumSideOffset = _viewModel.OffsetSpanMm / 304.8,
                PreferredDirection = _viewModel.PreferredDirection
            };

            var utils = new GeometricUtilities { SpatialIndex = _spatialIndex };
            int successCount = 0;
            int failureCount = 0;

            foreach (var clashItem in targetClashes)
            {
                var clash = clashItem.ClashInfo;
                if (clash?.Tray == null || clash.ClashElement == null)
                    continue;

                // Use clashItem.ClashInfo to read IDs safely regardless of wrapper property names
                string trayId = clash.Tray.Id.ToString();
                string clashElemId = clash.ClashElement.Id.ToString();
                string clashCat = clash.ClashElement.Category?.Name ?? "Element";
                string docTitle = clash.ClashElement.Document?.Title ?? "Linked";

                string clashHeader = $"[{clashItem.ClashId} | Tray: {trayId} vs {clashCat}: {clashElemId} ({docTitle})]";

                // 1. Solve geometry
                List<XYZ> routingPoints;
                bool routeOk = utils.TryBuildBypassRoutingPoints(clash, routeSettings, out routingPoints);

                if (!routeOk || routingPoints == null || routingPoints.Count < 4)
                {
                    failureCount++;
                    clashItem.IsFeasible = false;
                    clashItem.ResolutionLogMessage = "Geometric solver failed: Insufficient clearance in pocket or span outside tray bounds.";
                    continue;
                }

                // 2. Commit transaction using your tool's existing route creation logic
                using (Transaction trans = new Transaction(doc, $"Resolve Clash {trayId}"))
                {
                    trans.Start();
                    try
                    {
                        // Call your existing bypass creation routine in this tool
                        // (e.g. this.CreateBypassTrays or your specific helper)
                        bool created = CreateBypassElements(doc, clash.Tray, routingPoints, routeSettings);

                        if (created)
                        {
                            trans.Commit();
                            successCount++;
                            clashItem.IsResolved = true; // Automatically sets Status to "Resolved"
                            clashItem.ResolvedRiseMm = Math.Abs(routingPoints[1].Z - routingPoints[0].Z) * 304.8;
                            clashItem.ResolvedAngleDeg = routeSettings.BendAngle;
                            clashItem.ResolutionLogMessage = $"Resolved successfully at +{clashItem.ResolvedRiseMm:F1}mm displacement.";
                        }
                        else
                        {
                            trans.RollBack();
                            failureCount++;
                            clashItem.ResolutionLogMessage = "Revit API rejected fitting or tray creation.";
                        }
                    }
                    catch (Exception ex)
                    {
                        trans.RollBack();
                        failureCount++;
                        clashItem.ResolutionLogMessage = $"Exception: {ex.Message}";
                    }
                }
            }

            // Refresh UI status properties
            _viewModel.NotifyStatusChanged();
        }

        private bool CreateBypassElements(Document doc, CableTray originalTray, List<XYZ> points, RouteSettingDTOs settings)
        {
            if (points == null || points.Count < 2) return false;

            using (Transaction t = new Transaction(doc, "Resolve Clash Bypass"))
            {
                t.Start();
                try
                {
                    ElementId typeId = originalTray.GetTypeId();
                    ElementId levelId = originalTray.LevelId;

                    double width = originalTray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.AsDouble() ?? (300.0 / 304.8);
                    double height = originalTray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.AsDouble() ?? (100.0 / 304.8);

                    if (!(originalTray.Location is LocationCurve lc) || !(lc.Curve is Line origLine))
                    {
                        t.RollBack();
                        return false;
                    }

                    XYZ pStart = origLine.GetEndPoint(0);
                    XYZ pEnd = origLine.GetEndPoint(1);

                    // Construct strict ordered coordinate sequence
                    var allPts = new List<XYZ> { pStart };
                    allPts.AddRange(points);
                    allPts.Add(pEnd);

                    // Filter out duplicate or collinear micro-vertices
                    var cleanPts = new List<XYZ> { allPts[0] };
                    for (int i = 1; i < allPts.Count; i++)
                    {
                        if (allPts[i].DistanceTo(cleanPts.Last()) > 40.0 / 304.8)
                        {
                            cleanPts.Add(allPts[i]);
                        }
                    }

                    if (cleanPts.Count < 3)
                    {
                        t.RollBack();
                        return false;
                    }

                    // 1. Create Cable Tray Segments
                    var trays = new List<CableTray>();
                    for (int i = 0; i < cleanPts.Count - 1; i++)
                    {
                        CableTray seg = CableTray.Create(doc, typeId, cleanPts[i], cleanPts[i + 1], levelId);
                        seg.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.Set(width);
                        seg.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.Set(height);
                        trays.Add(seg);
                    }

                    // 2. Commit elements to Revit internal BSP tree
                    doc.Regenerate();

                    // 3. Connect sequential segments with elbow fittings
                    for (int i = 0; i < trays.Count - 1; i++)
                    {
                        ConnectTraysWithFitting(doc, trays[i], trays[i + 1]);
                    }

                    // 4. Remove original tray
                    doc.Delete(originalTray.Id);

                    t.Commit();
                    return true;
                }
                catch
                {
                    t.RollBack();
                    return false;
                }
            }
        }

        private static void ConnectTraysWithFitting(Document doc, CableTray t1, CableTray t2)
        {
            Connector c1 = null;
            Connector c2 = null;
            double minDist = double.MaxValue;

            foreach (Connector con1 in t1.ConnectorManager.Connectors)
            {
                foreach (Connector con2 in t2.ConnectorManager.Connectors)
                {
                    double dist = con1.Origin.DistanceTo(con2.Origin);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        c1 = con1;
                        c2 = con2;
                    }
                }
            }

            if (c1 != null && c2 != null && minDist < 2.0)
            {
                try
                {
                    doc.Create.NewElbowFitting(c1, c2);
                }
                catch
                {
                    // Direct physical snap if fitting geometry template is unconstrained
                    try
                    {
                        if (!c1.IsConnectedTo(c2))
                            c1.ConnectTo(c2);
                    }
                    catch { }
                }
            }
        }
    }
}