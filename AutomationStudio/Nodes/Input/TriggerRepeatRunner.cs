using System;
using System.Threading;

namespace AutomationStudioWpf.Nodes.Input;

internal static class TriggerRepeatRunner
{
    public static void Run(int triggerCount, int triggerIntervalMs, CancellationToken cancellationToken, Action<int> trigger)
    {
        int count = Math.Max(0, triggerCount);
        int interval = Math.Max(1, triggerIntervalMs);

        for (int index = 0; count == 0 || index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            trigger(index + 1);

            bool hasNext = count == 0 || index < count - 1;
            if (hasNext)
                Wait(interval, cancellationToken);
        }
    }

    public static void Wait(int intervalMs, CancellationToken cancellationToken)
    {
        int interval = Math.Max(1, intervalMs);
        if (cancellationToken.WaitHandle.WaitOne(interval))
            throw new OperationCanceledException(cancellationToken);
    }

    public static string CountLabel(int triggerCount) =>
        triggerCount == 0 ? "无限次" : $"{Math.Max(1, triggerCount)} 次";
}
