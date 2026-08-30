using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace TheResolver.Button
{
    public class ImageLoader
    {
        public static BitmapImage Load(string imageName)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string resourceName = $"TheResolver.Resources.{imageName}";
            Stream stream = assembly.GetManifestResourceStream(resourceName); 
            BitmapImage image = new BitmapImage();
            //stream.Position = 0;
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            //image.UriSource = new Uri(@"C:\Users\mpeddinti\source\repos\TheResolver\Resources\ClashTool-16.png", UriKind.RelativeOrAbsolute);
            //"C:\Users\mpeddinti\source\repos\TheResolver\Resources\ClashTool-32.png"
            image.EndInit();
            image.Freeze();

            return image;
        }        
    }
}
