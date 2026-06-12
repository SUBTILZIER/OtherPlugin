using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Runtime;

namespace AutomationStudioWpf.Services;

/// <summary>
/// 节点序列化器 - 负责节点与文件模型/运行时模型之间的转换
/// </summary>
public static class NodeSerializer
{
    public static NodeFileModel ToFileModel(NodeBaseViewModel node)
    {
        var file = new NodeFileModel
        {
            Id = node.Id,
            NodeTypeKey = node.NodeTypeKey,
            Title = node.Title,
            NodeNumber = node.NodeNumber,
            X = node.X,
            Y = node.Y,
        };

        switch (node)
        {
            case FindImageNodeViewModel findImage:
                file.ImagePath = findImage.ImagePath;
                file.SourceImagePath = findImage.SourceImagePath;
                file.ImageSearchSourceMode = findImage.SourceMode.ToString();
                file.SimilarityThresholdPercent = findImage.SimilarityThresholdPercent;
                file.UseFindImageRegion = findImage.UseRegion;
                file.FindImageRegionX = findImage.RegionX;
                file.FindImageRegionY = findImage.RegionY;
                file.FindImageRegionWidth = findImage.RegionWidth;
                file.FindImageRegionHeight = findImage.RegionHeight;
                break;

            case StartProgramNodeViewModel startProg:
                file.ProgramPath = startProg.ProgramPath;
                file.WaitTimeoutMs = startProg.WaitTimeoutMs;
                file.FailureAction = startProg.FailureAction.ToString();
                file.RetryCount = startProg.RetryCount;
                break;

            case MouseClickNodeViewModel mouseNode:
                file.OperationMode = mouseNode.OperationMode.ToString();
                file.PositionX = mouseNode.PositionX;
                file.PositionY = mouseNode.PositionY;
                file.MouseButton = mouseNode.MouseButton.ToString();
                break;

            case KeyboardNodeViewModel keyboardNode:
                file.OperationMode = keyboardNode.OperationMode.ToString();
                file.Key = keyboardNode.Key;
                break;

            case ScrollWheelNodeViewModel scrollNode:
                file.ScrollAction = scrollNode.ScrollAction.ToString();
                file.ScrollSpeed = scrollNode.ScrollSpeed;
                file.ScrollInterval = scrollNode.ScrollInterval;
                file.ScrollDuration = scrollNode.ScrollDuration;
                break;

            case RerouteNodeViewModel rerouteNode:
                file.RoutedKind = rerouteNode.RoutedKind.ToString();
                break;

            case IfNodeViewModel ifNode:
                file.ConditionValue = ifNode.ConditionValue;
                break;

            case WhileLoopNodeViewModel whileNode:
                file.ConditionValue = whileNode.ConditionValue;
                file.WhileLoopMode = whileNode.LoopMode.ToString();
                file.MaxIterations = whileNode.MaxIterations;
                break;

            case ToDoNodeViewModel toDoNode:
                file.TargetNodeTitle = toDoNode.TargetNodeTitle;
                file.TargetNodeNumber = toDoNode.TargetNodeNumber;
                file.TargetNodeId = toDoNode.TargetNodeId;
                file.ReturnAfterTarget = toDoNode.ReturnAfterTarget;
                break;

            case ForLoopNodeViewModel forNode:
                file.LoopCount = forNode.LoopCount;
                file.ConditionValue = forNode.EndConditionValue;
                break;

            case DelayNodeViewModel delayNode:
                file.DelayMs = delayNode.DelayMs;
                break;

            case MouseMoveNodeViewModel moveNode:
                file.PositionX = moveNode.PositionX;
                file.PositionY = moveNode.PositionY;
                break;

            case PrintLogNodeViewModel printNode:
                file.PrintLogMessage = printNode.Message;
                break;

            case SelectWindowNodeViewModel selectWindowNode:
                file.ProcessName = selectWindowNode.ProcessName;
                file.WindowInputMode = selectWindowNode.InputMode.ToString();
                break;

            case CommonNodeViewModel commonNode:
                file.Text = commonNode.Text;
                file.Text2 = commonNode.Text2;
                file.Text3 = commonNode.Text3;
                file.Number = commonNode.Number;
                file.Number2 = commonNode.Number2;
                file.Number3 = commonNode.Number3;
                file.Number4 = commonNode.Number4;
                file.Flag = commonNode.Flag;
                break;

            case ParameterNodeBaseViewModel parameterNode:
                file.Parameters = parameterNode.Parameters.Select(ToParameterFile).ToList();
                if (parameterNode is CustomEventNodeViewModel customEvent)
                    file.CustomEventId = customEvent.CustomEventId;
                break;

            case FunctionCallNodeViewModel functionCall:
                file.FunctionId = functionCall.FunctionId;
                file.InputParameters = functionCall.InputParameters.Select(ToParameterFile).ToList();
                file.OutputParameters = functionCall.OutputParameters.Select(ToParameterFile).ToList();
                break;

            case CustomEventCallNodeViewModel customEventCall:
                file.CustomEventId = customEventCall.CustomEventId;
                file.InputParameters = customEventCall.InputParameters.Select(ToParameterFile).ToList();
                break;
        }

        return file;
    }

