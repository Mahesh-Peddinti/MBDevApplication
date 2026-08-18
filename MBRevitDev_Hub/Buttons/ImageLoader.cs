using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace MBRevitDev_Hub.Buttons
{
    public class ImageLoader
    {
        public static BitmapImage Load(string imageName)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();

            Stream stream = assembly.GetManifestResourceStream($"MBRevitDev_Hub.Resources.{imageName}");

            BitmapImage image = new BitmapImage();

            image.BeginInit();
            image.StreamSource = stream;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();

            return image;
        }

    }
}
