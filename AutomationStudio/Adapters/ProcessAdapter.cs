using System.Diagnostics;
using System.IO;
using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Adapters;

public sealed class ProcessAdapter : IProcessAdapter
{
    public ProcessStartResult StartProgram(
        string programPath,
        int waitTimeoutMs,
        ProgramStartFailureAction failureAction,
        int retryCount,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(programPath))
            return new ProcessStartResult(false, string.Empty, "程序路径为空。");

        if (!File.Exists(programPath))
            return new ProcessStartResult(false, string.Empty, $"文件不存在：{programPath}");

        string processName = Path.GetFileNameWithoutExtension(programPath);
        int attempts = 1 + (failureAction == ProgramStartFailureAction.Retry ? Math.Max(0, retryCount) : 0);

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var psi = new ProcessStartInfo(programPath)
            {
                UseShellExecute = true,
                CreateNoWindow = false,
            };
            Process? proc = Process.Start(psi);
            int waited = 0;

            while (waited < waitTimeoutMs)
            {
                ct.ThrowIfCancellationRequested();
                int sleepMs = Math.Min(1000, waitTimeoutMs - waited);
                Thread.Sleep(sleepMs);
                waited += sleepMs;

                try
                {
                    proc?.Refresh();
                    if (proc is not null && !proc.HasExited)
                        return new ProcessStartResult(true, processName, $"程序已启动：{processName}");
                }
                catch
                {
                    // Process may exit quickly; retry loop handles it.
                }
            }
        }

        return new ProcessStartResult(false, processName, $"程序启动失败：{processName}");
    }
}

