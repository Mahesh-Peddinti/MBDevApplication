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

        public Result OnStartup(UIControlledApplication application)
        {
            RibbonBuilder.Build(application);

            RegisterDockablePanel(application);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private void RegisterDockablePanel(
            UIControlledApplication application)
        {
            ResolverDockablePane provider =
                new ResolverDockablePane();

            application.RegisterDockablePane(
                ResolverPaneId,
                "The Resolver",
                provider);
        }
    }
}