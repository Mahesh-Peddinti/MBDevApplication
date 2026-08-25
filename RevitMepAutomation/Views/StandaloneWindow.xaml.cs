using System.Windows;
using RevitMepAutomation.ViewModels;

namespace RevitMepAutomation.Views
{
    public partial class StandaloneWindow : Window
    {
        public MainViewModel ViewModel { get; }

        public StandaloneWindow()
        {
            InitializeComponent();
            ViewModel = new MainViewModel();
            DockableHost.DataContext = ViewModel;

            // Wire up Revit navigation events for demonstration
            ViewModel.RequestZoomToClash += OnRequestZoomToClash;
            ViewModel.RequestCommitAllResolutions += OnRequestCommitAllResolutions;
        }

        private void OnRequestZoomToClash(ClashItemViewModel clash)
        {
            // Demonstration / logging for zoom hook
            System.Diagnostics.Debug.WriteLine($"[Revit Hook] Zooming viewport to Clash: {clash.Name}, ElementId: {clash.ElementId}, ObstacleId: {clash.ObstacleId}");
        }

        private void OnRequestCommitAllResolutions(MainViewModel vm)
        {
            MessageBox.Show(
                $"Successfully resolved {vm.ResolvedCount} of {vm.TotalCount} clashes!\n\nParameters committed:\n" +
                string.Join("\n", System.Linq.Enumerable.Select(vm.Clashes, c => $"• {c.Name}: Status={c.Status}, Radius={c.BendRadiusMm}mm, Angle={c.BendAngleDeg}°, Span={c.OffsetSpanMm}mm, Clearance={c.TopClearanceMm}mm, Dir={c.Direction}")),
                "Clash Resolution Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
