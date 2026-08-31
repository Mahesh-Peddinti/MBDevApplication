using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TheResolver.DTOs;
using TheResolver.Services;
using TheResolver.Utilities;

namespace TheResolver.ViewModel
{
    /// <summary>
    /// View model behind the Resolver dockable pane.
    /// All Revit API work is marshalled onto a single ExternalEvent
    /// (<see cref="ClashResolveTool"/>) because the pane runs outside any
    /// valid API context.
    /// </summary>
    public class ClashViewModel : INotifyPropertyChanged
    {
        /// <summary>Revit internal length unit is feet.</summary>
        private const double MmToFeet = 1.0 / 304.8;

        private readonly IModelSelectionService _modelSelectionService;
        private readonly ClashResolveTool _clashResolveTool;
        private readonly ExternalEvent _modelEvent;

        private readonly RelayCommand _runClashDetectionCommand;
        private readonly RelayCommand _acceptCommand;
        private readonly RelayCommand _cancelCommand;
        private readonly RelayCommand _finishCommand;

        private bool _suspendPreview;
        private bool _isUpdatingSelection;
        private bool _isAllSelected;

        private ClashGridItemViewModel _selectedClash;
        private BitmapImage _previewImage;
        private string _statusText = "Ready. Run clash detection to begin.";

        private double _bendRadiusMm = 100.0;
        private double _bendAngleDegrees = 30.0;
        private double _offsetSpanMm = 150.0;
        private double _topClearanceMm = 100.0;
        private RouteDirection _preferredDirection = RouteDirection.Auto;
        private PreviewRouteData _previewRouteData;

        public ClashViewModel()
            : this(new ModelSelectionService())
        {
        }

        public ClashViewModel(IModelSelectionService modelSelectionService)
        {
            _modelSelectionService =
                modelSelectionService ?? new ModelSelectionService();

            _clashResolveTool = new ClashResolveTool(this);

            _modelEvent = ExternalEvent.Create(_clashResolveTool);

            _runClashDetectionCommand =
                new RelayCommand(RunClashDetection);

            _acceptCommand =
                new RelayCommand(RunClashResolution, HasSelectedClashes);

            _cancelCommand =
                new RelayCommand(CancelPreview, CanCancel);

            _finishCommand =
                new RelayCommand(Finish);
        }

        public ObservableCollection<ClashModelItem> AvailableModels { get; }
            = new ObservableCollection<ClashModelItem>();

        public ObservableCollection<ClashGridItemViewModel> Clashes { get; }
            = new ObservableCollection<ClashGridItemViewModel>();

        public ICommand RunClashDetectionCommand => _runClashDetectionCommand;

        public ICommand AcceptCommand => _acceptCommand;

        public ICommand CancelCommand => _cancelCommand;

        public ICommand FinishCommand => _finishCommand;

        // ------------------------------------------------------------
        // MODEL SELECTION
        // ------------------------------------------------------------

        /// <summary>
        /// Rebuilds the host + link model list. Must be called from a valid
        /// Revit API context (an external command or the ExternalEvent
        /// handler), which is why the constructor cannot do it: the pane is
        /// created during OnStartup, before any document exists.
        /// Previously ticked models stay ticked across a refresh.
        /// </summary>
        public void Initialize(Document doc)
        {
            if (doc == null)
                return;

            var previouslyUnselected =
                new HashSet<string>(
                    AvailableModels
                        .Where(m => !m.IsSelected)
                        .Select(m => m.Name ?? string.Empty));

            foreach (var stale in AvailableModels)
                stale.SelectionChangedAction = null;

            AvailableModels.Clear();

            foreach (var model in _modelSelectionService.GetAvailableModels(doc))
            {
                model.IsSelected =
                    !previouslyUnselected.Contains(model.Name ?? string.Empty);

                model.SelectionChangedAction = OnModelSelectionChanged;

                AvailableModels.Add(model);
            }

            OnPropertyChanged(nameof(ModelSelectionSummary));
        }

        /// <summary>
        /// Text shown on the closed combo box, since a multi-select list has
        /// no single SelectedItem to display.
        /// </summary>
        public string ModelSelectionSummary
        {
            get
            {
                if (AvailableModels.Count == 0)
                    return "No models loaded";

                int selected = AvailableModels.Count(m => m.IsSelected);

                return $"{selected} of {AvailableModels.Count} model(s) selected";
            }
        }

        private void OnModelSelectionChanged()
        {
            OnPropertyChanged(nameof(ModelSelectionSummary));
        }

        /// <summary>
        /// Link instances the user wants checked. Null means "no link filter
        /// was ever loaded", which the handler treats as "check everything"
        /// so detection still works if Initialize never ran.
        /// </summary>
        public HashSet<ElementId> GetSelectedLinkInstanceIds()
        {
            if (AvailableModels.Count == 0)
                return null;

            return new HashSet<ElementId>(
                AvailableModels
                    .Where(m => m.IsSelected
                                && !m.IsHost
                                && m.LinkInstanceId != null)
                    .Select(m => m.LinkInstanceId));
        }

