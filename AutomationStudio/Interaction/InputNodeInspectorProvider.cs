using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Interaction;

/// <summary>
/// Structured inspector for input nodes whose fields are pure node state.
/// Device side effects remain in the existing runtime adapters.
/// </summary>
internal sealed class InputNodeInspectorProvider : INodeInspectorProvider
{
    private static readonly InspectorOption[] OperationModes =
    [
        new(nameof(PressReleaseMode.Press), "按下"),
        new(nameof(PressReleaseMode.Release), "抬起"),
        new(nameof(PressReleaseMode.Click), "点击"),
    ];

    private static readonly InspectorOption[] MouseButtons =
    [
        new(nameof(MouseButton.Left), "左键"),
        new(nameof(MouseButton.Right), "右键"),
        new(nameof(MouseButton.Middle), "中键"),
        new(nameof(MouseButton.XButton1), "侧键1"),
        new(nameof(MouseButton.XButton2), "侧键2"),
    ];

    public bool CanHandle(NodeBaseViewModel node) => node is
        KeyboardNodeViewModel or MouseClickNodeViewModel or MouseMoveNodeViewModel or KeyChordNodeViewModel;

    public void Load(NodeBaseViewModel node, InspectorViewModel inspector)
    {
        inspector.Reset(node);
        InspectorSectionViewModel values = inspector.AddSection("参数");

        switch (node)
        {
            case KeyboardNodeViewModel keyboard:
                inspector.AddField(values, "key", "按键", InspectorFieldKind.Text, keyboard.Key,
                    helpText: "未设置按键时不会发送键盘输入。");
                AddOperationMode(inspector, values, keyboard.OperationMode);
                AddRepeatFields(inspector, values, keyboard);
                break;

            case MouseClickNodeViewModel mouse:
                AddOperationMode(inspector, values, mouse.OperationMode);
                inspector.AddEnumField(values, "button", "鼠标按钮", MouseButtons,
                    mouse.MouseButton.ToString());
                AddPositionFields(inspector, values, mouse, "position");
                AddRepeatFields(inspector, values, mouse);
                break;

            case MouseMoveNodeViewModel move:
                AddPositionFields(inspector, values, move, "position");
                break;

            case KeyChordNodeViewModel chord:
                inspector.AddField(values, "chord", "组合键", InspectorFieldKind.Text, chord.Chord,
                    helpText: "例如 Ctrl+C、Ctrl+Shift+Esc。");
                AddOperationMode(inspector, values, chord.OperationMode);
                AddRepeatFields(inspector, values, chord);
                break;
        }
    }

    public void Apply(NodeBaseViewModel node, InspectorViewModel inspector)
    {
        switch (node)
        {
            case KeyboardNodeViewModel keyboard:
                keyboard.Key = inspector.Find("key")?.Value.Trim() ?? string.Empty;
                ApplyOperationMode(keyboard, inspector);
                ApplyRepeatFields(keyboard, inspector);
                break;

            case MouseClickNodeViewModel mouse:
                ApplyOperationMode(mouse, inspector);
                if (Enum.TryParse(inspector.Find("button")?.SelectedOption, out MouseButton button))
                    mouse.MouseButton = button;
                ApplyPositionFields(mouse, inspector);
                ApplyRepeatFields(mouse, inspector);
                break;

            case MouseMoveNodeViewModel move:
                ApplyPositionFields(move, inspector);
                break;

            case KeyChordNodeViewModel chord:
                chord.Chord = inspector.Find("chord")?.Value.Trim() ?? string.Empty;
                ApplyOperationMode(chord, inspector);
                ApplyRepeatFields(chord, inspector);
                break;
        }
    }

    private static void AddOperationMode(InspectorViewModel inspector, InspectorSectionViewModel section, PressReleaseMode mode) =>
        inspector.AddEnumField(section, "operation_mode", "操作模式", OperationModes, mode.ToString());

    private static void ApplyOperationMode(InputNodeBase node, InspectorViewModel inspector)
    {
        if (Enum.TryParse(inspector.Find("operation_mode")?.SelectedOption, out PressReleaseMode mode))
            node.OperationMode = mode;
    }

    private static void AddRepeatFields(InspectorViewModel inspector, InspectorSectionViewModel section, InputNodeBase node)
    {
        inspector.AddField(section, "trigger_count", "触发次数", InspectorFieldKind.Number,
            node.TriggerCount.ToString(), helpText: "0 表示持续触发，直到脚本被取消。");
        inspector.AddField(section, "trigger_interval", "触发间隔 (ms)", InspectorFieldKind.Number,
            node.TriggerIntervalMs.ToString(), helpText: "只在两次触发之间等待。");
    }

    private static void ApplyRepeatFields(InputNodeBase node, InspectorViewModel inspector)
    {
        if (int.TryParse(inspector.Find("trigger_count")?.Value, out int count))
            node.TriggerCount = Math.Max(0, count);
        if (int.TryParse(inspector.Find("trigger_interval")?.Value, out int interval))
            node.TriggerIntervalMs = Math.Max(1, interval);
    }

    private static void AddPositionFields(InspectorViewModel inspector, InspectorSectionViewModel section, NodeBaseViewModel node, string pinName)
    {
        bool connected = node.InputPins.FirstOrDefault(pin => pin.Name == pinName)?.HasConnection == true;
        double x;
        double y;
        switch (node)
        {
            case MouseClickNodeViewModel mouse:
                x = mouse.PositionX;
                y = mouse.PositionY;
                break;
            case MouseMoveNodeViewModel move:
                x = move.PositionX;
                y = move.PositionY;
                break;
            default:
                return;
        }

        string? help = connected ? "前置输入" : null;
        inspector.AddField(section, "position_x", "坐标 X", InspectorFieldKind.Number,
            x.ToString("0.##"), isEnabled: !connected, helpText: help);
        inspector.AddField(section, "position_y", "坐标 Y", InspectorFieldKind.Number,
            y.ToString("0.##"), isEnabled: !connected, helpText: help);
    }

    private static void ApplyPositionFields(NodeBaseViewModel node, InspectorViewModel inspector)
    {
        if (node.InputPins.FirstOrDefault(pin => pin.Name == "position")?.HasConnection == true)
            return;

        if (!double.TryParse(inspector.Find("position_x")?.Value, out double x) ||
            !double.TryParse(inspector.Find("position_y")?.Value, out double y))
            return;

        switch (node)
        {
            case MouseClickNodeViewModel mouse:
                mouse.PositionX = x;
                mouse.PositionY = y;
                break;
            case MouseMoveNodeViewModel move:
                move.PositionX = x;
                move.PositionY = y;
                break;
        }
    }
}
