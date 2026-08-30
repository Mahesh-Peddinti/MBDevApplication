using Autodesk.Revit.UI;

namespace TheResolver.UI
{
    public class ResolverDockablePane : IDockablePaneProvider
    {
        private ResolverView _view;

        public ResolverDockablePane()
        {
            _view = new ResolverView();
        }

        public void SetupDockablePane(
            DockablePaneProviderData data)
        {
            data.FrameworkElement = _view;

            data.InitialState =
                new DockablePaneState
                {
                    DockPosition = DockPosition.Right
                };
        }
       
    }
}