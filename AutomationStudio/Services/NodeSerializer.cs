using AutomationStudioWpf.Graph;
using AutomationStudioWpf.GraphCore;
using AutomationStudioWpf.Interaction;
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
            NodeTypeKey = NodeDescriptorCatalog.TryGet(node.NodeKind, out var descriptor)
                ? descriptor.TypeKey
                : node.NodeTypeKey,
            Title = node.Title,
            NodeNumber = NodeTraits.ShouldAssignNodeNumber(node.NodeKind) ? node.NodeNumber : string.Empty,
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
                file.HasManualPosition = mouseNode.HasManualPosition;
                file.MouseButton = mouseNode.MouseButton.ToString();
                file.TriggerCount = mouseNode.TriggerCount;
                file.TriggerIntervalMs = mouseNode.TriggerIntervalMs;
                break;

            case KeyboardNodeViewModel keyboardNode:
                file.OperationMode = keyboardNode.OperationMode.ToString();
                file.Key = keyboardNode.Key;
                file.TriggerCount = keyboardNode.TriggerCount;
                file.TriggerIntervalMs = keyboardNode.TriggerIntervalMs;
                break;

            case KeyChordNodeViewModel keyChordNode:
                file.OperationMode = keyChordNode.OperationMode.ToString();
                file.Text = keyChordNode.Chord;
                file.TriggerCount = keyChordNode.TriggerCount;
                file.TriggerIntervalMs = keyChordNode.TriggerIntervalMs;
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
                file.HasManualPosition = moveNode.HasManualPosition;
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
                file.VariadicInputCount = commonNode.CanAddVariadicInput ? commonNode.VariadicInputCount : 0;
                file.VariadicInputDefaults = commonNode.CanAddVariadicInput
                    ? commonNode.VariadicInputDefaults.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                    : null;
                break;

            case MultiThreadNodeViewModel multiThreadNode:
                file.ThreadOutputCount = multiThreadNode.ThreadOutputCount;
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

        bool isLegacyDoubleClick = string.Equals(file.NodeTypeKey, "mouse_double_click", StringComparison.OrdinalIgnoreCase);
        NodeKind? serializedKind = NodeDescriptorCatalog.FromTypeKey(file.NodeTypeKey);
        NodeBaseViewModel? node = isLegacyDoubleClick
            ? CreateLegacyDoubleClickFromFile(file)
            : serializedKind switch
        {
            NodeKind.Start => new StartNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
            },

            NodeKind.FindImage => new FindImageNodeViewModel(file.Id)
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

            NodeKind.StartProgram => new StartProgramNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                ProgramPath = file.ProgramPath ?? file.ImagePath ?? string.Empty,
                WaitTimeoutMs = file.WaitTimeoutMs > 0 ? file.WaitTimeoutMs : (file.DelayMs > 0 ? file.DelayMs : 60000),
                FailureAction = Enum.TryParse<ProgramStartFailureAction>(file.FailureAction ?? file.ScrollAction, true, out var fa) ? fa : ProgramStartFailureAction.None,
                RetryCount = file.RetryCount > 0 ? file.RetryCount : (file.ScrollSpeed > 0 ? file.ScrollSpeed : 3),
            },

            NodeKind.MouseClick => new MouseClickNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                OperationMode = DeserializeOperationMode(file.OperationMode),
                MouseButton = Enum.TryParse<MouseButton>(file.MouseButton, true, out var button) ? button : MouseButton.Left,
                PositionX = file.PositionX,
                PositionY = file.PositionY,
                HasManualPosition = ResolveManualPosition(file, file.PositionX, file.PositionY),
                TriggerCount = NormalizeTriggerCount(file.TriggerCount),
                TriggerIntervalMs = NormalizeTriggerInterval(file.TriggerIntervalMs),
            },

            NodeKind.Keyboard => new KeyboardNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                OperationMode = Enum.TryParse<PressReleaseMode>(file.OperationMode, true, out var kbdMode) ? kbdMode : PressReleaseMode.Press,
                Key = file.Key ?? string.Empty,
                TriggerCount = NormalizeTriggerCount(file.TriggerCount),
                TriggerIntervalMs = NormalizeTriggerInterval(file.TriggerIntervalMs),
            },

            NodeKind.ScrollWheel => new ScrollWheelNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                ScrollAction = Enum.TryParse<ScrollWheelAction>(file.ScrollAction, true, out var sa) ? sa : ScrollWheelAction.ScrollForward,
                ScrollSpeed = file.ScrollSpeed > 0 ? file.ScrollSpeed : 120,
                ScrollInterval = file.ScrollInterval > 0 ? file.ScrollInterval : 100,
                ScrollDuration = Math.Max(0, file.ScrollDuration),
            },

            NodeKind.Reroute => CreateRerouteFromFile(new NodeFileModel
            {
                Id = file.Id,
                RoutedKind = file.RoutedKind,
                Title = file.Title,
                X = file.X,
                Y = file.Y,
            }),

            NodeKind.If => new IfNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                ConditionValue = file.ConditionValue,
            },

            NodeKind.ForLoop => new ForLoopNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                LoopCount = file.LoopCount > 0 ? file.LoopCount : 5,
                EndConditionValue = file.ConditionValue,
            },

            NodeKind.WhileLoop => new WhileLoopNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                NodeNumber = file.NodeNumber,
                ConditionValue = file.ConditionValue,
                LoopMode = Enum.TryParse<WhileLoopMode>(file.WhileLoopMode ?? file.ScrollAction, true, out var lm) ? lm : WhileLoopMode.Finite,
                MaxIterations = file.MaxIterations > 0 ? file.MaxIterations : (file.DelayMs > 0 ? file.DelayMs : 10000),
            },

            NodeKind.ToDo => new ToDoNodeViewModel(file.Id)
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

            NodeKind.MultiThread => new MultiThreadNodeViewModel(file.Id)
            {
                Title = string.IsNullOrWhiteSpace(file.Title) ? "多线程" : file.Title,
                NodeNumber = file.NodeNumber,
                X = file.X,
                Y = file.Y,
                ThreadOutputCount = file.ThreadOutputCount > 0 ? file.ThreadOutputCount : MultiThreadNodeViewModel.MinimumThreadOutputCount,
            },

            NodeKind.Delay => new DelayNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                DelayMs = file.DelayMs,
            },

            NodeKind.MouseMove => new MouseMoveNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                PositionX = file.PositionX,
                PositionY = file.PositionY,
                HasManualPosition = ResolveManualPosition(file, file.PositionX, file.PositionY),
            },

            NodeKind.PrintLog => new PrintLogNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                Message = file.PrintLogMessage ?? file.ImagePath ?? string.Empty,
            },

            NodeKind.SelectWindow => new SelectWindowNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                ProcessName = file.ProcessName ?? file.ImagePath ?? string.Empty,
                InputMode = Enum.TryParse<WindowInputMode>(file.WindowInputMode, true, out var mode) ? mode : WindowInputMode.Manual,
            },

            NodeKind.GetMousePosition => CreateCommonFromFile(file, NodeKind.GetMousePosition),
            NodeKind.KeyChord => new KeyChordNodeViewModel(file.Id)
            {
                Title = string.IsNullOrWhiteSpace(file.Title) ? "组合键" : file.Title,
                X = file.X,
                Y = file.Y,
                Chord = file.Text ?? string.Empty,
                OperationMode = Enum.TryParse<PressReleaseMode>(file.OperationMode, true, out var chordMode) ? chordMode : PressReleaseMode.Click,
                TriggerCount = NormalizeTriggerCount(file.TriggerCount),
                TriggerIntervalMs = NormalizeTriggerInterval(file.TriggerIntervalMs),
            },
            NodeKind.WaitImage => CreateCommonFromFile(file, NodeKind.WaitImage),
            NodeKind.WaitImageDisappear => CreateCommonFromFile(file, NodeKind.WaitImageDisappear),
            NodeKind.Compare => CreateCommonFromFile(file, NodeKind.Compare),
            NodeKind.BooleanAnd => CreateCommonFromFile(file, NodeKind.BooleanAnd),
            NodeKind.BooleanOr => CreateCommonFromFile(file, NodeKind.BooleanOr),
            NodeKind.BooleanNot => CreateCommonFromFile(file, NodeKind.BooleanNot),
            NodeKind.StringConcat => CreateCommonFromFile(file, NodeKind.StringConcat),
            NodeKind.WaitWindow => CreateCommonFromFile(file, NodeKind.WaitWindow),
            NodeKind.CloseWindow => CreateCommonFromFile(file, NodeKind.CloseWindow),
            NodeKind.WindowExists => CreateCommonFromFile(file, NodeKind.WindowExists),
            NodeKind.GetForegroundWindow => CreateCommonFromFile(file, NodeKind.GetForegroundWindow),
            NodeKind.SaveScreenshot => CreateCommonFromFile(file, NodeKind.SaveScreenshot),
            NodeKind.ShowMessage => CreateCommonFromFile(file, NodeKind.ShowMessage),
            NodeKind.FunctionEntry => CreateParameterNodeFromFile(new FunctionEntryNodeViewModel(file.Id), file, NodePresentationCatalog.Get(NodeKind.FunctionEntry).DisplayName),
            NodeKind.FunctionReturn => CreateParameterNodeFromFile(new FunctionReturnNodeViewModel(file.Id), file, NodePresentationCatalog.Get(NodeKind.FunctionReturn).DisplayName),
            NodeKind.FunctionCall => CreateFunctionCallFromFile(file),
            NodeKind.CustomEvent => CreateParameterNodeFromFile(new CustomEventNodeViewModel(file.Id, file.CustomEventId), file, NodePresentationCatalog.Get(NodeKind.CustomEvent).DisplayName),
            NodeKind.CustomEventCall => CreateCustomEventCallFromFile(file),

            _ => null,
        };

        if (node is not null)
        {
            node.Title = NormalizeLegacyDefaultTitle(node.NodeKind, node.Title);
            if (node is ToDoNodeViewModel toDoNode)
                toDoNode.TargetNodeTitle = NormalizeLegacyDefaultTitle(null, toDoNode.TargetNodeTitle);
            node.NodeNumber = NodeTraits.ShouldAssignNodeNumber(node.NodeKind) ? file.NodeNumber : string.Empty;
            node.RefreshDescription();
        }

        return node;
    }

    private static bool IsDiscardedNodeType(string? nodeTypeKey) =>
        nodeTypeKey is "mouse_drag" or "input_text" or "key_sequence" or
            "click_image_center" or "set_variable" or
            "macro_entry" or "macro_output" or "macro_call" ||
        NodeDescriptorCatalog.TryFromTypeKey(nodeTypeKey, out var kind) &&
        !NodeDescriptorCatalog.Get(kind).HasSerializer;

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
                mouseNode.PositionX, mouseNode.PositionY,
                mouseNode.HasManualPosition,
                mouseNode.TriggerCount, mouseNode.TriggerIntervalMs),

            KeyboardNodeViewModel keyboardNode => GraphRuntimeNode.ForKeyboard(
                keyboardNode.Id, keyboardNode.Title,
                keyboardNode.OperationMode, keyboardNode.Key,
                keyboardNode.TriggerCount, keyboardNode.TriggerIntervalMs),

            KeyChordNodeViewModel keyChordNode => GraphRuntimeNode.ForKeyChord(
                keyChordNode.Id, keyChordNode.Title,
                keyChordNode.Chord, keyChordNode.OperationMode,
                keyChordNode.TriggerCount, keyChordNode.TriggerIntervalMs),

            ScrollWheelNodeViewModel scrollNode => GraphRuntimeNode.ForScrollWheel(
                scrollNode.Id, scrollNode.Title,
                scrollNode.ScrollAction, scrollNode.ScrollSpeed,
                scrollNode.ScrollInterval, scrollNode.ScrollDuration),

            DelayNodeViewModel delayNode => GraphRuntimeNode.ForDelay(
                delayNode.Id, delayNode.Title, delayNode.DelayMs),

            MouseMoveNodeViewModel moveNode => GraphRuntimeNode.ForMouseMove(
                moveNode.Id, moveNode.Title,
                moveNode.PositionX, moveNode.PositionY,
                moveNode.HasManualPosition),

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

            MultiThreadNodeViewModel multiThreadNode => GraphRuntimeNode.ForMultiThread(
                multiThreadNode.Id,
                multiThreadNode.Title,
                multiThreadNode.ThreadOutputCount),

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
                commonNode.Flag,
                commonNode.VariadicInputCount,
                commonNode.VariadicInputDefaults),

            FunctionEntryNodeViewModel functionEntry => GraphRuntimeNode.ForAssetNode(functionEntry.Id, functionEntry.Title, functionEntry.NodeKind, functionEntry.Parameters),
            FunctionReturnNodeViewModel functionReturn => GraphRuntimeNode.ForAssetNode(functionReturn.Id, functionReturn.Title, functionReturn.NodeKind, functionReturn.Parameters),
            FunctionCallNodeViewModel functionCall => GraphRuntimeNode.ForFunctionCall(functionCall.Id, functionCall.Title, functionCall.FunctionId, functionCall.InputParameters, functionCall.OutputParameters),
            CustomEventNodeViewModel customEvent => GraphRuntimeNode.ForCustomEvent(customEvent.Id, customEvent.Title, customEvent.CustomEventId, customEvent.Parameters),
            CustomEventCallNodeViewModel customEventCall => GraphRuntimeNode.ForCustomEventCall(customEventCall.Id, customEventCall.Title, customEventCall.CustomEventId, customEventCall.InputParameters),

            _ => throw new InvalidOperationException($"不支持执行的节点类型: {node.GetType().Name}"),
        };

        return runtime with { NodeNumber = NodeTraits.ShouldAssignNodeNumber(node.NodeKind) ? node.NodeNumber : string.Empty };
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

    private static MouseClickNodeViewModel CreateLegacyDoubleClickFromFile(NodeFileModel file)
    {
        double positionX = Math.Abs(file.PositionX) > 0.001 ? file.PositionX : file.Number;
        double positionY = Math.Abs(file.PositionY) > 0.001 ? file.PositionY : file.Number2;
        return new MouseClickNodeViewModel(file.Id)
        {
            Title = "鼠标点击",
            X = file.X,
            Y = file.Y,
            OperationMode = PressReleaseMode.Click,
            MouseButton = MouseButton.Left,
            PositionX = positionX,
            PositionY = positionY,
            HasManualPosition = ResolveManualPosition(file, positionX, positionY),
            TriggerCount = 2,
            TriggerIntervalMs = 80,
        };
    }

    private static CommonNodeViewModel CreateCommonFromFile(NodeFileModel file, NodeKind kind)
    {
        string fallbackTitle = NodePresentationCatalog.Get(kind).DisplayName;
        var node = new CommonNodeViewModel(file.Id, kind, NodeDescriptorCatalog.Get(kind).TypeKey, fallbackTitle)
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
        if (node.CanAddVariadicInput)
        {
            node.VariadicInputCount = file.VariadicInputCount > 0 ? file.VariadicInputCount : 2;
            node.LoadVariadicInputDefaults(file.VariadicInputDefaults);
        }
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
        var node = new FunctionCallNodeViewModel(file.Id, file.FunctionId ?? string.Empty, NormalizeCallableTitle(file.Title, "调用函数"))
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

    private static string NormalizeCallableTitle(string? title, string fallbackTitle)
    {
        if (string.IsNullOrWhiteSpace(title))
            return fallbackTitle;

        int slashIndex = title.LastIndexOf('/');
        return slashIndex >= 0 && slashIndex < title.Length - 1
            ? title[(slashIndex + 1)..]
            : title;
    }

    private static string NormalizeLegacyDefaultTitle(NodeKind? kind, string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        return title switch
        {
            "事件开始运行" => "开始运行",
            "找图节点" => "找图",
            "鼠标点击节点" => "鼠标点击",
            "鼠标移动节点" => "鼠标移动",
            "键盘节点" => "键盘",
            "鼠标滚轮节点" => "鼠标滚轮",
            "延迟节点" => "延迟",
            "分支节点" => "分支",
            "For循环节点" => "For循环",
            _ when kind == NodeKind.FunctionCall => NormalizeCallableTitle(title, "调用函数"),
            _ => title,
        };
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

    private static int NormalizeTriggerCount(int count) =>
        count < 0 ? 1 : count;

    private static int NormalizeTriggerInterval(int intervalMs) =>
        intervalMs > 0 ? intervalMs : 1000;

    private static bool ResolveManualPosition(NodeFileModel file, double x, double y) =>
        file.HasManualPosition ?? HasNonZeroPosition(x, y);

    private static bool HasNonZeroPosition(double x, double y) =>
        Math.Abs(x) > 0.001 || Math.Abs(y) > 0.001;
}
