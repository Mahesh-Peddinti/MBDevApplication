using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using TheResolver.BusinessLogics;
using TheResolver.DTOs;
using TheResolver.Utilities;
using TheResolver.ViewModel;

namespace TheResolver.Services
{
    // TheResolver.View is a sibling namespace, so an unqualified "View" binds
    // to the namespace rather than to the Revit type. The alias must sit inside
    // the namespace declaration to win that lookup.
    using View = Autodesk.Revit.DB.View;

    /// <summary>
    /// Single ExternalEvent handler for everything the Resolver pane needs to
    /// do inside a valid Revit API context. Requests are queued rather than
    /// stored in one field, because ExternalEvent.Raise() coalesces and a
    /// second request used to overwrite the first before Revit ran it.
    /// </summary>
    public class ClashResolveTool : IExternalEventHandler
    {
        private const string PreviewViewPrefix = "Resolver Preview";

        private readonly ClashViewModel _clashViewModel;

        private readonly Queue<ModelRequest> _pendingRequests =
            new Queue<ModelRequest>();

        /// <summary>
        /// View the user was looking at before the first preview, so Cancel
        /// and Finish can put them back.
        /// </summary>
        private ElementId _viewBeforePreview;

        public ClashResolveTool(ClashViewModel viewModel)
        {
            _clashViewModel =
                viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        }

        /// <summary>
        /// Rows the user ticked, supplied by the view model before it raises
        /// <see cref="ModelRequest.ResolveSelected"/>.
        /// </summary>
        public List<ClashGridItemViewModel> SelectedClashes { get; set; }

        public string GetName()
        {
            return "The Resolver Model Service";
        }

        /// <summary>
        /// Adds a request to the queue. Consecutive duplicates are collapsed so
        /// dragging down the grid does not queue one preview rebuild per row.
        /// </summary>
        public void EnqueueRequest(ModelRequest request)
        {
            if (request == ModelRequest.None)
                return;

            lock (_pendingRequests)
            {
                if (_pendingRequests.Count > 0 &&
                    _pendingRequests.Last() == request)
                {
                    return;
                }

                _pendingRequests.Enqueue(request);
            }
        }

        public void Execute(UIApplication app)
        {
            while (true)
            {
                ModelRequest request;

                lock (_pendingRequests)
                {
                    if (_pendingRequests.Count == 0)
                        return;

                    request = _pendingRequests.Dequeue();
                }

                try
                {
                    Dispatch(app, request);
                }
                catch (Exception ex)
                {
                    Logger.Log($"{request} failed: {ex}");

                    _clashViewModel.StatusText =
                        $"{request} failed: {ex.Message}";

                    TaskDialog.Show(
                        "The Resolver",
                        $"{request} could not be completed.\n\n{ex.Message}");
                }
            }
        }

        private void Dispatch(UIApplication app, ModelRequest request)
        {
            switch (request)
            {
                case ModelRequest.LoadModels:
                    LoadModels(app);
                    break;

                case ModelRequest.RunClashDetection:
                    RunClashDetection(app);
                    break;

                case ModelRequest.PreviewRoute:
                    PreviewRoute(app);
                    break;

                case ModelRequest.DiscardPreview:
                    DiscardPreview(app);
                    break;

                case ModelRequest.ResolveSelected:
                    ResolveSelected(app);
                    break;

                case ModelRequest.ResolveAll:
                    ResolveAll(app);
                    break;

                case ModelRequest.FinishSession:
                    FinishSession(app);
                    break;
            }
        }

        private static readonly BuiltInCategory[] ObstructionCategories =
        {
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_CableTray,
            BuiltInCategory.OST_Conduit,
            BuiltInCategory.OST_PipeCurves
        };

        private void LoadModels(UIApplication app)
        {
            Document doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return;

            _clashViewModel.Initialize(doc);
        }

