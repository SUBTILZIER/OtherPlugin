using System.Collections.ObjectModel;

namespace AutomationStudioWpf.Graph;

public sealed class CustomEventNodeViewModel : ParameterNodeBaseViewModel
{
    public CustomEventNodeViewModel(string id, string? customEventId = null) : base(id, "自定义事件")
    {
        CustomEventId = string.IsNullOrWhiteSpace(customEventId) ? id : customEventId;
        AddOutput("exec_out", "执行输出", PinKind.Execution);
        SyncPins();
    }

    public override NodeKind NodeKind => NodeKind.CustomEvent;

    public override string NodeTypeKey => "custom_event";

    public string CustomEventId { get; set; }

    public override void RefreshDescription() => Description = $"事件参数 {Parameters.Count} 个";

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

public sealed class CustomEventCallNodeViewModel : NodeBaseViewModel
{
    public CustomEventCallNodeViewModel(string id, string customEventId, string title) : base(id, title)
    {
        CustomEventId = customEventId;
    }

    public override NodeKind NodeKind => NodeKind.CustomEventCall;

    public override string NodeTypeKey => "custom_event_call";

    public string CustomEventId { get; set; }
    public ObservableCollection<GraphParameterDefinition> InputParameters { get; } = [];

    public void ConfigurePins(IEnumerable<GraphParameterDefinition> inputs)
    {
        CallNodeParameterSync.Merge(InputParameters, inputs, preserveDefaultValue: true);
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
        RefreshDescription();
    }

    public override void RefreshDescription() => Description = $"调用自定义事件：{Title}";
}
