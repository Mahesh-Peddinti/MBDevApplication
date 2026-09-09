using Autodesk.Revit.UI;
using TheResolver.ViewModel;

namespace TheResolver.UI
{
    public class ResolverDockablePane : IDockablePaneProvider
    {
        public static ResolverDockablePane Instance { get; private set; }

        public ResolverView View { get; private set; }

        public ClashViewModel ViewModel => View?.ClashVm ?? View?.ViewModel;

        public ResolverDockablePane()
        {
            Instance = this;
            // Initialize View immediately in the constructor
            View = new ResolverView();
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            // Ensure View is initialized if constructor wasn't called via standard path
            if (View == null)
            {
                View = new ResolverView();
            }

            data.FrameworkElement = View;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Right
            };
        }
    }
}