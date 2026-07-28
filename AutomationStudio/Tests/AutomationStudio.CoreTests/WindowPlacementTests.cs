namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class WindowPlacementTests
{
    [TestMethod]
    public void MaximizedBoundsStayInsideMonitorWorkArea()
    {
        var monitor = new AutomationStudioWpf.MainWindow.RectI
        {
            Left = -1920,
            Top = 0,
            Right = 0,
            Bottom = 1080,
        };
        var work = new AutomationStudioWpf.MainWindow.RectI
        {
            Left = -1920,
            Top = 0,
            Right = 0,
            Bottom = 1040,
        };

        AutomationStudioWpf.MainWindow.WindowMaximizedBounds bounds =
            AutomationStudioWpf.MainWindow.CalculateMaximizedBounds(monitor, work);

        Assert.AreEqual(0, bounds.X);
        Assert.AreEqual(0, bounds.Y);
        Assert.AreEqual(1920, bounds.Width);
        Assert.AreEqual(1040, bounds.Height);
    }
}
