using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AutomationStudioWpf;

public static class WindowIconHelper
{
    private const float ShellIconScale = 1.12f;

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
                return CreateImageSource(RenderScaledBitmap(icon));
        }
        catch
        {
        }

        return CreateImageSource(RenderScaledBitmap(SystemIcons.Application));
    }

    private static Icon CreateTrayIconInternal()
    {
        try
        {
            using Icon? icon = ExtractEmbeddedIcon();
            if (icon is not null)
                return CreateIcon(RenderScaledBitmap(icon));
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

    private static Bitmap RenderScaledBitmap(Icon icon)
    {
        using Bitmap source = Bitmap.FromHicon(icon.Handle);
        var result = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using Graphics graphics = Graphics.FromImage(result);
        graphics.Clear(System.Drawing.Color.Transparent);
        graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

        float size = source.Width * ShellIconScale;
        float offset = (source.Width - size) / 2f;
        graphics.DrawImage(source, offset, offset, size, size);
        return result;
    }

    private static Icon CreateIcon(Bitmap bitmap)
    {
        IntPtr iconHandle = bitmap.GetHicon();
        try
        {
            using Icon icon = Icon.FromHandle(iconHandle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(iconHandle);
            bitmap.Dispose();
        }
    }

    private static ImageSource CreateImageSource(Bitmap bitmap)
    {
        IntPtr iconHandle = bitmap.GetHicon();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                iconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(iconHandle);
            bitmap.Dispose();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
