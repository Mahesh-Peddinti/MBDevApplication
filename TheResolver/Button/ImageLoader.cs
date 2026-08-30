using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace TheResolver.Button
{
    public static class ImageLoader
    {
        /// <summary>
        /// Loads an embedded PNG from Resources.
        /// Returns null when the resource is missing so a bad image name
        /// degrades to a button without an icon instead of failing OnStartup.
        /// </summary>
        public static BitmapImage Load(string imageName)
        {
            if (string.IsNullOrWhiteSpace(imageName))
                return null;

            Assembly assembly = Assembly.GetExecutingAssembly();

            string resourceName = $"TheResolver.Resources.{imageName}";

            using (Stream stream =
                assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    return null;

                BitmapImage image = new BitmapImage();

                image.BeginInit();

                // OnLoad reads the whole stream during EndInit,
                // so the stream is safe to dispose immediately after.
                image.CacheOption = BitmapCacheOption.OnLoad;

                image.StreamSource = stream;

                image.EndInit();

                image.Freeze();

                return image;
            }
        }
    }
}
