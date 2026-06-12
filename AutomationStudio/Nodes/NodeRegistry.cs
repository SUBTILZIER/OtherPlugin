using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Nodes.Common;
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

    public IReadOnlyCollection<INodeDefinition> Definitions => _definitions.Values;

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
            new CommonNodeExecutor(NodeKind.MouseDoubleClick),
            new CommonNodeExecutor(NodeKind.GetMousePosition),
            new CommonNodeExecutor(NodeKind.KeyChord),
            new CommonNodeExecutor(NodeKind.WaitImage),
            new CommonNodeExecutor(NodeKind.WaitImageDisappear),
            new CommonNodeExecutor(NodeKind.Compare),
            new CommonNodeExecutor(NodeKind.BooleanAnd),
            new CommonNodeExecutor(NodeKind.BooleanOr),
            new CommonNodeExecutor(NodeKind.BooleanNot),
            new CommonNodeExecutor(NodeKind.StringConcat),
            new CommonNodeExecutor(NodeKind.WaitWindow),
            new CommonNodeExecutor(NodeKind.CloseWindow),
            new CommonNodeExecutor(NodeKind.WindowExists),
            new CommonNodeExecutor(NodeKind.GetForegroundWindow),
            new CommonNodeExecutor(NodeKind.SaveScreenshot),
            new CommonNodeExecutor(NodeKind.ShowMessage),
        ],
        CreateDefaultDefinitions());
    }

    public bool TryGetExecutor(NodeKind kind, out INodeExecutor executor) => _executors.TryGetValue(kind, out executor!);

    public bool TryGetDefinition(NodeKind kind, out INodeDefinition definition) => _definitions.TryGetValue(kind, out definition!);

    private static IReadOnlyList<INodeDefinition> CreateDefaultDefinitions()
    {
        return
        [
            Definition(NodeKind.Start, "start", "事件开始运行", "核心", [OutExec("exec_out", "执行输出")]),
            Definition(NodeKind.Delay, "delay", "延迟", "核心", [InExec(), OutExec()]),
            Definition(NodeKind.If, "if", "分支", "核心", [InExec(), InBool("condition", "条件"), OutExec("exec_true", "True"), OutExec("exec_false", "False")]),
            Definition(NodeKind.ForLoop, "for_loop", "For循环", "核心", [InExec(), InBool("end_condition", "结束条件"), OutExec("exec_loop_body", "循环体"), OutExec("exec_completed", "完成")]),
            Definition(NodeKind.WhileLoop, "while_loop", "While循环", "核心", [InExec(), InBool("condition", "退出条件"), OutExec("exec_loop_body", "循环体"), OutExec("exec_completed", "完成")]),
            Definition(NodeKind.ToDo, "todo", "ToDo跳转", "核心", [InExec(), InString("target_title", "节点名"), InString("target_number", "编号"), OutExec()]),
            Definition(NodeKind.Reroute, "reroute", "转接点", "核心", []),

            Definition(NodeKind.MouseClick, "mouse_click", "鼠标点击", "输入/鼠标", [InExec(), InVector("position", "点击位置"), OutExec(), OutBool("result", "结果")]),
            Definition(NodeKind.MouseMove, "mouse_move", "鼠标移动", "输入/鼠标", [InExec(), InVector("position", "目标坐标"), OutExec(), OutBool("result", "结果"), OutVector("position", "当前位置")]),
            Definition(NodeKind.MouseDoubleClick, "mouse_double_click", "鼠标双击", "输入/鼠标", [InExec(), InVector("position", "点击位置"), OutExec(), OutBool("result", "结果")]),
            Definition(NodeKind.GetMousePosition, "get_mouse_position", "获取鼠标位置", "输入/鼠标", [InExec(), OutExec(), OutVector("position", "当前位置"), OutBool("result", "结果")]),
            Definition(NodeKind.ScrollWheel, "scroll_wheel", "鼠标滚轮", "输入/鼠标", [InExec(), OutExec(), OutBool("result", "结果")]),

            Definition(NodeKind.Keyboard, "keyboard", "键盘", "输入/键盘", [InExec(), OutExec(), OutBool("result", "结果")]),
            Definition(NodeKind.KeyChord, "key_chord", "组合键", "输入/键盘", [InExec(), OutExec(), OutBool("result", "结果")]),

            Definition(NodeKind.StartProgram, "start_program", "启动程序", "系统/窗口", [InExec(), OutExec(), OutString("process_name", "进程名"), OutBool("result", "结果")]),
            Definition(NodeKind.SelectWindow, "select_window", "选中窗口", "系统/窗口", [InExec(), InString("process_name", "进程名"), OutExec(), OutString("process_name", "进程名"), OutBool("result", "结果")]),
            Definition(NodeKind.WaitWindow, "wait_window", "等待窗口", "系统/窗口", [InExec(), InString("process_name", "进程名"), OutExec(), OutString("process_name", "进程名"), OutBool("result", "结果")]),
            Definition(NodeKind.CloseWindow, "close_window", "关闭窗口", "系统/窗口", [InExec(), InString("process_name", "进程名"), OutExec(), OutString("process_name", "进程名"), OutBool("result", "结果")]),
            Definition(NodeKind.WindowExists, "window_exists", "窗口是否存在", "系统/窗口", [InExec(), InString("process_name", "进程名"), OutExec(), OutString("process_name", "进程名"), OutBool("result", "结果")]),
            Definition(NodeKind.GetForegroundWindow, "get_foreground_window", "获取前台窗口", "系统/窗口", [InExec(), OutExec(), OutString("process_name", "进程名"), OutString("window_title", "窗口标题"), OutBool("result", "结果")]),

            Definition(NodeKind.FindImage, "find_image", "找图", "插件/图像识别", [InExec(), InString("source_image_path", "查找源"), InString("image_path", "查找目标"), OutExec(), OutBool("result", "结果"), OutVector("center", "中心点")]),
            Definition(NodeKind.WaitImage, "wait_image", "等待图片", "插件/图像识别", [InExec(), InString("source_image_path", "查找源"), InString("image_path", "查找目标"), OutExec(), OutBool("result", "结果"), OutVector("center", "中心点"), OutString("image_path", "查找目标")]),
            Definition(NodeKind.WaitImageDisappear, "wait_image_disappear", "图片消失", "插件/图像识别", [InExec(), InString("source_image_path", "查找源"), InString("image_path", "查找目标"), OutExec(), OutBool("result", "结果")]),

            Definition(NodeKind.Compare, "compare", "比较", "逻辑/判断", [InExec(), InString("left", "左值"), InString("right", "右值"), OutExec(), OutBool("result", "结果")]),
            Definition(NodeKind.BooleanAnd, "boolean_and", "布尔与", "逻辑/布尔", [InExec(), InBool("left", "左值"), InBool("right", "右值"), OutExec(), OutBool("result", "结果")]),
            Definition(NodeKind.BooleanOr, "boolean_or", "布尔或", "逻辑/布尔", [InExec(), InBool("left", "左值"), InBool("right", "右值"), OutExec(), OutBool("result", "结果")]),
            Definition(NodeKind.BooleanNot, "boolean_not", "布尔非", "逻辑/布尔", [InExec(), InBool("value", "输入"), OutExec(), OutBool("result", "结果")]),
            Definition(NodeKind.StringConcat, "string_concat", "字符串拼接", "逻辑/字符串", [InExec(), InString("left", "左文本"), InString("right", "右文本"), OutExec(), OutString("value", "结果")]),

            Definition(NodeKind.PrintLog, "print_log", "打印log", "调试", [InExec(), InString("message", "消息"), OutExec()]),
            Definition(NodeKind.SaveScreenshot, "save_screenshot", "截图", "插件/图像识别", [InExec(), InString("path", "保存路径"), OutExec(), OutString("image_path", "图像路径")]),
            Definition(NodeKind.ShowMessage, "show_message", "弹窗提示", "调试", [InExec(), InString("text", "文本"), OutExec(), OutBool("result", "结果")]),
            Definition(NodeKind.FunctionEntry, "function_entry", "函数开始", "自定义函数", [OutExec()]),
            Definition(NodeKind.FunctionReturn, "function_return", "函数返回", "自定义函数", [InExec()]),
            Definition(NodeKind.FunctionCall, "function_call", "函数调用", "自定义函数", [InExec(), OutExec()]),
            Definition(NodeKind.CustomEvent, "custom_event", "自定义事件", "事件", [OutExec()]),
            Definition(NodeKind.CustomEventCall, "custom_event_call", "调用自定义事件", "事件", [InExec(), OutExec()]),
        ];
    }

    private static NodeDefinition Definition(NodeKind kind, string typeKey, string displayName, string category, IReadOnlyList<NodePinDefinition> pins) =>
        new(kind, typeKey, displayName, category, pins, BuildSearchTags(kind, typeKey, category, pins), InspectorSchemaKey(kind));

    private static IReadOnlyList<string> BuildSearchTags(NodeKind kind, string typeKey, string category, IReadOnlyList<NodePinDefinition> pins)
    {
        return
        [
            kind.ToString(),
            typeKey,
            .. typeKey.Split('_', StringSplitOptions.RemoveEmptyEntries),
            category,
            .. category.Split('/', StringSplitOptions.RemoveEmptyEntries),
            .. pins.Select(pin => pin.Name),
            .. pins.Select(pin => pin.Label),
        ];
    }

    private static string InspectorSchemaKey(NodeKind kind) => kind switch
    {
        NodeKind.FindImage => "find_image",
        NodeKind.MouseClick => "mouse_click",
        NodeKind.MouseMove => "mouse_move",
        NodeKind.Keyboard => "keyboard",
        NodeKind.ScrollWheel => "scroll_wheel",
        NodeKind.Delay => "delay",
        NodeKind.If => "if",
        NodeKind.ForLoop => "for_loop",
        NodeKind.WhileLoop => "while_loop",
        NodeKind.ToDo => "todo",
        NodeKind.StartProgram => "start_program",
        NodeKind.SelectWindow => "select_window",
        NodeKind.FunctionEntry or NodeKind.FunctionReturn => "parameterized_entry",
        NodeKind.FunctionCall or NodeKind.CustomEvent or NodeKind.CustomEventCall => "callable",
        _ => "common",
    };

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
