using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AutomationStudioWpf;

public static class WindowIconHelper
{
    private static readonly Lazy<ImageSource> AppIconValue = new(CreateAppIcon);
    private static readonly Lazy<Icon> TrayIconValue = new(CreateTrayIconInternal);

    public static ImageSource AppIcon => AppIconValue.Value;

    public static Icon TrayIcon => (Icon)TrayIconValue.Value.Clone();

    public static string IconPath => Path.Combine(AppContext.BaseDirectory, "Resources", "AutomationStudio.ico");

    private static ImageSource CreateAppIcon()
    {
        try
        {
            if (File.Exists(IconPath))
            {
                using var stream = File.OpenRead(IconPath);
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                frame.Freeze();
                return frame;
            }
        }
        catch
        {
        }

        return Imaging.CreateBitmapSourceFromHIcon(SystemIcons.Application.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
    }

    private static Icon CreateTrayIconInternal()
    {
        try
        {
            if (File.Exists(IconPath))
                return new Icon(IconPath);
        }
        catch
        {
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}
