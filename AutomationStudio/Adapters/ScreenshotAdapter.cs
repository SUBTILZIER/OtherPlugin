using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using AutomationStudioWpf.Services;

namespace AutomationStudioWpf.Adapters;

public sealed class ScreenshotAdapter : IScreenshotAdapter
{
    public ScreenshotResult SaveScreenshot(string path, int x, int y, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new ScreenshotResult(false, string.Empty, "截图保存路径为空。");

        try
        {
            Rectangle bounds = width > 0 && height > 0
                ? new Rectangle(x, y, width, height)
                : SystemInformation.VirtualScreen;

            string fullPath = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(ApplicationPaths.AutoScreenshotDirectory, path));
            string directory = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("截图保存路径缺少目录。");
            Directory.CreateDirectory(directory);

            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
            bitmap.Save(fullPath, ImageFormat.Png);
            return new ScreenshotResult(true, fullPath, $"截图已保存：{fullPath}");
        }
        catch (Exception ex)
        {
            return new ScreenshotResult(false, path, $"截图失败：{ex.Message}");
        }
    }
}
