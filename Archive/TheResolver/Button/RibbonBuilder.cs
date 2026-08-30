using Autodesk.Revit.UI;

namespace TheResolver.Button
{
    public class RibbonBuilder
    {
        public static void Build(UIControlledApplication app)
        {
            const string tab = "The Resolver";

            try
            {
                app.CreateRibbonTab(tab);
            }
            catch(Exception ex) 
            {
                TaskDialog.Show("Error Message",ex.Message);
            }
            //Panel details
            RibbonPanel panel =
                app.CreateRibbonPanel(tab, "Clash Tools");
            //Add ButtonInfo
            //Repeate the process to create new Buttons
            panel.AddItem(
                RibbonButtonFactory.Create(
                    new RibbonButtonInfo
                    {
                        Name = "ClashDetector",

                        Text = "Clash\nDetector",

                        CommandClass = "TheResolver.AddInCommand",

                        Tooltip = "This Tool Detect Clash(s) and Resolve by Provides Best Routing solutions with in the Constrains",

                        LargeImage = "ClashTool-16.png",

                        SmallImage = "ClashTool-32.png"
                    }));
            panel.AddItem(
                RibbonButtonFactory.Create(
                    new RibbonButtonInfo
                    {
                        Name = "ClashResolver",

                        Text = "Clash\nResolver",

                        CommandClass = "TheResolver.ModelService",

                        Tooltip = "This Tool Detect Clash(s) and Resolve by Provides Best Routing solutions with in the Constrains",

                        LargeImage = "ResolveTool-16.png",

                        SmallImage = "ResolveTool-32.png"
                    }));

            panel.AddItem(
                RibbonButtonFactory.Create(
                    new RibbonButtonInfo
                    {
                        Name = "GroupClashResolver",

                        Text = "GroupClash\nResolver",

                        CommandClass = "TheResolver.ModelGroupServices",

                        Tooltip = "This Tool Detect Clash(s) and Resolve by Provides Best Routing solutions with in the Constrains",

                        LargeImage = "ResolveTool-16.png",

                        SmallImage = "ResolveTool-32.png"
                    }));
            panel.AddItem(
                RibbonButtonFactory.Create(
                    new RibbonButtonInfo
                    {
                        Name = "FittingResolver",

                        Text = "Fitting\nResolver",

                        CommandClass = "TheResolver.ReRouteCableTrayFittingCommand",

                        Tooltip = "resolves cable tray fitting routing issues",

                        LargeImage = "ResolveTool-16.png",

                        SmallImage = "ResolveTool-32.png"
                    }));
            panel.AddItem(
                RibbonButtonFactory.Create(
                    new RibbonButtonInfo
                    {
                        Name = "ClashResolution Tool",

                        Text = "Clash\nResolution App",

                        CommandClass = "TheResolver.Button.ShowResolverPane",

                        Tooltip = "resolves cable tray fitting routing issues",

                        LargeImage = "ClashApplication_16.png",

                        SmallImage = "ClashApplication_32.png"
                    }));
        }
    }
}
