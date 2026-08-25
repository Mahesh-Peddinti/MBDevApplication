using System;
using System.Windows.Controls;
using Autodesk.Revit.UI;
using RevitMepAutomation.ViewModels;

namespace RevitMepAutomation.Views
{
    /// <summary>
    /// Interaction logic for ClashResolutionDockablePage.xaml.
    /// Implements IDockablePaneProvider for seamless embedding in Revit's UI dockable pane system.
    /// </summary>
    public partial class ClashResolutionDockablePage : Page, IDockablePaneProvider
    {
        public MainViewModel ViewModel { get; }

        public ClashResolutionDockablePage(MainViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel ?? new MainViewModel();
            DataContext = ViewModel;
        }

        public ClashResolutionDockablePage() : this(new MainViewModel())
        {
        }

        /// <summary>
        /// Autodesk Revit IDockablePaneProvider implementation.
        /// Configures initial docking state and minimum dimensions.
        /// </summary>
        public void SetupDockablePane(DockablePaneProviderData data)
        {
            if (data == null) return;

            data.FrameworkElement = this;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Right,
                TabBehind = DockablePanes.BuiltInDockablePanes.ProjectBrowser
            };
        }
    }
}
