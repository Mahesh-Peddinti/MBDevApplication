using Autodesk.Revit.UI;
using TheResolver.Button;
using TheResolver.UI;

namespace TheResolver
{
    public class AddInApplication : IExternalApplication
    {
        public static DockablePaneId ResolverPaneId =
            new DockablePaneId(
                new System.Guid("5A67757B-4D6D-47A0-B3A7-3917C8D22911"));

        /// <summary>
        /// The registered provider. Revit keeps the pane alive for the whole
        /// session, so commands need this to reach the view model; the instance
        /// used to be created and dropped inside RegisterDockablePanel.
        /// </summary>
        public static ResolverDockablePane PaneProvider { get; private set; }

        public Result OnStartup(UIControlledApplication application)
        {
            RibbonBuilder.Build(application);

            RegisterDockablePanel(application);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            PaneProvider = null;

            return Result.Succeeded;
        }

        private void RegisterDockablePanel(
            UIControlledApplication application)
        {
            PaneProvider = new ResolverDockablePane();

            application.RegisterDockablePane(
                ResolverPaneId,
                "The Resolver",
                PaneProvider);
        }
    }
}