        /// <summary>
        /// True when the host document itself should be included.
        /// </summary>
        public bool IsHostModelSelected =>
            AvailableModels.Count == 0
            || AvailableModels.Any(m => m.IsHost && m.IsSelected);

        // ------------------------------------------------------------
        // ROUTE SETTINGS (entered in millimetres / degrees)
        // ------------------------------------------------------------

        public double BendRadiusMm
        {
            get => _bendRadiusMm;
            set
            {
                if (SetField(ref _bendRadiusMm, value))
                    OnRouteParameterChanged();
            }
        }

        public double BendAngleDegrees
        {
            get => _bendAngleDegrees;
            set
            {
                if (SetField(ref _bendAngleDegrees, value))
                    OnRouteParameterChanged();
            }
        }

        public double OffsetSpanMm
        {
            get => _offsetSpanMm;
            set
            {
                if (SetField(ref _offsetSpanMm, value))
                    OnRouteParameterChanged();
            }
        }

        public double TopClearanceMm
        {
            get => _topClearanceMm;
            set
            {
                if (SetField(ref _topClearanceMm, value))
                    OnRouteParameterChanged();
            }
        }

        public RouteDirection PreferredDirection
        {
            get => _preferredDirection;
            set
            {
                if (!SetField(ref _preferredDirection, value))
                    return;

                OnPropertyChanged(nameof(IsDirectionAuto));
                OnPropertyChanged(nameof(IsDirectionUp));
                OnPropertyChanged(nameof(IsDirectionDown));
                OnRouteParameterChanged();
            }
        }

        public bool IsDirectionAuto
        {
            get => _preferredDirection == RouteDirection.Auto;
            set { if (value) PreferredDirection = RouteDirection.Auto; }
        }

        public bool IsDirectionUp
        {
            get => _preferredDirection == RouteDirection.Up;
            set { if (value) PreferredDirection = RouteDirection.Up; }
        }

        public bool IsDirectionDown
        {
            get => _preferredDirection == RouteDirection.Down;
            set { if (value) PreferredDirection = RouteDirection.Down; }
        }

        /// <summary>
        /// Converts the user's millimetre input into Revit internal units and
        /// clamps it into the range the routing maths requires, so bad input
        /// cannot start a doomed transaction.
        /// </summary>
        public RouteSettingDTOs BuildRouteSettings()
        {
            return new RouteSettingDTOs
            {
                BendAngle = Clamp(_bendAngleDegrees, 1.0, 89.0),

                BendRadius = Math.Max(_bendRadiusMm, 1.0) * MmToFeet,

                MinimumClearance = Math.Max(_topClearanceMm, 0.0) * MmToFeet,

                MinimumSideOffset = Math.Max(_offsetSpanMm, 0.0) * MmToFeet,

                // Unitless multiplier - must not be scaled to feet.
                BendSafetyfactor = 1.05,

                PreferredDirection = _preferredDirection
            };
        }

        private static double Clamp(double value, double min, double max)
        {
            if (double.IsNaN(value))
                return min;

            return Math.Min(Math.Max(value, min), max);
        }

        // ------------------------------------------------------------
        // GRID STATE
        // ------------------------------------------------------------

        public bool IsAllSelected
        {
            get => _isAllSelected;
            set
            {
                if (_isAllSelected == value)
                    return;

                _isAllSelected = value;

                _isUpdatingSelection = true;

                try
                {
                    foreach (var clash in Clashes)
                        clash.IsSelected = value;
                }
                finally
                {
                    _isUpdatingSelection = false;
                }

                OnPropertyChanged();

                _acceptCommand.RaiseCanExecuteChanged();
            }
        }

        public ClashGridItemViewModel SelectedClash
        {
            get => _selectedClash;
            set
            {
                if (_selectedClash == value)
                    return;

                _selectedClash = value;

                OnPropertyChanged();

                UpdateStatusForSelection();

                _cancelCommand.RaiseCanExecuteChanged();

                // Automatically preview the selected clash.
                RaisePreview();
            }
        }

        public BitmapImage PreviewImage
        {
            get => _previewImage;
            set
            {
                if (ReferenceEquals(_previewImage, value))
                    return;

                _previewImage = value;

                OnPropertyChanged();

                        _cancelCommand.RaiseCanExecuteChanged();
            }
        }

        public PreviewRouteData PreviewRouteData
        {
            get => _previewRouteData;
            set
            {
                _previewRouteData = value;
                OnPropertyChanged();
            }
        }

        public void UpdatePreview(PreviewRouteData data)
        {
            PreviewRouteData = data;
        }

        public string StatusText
        {
            get => _statusText;
            set => SetField(ref _statusText, value);
        }

        public bool HasClashes => Clashes.Count > 0;

        // ------------------------------------------------------------
        // COMMANDS
        // ------------------------------------------------------------

        private bool HasSelectedClashes()
        {
            return Clashes.Any(c => c.IsSelected);
        }

        private bool CanCancel()
        {
            return _selectedClash != null || _previewImage != null;
        }

