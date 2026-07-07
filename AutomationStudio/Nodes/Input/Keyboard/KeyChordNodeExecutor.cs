using AutomationStudioWpf.Adapters;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Nodes.Input;
using AutomationStudioWpf.Runtime;

namespace AutomationStudioWpf.Nodes.Input.Keyboard;

public sealed class KeyChordNodeExecutor : INodeExecutor
{
    private const int ClickHoldMs = 50;

    public NodeKind NodeKind => NodeKind.KeyChord;

    public NodeExecutionResult Execute(NodeExecutionRequest request)
    {
        string chord = request.Node.Text ?? string.Empty;
        string[] keys = SplitChord(chord);
        if (keys.Length == 0)
        {
            request.Context.Set(request.Node.Id, "result", false);
            Logger.Warn("组合键：未设置按键。");
            return NodeExecutionResult.Warn($"已跳过节点：{request.Node.Title} 缺少组合键");
        }

        int interval = Math.Max(1, request.Node.TriggerIntervalMs);
        Logger.Info($"组合键：{string.Join("+", keys)} {request.Node.OperationMode}，触发 {TriggerRepeatRunner.CountLabel(request.Node.TriggerCount)}，间隔 {interval}ms");
        TriggerRepeatRunner.Run(
            request.Node.TriggerCount,
            interval,
            request.CancellationToken,
            _ => ExecuteChord(request.Adapters.Keyboard, keys, request.Node.OperationMode, request.CancellationToken));

        request.Context.Set(request.Node.Id, "result", true);
        return NodeExecutionResult.Ok($"组合键{string.Join("+", keys)}{request.Node.OperationMode}，触发 {TriggerRepeatRunner.CountLabel(request.Node.TriggerCount)}");
    }

    private static string[] SplitChord(string chord) =>
        chord.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static void ExecuteChord(IKeyboardAdapter keyboard, IReadOnlyList<string> keys, PressReleaseMode mode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (mode)
        {
            case PressReleaseMode.Press:
                foreach (string key in keys)
                    keyboard.ExecuteKey(key, PressReleaseMode.Press);
                break;

            case PressReleaseMode.Release:
                for (int index = keys.Count - 1; index >= 0; index--)
                    keyboard.ExecuteKey(keys[index], PressReleaseMode.Release);
                break;

            case PressReleaseMode.Click:
            default:
                var pressed = new List<string>(keys.Count);
                try
                {
                    foreach (string key in keys)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        keyboard.ExecuteKey(key, PressReleaseMode.Press);
                        pressed.Add(key);
                    }

                    TriggerRepeatRunner.Wait(ClickHoldMs, cancellationToken);
                }
                finally
                {
                    for (int index = pressed.Count - 1; index >= 0; index--)
                        keyboard.ExecuteKey(pressed[index], PressReleaseMode.Release);
                }

                break;
        }
    }
}
