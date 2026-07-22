using AutomationStudioWpf.Graph;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class ParameterRenameTests
{
    [TestMethod]
    public void FunctionEntryRenamePreservesPinAndConnection()
    {
        var entry = new FunctionEntryNodeViewModel("entry");
        var target = new FunctionReturnNodeViewModel("return");
        entry.AddParameter();
        target.AddParameter();
        GraphParameterDefinition parameter = entry.Parameters.Single();
        PinViewModel originalPin = entry.OutputPins.Single(pin => pin.Name == parameter.Id);
        var connection = new ConnectionViewModel(
            originalPin,
            target.InputPins.Single(pin => pin.Name == target.Parameters.Single().Id));

        bool changed = entry.RenameParameter(parameter, "Message");

        Assert.IsTrue(changed);
        Assert.AreEqual("Message", parameter.Name);
        Assert.AreSame(originalPin, entry.OutputPins.Single(pin => pin.Name == parameter.Id));
        Assert.AreEqual("Message", originalPin.DisplayName);
        Assert.AreSame(originalPin, connection.SourcePin);
    }

    [TestMethod]
    public void FunctionReturnRenamePreservesPinAndConnection()
    {
        var source = new FunctionEntryNodeViewModel("entry");
        var ret = new FunctionReturnNodeViewModel("return");
        source.AddParameter();
        ret.AddParameter();
        GraphParameterDefinition parameter = ret.Parameters.Single();
        PinViewModel originalPin = ret.InputPins.Single(pin => pin.Name == parameter.Id);
        var connection = new ConnectionViewModel(
            source.OutputPins.Single(pin => pin.Name == source.Parameters.Single().Id),
            originalPin);

        bool changed = ret.RenameParameter(parameter, "Result");

        Assert.IsTrue(changed);
        Assert.AreEqual("Result", parameter.Name);
        Assert.AreSame(originalPin, ret.InputPins.Single(pin => pin.Name == parameter.Id));
        Assert.AreEqual("Result", originalPin.DisplayName);
        Assert.AreSame(originalPin, connection.TargetPin);
    }

    [TestMethod]
    public void CustomEventRenamePreservesPinAndRejectsBlankName()
    {
        var node = new CustomEventNodeViewModel("event");
        node.AddParameter();
        GraphParameterDefinition parameter = node.Parameters.Single();
        PinViewModel originalPin = node.OutputPins.Single(pin => pin.Name == parameter.Id);
        string originalName = parameter.Name;

        Assert.IsFalse(node.RenameParameter(parameter, "   "));
        Assert.AreEqual(originalName, parameter.Name);
        Assert.IsTrue(node.RenameParameter(parameter, "EventValue"));
        Assert.AreSame(originalPin, node.OutputPins.Single(pin => pin.Name == parameter.Id));
        Assert.AreEqual("EventValue", originalPin.DisplayName);
    }
}
