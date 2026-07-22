using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Services;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class NodeCompatibilityTests
{
    [TestMethod]
    public void NewKeyboardNodeHasNoImplicitKey()
    {
        var node = new KeyboardNodeViewModel("keyboard");

        Assert.AreEqual(string.Empty, node.Key);
    }

    [TestMethod]
    public void ExplicitInfiniteScrollSurvivesFileRoundTrip()
    {
        var source = new ScrollWheelNodeViewModel("scroll")
        {
            ScrollDuration = 0,
        };

        NodeFileModel file = NodeSerializer.ToFileModel(source);
        var restored = NodeSerializer.FromFileModel(file) as ScrollWheelNodeViewModel;

        Assert.IsNotNull(restored);
        Assert.AreEqual(0, file.ScrollDuration);
        Assert.AreEqual(0, restored.ScrollDuration);
    }

    [TestMethod]
    public void LegacyDoubleClickMigratesToMouseClick()
    {
        var file = new NodeFileModel
        {
            Id = "legacy",
            NodeTypeKey = "mouse_double_click",
            Title = "鼠标双击",
            PositionX = 100,
            PositionY = 200,
        };

        var restored = NodeSerializer.FromFileModel(file) as MouseClickNodeViewModel;

        Assert.IsNotNull(restored);
        Assert.AreEqual(NodeKind.MouseClick, restored.NodeKind);
        Assert.AreEqual("鼠标点击", restored.Title);
        Assert.AreEqual(2, restored.TriggerCount);
        Assert.AreEqual(80, restored.TriggerIntervalMs);
    }
}
