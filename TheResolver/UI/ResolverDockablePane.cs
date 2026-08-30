using Autodesk.Revit.UI;
using TheResolver.ViewModel;

namespace TheResolver.UI
{
    public class ResolverDockablePane : IDockablePaneProvider
    {
        private readonly ResolverView _view;

        public ResolverDockablePane()
        {
            _view = new ResolverView();
        }

        /// <summary>
        /// View model behind the pane, so a command can refresh the model list
        /// once a document is open.
        /// </summary>
        public ClashViewModel ViewModel => _view.ViewModel;

        public void SetupDockablePane(DockablePaneProviderData data)
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
