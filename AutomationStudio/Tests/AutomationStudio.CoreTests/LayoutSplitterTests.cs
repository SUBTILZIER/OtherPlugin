using System.Text.RegularExpressions;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class LayoutSplitterTests
{
    [TestMethod]
    public void AllLayoutSplittersUseNativeGridSplitterWithExplicitResizeSettings()
    {
        string mainXaml = File.ReadAllText(FindProjectFile("MainWindow.xaml"));
        string surfaceXaml = File.ReadAllText(FindProjectFile("Controls/EditorSurfaceControl.xaml"));
        string combined = mainXaml + Environment.NewLine + surfaceXaml;

        Assert.AreEqual(5, Regex.Matches(combined, "<GridSplitter\\b").Count);
        Assert.AreEqual(5, Regex.Matches(combined, "ShowsPreview=\"False\"").Count);
        Assert.AreEqual(5, Regex.Matches(combined, "ResizeBehavior=\"PreviousAndNext\"").Count);
        Assert.AreEqual(5, Regex.Matches(combined, "DragCompleted=\"GridSplitter_DragCompleted\"").Count);

        StringAssert.Contains(mainXaml, "x:Name=\"LogPanelSplitter\" Grid.Row=\"1\"");
        StringAssert.Contains(mainXaml, "x:Name=\"ContentBrowserTreeSplitter\"");
        StringAssert.Contains(mainXaml, "ResizeDirection=\"Rows\"");
        StringAssert.Contains(mainXaml, "ResizeDirection=\"Columns\"");
        StringAssert.Contains(surfaceXaml, "x:Name=\"GraphSidebarSplitter\"");
        StringAssert.Contains(surfaceXaml, "x:Name=\"InspectorSplitter\"");
        Assert.IsTrue(Regex.IsMatch(surfaceXaml, "x:Name=\"GraphSidebarSplitter\"\\s+Grid.Column=\"1\""));
        Assert.IsTrue(Regex.IsMatch(surfaceXaml, "x:Name=\"InspectorSplitter\"\\s+Grid.Column=\"3\""));

        Assert.IsFalse(combined.Contains("controls:LayoutSplitter", StringComparison.Ordinal));
        foreach (Match splitter in Regex.Matches(combined, "<GridSplitter\\b(?<body>.*?)/>", RegexOptions.Singleline))
        {
            Assert.IsTrue(
                splitter.Groups["body"].Value.Contains("Width=\"7\"", StringComparison.Ordinal) ||
                splitter.Groups["body"].Value.Contains("Height=\"7\"", StringComparison.Ordinal),
                "every GridSplitter has a 7px hit dimension");
            StringAssert.Contains(splitter.Groups["body"].Value, "Background=\"Transparent\"");
            StringAssert.Contains(splitter.Groups["body"].Value, "IsHitTestVisible=\"True\"");
            Assert.IsFalse(splitter.Groups["body"].Value.Contains("Panel.ZIndex=\"1000\"", StringComparison.Ordinal));
            Assert.IsFalse(splitter.Groups["body"].Value.Contains("MinWidth=\"7\"", StringComparison.Ordinal));
        }
    }

    private static string FindProjectFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate project file: {relativePath}");
    }
}
