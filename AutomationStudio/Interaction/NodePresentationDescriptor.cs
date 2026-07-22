using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Interaction;

public enum NodeInspectorKind
{
    Common,
    FindImage,
    MouseClick,
    MouseMove,
    Keyboard,
    ScrollWheel,
    Delay,
    If,
    ForLoop,
    WhileLoop,
    ToDo,
    StartProgram,
    SelectWindow,
    ParameterizedEntry,
    Callable,
}

public sealed record NodePresentationDescriptor(
    NodeKind Kind,
    string DisplayName,
    string Category,
    NodeInspectorKind InspectorKind,
    string ThemeResourceKey);

public static class NodePresentationCatalog
{
    private static readonly IReadOnlyList<NodePresentationDescriptor> PresentationList =
    [
        Item(NodeKind.Start, "开始运行", "核心", NodeInspectorKind.Common, "NodeHeader.Start"),
        Item(NodeKind.FindImage, "找图", "插件/图像识别", NodeInspectorKind.FindImage, "NodeHeader.Image"),
        Item(NodeKind.MouseClick, "鼠标点击", "输入/鼠标", NodeInspectorKind.MouseClick, "NodeHeader.Mouse"),
        Item(NodeKind.Delay, "延迟", "核心", NodeInspectorKind.Delay, "NodeHeader.Flow"),
        Item(NodeKind.MouseMove, "鼠标移动", "输入/鼠标", NodeInspectorKind.MouseMove, "NodeHeader.Mouse"),
        Item(NodeKind.Keyboard, "键盘", "输入/键盘", NodeInspectorKind.Keyboard, "NodeHeader.Keyboard"),
        Item(NodeKind.ScrollWheel, "鼠标滚轮", "输入/鼠标", NodeInspectorKind.ScrollWheel, "NodeHeader.Mouse"),
        Item(NodeKind.Reroute, "转接点", "核心", NodeInspectorKind.Common, "NodeHeader.Reroute"),
        Item(NodeKind.If, "分支", "核心", NodeInspectorKind.If, "NodeHeader.Flow"),
        Item(NodeKind.ForLoop, "For循环", "核心", NodeInspectorKind.ForLoop, "NodeHeader.Flow"),
        Item(NodeKind.WhileLoop, "While循环", "核心", NodeInspectorKind.WhileLoop, "NodeHeader.Flow"),
        Item(NodeKind.ToDo, "ToDo跳转", "核心", NodeInspectorKind.ToDo, "NodeHeader.Flow"),
        Item(NodeKind.StartProgram, "启动程序", "系统/窗口", NodeInspectorKind.StartProgram, "NodeHeader.System"),
        Item(NodeKind.PrintLog, "打印log", "调试", NodeInspectorKind.Common, "NodeHeader.Debug"),
        Item(NodeKind.SelectWindow, "选中窗口", "系统/窗口", NodeInspectorKind.SelectWindow, "NodeHeader.Window"),
        Item(NodeKind.GetMousePosition, "获取鼠标位置", "输入/鼠标", NodeInspectorKind.Common, "NodeHeader.Mouse"),
        Item(NodeKind.KeyChord, "组合键", "输入/键盘", NodeInspectorKind.Common, "NodeHeader.Keyboard"),
        Item(NodeKind.WaitImage, "等待图片", "插件/图像识别", NodeInspectorKind.Common, "NodeHeader.Image"),
        Item(NodeKind.WaitImageDisappear, "图片消失", "插件/图像识别", NodeInspectorKind.Common, "NodeHeader.Image"),
        Item(NodeKind.Compare, "比较", "逻辑/判断", NodeInspectorKind.Common, "NodeHeader.Logic"),
        Item(NodeKind.BooleanAnd, "布尔与", "逻辑/布尔", NodeInspectorKind.Common, "NodeHeader.Logic"),
        Item(NodeKind.BooleanOr, "布尔或", "逻辑/布尔", NodeInspectorKind.Common, "NodeHeader.Logic"),
        Item(NodeKind.BooleanNot, "布尔非", "逻辑/布尔", NodeInspectorKind.Common, "NodeHeader.Logic"),
        Item(NodeKind.StringConcat, "字符串拼接", "逻辑/字符串", NodeInspectorKind.Common, "NodeHeader.Logic"),
        Item(NodeKind.WaitWindow, "等待窗口", "系统/窗口", NodeInspectorKind.Common, "NodeHeader.Window"),
        Item(NodeKind.CloseWindow, "关闭窗口", "系统/窗口", NodeInspectorKind.Common, "NodeHeader.Window"),
        Item(NodeKind.WindowExists, "窗口是否存在", "系统/窗口", NodeInspectorKind.Common, "NodeHeader.Window"),
        Item(NodeKind.GetForegroundWindow, "获取前台窗口", "系统/窗口", NodeInspectorKind.Common, "NodeHeader.Window"),
        Item(NodeKind.Comment, "注释", "编辑器", NodeInspectorKind.Common, "NodeHeader.Comment"),
        Item(NodeKind.SaveScreenshot, "截图", "插件/图像识别", NodeInspectorKind.Common, "NodeHeader.Image"),
        Item(NodeKind.ShowMessage, "弹窗提示", "调试", NodeInspectorKind.Common, "NodeHeader.Debug"),
        Item(NodeKind.FunctionEntry, "函数开始", "自定义函数", NodeInspectorKind.ParameterizedEntry, "NodeHeader.Function"),
        Item(NodeKind.FunctionReturn, "函数返回", "自定义函数", NodeInspectorKind.ParameterizedEntry, "NodeHeader.Function"),
        Item(NodeKind.FunctionCall, "函数调用", "自定义函数", NodeInspectorKind.Callable, "NodeHeader.Function"),
        Item(NodeKind.CustomEvent, "自定义事件", "事件", NodeInspectorKind.Callable, "NodeHeader.Event"),
        Item(NodeKind.CustomEventCall, "调用自定义事件", "事件", NodeInspectorKind.Callable, "NodeHeader.Event"),
        Item(NodeKind.MultiThread, "多线程", "核心", NodeInspectorKind.Common, "NodeHeader.Flow"),
    ];

    private static readonly IReadOnlyDictionary<NodeKind, NodePresentationDescriptor> ByKind =
        PresentationList.ToDictionary(item => item.Kind);

    public static IReadOnlyList<NodePresentationDescriptor> All => PresentationList;

    public static NodePresentationDescriptor Get(NodeKind kind) =>
        ByKind.TryGetValue(kind, out var descriptor)
            ? descriptor
            : throw new KeyNotFoundException($"未注册节点展示描述：{kind}");

    public static bool TryGet(NodeKind kind, out NodePresentationDescriptor descriptor) =>
        ByKind.TryGetValue(kind, out descriptor!);

    private static NodePresentationDescriptor Item(
        NodeKind kind,
        string displayName,
        string category,
        NodeInspectorKind inspectorKind,
        string themeResourceKey) =>
        new(kind, displayName, category, inspectorKind, themeResourceKey);
}