        private void RunClashDetection(UIApplication app)
        {
            UIDocument uidoc = app.ActiveUIDocument;

            Document doc = uidoc?.Document;

            if (doc == null)
            {
                _clashViewModel.StatusText = "No active document.";
                return;
            }

            // Keep the model list current, then honour the ticks in it.
            _clashViewModel.Initialize(doc);

            RouteSettingDTOs settings =
                _clashViewModel.BuildRouteSettings();

            List<CableTray> trays =
                new FilteredElementCollector(doc)
                    .OfClass(typeof(CableTray))
                    .WhereElementIsNotElementType()
                    .Cast<CableTray>()
                    .ToList();

            if (trays.Count == 0)
            {
                _clashViewModel.LoadClashes(new List<ClashInfoDTOs>());

                _clashViewModel.StatusText =
                    "No cable trays in the host model.";

                TaskDialog.Show(
                    "Clash Detection",
                    "No cable trays were found in the host model.");

                return;
            }

            List<Element> obstructions = CollectObstructions(doc);

            if (obstructions.Count == 0)
            {
                _clashViewModel.LoadClashes(new List<ClashInfoDTOs>());

                _clashViewModel.StatusText =
                    "No elements to check against. Tick at least one model.";

                TaskDialog.Show(
                    "Clash Detection",
                    "None of the selected models contain duct, pipe, "
                    + "conduit or cable tray elements to check against.");

                return;
            }

            List<ClashInfoDTOs> clashes =
                new GeometricUtilities()
                    .FindClashes(trays, obstructions, settings);

            _clashViewModel.LoadClashes(clashes);

            _clashViewModel.StatusText =
                $"{clashes.Count} clash(es) found in {trays.Count} tray(s).";

            TaskDialog.Show(
                "Clash Detection",
                clashes.Count > 0
                    ? $"Total : {clashes.Count} clash(es) found"
                    : "No clash found.");
        }

        /// <summary>
        /// Builds the list of elements the trays are checked against, honouring
        /// the "Clash Check Model" ticks. Cable trays in the host model are
        /// excluded: a tray bypassing another tray is not what this tool
        /// resolves, and every tray would otherwise clash with its neighbours.
        /// </summary>
        private List<Element> CollectObstructions(Document doc)
        {
            var obstructions = new List<Element>();

            if (_clashViewModel.IsHostModelSelected)
            {
                var hostCategories =
                    ObstructionCategories
                        .Where(c => c != BuiltInCategory.OST_CableTray)
                        .ToList();

                obstructions.AddRange(
                    new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType()
                        .WherePasses(
                            new ElementMulticategoryFilter(hostCategories))
                        .ToList());
            }

            var selectedLinkIds =
                _clashViewModel.GetSelectedLinkInstanceIds();

            var linkFilter =
                new ElementMulticategoryFilter(ObstructionCategories.ToList());

            List<RevitLinkInstance> linkInstances =
                new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .ToList();

            foreach (RevitLinkInstance linkInstance in linkInstances)
            {
                // A null set means the model list was never loaded, so fall
                // back to checking every link rather than finding nothing.
                if (selectedLinkIds != null &&
                    !selectedLinkIds.Contains(linkInstance.Id))
                {
                    continue;
                }

                Document linkDocument = linkInstance.GetLinkDocument();

                if (linkDocument == null)
                    continue;

                obstructions.AddRange(
                    new FilteredElementCollector(linkDocument)
                        .WhereElementIsNotElementType()
                        .WherePasses(linkFilter)
                        .ToList());
            }

            return obstructions;
        }

        private void ResolveSelected(UIApplication app)
        {
            List<ClashGridItemViewModel> rows =
                SelectedClashes?.ToList()
                ?? new List<ClashGridItemViewModel>();

            SelectedClashes = null;

            if (rows.Count == 0)
            {
                _clashViewModel.StatusText = "Nothing selected to resolve.";
                return;
            }

            RouteSettingDTOs settings =
                _clashViewModel.BuildRouteSettings();

            var resolver = new ClashResolver();

            int resolved = 0;
            int failed = 0;

            foreach (ClashGridItemViewModel row in rows)
            {
                bool success = false;

                try
                {
                    success =
                        resolver.ResolveSelectedClash(
                            app,
                            row.ClashInfo,
                            settings);
                }
                catch (Exception ex)
                {
                    Logger.Log(
                        $"{row.ClashId}: resolve threw - {ex.Message}");
                }

                _clashViewModel.ReportResolveResult(row, success);

                if (success)
                    resolved++;
                else
                    failed++;
            }

            _clashViewModel.StatusText =
                $"Resolved {resolved} of {rows.Count} clash(es).";

            // Report what actually happened - this used to always claim
            // success even when every route failed.
            TaskDialog.Show(
                "Clash Resolve",
                failed == 0
                    ? $"{resolved} clash(es) resolved successfully."
                    : $"{resolved} resolved, {failed} failed.\n\n"
                      + "See the log on your Desktop "
                      + "(CableTrayResolver.log) for details.");
        }

