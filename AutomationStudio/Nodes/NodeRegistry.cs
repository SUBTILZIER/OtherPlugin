using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Nodes.Core;
using AutomationStudioWpf.Nodes.Debug;
using AutomationStudioWpf.Nodes.Input.Keyboard;
using AutomationStudioWpf.Nodes.Input.Mouse;
using AutomationStudioWpf.Nodes.Plugins.ImageRecognition;
using AutomationStudioWpf.Nodes.System.Window;
using AutomationStudioWpf.Runtime;

namespace AutomationStudioWpf.Nodes;

public sealed class NodeRegistry
{
    private readonly Dictionary<NodeKind, INodeExecutor> _executors;
    private readonly Dictionary<NodeKind, INodeDefinition> _definitions;

    private NodeRegistry(IEnumerable<INodeExecutor> executors, IEnumerable<INodeDefinition> definitions)
    {
        _executors = executors.ToDictionary(e => e.NodeKind);
        _definitions = definitions.ToDictionary(d => d.NodeKind);
    }

    public static NodeRegistry CreateDefault()
    {
        return new NodeRegistry(
        [
            new DelayNodeExecutor(),
            new MouseClickNodeExecutor(),
            new MouseMoveNodeExecutor(),
            new ScrollWheelNodeExecutor(),
            new KeyboardNodeExecutor(),
            new StartProgramNodeExecutor(),
            new SelectWindowNodeExecutor(),
            new PrintLogNodeExecutor(),
            new FindImageNodeExecutor(),
        ],
        CreateDefaultDefinitions());
    }

    public bool TryGetExecutor(NodeKind kind, out INodeExecutor executor) => _executors.TryGetValue(kind, out executor!);

    public bool TryGetDefinition(NodeKind kind, out INodeDefinition definition) => _definitions.TryGetValue(kind, out definition!);

    public IReadOnlyCollection<INodeDefinition> Definitions => _definitions.Values;

    private static IReadOnlyList<INodeDefinition> CreateDefaultDefinitions()
    {
        return
        [
            Definition(NodeKind.Start, "start", "事件开始运行", "核心", [OutExec("exec_out", "执行输出")]),
            Definition(NodeKind.Delay, "delay", "延迟", "核心", [InExec(), OutExec()]),
            Definition(NodeKind.If, "if", "分支", "核心", [InExec(), InBool("condition", "条件"), OutExec("exec_true", "True"), OutExec("exec_false", "False")]),
            Definition(NodeKind.ForLoop, "for_loop", "For循环", "核心", [InExec(), InBool("end_condition", "结束条件"), OutExec("exec_loop_body", "循环体"), OutExec("exec_completed", "完成")]),
            Definition(NodeKind.WhileLoop, "while_loop", "While循环", "核心", [InExec(), InBool("condition", "退出条件"), OutExec("exec_loop_body", "循环体"), OutExec("exec_completed", "完成")]),
            Definition(NodeKind.Reroute, "reroute", "转接点", "核心", []),

            Definition(NodeKind.MouseClick, "mouse_click", "鼠标点击", "输入/鼠标", [InExec(), InVector("position", "点击位置"), OutExec(), OutBool("result", "结果")]),
            Definition(NodeKind.MouseMove, "mouse_move", "鼠标移动", "输入/鼠标", [InExec(), InVector("position", "目标坐标"), OutExec(), OutBool("result", "结果"), OutVector("position", "当前位置")]),
            Definition(NodeKind.ScrollWheel, "scroll_wheel", "鼠标滚轮", "输入/鼠标", [InExec(), OutExec(), OutBool("result", "结果")]),
            Definition(NodeKind.Keyboard, "keyboard", "键盘", "输入/键盘", [InExec(), OutExec(), OutBool("result", "结果")]),

            Definition(NodeKind.StartProgram, "start_program", "启动程序", "系统/窗口", [InExec(), OutExec(), OutString("process_name", "进程名"), OutBool("result", "结果")]),
            Definition(NodeKind.SelectWindow, "select_window", "选中窗口", "系统/窗口", [InExec(), InString("process_name", "进程名"), OutExec(), OutString("process_name", "进程名"), OutBool("result", "结果")]),
            Definition(NodeKind.PrintLog, "print_log", "打印log", "调试", [InExec(), InString("message", "消息"), OutExec()]),

            Definition(NodeKind.FindImage, "find_image", "找图", "插件/图像识别", [InExec(), OutExec(), OutBool("result", "结果"), OutVector("center", "中心点")]),
        ];
    }

    private static NodeDefinition Definition(NodeKind kind, string typeKey, string displayName, string category, IReadOnlyList<NodePinDefinition> pins) =>
        new(kind, typeKey, displayName, category, pins);

    private static NodePinDefinition InExec(string name = "exec_in", string label = "执行输入") =>
        new(name, label, PinKind.Execution, PinDirection.Input);

    private static NodePinDefinition OutExec(string name = "exec_out", string label = "执行输出") =>
        new(name, label, PinKind.Execution, PinDirection.Output);

    private static NodePinDefinition InBool(string name, string label) =>
        new(name, label, PinKind.Boolean, PinDirection.Input);

    private static NodePinDefinition OutBool(string name, string label) =>
        new(name, label, PinKind.Boolean, PinDirection.Output);

    private static NodePinDefinition InVector(string name, string label) =>
        new(name, label, PinKind.Vector2D, PinDirection.Input);

    private static NodePinDefinition OutVector(string name, string label) =>
        new(name, label, PinKind.Vector2D, PinDirection.Output);

    private static NodePinDefinition InString(string name, string label) =>
        new(name, label, PinKind.String, PinDirection.Input);

    private static NodePinDefinition OutString(string name, string label) =>
        new(name, label, PinKind.String, PinDirection.Output);
}
