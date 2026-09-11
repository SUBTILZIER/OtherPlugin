using System.Text.RegularExpressions;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class ConnectionLayeringTests
{
    [TestMethod]
    public void EditorSurfaceKeepsWirePassBelowNodesAndPreviewAbove()
    {
        string xaml = File.ReadAllText(FindProjectFile("Controls/EditorSurfaceControl.xaml"));

        int[] layers = Regex.Matches(xaml, "Panel.ZIndex=\"(?<layer>\\d+)\"")
            .Select(match => int.Parse(match.Groups["layer"].Value))
            .ToArray();
        int hitLayer = layers.First(layer => layer == 100);
        int visualLayer = layers.First(layer => layer == 150);
        int nodeLayer = layers.First(layer => layer == 200);
        int previewLayer = layers.First(layer => layer == 450);
        Assert.IsTrue(hitLayer < visualLayer && visualLayer < nodeLayer && previewLayer > nodeLayer);

        Match visualBlock = Regex.Match(
            xaml,
            "<ItemsControl ItemsSource=\"\\{Binding ConnectionPaths\\}\"\\s+Panel.ZIndex=\"150\"(?<body>.*?)</ItemsControl>",
            RegexOptions.Singleline);
        Assert.IsTrue(visualBlock.Success);
        StringAssert.Contains(visualBlock.Groups["body"].Value, "IsHitTestVisible=\"False\"");

        Match hitBlock = Regex.Match(
            xaml,
            "<ItemsControl ItemsSource=\"\\{Binding ConnectionPaths\\}\"\\s+Panel.ZIndex=\"100\"(?<body>.*?)</ItemsControl>",
            RegexOptions.Singleline);
        Assert.IsTrue(hitBlock.Success);
        Assert.DoesNotContain("IsHitTestVisible=\"False\"", hitBlock.Groups["body"].Value);
    }

    [TestMethod]
    public void NodeBackgroundUsesAlphaWithoutFadingNodeContent()
    {
        string appXaml = File.ReadAllText(FindProjectFile("App.xaml"));
        StringAssert.Contains(appXaml, "x:Key=\"EditorNodeBackgroundBrush\" Color=\"#E624272B\"");

        string themeService = File.ReadAllText(FindProjectFile("Services/AppThemeService.cs"));
        StringAssert.Contains(themeService, "[\"EditorNodeBackgroundBrush\"] = \"#E624272B\"");
        StringAssert.Contains(themeService, "[\"EditorNodeBackgroundBrush\"] = \"#E6F1F3F1\"");

        string surfaceXaml = File.ReadAllText(FindProjectFile("Controls/EditorSurfaceControl.xaml"));
        StringAssert.Contains(surfaceXaml, "x:Name=\"NodeCard\"");
        StringAssert.Contains(surfaceXaml, "Opacity=\"1\"");
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
