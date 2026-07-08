namespace AutomationStudioWpf.Runtime;

internal static class CancellationWait
{
    public static void WaitOrThrow(int milliseconds, CancellationToken cancellationToken)
    {
        int delay = Math.Max(0, milliseconds);
        if (delay == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        if (cancellationToken.WaitHandle.WaitOne(delay))
            throw new OperationCanceledException(cancellationToken);
    }
}
