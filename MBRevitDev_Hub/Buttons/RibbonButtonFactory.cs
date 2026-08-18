using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace MBRevitDev_Hub.Buttons
{
    public class RibbonButtonFactory
    {
        public static PushButtonData Create(
            RibbonButtonInfo info)
        {
            string assemblyPath =
                Assembly.GetExecutingAssembly().Location;

            PushButtonData button =
                new PushButtonData(
                    info.Name,
                    info.Text,
                    assemblyPath,
                    info.CommandClass);

            button.ToolTip = info.Tooltip;

            button.LargeImage = ImageLoader.Load(info.LargeImage);

            button.Image = ImageLoader.Load(info.SmallImage);

            return button;
        }
    }
}
