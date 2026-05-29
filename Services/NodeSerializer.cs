using AutomationStudioWpf.Graph;
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
            X = node.X,
            Y = node.Y,
        };

        switch (node)
        {
            case FindImageNodeViewModel findImage:
                file.ImagePath = findImage.ImagePath;
                file.SimilarityThresholdPercent = findImage.SimilarityThresholdPercent;
                break;

            case StartProgramNodeViewModel startProg:
                file.ImagePath = startProg.ProgramPath;
                file.DelayMs = startProg.WaitTimeoutMs;
                file.ScrollAction = startProg.FailureAction.ToString();
                file.ScrollSpeed = startProg.RetryCount;
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
                file.ScrollAction = whileNode.LoopMode.ToString();
                file.DelayMs = whileNode.MaxIterations;
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
                file.ImagePath = printNode.Message;
                break;

            case SelectWindowNodeViewModel selectWindowNode:
                file.ProcessName = selectWindowNode.ProcessName;
                break;

            case FindTextNodeViewModel findTextNode:
                file.ImagePath = findTextNode.Text;
                file.SimilarityThresholdPercent = findTextNode.SimilarityThresholdPercent;
                break;
        }

        return file;
    }

    public static NodeBaseViewModel? FromFileModel(NodeFileModel file)
    {
        return file.NodeTypeKey switch
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
                SimilarityThresholdPercent = file.SimilarityThresholdPercent,
            },

            "find_text" => new FindTextNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                Text = file.ImagePath ?? string.Empty,
                SimilarityThresholdPercent = file.SimilarityThresholdPercent,
            },

            "start_program" => new StartProgramNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                ProgramPath = file.ImagePath ?? string.Empty,
                WaitTimeoutMs = file.DelayMs > 0 ? file.DelayMs : 60000,
                FailureAction = Enum.TryParse<ProgramStartFailureAction>(file.ScrollAction, true, out var fa) ? fa : ProgramStartFailureAction.None,
                RetryCount = file.ScrollSpeed > 0 ? file.ScrollSpeed : 3,
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
                ConditionValue = file.ConditionValue,
                LoopMode = Enum.TryParse<WhileLoopMode>(file.ScrollAction, true, out var lm) ? lm : WhileLoopMode.Finite,
                MaxIterations = file.DelayMs > 0 ? file.DelayMs : 10000,
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
                Message = file.ImagePath ?? string.Empty,
            },

            "select_window" => new SelectWindowNodeViewModel(file.Id)
            {
                Title = file.Title,
                X = file.X,
                Y = file.Y,
                ProcessName = file.ProcessName ?? file.ImagePath ?? string.Empty,
            },

            _ => null,
        };
    }

    public static GraphRuntimeNode ToRuntimeNode(NodeBaseViewModel node)
    {
        return node switch
        {
            StartNodeViewModel startNode => GraphRuntimeNode.ForStart(startNode.Id, startNode.Title),

            FindImageNodeViewModel findImageNode => GraphRuntimeNode.ForFindImage(
                findImageNode.Id, findImageNode.Title,
                findImageNode.ImagePath, findImageNode.SimilarityThresholdPercent),

            FindTextNodeViewModel findTextNode => GraphRuntimeNode.ForFindText(
                findTextNode.Id, findTextNode.Title,
                findTextNode.Text, findTextNode.SimilarityThresholdPercent),

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

            PrintLogNodeViewModel printNode => GraphRuntimeNode.ForPrintLog(printNode.Id, printNode.Title, printNode.Message),

            SelectWindowNodeViewModel selectWindowNode => GraphRuntimeNode.ForSelectWindow(
                selectWindowNode.Id, selectWindowNode.Title, selectWindowNode.ProcessName),

            _ => throw new InvalidOperationException($"不支持执行的节点类型: {node.GetType().Name}"),
        };
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

    private static PressReleaseMode DeserializeOperationMode(string? mode)
    {
        if (Enum.TryParse<PressReleaseMode>(mode, true, out var result))
            return result;
        return PressReleaseMode.Press;
    }
}
