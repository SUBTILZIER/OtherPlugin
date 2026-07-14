namespace AutomationStudioWpf.Adapters;

internal static class RuntimeShutdownGate
{
    private static int _shutdownStarted;

    public static bool IsShutdownStarted => Volatile.Read(ref _shutdownStarted) != 0;

    public static void BeginShutdown() => Interlocked.Exchange(ref _shutdownStarted, 1);

    public static void ThrowIfShutdownStarted()
    {
        if (IsShutdownStarted)
            throw new OperationCanceledException("应用正在退出，已禁止新的运行时输入。");
    }
}
