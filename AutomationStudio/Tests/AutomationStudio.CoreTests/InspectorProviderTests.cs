using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Interaction;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class InspectorProviderTests
{
    [TestMethod]
    public void KeyboardProviderRoundTripsKeyModeAndRepeatSettings()
    {
        var node = new KeyboardNodeViewModel("keyboard")
        {
            Key = "F2",
            OperationMode = PressReleaseMode.Release,
            TriggerCount = 3,
            TriggerIntervalMs = 250,
        };
        var inspector = new InspectorViewModel();
        var provider = new InputNodeInspectorProvider();

        provider.Load(node, inspector);
        inspector.Find("key")!.Value = "F3";
        inspector.Find("operation_mode")!.SelectedOption = nameof(PressReleaseMode.Click);
        inspector.Find("trigger_count")!.Value = "4";
        inspector.Find("trigger_interval")!.Value = "400";
        provider.Apply(node, inspector);

        Assert.AreEqual("F3", node.Key);
        Assert.AreEqual(PressReleaseMode.Click, node.OperationMode);
        Assert.AreEqual(4, node.TriggerCount);
        Assert.AreEqual(400, node.TriggerIntervalMs);
    }

    [TestMethod]
    public void MouseProviderKeepsPositionUnsetUntilUserChangesIt()
    {
        var node = new MouseMoveNodeViewModel("move");
        var inspector = new InspectorViewModel();
        var provider = new InputNodeInspectorProvider();

        provider.Load(node, inspector);
        provider.Apply(node, inspector);
        Assert.IsFalse(node.HasManualPosition);

        inspector.Find("position_x")!.Value = "0";
        inspector.Find("position_y")!.Value = "0";
        provider.Apply(node, inspector);

        Assert.IsTrue(node.HasManualPosition);
        Assert.AreEqual(0, node.PositionX);
        Assert.AreEqual(0, node.PositionY);
    }

    [TestMethod]
    public void ConnectedMousePositionIsReadOnlyInStructuredInspector()
    {
        var node = new MouseClickNodeViewModel("click");
        node.InputPins.Single(pin => pin.Name == "position").HasConnection = true;
        var inspector = new InspectorViewModel();
        var provider = new InputNodeInspectorProvider();

        provider.Load(node, inspector);

        Assert.IsFalse(inspector.Find("position_x")!.IsEnabled);
        Assert.IsFalse(inspector.Find("position_y")!.IsEnabled);
        Assert.AreEqual("前置输入", inspector.Find("position_x")!.HelpText);
    }

    [TestMethod]
    public void ControlFlowProviderPreservesConnectedBooleanDefaults()
    {
        var node = new IfNodeViewModel("if");
        node.InputPins.Single(pin => pin.Name == "condition").HasConnection = true;
        var inspector = new InspectorViewModel();
        var provider = new ControlFlowInspectorProvider();

        provider.Load(node, inspector);

        Assert.IsFalse(inspector.Find("condition")!.IsEnabled);
        Assert.AreEqual("前置输入", inspector.Find("condition")!.HelpText);
        bool original = node.ConditionValue;
        inspector.Find("condition")!.BooleanValue = !original;
        provider.Apply(node, inspector);
        Assert.AreEqual(original, node.ConditionValue);
    }

    [TestMethod]
    public void PrintLogProviderPreservesMultilineText()
    {
        var node = new PrintLogNodeViewModel("print") { Message = "line1\nline2" };
        var inspector = new InspectorViewModel();
        var provider = new ControlFlowInspectorProvider();

        provider.Load(node, inspector);

        Assert.IsTrue(inspector.Find("message")!.IsMultiline);
        Assert.AreEqual("line1\nline2", inspector.Find("message")!.Value);
    }
}
