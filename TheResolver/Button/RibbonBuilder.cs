using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;

namespace TheResolver.Button
{
    public static class RibbonBuilder
    {
        private const string TabName = "The Resolver";
        private const string PanelName = "Clash Tools";

        public static void Build(UIControlledApplication app)
        {
            try
            {
                // Throws when the tab already exists, which is normal on a
                // reload. Showing a TaskDialog here interrupted Revit's
                // start-up for something entirely harmless.
                app.CreateRibbonTab(TabName);
            }
            catch (Exception ex)
            {
                Logger.Log($"Ribbon tab already present: {ex.Message}");
            }

            RibbonPanel panel;

            try
            {
                panel = app.CreateRibbonPanel(TabName, PanelName);
            }
            catch (Exception ex)
            {
                Logger.Log($"Could not create the ribbon panel: {ex.Message}");
                return;
            }

            foreach (RibbonButtonInfo info in GetButtons())
            {
                try
                {
                    panel.AddItem(RibbonButtonFactory.Create(info));
                }
                catch (Exception ex)
                {
                    Logger.Log($"Button '{info.Name}' failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Only commands that actually implement IExternalCommand are listed.
        /// Buttons for ModelService (an IExternalEventHandler),
        /// ModelGroupServices and ReRouteCableTrayFittingCommand (neither type
        /// exists) were removed: Revit accepted them at start-up and then threw
        /// as soon as the user clicked one.
        /// </summary>
        private static IEnumerable<RibbonButtonInfo> GetButtons()
        {
            yield return new RibbonButtonInfo
            {
                Name = "ClashDetector",

                Text = "Clash\nDetector",

                CommandClass = "TheResolver.AddInCommand",

                Tooltip =
                    "Detects clashes and proposes routing solutions "
                    + "within the given constraints.",

                // 32px belongs on LargeImage and 16px on Image; these were
                // swapped, so the ribbon scaled both the wrong way.
                LargeImage = "ClashTool-32.png",

                SmallImage = "ClashTool-16.png"
            };

            yield return new RibbonButtonInfo
            {
                Name = "ClashResolutionTool",

                Text = "Clash\nResolution App",

                CommandClass = "TheResolver.Button.ShowResolverPane",

                Tooltip =
                    "Opens The Resolver pane to detect and reroute "
                    + "cable tray clashes.",

                LargeImage = "ClashApplication_32.png",

                SmallImage = "ClashApplication_16.png"
            };
        }
    }
}
