using Autodesk.Revit.UI;
using System.Reflection;

namespace TheResolver.Button
{
    public static class RibbonButtonFactory
    {
        public static PushButtonData Create(RibbonButtonInfo info)
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
