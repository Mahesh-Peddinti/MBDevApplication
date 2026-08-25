using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using RevitMepAutomation.Common;
using RevitMepAutomation.Models;
using RevitMepAutomation.Themes;

namespace RevitMepAutomation.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private ObservableCollection<ClashItemViewModel> _clashes = new ObservableCollection<ClashItemViewModel>();
        private ClashItemViewModel? _selectedClash;
        private bool _isDarkTheme = true;
        private bool _isInfoOpen = false;
        private string _searchFilter = string.Empty;
        private string _statusMessage = "Ready to resolve clashes";

        public ObservableCollection<ClashItemViewModel> Clashes
        {
            get => _clashes;
            set => SetProperty(ref _clashes, value);
        }

        public ClashItemViewModel? SelectedClash
        {
            get => _selectedClash;
            set
            {
                if (SetProperty(ref _selectedClash, value))
                {
                    OnPropertyChanged(nameof(HasSelectedClash));
                    OnPropertyChanged(nameof(SelectedClashName));
                    
                    // Trigger Zoom-to-Clash Revit navigation hook
                    if (_selectedClash != null)
                    {
                        RequestZoomToClash?.Invoke(_selectedClash);
                    }
                }
            }
        }

        public bool HasSelectedClash => SelectedClash != null;
        public string SelectedClashName => SelectedClash?.Name ?? "No Selection";

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set
            {
                if (SetProperty(ref _isDarkTheme, value))
                {
                    ThemeManager.SetTheme(value ? AppThemeMode.Dark : AppThemeMode.Light);
                }
            }
        }

        public bool IsInfoOpen
        {
            get => _isInfoOpen;
            set => SetProperty(ref _isInfoOpen, value);
        }

        public string SearchFilter
        {
            get => _searchFilter;
            set
            {
                if (SetProperty(ref _searchFilter, value))
                {
                    // Filter or search logic
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // Statistics
        public int TotalCount => Clashes.Count;
        public int ResolvedCount => Clashes.Count(c => c.Status == ClashStatus.Resolved);
        public int UnresolvedCount => Clashes.Count(c => c.Status != ClashStatus.Resolved);
        public double ProgressPercentage => TotalCount == 0 ? 0 : (double)ResolvedCount / TotalCount * 100.0;
        public string ProgressSummary => $"{ResolvedCount} of {TotalCount} Resolved ({ProgressPercentage:0}%)";

        // Commands
        public ICommand AcceptCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand FinishCommand { get; }
        public ICommand ToggleThemeCommand { get; }
        public ICommand ToggleInfoCommand { get; }
        public ICommand QuickAnglePresetCommand { get; }
        public ICommand NextClashCommand { get; }
        public ICommand PreviousClashCommand { get; }

        // Events for Revit API Integration
        public event Action<ClashItemViewModel>? RequestZoomToClash;
        public event Action<MainViewModel>? RequestCommitAllResolutions;

        public MainViewModel()
        {
            AcceptCommand = new RelayCommand(ExecuteAccept, () => HasSelectedClash);
            CancelCommand = new RelayCommand(ExecuteCancel, () => HasSelectedClash);
            FinishCommand = new RelayCommand(ExecuteFinish);
            ToggleThemeCommand = new RelayCommand(() => IsDarkTheme = !IsDarkTheme);
            ToggleInfoCommand = new RelayCommand(() => IsInfoOpen = !IsInfoOpen);
            QuickAnglePresetCommand = new RelayCommand<double?>(SetQuickAngle);
            NextClashCommand = new RelayCommand(SelectNextClash);
            PreviousClashCommand = new RelayCommand(SelectPrevClash);

            LoadSampleClashes();
        }

        public void LoadSampleClashes()
        {
            Clashes.Clear();

            var c1 = new ClashItemModel
            {
                Id = 1,
                Name = "Clash-1",
                Status = ClashStatus.Resolved,
                BendRadiusMm = 300,
                BendAngleDeg = 45,
                OffsetSpanMm = 500,
                TopClearanceMm = 50,
                Direction = RoutingDirection.Up,
                ElementCategory = "Cable Tray 300x100",
                ObstacleCategory = "Rectangular HVAC Duct 600x300",
                ElementId = 482910,
                ObstacleId = 593021,
                ClashX = 14.5, ClashY = 8.2, ClashZ = 3.4
            };

            var c2 = new ClashItemModel
            {
                Id = 2,
                Name = "Clash-2",
                Status = ClashStatus.NotResolved,
                BendRadiusMm = 300,
                BendAngleDeg = 30,
                OffsetSpanMm = 600,
                TopClearanceMm = 50,
                Direction = RoutingDirection.Down,
                ElementCategory = "Cable Tray 400x100",
                ObstacleCategory = "Structural Steel Beam W14x30",
                ElementId = 482915,
                ObstacleId = 601244,
                ClashX = 22.1, ClashY = 8.2, ClashZ = 3.4
            };

            var c3 = new ClashItemModel
            {
                Id = 3,
                Name = "Clash-3",
                Status = ClashStatus.NotResolved,
                BendRadiusMm = 300,
                BendAngleDeg = 45,
                OffsetSpanMm = 500,
                TopClearanceMm = 75,
                Direction = RoutingDirection.Up,
                ElementCategory = "Cable Tray 300x100",
                ObstacleCategory = "Fire Protection Pipe 150mm Ø",
                ElementId = 482920,
                ObstacleId = 711090,
                ClashX = 35.8, ClashY = 8.2, ClashZ = 3.4
            };

            var vm1 = new ClashItemViewModel(c1);
            var vm2 = new ClashItemViewModel(c2);
            var vm3 = new ClashItemViewModel(c3);

            Clashes.Add(vm1);
            Clashes.Add(vm2);
            Clashes.Add(vm3);

            // Select first clash by default
            SelectedClash = vm1;
            UpdateStats();
        }

        private void ExecuteAccept()
        {
            if (SelectedClash == null) return;

            SelectedClash.Accept();
            StatusMessage = $"✓ {SelectedClash.Name} accepted as Resolved.";
            UpdateStats();

            // Optionally navigate to next unresolved clash
            var nextUnresolved = Clashes.FirstOrDefault(c => c.Status != ClashStatus.Resolved);
            if (nextUnresolved != null)
            {
                SelectedClash = nextUnresolved;
            }
        }

        private void ExecuteCancel()
        {
            if (SelectedClash == null) return;

            SelectedClash.Cancel();
            StatusMessage = $"↺ Changes to {SelectedClash.Name} cancelled.";
            UpdateStats();
        }

        private void ExecuteFinish()
        {
            StatusMessage = $"🚀 Finishing: {ResolvedCount} of {TotalCount} clashes applied to Revit model.";
            RequestCommitAllResolutions?.Invoke(this);
        }

        private void SetQuickAngle(double? angle)
        {
            if (SelectedClash != null && angle.HasValue)
            {
                SelectedClash.BendAngleDeg = angle.Value;
            }
        }

        private void SelectNextClash()
        {
            if (SelectedClash == null && Clashes.Any())
            {
                SelectedClash = Clashes.First();
                return;
            }
            int idx = Clashes.IndexOf(SelectedClash!);
            if (idx >= 0 && idx < Clashes.Count - 1)
            {
                SelectedClash = Clashes[idx + 1];
            }
        }

        private void SelectPrevClash()
        {
            if (SelectedClash == null && Clashes.Any())
            {
                SelectedClash = Clashes.First();
                return;
            }
            int idx = Clashes.IndexOf(SelectedClash!);
            if (idx > 0)
            {
                SelectedClash = Clashes[idx - 1];
            }
        }

        public void UpdateStats()
        {
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(ResolvedCount));
            OnPropertyChanged(nameof(UnresolvedCount));
            OnPropertyChanged(nameof(ProgressPercentage));
            OnPropertyChanged(nameof(ProgressSummary));
        }
    }
}