        private void ResolveAll(UIApplication app)
        {
            SelectedClashes = _clashViewModel.Clashes.ToList();

            ResolveSelected(app);
        }

        private void PreviewRoute(UIApplication app)
        {
            ClashInfoDTOs clash =
                _clashViewModel.SelectedClash?.ClashInfo;

            if (clash == null)
                return;

            if (clash.Intersection == null || clash.Tray == null)
            {
                _clashViewModel.StatusText =
                    "This clash has no intersection geometry to preview.";

                return;
            }

            CreatePreviewView(app, clash);
        }

        private void CreatePreviewView(
                        UIApplication app,
                        ClashInfoDTOs clash)
        {
            UIDocument uiDoc = app.ActiveUIDocument;

            Document doc = uiDoc?.Document;

            if (doc == null)
                return;

            ViewFamilyType viewType =
                new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(x =>
                        x.ViewFamily == ViewFamily.ThreeDimensional);

            if (viewType == null)
            {
                _clashViewModel.StatusText =
                    "No 3D view family type available for previews.";

                return;
            }

            // Revit refuses to delete the active view, so step off any preview
            // view before the cleanup transaction runs.
            LeavePreviewView(uiDoc);

            XYZ centre = clash.Intersection.ComputeCentroid();

            bool clashElementIsInHost =
                clash.ClashElement != null &&
                clash.ClashElement.Document != null &&
                clash.ClashElement.Document.Equals(doc);

            View3D view;

            using (Transaction tx =
                new Transaction(doc, "Resolver Preview"))
            {
                tx.Start();

                DeleteOldPreviewViews(doc);

                view = View3D.CreateIsometric(doc, viewType.Id);

                // The view MUST be named: cleanup finds previous previews by
                // this prefix, and an unnamed view was never matched.
                view.Name = BuildUniquePreviewName(doc);

                view.SetSectionBox(
                    new BoundingBoxXYZ
                    {
                        Min = centre - new XYZ(5, 5, 5),
                        Max = centre + new XYZ(5, 5, 5)
                    });

                ApplyPreviewGraphics(view, clash, clashElementIsInHost);

                tx.Commit();
            }

            uiDoc.ActiveView = view;

            _clashViewModel.StatusText =
                clashElementIsInHost
                    ? $"Previewing {_clashViewModel.SelectedClash?.ClashId}."
                    : $"Previewing {_clashViewModel.SelectedClash?.ClashId} "
                      + "(linked element shown in context).";
        }

        /// <summary>
        /// Colours the tray and, when it lives in the host document, the
        /// clashing element. Element ids from a linked document are not valid
        /// in the host document, so isolation and overrides are skipped for
        /// linked clashes and the section box alone frames the clash.
        /// </summary>
        private static void ApplyPreviewGraphics(
                                View3D view,
                                ClashInfoDTOs clash,
                                bool clashElementIsInHost)
        {
            Document doc = view.Document;

            var trayOverride =
                new OverrideGraphicSettings()
                    .SetProjectionLineColor(new Color(0, 170, 0))
                    .SetProjectionLineWeight(6);

            if (clash.Tray != null &&
                clash.Tray.Document != null &&
                clash.Tray.Document.Equals(doc))
            {
                view.SetElementOverrides(clash.Tray.Id, trayOverride);
            }

            if (!clashElementIsInHost)
                return;

            var clashOverride =
                new OverrideGraphicSettings()
                    .SetProjectionLineColor(new Color(200, 0, 0))
                    .SetProjectionLineWeight(6);

            view.SetElementOverrides(clash.ClashElement.Id, clashOverride);
        }

