using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.GraphCore;

[Flags]
public enum NodeTraitFlags
{
    None = 0,
    Pure = 1 << 0,
    Execution = 1 << 1,
    ToDoTarget = 1 << 2,
    DynamicPins = 1 << 3,
}

public enum NodeRuntimeSupport
{
    RegistryExecutor,
    BuiltInControlFlow,
    PureEvaluator,
    Structural,
    Unsupported,
}

public enum NodePreviewSupport
{
    Emitter,
    ExplicitFallback,
    Hidden,
}

public sealed record PinDefinition(
    string Name,
    string Label,
    PinKind Kind,
    PinDirection Direction);

public sealed record NodeDescriptor(
    NodeKind Kind,
    string TypeKey,
    NodeTraitFlags Traits,
    IReadOnlyList<PinDefinition> Pins,
    bool CanDelete,
    bool ShouldAssignNumber,
    bool CanCreate,
    NodeRuntimeSupport RuntimeSupport,
    NodePreviewSupport PreviewSupport,
    bool HasSerializer)
{
    public bool IsPure => Traits.HasFlag(NodeTraitFlags.Pure);

    public bool HasExecutionPins => Traits.HasFlag(NodeTraitFlags.Execution);

    public bool IsToDoTarget => Traits.HasFlag(NodeTraitFlags.ToDoTarget);
}

