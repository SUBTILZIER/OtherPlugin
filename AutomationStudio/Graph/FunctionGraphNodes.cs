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
        Parameters.Add(new GraphParameterDefinition { Name = $"{prefix}{Parameters.Count + 1}" });
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

public sealed class MacroEntryNodeViewModel : ParameterNodeBaseViewModel
{
    public MacroEntryNodeViewModel(string id) : base(id, "宏开始")
    {
        AddOutput("exec_out", "执行输出", PinKind.Execution);
        SyncPins();
    }

    public override NodeKind NodeKind => NodeKind.MacroEntry;
    public override string NodeTypeKey => "macro_entry";

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

public sealed class MacroOutputNodeViewModel : ParameterNodeBaseViewModel
{
    private string _exitName = "完成";

    public MacroOutputNodeViewModel(string id) : base(id, "宏输出")
    {
        AddInput("exec_in", "执行输入", PinKind.Execution);
        SyncPins();
    }

    public override NodeKind NodeKind => NodeKind.MacroOutput;
    public override string NodeTypeKey => "macro_output";

    public string ExitName
    {
        get => _exitName;
        set
        {
            if (SetProperty(ref _exitName, string.IsNullOrWhiteSpace(value) ? "完成" : value.Trim()))
                RefreshDescription();
        }
    }

    public override void RefreshDescription() => Description = $"出口：{ExitName}\n输出参数 {Parameters.Count} 个";

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

    public void ConfigurePins(IEnumerable<GraphParameterDefinition> inputs, IEnumerable<GraphParameterDefinition> outputs)
    {
        InputPins.Clear();
        OutputPins.Clear();
        AddInput("exec_in", "执行输入", PinKind.Execution);
        foreach (var input in inputs)
            AddInput(input.Id, input.Name, input.ToPinKind());
        AddOutput("exec_out", "执行输出", PinKind.Execution);
        foreach (var output in outputs)
            AddOutput(output.Id, output.Name, output.ToPinKind());
        RefreshDescription();
    }

    public override void RefreshDescription() => Description = $"调用函数：{Title}";
}

public sealed class MacroCallNodeViewModel : NodeBaseViewModel
{
    public MacroCallNodeViewModel(string id, string macroId, string title) : base(id, title)
    {
        MacroId = macroId;
    }

    public override NodeKind NodeKind => NodeKind.MacroCall;
    public override string NodeTypeKey => "macro_call";
    public string MacroId { get; set; }

    public void ConfigurePins(
        IEnumerable<GraphParameterDefinition> inputs,
        IEnumerable<GraphParameterDefinition> outputs,
        IEnumerable<(string Id, string Name)> exits)
    {
        InputPins.Clear();
        OutputPins.Clear();
        AddInput("exec_in", "执行输入", PinKind.Execution);
        foreach (var input in inputs)
            AddInput(input.Id, input.Name, input.ToPinKind());
        foreach (var exit in exits)
            AddOutput($"exec_{exit.Id}", exit.Name, PinKind.Execution);
        foreach (var output in outputs)
            AddOutput(output.Id, output.Name, output.ToPinKind());
        RefreshDescription();
    }

    public override void RefreshDescription() => Description = $"调用宏：{Title}";
}
