using System.Drawing;
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

    private static ImageSource CreateAppIcon()
    {
        try
        {
            using Icon? icon = ExtractEmbeddedIcon();
            if (icon is not null)
                return CreateImageSource(icon);
        }
        catch
        {
        }

        return CreateImageSource(SystemIcons.Application);
    }

    private static Icon CreateTrayIconInternal()
    {
        try
        {
            Icon? icon = ExtractEmbeddedIcon();
            if (icon is not null)
                return icon;
        }
        catch
        {
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    private static Icon? ExtractEmbeddedIcon()
    {
        string? executablePath = Environment.ProcessPath;
        return string.IsNullOrWhiteSpace(executablePath)
            ? null
            : Icon.ExtractAssociatedIcon(executablePath);
    }

    private static ImageSource CreateImageSource(Icon icon)
    {
        var source = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }
}