public static class NodeDescriptorCatalog
{
    private static readonly IReadOnlyList<NodeDescriptor> DescriptorList =
    [
        BuiltIn(NodeKind.Start, "start", [OutExec()], canCreate: false, canDelete: false),
        Executor(NodeKind.FindImage, "find_image", [InExec(), InString("source_image_path", "查找源"), InString("image_path", "查找目标"), OutExec(), OutBool("result", "结果"), OutVector("center", "中心点")]),
        Executor(NodeKind.MouseClick, "mouse_click", [InExec(), InVector("position", "点击位置"), OutExec(), OutBool("result", "结果")]),
        Executor(NodeKind.Delay, "delay", [InExec(), OutExec()]),
        Executor(NodeKind.MouseMove, "mouse_move", [InExec(), InVector("position", "目标坐标"), OutExec(), OutBool("result", "结果"), OutVector("position", "当前位置")]),
        Executor(NodeKind.Keyboard, "keyboard", [InExec(), OutExec(), OutBool("result", "结果")]),
        Executor(NodeKind.ScrollWheel, "scroll_wheel", [InExec(), OutExec(), OutBool("result", "结果")]),
        Structural(NodeKind.Reroute, "reroute", canDelete: true),
        BuiltIn(NodeKind.If, "if", [InExec(), InBool("condition", "条件"), OutExec("exec_true", "True"), OutExec("exec_false", "False")]),
        BuiltIn(NodeKind.ForLoop, "for_loop", [InExec(), InBool("end_condition", "结束条件"), OutExec("exec_loop_body", "循环体"), OutExec("exec_completed", "完成")]),
        BuiltIn(NodeKind.WhileLoop, "while_loop", [InExec(), InBool("condition", "退出条件"), OutExec("exec_loop_body", "循环体"), OutExec("exec_completed", "完成")]),
        BuiltIn(NodeKind.ToDo, "todo", [InExec(), InString("target_title", "节点名"), InString("target_number", "编号"), OutExec()]),
        Executor(NodeKind.StartProgram, "start_program", [InExec(), OutExec(), OutString("process_name", "进程名"), OutBool("result", "结果")]),
        Executor(NodeKind.PrintLog, "print_log", [InExec(), InString("message", "消息"), OutExec()]),
        Executor(NodeKind.SelectWindow, "select_window", [InExec(), InString("process_name", "进程名"), OutExec(), OutString("process_name", "进程名"), OutBool("result", "结果")]),
        Executor(NodeKind.GetMousePosition, "get_mouse_position", [InExec(), OutExec(), OutVector("position", "当前位置"), OutBool("result", "结果")]),
        Executor(NodeKind.KeyChord, "key_chord", [InExec(), OutExec(), OutBool("result", "结果")]),
        Executor(NodeKind.WaitImage, "wait_image", [InExec(), InString("source_image_path", "查找源"), InString("image_path", "查找目标"), OutExec(), OutBool("result", "结果"), OutVector("center", "中心点"), OutString("image_path", "查找目标")]),
        Executor(NodeKind.WaitImageDisappear, "wait_image_disappear", [InExec(), InString("source_image_path", "查找源"), InString("image_path", "查找目标"), OutExec(), OutBool("result", "结果")]),
        Pure(NodeKind.Compare, "compare", [InString("left", "左值"), InString("right", "右值"), OutBool("result", "结果")]),
        Pure(NodeKind.BooleanAnd, "boolean_and", [InBool("left", "布尔1"), InBool("right", "布尔2"), OutBool("result", "结果")], dynamicPins: true),
        Pure(NodeKind.BooleanOr, "boolean_or", [InBool("left", "布尔1"), InBool("right", "布尔2"), OutBool("result", "结果")], dynamicPins: true),
        Pure(NodeKind.BooleanNot, "boolean_not", [InBool("value", "输入"), OutBool("result", "结果")]),
        Pure(NodeKind.StringConcat, "string_concat", [InString("left", "文本1"), InString("right", "文本2"), OutString("value", "结果")], dynamicPins: true),
        Executor(NodeKind.WaitWindow, "wait_window", [InExec(), InString("process_name", "进程名"), OutExec(), OutString("process_name", "进程名"), OutBool("result", "结果")]),
        Executor(NodeKind.CloseWindow, "close_window", [InExec(), InString("process_name", "进程名"), OutExec(), OutString("process_name", "进程名"), OutBool("result", "结果")]),
        Executor(NodeKind.WindowExists, "window_exists", [InExec(), InString("process_name", "进程名"), OutExec(), OutString("process_name", "进程名"), OutBool("result", "结果")]),
        Executor(NodeKind.GetForegroundWindow, "get_foreground_window", [InExec(), OutExec(), OutString("process_name", "进程名"), OutString("window_title", "窗口标题"), OutBool("result", "结果")]),
        Removed(NodeKind.Comment, "comment"),
        Executor(NodeKind.SaveScreenshot, "save_screenshot", [InExec(), InString("path", "保存路径"), OutExec(), OutString("image_path", "图像路径")]),
        Executor(NodeKind.ShowMessage, "show_message", [InExec(), InString("text", "文本"), OutExec(), OutBool("result", "结果")]),
        BuiltIn(NodeKind.FunctionEntry, "function_entry", [OutExec()], canCreate: false, canDelete: false),
        BuiltIn(NodeKind.FunctionReturn, "function_return", [InExec()], canCreate: false, canDelete: false),
        BuiltIn(NodeKind.FunctionCall, "function_call", [InExec(), OutExec()], canCreate: false),
        BuiltIn(NodeKind.CustomEvent, "custom_event", [OutExec()]),
        BuiltIn(NodeKind.CustomEventCall, "custom_event_call", [InExec(), OutExec()], canCreate: false),
        BuiltIn(NodeKind.MultiThread, "multi_thread", [InExec(), OutExec("exec_thread_1", "线程1"), OutExec("exec_thread_2", "线程2"), OutExec("exec_completed", "全部完成")]),
    ];

    private static readonly IReadOnlyDictionary<NodeKind, NodeDescriptor> ByKind =
        DescriptorList.ToDictionary(item => item.Kind);

    private static readonly IReadOnlyDictionary<string, NodeKind> ByTypeKey =
        BuildTypeKeyMap();

    public static IReadOnlyList<NodeDescriptor> All => DescriptorList;

    public static NodeDescriptor Get(NodeKind kind) =>
        ByKind.TryGetValue(kind, out var descriptor)
            ? descriptor
            : throw new KeyNotFoundException($"未注册节点描述：{kind}");

    public static bool TryGet(NodeKind kind, out NodeDescriptor descriptor) =>
        ByKind.TryGetValue(kind, out descriptor!);

