using System.Collections.ObjectModel;

namespace AutomationStudioWpf.Graph;

public abstract class ParameterNodeBaseViewModel : NodeBaseViewModel
{
    protected ParameterNodeBaseViewModel(string id, string title) : base(id, title)
    {
    }

    public ObservableCollection<GraphParameterDefinition> Parameters { get; } = [];

    public void AddParameter(string prefix = "NewParam")
    {
        Parameters.Add(new GraphParameterDefinition
        {
            Name = $"{prefix}{Parameters.Count + 1}",
            DefaultValue = GraphParameterDefinition.DefaultValueForType(GraphParameterType.Boolean),
        });
        SyncPins();
    }

    public void RemoveParameter(GraphParameterDefinition parameter)
    {
        Parameters.Remove(parameter);
        SyncPins();
    }

    public void MoveParameter(GraphParameterDefinition parameter, int offset)
    {
        int oldIndex = Parameters.IndexOf(parameter);
        if (oldIndex < 0)
            return;
        int newIndex = Math.Clamp(oldIndex + offset, 0, Parameters.Count - 1);
        if (oldIndex == newIndex)
            return;
        Parameters.Move(oldIndex, newIndex);
        SyncPins();
    }

    public abstract void SyncPins();
}

public sealed class FunctionEntryNodeViewModel : ParameterNodeBaseViewModel
{
    public FunctionEntryNodeViewModel(string id) : base(id, "函数开始")
    {
        AddOutput("exec_out", "执行输出", PinKind.Execution);
        SyncPins();
    }

    public override NodeKind NodeKind => NodeKind.FunctionEntry;
    public override string NodeTypeKey => "function_entry";

    public override void RefreshDescription() => Description = $"输入参数 {Parameters.Count} 个";

    public override void SyncPins()
    {
        for (int i = OutputPins.Count - 1; i >= 0; i--)
            if (OutputPins[i].Kind != PinKind.Execution)
                OutputPins.RemoveAt(i);
        foreach (var parameter in Parameters)
            AddOutput(parameter.Id, parameter.Name, parameter.ToPinKind());
        RefreshDescription();
    }
}

public sealed class FunctionReturnNodeViewModel : ParameterNodeBaseViewModel
{
    public FunctionReturnNodeViewModel(string id) : base(id, "函数返回")
    {
        AddInput("exec_in", "执行输入", PinKind.Execution);
        SyncPins();
    }

    public override NodeKind NodeKind => NodeKind.FunctionReturn;
    public override string NodeTypeKey => "function_return";

    public override void RefreshDescription() => Description = $"输出参数 {Parameters.Count} 个";

    public override void SyncPins()
    {
        for (int i = InputPins.Count - 1; i >= 0; i--)
            if (InputPins[i].Kind != PinKind.Execution)
                InputPins.RemoveAt(i);
        foreach (var parameter in Parameters)
            AddInput(parameter.Id, parameter.Name, parameter.ToPinKind());
        RefreshDescription();
    }
}

public sealed class FunctionCallNodeViewModel : NodeBaseViewModel
{
    public FunctionCallNodeViewModel(string id, string functionId, string title) : base(id, title)
    {
        FunctionId = functionId;
    }

    public override NodeKind NodeKind => NodeKind.FunctionCall;
    public override string NodeTypeKey => "function_call";
    public string FunctionId { get; set; }
    public ObservableCollection<GraphParameterDefinition> InputParameters { get; } = [];
    public ObservableCollection<GraphParameterDefinition> OutputParameters { get; } = [];

    public void ConfigurePins(IEnumerable<GraphParameterDefinition> inputs, IEnumerable<GraphParameterDefinition> outputs)
    {
        CallNodeParameterSync.Merge(InputParameters, inputs, preserveDefaultValue: true);
        CallNodeParameterSync.Merge(OutputParameters, outputs, preserveDefaultValue: false);
        SyncPins();
    }

    public void SyncPins()
    {
        InputPins.Clear();
        OutputPins.Clear();
        AddInput("exec_in", "执行输入", PinKind.Execution);
        foreach (var input in InputParameters)
            AddInput(input.Id, input.Name, input.ToPinKind());
        AddOutput("exec_out", "执行输出", PinKind.Execution);
        foreach (var output in OutputParameters)
            AddOutput(output.Id, output.Name, output.ToPinKind());
        RefreshDescription();
    }

    public override void RefreshDescription() => Description = $"调用函数：{Title}";
}

internal static class CallNodeParameterSync
{
    public static void Merge(
        ObservableCollection<GraphParameterDefinition> target,
        IEnumerable<GraphParameterDefinition> signature,
        bool preserveDefaultValue)
    {
        var oldById = target.ToDictionary(parameter => parameter.Id, StringComparer.Ordinal);
        target.Clear();
        foreach (var parameter in signature)
        {
            var next = parameter.Clone();
            if (preserveDefaultValue &&
                oldById.TryGetValue(parameter.Id, out var old) &&
                old.Type == parameter.Type)
            {
                next.DefaultValue = old.DefaultValue;
            }

            target.Add(next);
        }
    }
}
