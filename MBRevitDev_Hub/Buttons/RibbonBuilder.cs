using Autodesk.Revit.UI;

namespace MBRevitDev_Hub.Buttons
{
    public class RibbonBuilder
    {
        public static void Build(UIControlledApplication app)
        {
            const string tab = "MBDev-Tools";

            try
            {
                app.CreateRibbonTab(tab);
            }
            catch
            {

            }

            RibbonPanel panel =
                app.CreateRibbonPanel(tab, "MBDevTools");

            panel.AddItem(
                RibbonButtonFactory.Create(
                    new RibbonButtonInfo
                    {
                        Name = "MBDevInfo",

                        Text = "MBDevInfo",

                        CommandClass =
                            "MBRevitDev_Hub.MBDevInfoCommand",

                        Tooltip =
                            "Creates Bus Duct",

                        LargeImage = "ToolBox_32.png",

                        SmallImage = "ToolBox_16.png"
                    }));

            panel.AddItem(
                RibbonButtonFactory.Create(
                    new RibbonButtonInfo
                    {
                        Name = "ClashTool",

                        Text = "ClashTool",

                        CommandClass =
                            "MBRevitDev_Hub.CableTrayClashDetectionCommand",

                        Tooltip =
                            "Resolve Cable Tray Clashes",

                        LargeImage = "ToolBox_32.png",

                        SmallImage = "ToolBox_16.png"
                    }));
            panel.AddItem(
                RibbonButtonFactory.Create(
                    new RibbonButtonInfo
                    {
                        Name = "CableTray",

                        Text = "Cable Tray",

                        CommandClass =
                            "MBRevitDev_Hub.AddInCommand",

                        Tooltip =
                            "Creates Cable Tray ",

                        LargeImage = "ToolBox_32.png",

                        SmallImage = "ToolBox_16.png"
                    }));
        }
    }
}