    public static bool TryFromTypeKey(string? typeKey, out NodeKind kind)
    {
        if (!string.IsNullOrWhiteSpace(typeKey) && ByTypeKey.TryGetValue(typeKey, out kind))
            return true;

        kind = default;
        return false;
    }

    public static NodeKind? FromTypeKey(string? typeKey) =>
        TryFromTypeKey(typeKey, out var kind) ? kind : null;

    private static IReadOnlyDictionary<string, NodeKind> BuildTypeKeyMap()
    {
        var map = new Dictionary<string, NodeKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in DescriptorList)
            map[descriptor.TypeKey] = descriptor.Kind;

        map["mouse_left_click"] = NodeKind.MouseClick;
        map["mouse_double_click"] = NodeKind.MouseClick;
        return map;
    }

    private static NodeDescriptor Executor(NodeKind kind, string typeKey, IReadOnlyList<PinDefinition> pins) =>
        Create(kind, typeKey, ExecutionTraits(kind), pins, canCreate: true, runtimeSupport: NodeRuntimeSupport.RegistryExecutor);

    private static NodeDescriptor BuiltIn(NodeKind kind, string typeKey, IReadOnlyList<PinDefinition> pins, bool canCreate = true, bool canDelete = true) =>
        Create(kind, typeKey, ExecutionTraits(kind), pins, canCreate, NodeRuntimeSupport.BuiltInControlFlow, canDelete);

    private static NodeDescriptor Pure(NodeKind kind, string typeKey, IReadOnlyList<PinDefinition> pins, bool dynamicPins = false) =>
        Create(kind, typeKey, NodeTraitFlags.Pure | (dynamicPins ? NodeTraitFlags.DynamicPins : NodeTraitFlags.None), pins, canCreate: true, NodeRuntimeSupport.PureEvaluator);

    private static NodeDescriptor Structural(NodeKind kind, string typeKey, bool canDelete) =>
        Create(kind, typeKey, NodeTraitFlags.None, [], canCreate: false, NodeRuntimeSupport.Structural, canDelete);

    private static NodeDescriptor Removed(NodeKind kind, string typeKey) =>
        Create(kind, typeKey, NodeTraitFlags.None, [], canCreate: false, NodeRuntimeSupport.Unsupported, canDelete: true, hasSerializer: false, previewSupport: NodePreviewSupport.Hidden);

    private static NodeDescriptor Create(
        NodeKind kind,
        string typeKey,
        NodeTraitFlags traits,
        IReadOnlyList<PinDefinition> pins,
        bool canCreate,
        NodeRuntimeSupport runtimeSupport,
        bool canDelete = true,
        bool hasSerializer = true,
        NodePreviewSupport previewSupport = NodePreviewSupport.Emitter) =>
        new(kind, typeKey, traits, pins, canDelete, traits.HasFlag(NodeTraitFlags.Execution), canCreate, runtimeSupport, previewSupport, hasSerializer);

    private static NodeTraitFlags ExecutionTraits(NodeKind kind) =>
        NodeTraitFlags.Execution | NodeTraitFlags.ToDoTarget;

    private static PinDefinition InExec(string name = "exec_in", string label = "执行输入") =>
        new(name, label, PinKind.Execution, PinDirection.Input);

    private static PinDefinition OutExec(string name = "exec_out", string label = "执行输出") =>
        new(name, label, PinKind.Execution, PinDirection.Output);

    private static PinDefinition InBool(string name, string label) =>
        new(name, label, PinKind.Boolean, PinDirection.Input);

    private static PinDefinition OutBool(string name, string label) =>
        new(name, label, PinKind.Boolean, PinDirection.Output);

    private static PinDefinition InVector(string name, string label) =>
        new(name, label, PinKind.Vector2D, PinDirection.Input);

    private static PinDefinition OutVector(string name, string label) =>
        new(name, label, PinKind.Vector2D, PinDirection.Output);

    private static PinDefinition InString(string name, string label) =>
        new(name, label, PinKind.String, PinDirection.Input);

    private static PinDefinition OutString(string name, string label) =>
        new(name, label, PinKind.String, PinDirection.Output);
}