        /// <summary>
        /// Remembers the user's own view the first time a preview is opened and
        /// makes sure the active view is not a preview, which Revit would
        /// refuse to delete.
        /// </summary>
        private void LeavePreviewView(UIDocument uiDoc)
        {
            View active = uiDoc.ActiveView;

            if (active == null)
                return;

            if (!IsPreviewView(active))
            {
                _viewBeforePreview = active.Id;
                return;
            }

            View fallback = FindViewToReturnTo(uiDoc.Document, active.Id);

            if (fallback != null)
                uiDoc.ActiveView = fallback;
        }

        /// <summary>
        /// Picks the view to drop the user back into: the one they were on
        /// before the first preview when it still exists, otherwise any
        /// non-preview, non-template graphical view.
        /// </summary>
        private View FindViewToReturnTo(Document doc, ElementId currentViewId)
        {
            if (_viewBeforePreview != null &&
                _viewBeforePreview != currentViewId)
            {
                if (doc.GetElement(_viewBeforePreview) is View remembered &&
                    !remembered.IsTemplate &&
                    !IsPreviewView(remembered))
                {
                    return remembered;
                }
            }

            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(v =>
                    !v.IsTemplate &&
                    v.Id != currentViewId &&
                    !IsPreviewView(v) &&
                    v.CanBePrinted);
        }

        private void DiscardPreview(UIApplication app)
        {
            UIDocument uiDoc = app.ActiveUIDocument;

            Document doc = uiDoc?.Document;

            if (doc == null)
                return;

            LeavePreviewView(uiDoc);

            using (Transaction tx =
                new Transaction(doc, "Discard Resolver Preview"))
            {
                tx.Start();

                DeleteOldPreviewViews(doc);

                tx.Commit();
            }
        }

        /// <summary>
        /// Ends the session: drops any preview view, puts the user back where
        /// they started and closes the pane.
        /// </summary>
        private void FinishSession(UIApplication app)
        {
            DiscardPreview(app);

            _viewBeforePreview = null;

            _clashViewModel.StatusText = "Session finished.";

            try
            {
                DockablePane pane =
                    app.GetDockablePane(AddInApplication.ResolverPaneId);

                pane?.Hide();
            }
            catch (Exception ex)
            {
                // Hiding the pane is a convenience, never a reason to fail.
                Logger.Log($"Could not hide the Resolver pane: {ex.Message}");
            }
        }

        /// <summary>
        /// Deletes every view this tool created earlier. Must be called inside
        /// an open transaction, and never while one of them is active.
        /// </summary>
        private static void DeleteOldPreviewViews(Document doc)
        {
            List<ElementId> stale =
                new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D))
                    .Cast<View3D>()
                    .Where(v => !v.IsTemplate && IsPreviewView(v))
                    .Select(v => v.Id)
                    .ToList();

            if (stale.Count == 0)
                return;

            try
            {
                doc.Delete(stale);
            }
            catch (Exception ex)
            {
                Logger.Log($"Preview cleanup failed: {ex.Message}");
            }
        }

        private static bool IsPreviewView(View view)
        {
            return view != null &&
                   !string.IsNullOrEmpty(view.Name) &&
                   view.Name.StartsWith(
                       PreviewViewPrefix,
                       StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Revit throws when a view name is already taken, which happens as
        /// soon as an old preview survived cleanup.
        /// </summary>
        private static string BuildUniquePreviewName(Document doc)
        {
            var taken =
                new HashSet<string>(
                    new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .Select(v => v.Name)
                        .Where(n => !string.IsNullOrEmpty(n)),
                    StringComparer.OrdinalIgnoreCase);

            for (int i = 1; ; i++)
            {
                string candidate = $"{PreviewViewPrefix} {i}";

                if (!taken.Contains(candidate))
                    return candidate;
            }
        }
    }
}
