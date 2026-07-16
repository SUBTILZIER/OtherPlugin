using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;

namespace AutomationStudioWpf.Services;

internal sealed class CrashReporter : IDisposable
{
    private readonly WpfApplication _application;
    private readonly Action<Exception, string?> _fatalDispatcherHandler;
    private int _fatalDispatcherStarted;
    private bool _disposed;

    public CrashReporter(WpfApplication application, Action<Exception, string?> fatalDispatcherHandler)
    {
        _application = application;
        _fatalDispatcherHandler = fatalDispatcherHandler;
        _application.DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _application.DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        string? reportPath = WriteReport("DispatcherUnhandledException", e.Exception, isFatal: true);
        e.Handled = true;
        if (Interlocked.Exchange(ref _fatalDispatcherStarted, 1) != 0)
            return;

        try
        {
            _fatalDispatcherHandler(e.Exception, reportPath);
        }
        catch
        {
            _application.Shutdown(-1);
        }
    }

    private static void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        RuntimeEmergencyCleanup.Run();
        Exception exception = e.ExceptionObject as Exception ??
                              new InvalidOperationException(e.ExceptionObject?.ToString() ?? "未知未处理异常");
        WriteReport("AppDomain.UnhandledException", exception, e.IsTerminating);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteReport("TaskScheduler.UnobservedTaskException", e.Exception, isFatal: false);
        e.SetObserved();
    }

    private static string? WriteReport(string source, Exception exception, bool isFatal)
    {
        try
        {
            string directory;
            try
            {
                directory = ApplicationPaths.CrashReportDirectory;
            }
            catch
            {
                directory = Path.Combine(Path.GetTempPath(), "AutomationStudioWpf", "CrashReports");
                Directory.CreateDirectory(directory);
            }

            DateTimeOffset now = DateTimeOffset.Now;
            string path = Path.Combine(
                directory,
                $"Crash_{now:yyyyMMdd_HHmmss_fff}_PID{Environment.ProcessId}.txt");
            Assembly? entryAssembly = Assembly.GetEntryAssembly();
            string version = entryAssembly?.GetName().Version?.ToString() ?? "unknown";
            string informationalVersion = entryAssembly?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? version;

            var report = new StringBuilder()
                .AppendLine("AutomationStudio crash report")
                .AppendLine($"Source: {source}")
                .AppendLine($"Fatal: {isFatal}")
                .AppendLine($"LocalTime: {now:O}")
                .AppendLine($"UtcTime: {now.UtcDateTime:O}")
                .AppendLine($"Version: {version}")
                .AppendLine($"InformationalVersion: {informationalVersion}")
                .AppendLine($"ProcessId: {Environment.ProcessId}")
                .AppendLine($"OS: {RuntimeInformation.OSDescription}")
                .AppendLine($"ProcessArchitecture: {RuntimeInformation.ProcessArchitecture}")
                .AppendLine($"Framework: {RuntimeInformation.FrameworkDescription}")
                .AppendLine()
                .AppendLine(exception.ToString());

            File.WriteAllText(path, report.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return path;
        }
        catch
        {
            return null;
        }
    }
}