        private void RunClashDetection()
        {
            StatusText = "Running clash detection...";

            Raise(ModelRequest.RunClashDetection);
        }

        private void RunClashResolution()
        {
            var selectedClashes =
                Clashes.Where(c => c.IsSelected).ToList();

            if (selectedClashes.Count == 0)
            {
                StatusText = "Select at least one clash to resolve.";
                return;
            }

            _clashResolveTool.SelectedClashes = selectedClashes;

            StatusText =
                $"Resolving {selectedClashes.Count} clash(es)...";

            Raise(ModelRequest.ResolveSelected);
        }

        private void CancelPreview()
        {
            SelectedClash = null;

            PreviewImage = null;

            PreviewRouteData = null;

            StatusText = "Preview discarded.";

            Raise(ModelRequest.DiscardPreview);
        }

        private void Finish()
        {
            Raise(ModelRequest.FinishSession);
        }

        /// <summary>
        /// Asks the model to refresh the host/link list.
        /// </summary>
        public void RequestModelRefresh()
        {
            Raise(ModelRequest.LoadModels);
        }

        private void RaisePreview()
        {
            if (_suspendPreview || _selectedClash == null)
                return;

            Raise(ModelRequest.PreviewRoute);
        }

        /// <summary>
        /// Recomputes only the 2D schematic (cheap, no Revit 3D view churn)
        /// when the user tweaks a route parameter or the detour direction.
        /// </summary>
        private void RaiseBypassPreview()
        {
            if (_selectedClash == null)
                return;

            Raise(ModelRequest.UpdateBypassPreview);
        }

        private void OnRouteParameterChanged()
        {
            RaiseBypassPreview();
        }

        private void Raise(ModelRequest request)
        {
            _clashResolveTool.EnqueueRequest(request);

            _modelEvent.Raise();
        }

        // ------------------------------------------------------------
        // GRID POPULATION
        // ------------------------------------------------------------

        /// <summary>
        /// Replaces the grid contents. Called from the ExternalEvent handler,
        /// which runs on Revit's main UI thread, so no dispatcher marshalling
        /// is required.
        /// </summary>
        public void LoadClashes(List<ClashInfoDTOs> clashes)
        {
            _suspendPreview = true;

            try
            {
                foreach (var stale in Clashes)
                    stale.SelectionChangedAction = null;

                Clashes.Clear();

                _isAllSelected = false;
                OnPropertyChanged(nameof(IsAllSelected));

                int clashNumber = 1;

                foreach (var clash in clashes ?? new List<ClashInfoDTOs>())
                {
                    Clashes.Add(
                        new ClashGridItemViewModel
                        {
                            ClashId = $"Clash - {clashNumber++}",

                            Element1 = clash.Tray?.Category?.Name,

                            Element1Id = clash.Tray?.Id?.ToString(),

                            Element2 = clash.ClashElement?.Category?.Name,

                            Element2Id = clash.ClashElement?.Id?.ToString(),

                            Element2ProjectName =
                                clash.ClashElement?.Document?.Title
                                ?? "Current Model",

                            ClashInfo = clash,

                            SelectionChangedAction = UpdateSelectAllState
                        });
                }

                SelectedClash = Clashes.FirstOrDefault();
            }
            finally
            {
                _suspendPreview = false;
            }

            OnPropertyChanged(nameof(HasClashes));

            _acceptCommand.RaiseCanExecuteChanged();
            _cancelCommand.RaiseCanExecuteChanged();

            // The first row was selected while previews were suspended,
            // so trigger its preview now.
            RaisePreview();
        }

        /// <summary>
        /// Keeps the header "select all" checkbox in sync with the rows.
        /// </summary>
        public void UpdateSelectAllState()
        {
            _acceptCommand.RaiseCanExecuteChanged();

            if (_isUpdatingSelection)
                return;

            bool allSelected =
                Clashes.Count > 0 &&
                Clashes.All(c => c.IsSelected);

            if (_isAllSelected == allSelected)
                return;

            _isAllSelected = allSelected;

            OnPropertyChanged(nameof(IsAllSelected));
        }

        /// <summary>
        /// Records the outcome of a resolve attempt on the matching row.
        /// </summary>
        public void ReportResolveResult(
            ClashGridItemViewModel row,
            bool succeeded)
        {
            if (row == null)
                return;

            if (succeeded)
                row.IsResolved = true;
            else
                row.ResolveFailed = true;
        }

        private void UpdateStatusForSelection()
        {
            if (_selectedClash == null)
            {
                StatusText =
                    Clashes.Count == 0
                        ? "No clashes loaded."
                        : "No clash selected.";

                return;
            }

            StatusText =
                $"{_selectedClash.ClashId}: {_selectedClash.Status}";
        }

        // ------------------------------------------------------------
        // INotifyPropertyChanged
        // ------------------------------------------------------------

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(
            [CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(propertyName));
        }

        private bool SetField<T>(
            ref T field,
            T value,
            [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;

            OnPropertyChanged(propertyName);

            return true;
        }
    }
}
