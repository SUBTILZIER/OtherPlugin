namespace AutomationStudioWpf.Adapters;

public sealed record ScreenshotResult(bool Success, string Path, string Message);

public interface IScreenshotAdapter
{
    ScreenshotResult SaveScreenshot(string path, int x, int y, int width, int height);
}
