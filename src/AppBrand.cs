using System;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClearDesk
{
    public static class AppBrand
    {
        public const string Version = "0.6.0";
        // Keep the embedded icon alive for the tray's lifetime.
        static readonly System.Drawing.Icon icon = LoadIcon();
        static readonly ImageSource image = LoadImage();
        public static System.Drawing.Icon TrayIcon { get { return icon; } }
        public static ImageSource WindowIcon { get { return image; } }
        static System.Drawing.Icon LoadIcon()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ClearDesk.AppIcon"))
            using (var original = new System.Drawing.Icon(stream, 32, 32)) return (System.Drawing.Icon)original.Clone();
        }
        static ImageSource LoadImage()
        {
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze(); return bitmap;
        }
    }
}