    public static NodeBaseViewModel? FromFileModel(NodeFileModel file)
    {
        if (IsDiscardedNodeType(file.NodeTypeKey))
        {
            Logger.Warn($"旧图谱包含已删除节点，已跳过：{file.Title} ({file.NodeTypeKey})");
            return null;
        }

        NodeBaseViewModel? node = file.NodeTypeKey switch
        {
            "start" => new StartNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
            },

            "find_image" => new FindImageNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                ImagePath = file.ImagePath ?? string.Empty,
                SourceImagePath = file.SourceImagePath ?? string.Empty,
                SourceMode = Enum.TryParse<ImageSearchSourceMode>(file.ImageSearchSourceMode, true, out var sourceMode)
                    ? sourceMode
                    : ImageSearchSourceMode.RealtimeScreenshot,
                SimilarityThresholdPercent = file.SimilarityThresholdPercent,
                UseRegion = file.UseFindImageRegion,
                RegionX = file.FindImageRegionX,
                RegionY = file.FindImageRegionY,
                RegionWidth = file.FindImageRegionWidth,
                RegionHeight = file.FindImageRegionHeight,
            },

            "start_program" => new StartProgramNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                ProgramPath = file.ProgramPath ?? file.ImagePath ?? string.Empty,
                WaitTimeoutMs = file.WaitTimeoutMs > 0 ? file.WaitTimeoutMs : (file.DelayMs > 0 ? file.DelayMs : 60000),
                FailureAction = Enum.TryParse<ProgramStartFailureAction>(file.FailureAction ?? file.ScrollAction, true, out var fa) ? fa : ProgramStartFailureAction.None,
                RetryCount = file.RetryCount > 0 ? file.RetryCount : (file.ScrollSpeed > 0 ? file.ScrollSpeed : 3),
            },

            "mouse_left_click" or "mouse_click" => new MouseClickNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                OperationMode = DeserializeOperationMode(file.OperationMode),
                MouseButton = Enum.TryParse<MouseButton>(file.MouseButton, true, out var button) ? button : MouseButton.Left,
                PositionX = file.PositionX,
                PositionY = file.PositionY,
            },

            "keyboard" => new KeyboardNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                OperationMode = Enum.TryParse<PressReleaseMode>(file.OperationMode, true, out var kbdMode) ? kbdMode : PressReleaseMode.Press,
                Key = file.Key ?? "A",
            },

            "scroll_wheel" => new ScrollWheelNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                ScrollAction = Enum.TryParse<ScrollWheelAction>(file.ScrollAction, true, out var sa) ? sa : ScrollWheelAction.ScrollForward,
                ScrollSpeed = file.ScrollSpeed > 0 ? file.ScrollSpeed : 120,
                ScrollInterval = file.ScrollInterval > 0 ? file.ScrollInterval : 100,
                ScrollDuration = file.ScrollDuration > 0 ? file.ScrollDuration : 1000,
            },

            "reroute" => CreateRerouteFromFile(new NodeFileModel
            {
                Id = file.Id,
                RoutedKind = file.RoutedKind,
                Title = file.Title,
                X = file.X,
                Y = file.Y,
            }),

            "if" => new IfNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                ConditionValue = file.ConditionValue,
            },

            "for_loop" => new ForLoopNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                LoopCount = file.LoopCount > 0 ? file.LoopCount : 5,
                EndConditionValue = file.ConditionValue,
            },

            "while_loop" => new WhileLoopNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                NodeNumber = file.NodeNumber,
                ConditionValue = file.ConditionValue,
                LoopMode = Enum.TryParse<WhileLoopMode>(file.WhileLoopMode ?? file.ScrollAction, true, out var lm) ? lm : WhileLoopMode.Finite,
                MaxIterations = file.MaxIterations > 0 ? file.MaxIterations : (file.DelayMs > 0 ? file.DelayMs : 10000),
            },

            "todo" => new ToDoNodeViewModel(file.Id)
            {
                Title = string.IsNullOrWhiteSpace(file.Title) ? "ToDo跳转" : file.Title,
                NodeNumber = file.NodeNumber,
                X = file.X,
                Y = file.Y,
                TargetNodeTitle = file.TargetNodeTitle ?? string.Empty,
                TargetNodeNumber = file.TargetNodeNumber ?? string.Empty,
                TargetNodeId = file.TargetNodeId,
                ReturnAfterTarget = file.ReturnAfterTarget,
            },

            "delay" => new DelayNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                DelayMs = file.DelayMs,
            },

            "mouse_move" => new MouseMoveNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                PositionX = file.PositionX,
                PositionY = file.PositionY,
            },

            "print_log" => new PrintLogNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                Message = file.PrintLogMessage ?? file.ImagePath ?? string.Empty,
            },

            "select_window" => new SelectWindowNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                ProcessName = file.ProcessName ?? file.ImagePath ?? string.Empty,
                InputMode = Enum.TryParse<WindowInputMode>(file.WindowInputMode, true, out var mode) ? mode : WindowInputMode.Manual,
            },

            "mouse_double_click" => CreateCommonFromFile(file, NodeKind.MouseDoubleClick, "鼠标双击"),
            "get_mouse_position" => CreateCommonFromFile(file, NodeKind.GetMousePosition, "获取鼠标位置"),
            "key_chord" => CreateCommonFromFile(file, NodeKind.KeyChord, "组合键"),
            "wait_image" => CreateCommonFromFile(file, NodeKind.WaitImage, "等待图片"),
            "wait_image_disappear" => CreateCommonFromFile(file, NodeKind.WaitImageDisappear, "图片消失"),
            "compare" => CreateCommonFromFile(file, NodeKind.Compare, "比较"),
            "boolean_and" => CreateCommonFromFile(file, NodeKind.BooleanAnd, "布尔与"),
            "boolean_or" => CreateCommonFromFile(file, NodeKind.BooleanOr, "布尔或"),
            "boolean_not" => CreateCommonFromFile(file, NodeKind.BooleanNot, "布尔非"),
            "string_concat" => CreateCommonFromFile(file, NodeKind.StringConcat, "字符串拼接"),
            "wait_window" => CreateCommonFromFile(file, NodeKind.WaitWindow, "等待窗口"),
            "close_window" => CreateCommonFromFile(file, NodeKind.CloseWindow, "关闭窗口"),
            "window_exists" => CreateCommonFromFile(file, NodeKind.WindowExists, "窗口是否存在"),
            "get_foreground_window" => CreateCommonFromFile(file, NodeKind.GetForegroundWindow, "获取前台窗口"),
            "save_screenshot" => CreateCommonFromFile(file, NodeKind.SaveScreenshot, "截图"),
            "show_message" => CreateCommonFromFile(file, NodeKind.ShowMessage, "弹窗提示"),
            "function_entry" => CreateParameterNodeFromFile(new FunctionEntryNodeViewModel(file.Id), file, "函数开始"),
            "function_return" => CreateParameterNodeFromFile(new FunctionReturnNodeViewModel(file.Id), file, "函数返回"),
            "function_call" => CreateFunctionCallFromFile(file),
            "custom_event" => CreateParameterNodeFromFile(new CustomEventNodeViewModel(file.Id, file.CustomEventId), file, "自定义事件"),
            "custom_event_call" => CreateCustomEventCallFromFile(file),

            _ => null,
        };

        if (node is not null)
        {
            node.NodeNumber = file.NodeNumber;
            node.RefreshDescription();
        }

        return node;
    }

    private static bool IsDiscardedNodeType(string? nodeTypeKey) =>
        nodeTypeKey is "mouse_drag" or "input_text" or "key_sequence" or
            "click_image_center" or "set_variable" or "comment" or
            "macro_entry" or "macro_output" or "macro_call";

    public static GraphRuntimeNode ToRuntimeNode(NodeBaseViewModel node)
    {
        GraphRuntimeNode runtime = node switch
        {
            StartNodeViewModel startNode => GraphRuntimeNode.ForStart(startNode.Id, startNode.Title),

            FindImageNodeViewModel findImageNode => GraphRuntimeNode.ForFindImage(
                findImageNode.Id, findImageNode.Title,
                findImageNode.ImagePath,
                findImageNode.SourceImagePath,
                findImageNode.SourceMode,
                findImageNode.SimilarityThresholdPercent,
                findImageNode.UseRegion,
                findImageNode.RegionX,
                findImageNode.RegionY,
                findImageNode.RegionWidth,
                findImageNode.RegionHeight),

            StartProgramNodeViewModel startProg => GraphRuntimeNode.ForStartProgram(
                startProg.Id, startProg.Title,
                startProg.ProgramPath, startProg.WaitTimeoutMs,
                startProg.FailureAction, startProg.RetryCount),

            MouseClickNodeViewModel mouseNode => GraphRuntimeNode.ForMouseClick(
                mouseNode.Id, mouseNode.Title,
                mouseNode.OperationMode, mouseNode.MouseButton,
                mouseNode.PositionX, mouseNode.PositionY),

            KeyboardNodeViewModel keyboardNode => GraphRuntimeNode.ForKeyboard(
                keyboardNode.Id, keyboardNode.Title,
                keyboardNode.OperationMode, keyboardNode.Key),

            ScrollWheelNodeViewModel scrollNode => GraphRuntimeNode.ForScrollWheel(
                scrollNode.Id, scrollNode.Title,
                scrollNode.ScrollAction, scrollNode.ScrollSpeed,
                scrollNode.ScrollInterval, scrollNode.ScrollDuration),

            DelayNodeViewModel delayNode => GraphRuntimeNode.ForDelay(
                delayNode.Id, delayNode.Title, delayNode.DelayMs),

            MouseMoveNodeViewModel moveNode => GraphRuntimeNode.ForMouseMove(
                moveNode.Id, moveNode.Title,
                moveNode.PositionX, moveNode.PositionY),

            RerouteNodeViewModel rerouteNode => GraphRuntimeNode.ForReroute(rerouteNode.Id, rerouteNode.Title, rerouteNode.RoutedKind),

            IfNodeViewModel ifNode => GraphRuntimeNode.ForIf(ifNode.Id, ifNode.Title, ifNode.ConditionValue),

            ForLoopNodeViewModel forNode => GraphRuntimeNode.ForForLoop(
                forNode.Id, forNode.Title, forNode.LoopCount, forNode.EndConditionValue),

            WhileLoopNodeViewModel whileNode => GraphRuntimeNode.ForWhileLoop(
                whileNode.Id, whileNode.Title, whileNode.ConditionValue, whileNode.LoopMode, whileNode.MaxIterations),

            ToDoNodeViewModel toDoNode => GraphRuntimeNode.ForToDo(
                toDoNode.Id,
                toDoNode.Title,
                toDoNode.TargetNodeTitle,
                toDoNode.TargetNodeNumber,
                toDoNode.TargetNodeId,
                toDoNode.ReturnAfterTarget),

            PrintLogNodeViewModel printNode => GraphRuntimeNode.ForPrintLog(printNode.Id, printNode.Title, printNode.Message),

            SelectWindowNodeViewModel selectWindowNode => GraphRuntimeNode.ForSelectWindow(
                selectWindowNode.Id, selectWindowNode.Title, selectWindowNode.ProcessName),

            CommonNodeViewModel commonNode => GraphRuntimeNode.ForCommon(
                commonNode.Id,
                commonNode.Title,
                commonNode.NodeKind,
                commonNode.Text,
                commonNode.Text2,
                commonNode.Text3,
                commonNode.Number,
                commonNode.Number2,
                commonNode.Number3,
                commonNode.Number4,
                commonNode.Flag),

            FunctionEntryNodeViewModel functionEntry => GraphRuntimeNode.ForAssetNode(functionEntry.Id, functionEntry.Title, functionEntry.NodeKind, functionEntry.Parameters),
            FunctionReturnNodeViewModel functionReturn => GraphRuntimeNode.ForAssetNode(functionReturn.Id, functionReturn.Title, functionReturn.NodeKind, functionReturn.Parameters),
            FunctionCallNodeViewModel functionCall => GraphRuntimeNode.ForFunctionCall(functionCall.Id, functionCall.Title, functionCall.FunctionId, functionCall.InputParameters),
            CustomEventNodeViewModel customEvent => GraphRuntimeNode.ForCustomEvent(customEvent.Id, customEvent.Title, customEvent.CustomEventId, customEvent.Parameters),
            CustomEventCallNodeViewModel customEventCall => GraphRuntimeNode.ForCustomEventCall(customEventCall.Id, customEventCall.Title, customEventCall.CustomEventId, customEventCall.InputParameters),

            _ => throw new InvalidOperationException($"不支持执行的节点类型: {node.GetType().Name}"),
        };

        return runtime with { NodeNumber = node.NodeNumber };
    }

    private static RerouteNodeViewModel CreateRerouteFromFile(NodeFileModel file)
    {
        var kind = Enum.TryParse<PinKind>(file.RoutedKind, true, out var pk) ? pk : PinKind.Execution;
        return new RerouteNodeViewModel(file.Id, kind)
        {
            Title = file.Title,
            X = file.X,
            Y = file.Y,
        };
    }

    private static CommonNodeViewModel CreateCommonFromFile(NodeFileModel file, NodeKind kind, string fallbackTitle)
    {
        var node = new CommonNodeViewModel(file.Id, kind, file.NodeTypeKey, fallbackTitle)
        {
            Title = string.IsNullOrWhiteSpace(file.Title) ? fallbackTitle : file.Title,
            X = file.X,
            Y = file.Y,
            Text = file.Text ?? string.Empty,
            Text2 = string.IsNullOrWhiteSpace(file.Text2) && kind is NodeKind.WaitImage or NodeKind.WaitImageDisappear
                ? ImageSearchSourceMode.RealtimeScreenshot.ToString()
                : string.IsNullOrWhiteSpace(file.Text2) && kind == NodeKind.SaveScreenshot
                    ? "Auto"
                : file.Text2 ?? string.Empty,
            Text3 = file.Text3 ?? string.Empty,
            Number = file.Number,
            Number2 = file.Number2,
            Number3 = file.Number3,
            Number4 = file.Number4,
            Flag = file.Flag,
        };
        return node;
    }

    private static T CreateParameterNodeFromFile<T>(T node, NodeFileModel file, string fallbackTitle)
        where T : ParameterNodeBaseViewModel
    {
        node.Title = string.IsNullOrWhiteSpace(file.Title) ? fallbackTitle : file.Title;
        node.X = file.X;
        node.Y = file.Y;
        node.Parameters.Clear();
        foreach (var parameter in file.Parameters)
            node.Parameters.Add(FromParameterFile(parameter));
        node.SyncPins();
        return node;
    }

    private static FunctionCallNodeViewModel CreateFunctionCallFromFile(NodeFileModel file)
    {
        var node = new FunctionCallNodeViewModel(file.Id, file.FunctionId ?? string.Empty, string.IsNullOrWhiteSpace(file.Title) ? "调用函数" : file.Title)
        {
            X = file.X,
            Y = file.Y,
        };
        node.ConfigurePins(file.InputParameters.Select(FromParameterFile), file.OutputParameters.Select(FromParameterFile));
        return node;
    }

    private static CustomEventCallNodeViewModel CreateCustomEventCallFromFile(NodeFileModel file)
    {
        var node = new CustomEventCallNodeViewModel(file.Id, file.CustomEventId ?? string.Empty, string.IsNullOrWhiteSpace(file.Title) ? "调用自定义事件" : file.Title)
        {
            X = file.X,
            Y = file.Y,
        };
        node.ConfigurePins(file.InputParameters.Select(FromParameterFile));
        return node;
    }

    private static GraphParameterFileModel ToParameterFile(GraphParameterDefinition parameter) => new()
    {
        Id = parameter.Id,
        Name = parameter.Name,
        Type = parameter.Type,
        DefaultValue = parameter.DefaultValue,
    };

    private static GraphParameterDefinition FromParameterFile(GraphParameterFileModel parameter) => new()
    {
        Id = string.IsNullOrWhiteSpace(parameter.Id) ? Guid.NewGuid().ToString("N") : parameter.Id,
        Name = string.IsNullOrWhiteSpace(parameter.Name) ? "NewParam" : parameter.Name,
        Type = parameter.Type,
        DefaultValue = string.IsNullOrWhiteSpace(parameter.DefaultValue)
            ? GraphParameterDefinition.DefaultValueForType(parameter.Type)
            : parameter.DefaultValue,
    };

    private static List<GraphParameterFileModel> PinsToParameterFiles(IEnumerable<PinViewModel> pins) =>
        pins.Select(pin => new GraphParameterFileModel
        {
            Id = pin.Name,
            Name = pin.DisplayName,
            Type = pin.Kind switch
            {
                PinKind.Boolean => GraphParameterType.Boolean,
                PinKind.Vector2D => GraphParameterType.Vector2D,
                _ => GraphParameterType.String,
            },
            DefaultValue = GraphParameterDefinition.DefaultValueForType(pin.Kind switch
            {
                PinKind.Boolean => GraphParameterType.Boolean,
                PinKind.Vector2D => GraphParameterType.Vector2D,
                _ => GraphParameterType.String,
            }),
        }).ToList();

    private static PressReleaseMode DeserializeOperationMode(string? mode)
    {
        if (Enum.TryParse<PressReleaseMode>(mode, true, out var result))
            return result;
        return PressReleaseMode.Press;
    }
}
