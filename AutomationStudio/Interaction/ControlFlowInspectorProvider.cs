using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Interaction;

/// <summary>
/// Structured inspector for control-flow and text-only nodes.
/// Nodes that need file/window pickers remain on the legacy provider path.
/// </summary>
internal sealed class ControlFlowInspectorProvider : INodeInspectorProvider
{
    private static readonly InspectorOption[] ScrollActions =
    [
        new(nameof(ScrollWheelAction.Press), "中键按下"),
        new(nameof(ScrollWheelAction.Release), "中键抬起"),
        new(nameof(ScrollWheelAction.ScrollForward), "向前滚动"),
        new(nameof(ScrollWheelAction.ScrollBackward), "向后滚动"),
    ];

    private static readonly InspectorOption[] WhileModes =
    [
        new(nameof(WhileLoopMode.Finite), "有限循环"),
        new(nameof(WhileLoopMode.Infinite), "持续循环"),
    ];

    public bool CanHandle(NodeBaseViewModel node) => node is
        DelayNodeViewModel or IfNodeViewModel or ForLoopNodeViewModel or WhileLoopNodeViewModel or
        ScrollWheelNodeViewModel or PrintLogNodeViewModel;

    public void Load(NodeBaseViewModel node, InspectorViewModel inspector)
    {
        inspector.Reset(node);
        InspectorSectionViewModel values = inspector.AddSection("参数");

        switch (node)
        {
            case DelayNodeViewModel delay:
                inspector.AddField(values, "delay_ms", "延迟 (ms)", InspectorFieldKind.Number,
                    delay.DelayMs.ToString());
                break;

            case IfNodeViewModel branch:
                AddBooleanInput(inspector, values, branch, "condition", "判断条件", branch.ConditionValue);
                break;

            case ForLoopNodeViewModel loop:
                inspector.AddField(values, "loop_count", "循环次数", InspectorFieldKind.Number,
                    loop.LoopCount.ToString());
                AddBooleanInput(inspector, values, loop, "end_condition", "结束条件", loop.EndConditionValue);
                break;

            case WhileLoopNodeViewModel loop:
                AddBooleanInput(inspector, values, loop, "condition", "循环条件", loop.ConditionValue);
                inspector.AddEnumField(values, "loop_mode", "循环模式", WhileModes, loop.LoopMode.ToString());
                inspector.AddField(values, "max_iterations", "最大次数", InspectorFieldKind.Number,
                    loop.MaxIterations.ToString(), helpText: "有限循环生效；持续循环由停止执行控制。");
                break;

            case ScrollWheelNodeViewModel scroll:
                inspector.AddEnumField(values, "scroll_action", "滚轮操作", ScrollActions,
                    scroll.ScrollAction.ToString());
                inspector.AddField(values, "scroll_speed", "滚动速度", InspectorFieldKind.Number,
                    scroll.ScrollSpeed.ToString());
                inspector.AddField(values, "scroll_interval", "触发间隔 (ms)", InspectorFieldKind.Number,
                    scroll.ScrollInterval.ToString());
                inspector.AddField(values, "scroll_duration", "持续时间 (ms)", InspectorFieldKind.Number,
                    scroll.ScrollDuration.ToString(), helpText: "0 表示持续到脚本取消。");
                break;

            case PrintLogNodeViewModel print:
                bool connected = HasConnection(print, "message");
                inspector.AddField(values, "message", "消息内容", InspectorFieldKind.Text,
                    connected ? string.Empty : print.Message,
                    isEnabled: !connected,
                    multiline: !connected,
                    helpText: connected ? "前置输入" : "支持换行，未连接时使用此默认值。");
                break;
        }
    }

    public void Apply(NodeBaseViewModel node, InspectorViewModel inspector)
    {
        switch (node)
        {
            case DelayNodeViewModel delay:
                if (ReadInt(inspector, "delay_ms", out int delayMs))
                    delay.DelayMs = delayMs;
                break;

            case IfNodeViewModel branch:
                if (!HasConnection(branch, "condition") && inspector.Find("condition") is { } branchCondition)
                    branch.ConditionValue = branchCondition.BooleanValue;
                break;

            case ForLoopNodeViewModel loop:
                if (ReadInt(inspector, "loop_count", out int loopCount))
                    loop.LoopCount = loopCount;
                if (!HasConnection(loop, "end_condition") && inspector.Find("end_condition") is { } end)
                    loop.EndConditionValue = end.BooleanValue;
                break;

            case WhileLoopNodeViewModel loop:
                if (!HasConnection(loop, "condition") && inspector.Find("condition") is { } whileCondition)
                    loop.ConditionValue = whileCondition.BooleanValue;
                if (Enum.TryParse(inspector.Find("loop_mode")?.SelectedOption, out WhileLoopMode mode))
                    loop.LoopMode = mode;
                if (ReadInt(inspector, "max_iterations", out int maxIterations))
                    loop.MaxIterations = maxIterations;
                break;

            case ScrollWheelNodeViewModel scroll:
                if (Enum.TryParse(inspector.Find("scroll_action")?.SelectedOption, out ScrollWheelAction action))
                    scroll.ScrollAction = action;
                if (ReadInt(inspector, "scroll_speed", out int speed))
                    scroll.ScrollSpeed = speed;
                if (ReadInt(inspector, "scroll_interval", out int interval))
                    scroll.ScrollInterval = interval;
                if (ReadInt(inspector, "scroll_duration", out int duration))
                    scroll.ScrollDuration = duration;
                break;

            case PrintLogNodeViewModel print:
                if (!HasConnection(print, "message") && inspector.Find("message") is { } message)
                    print.Message = message.Value;
                break;
        }
    }

    private static void AddBooleanInput(
        InspectorViewModel inspector,
        InspectorSectionViewModel section,
        NodeBaseViewModel node,
        string pinName,
        string label,
        bool value)
    {
        bool connected = HasConnection(node, pinName);
        inspector.AddField(section, pinName, label, InspectorFieldKind.Boolean,
            booleanValue: value,
            isEnabled: !connected,
            helpText: connected ? "前置输入" : "未连接时使用此默认值");
    }

    private static bool ReadInt(InspectorViewModel inspector, string key, out int value) =>
        int.TryParse(inspector.Find(key)?.Value, out value);

    private static bool HasConnection(NodeBaseViewModel node, string pinName) =>
        node.InputPins.FirstOrDefault(pin => pin.Name == pinName)?.HasConnection == true;
}
