using System.Windows;
using AutomationStudioWpf.Adapters;
using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Services;

namespace AutomationStudioWpf;

public partial class App : System.Windows.Application
{
    private SingleInstanceCoordinator? _singleInstanceCoordinator;
    private CrashReporter? _crashReporter;
    private bool _activationPending;

    protected override void OnStartup(StartupEventArgs e)
    {
        bool shutdownForUpdate = e.Args.Any(argument =>
            string.Equals(argument, "--shutdown-for-update", StringComparison.OrdinalIgnoreCase));
        _singleInstanceCoordinator = new SingleInstanceCoordinator();
        if (!_singleInstanceCoordinator.TryBecomePrimary())
        {
            bool signaled = _singleInstanceCoordinator.SignalPrimary(
                shutdownForUpdate ? SingleInstanceCommand.ShutdownForUpdate : SingleInstanceCommand.Activate);
            _singleInstanceCoordinator.Dispose();
            _singleInstanceCoordinator = null;
            Shutdown(signaled ? 0 : 2);
            return;
        }

        if (shutdownForUpdate)
        {
            _singleInstanceCoordinator.Dispose();
            _singleInstanceCoordinator = null;
            Shutdown(0);
            return;
        }

        _singleInstanceCoordinator.ActivationRequested += OnActivationRequested;
        _singleInstanceCoordinator.ShutdownForUpdateRequested += OnShutdownForUpdateRequested;
        base.OnStartup(e);
        _crashReporter = new CrashReporter(this, HandleFatalDispatcherException);
        RuntimeFileMaintenance.Start();

        if (MainWindow == null)
        {
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }

        if (_activationPending)
        {
            _activationPending = false;
            ActivatePrimaryWindow();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        RuntimeShutdownGate.BeginShutdown();
        if (MainWindow is MainWindow mainWindow)
            mainWindow.EmergencyStopRuntimeForApplicationExit();
        PythonEnvironmentService.Shared.Dispose();

        _crashReporter?.Dispose();
        _crashReporter = null;

        if (_singleInstanceCoordinator is not null)
        {
            _singleInstanceCoordinator.ActivationRequested -= OnActivationRequested;
            _singleInstanceCoordinator.ShutdownForUpdateRequested -= OnShutdownForUpdateRequested;
            _singleInstanceCoordinator.Dispose();
            _singleInstanceCoordinator = null;
        }

        base.OnExit(e);
    }

    private void HandleFatalDispatcherException(Exception exception, string? reportPath)
    {
        RuntimeShutdownGate.BeginShutdown();
        if (MainWindow is MainWindow mainWindow)
            mainWindow.EmergencyStopRuntimeForApplicationExit();
        else
            PythonEnvironmentService.Shared.TerminateAllProcessesImmediately();

        string reportHint = string.IsNullOrWhiteSpace(reportPath)
            ? "崩溃报告写入失败。"
            : $"崩溃报告：{reportPath}";
        try
        {
            ThemedDialog.Show(
                MainWindow,
                $"程序遇到无法恢复的错误，即将退出。\n\n{exception.Message}\n\n{reportHint}",
                "AutomationStudio 已停止运行",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Shutdown(-1);
        }
    }

    private void OnActivationRequested()
    {
        Dispatcher.BeginInvoke(ActivatePrimaryWindow);
    }

    private void OnShutdownForUpdateRequested()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (MainWindow is MainWindow mainWindow)
                mainWindow.RequestShutdownForUpdate();
            else
                Shutdown(0);
        });
    }

    private void ActivatePrimaryWindow()
    {
        if (MainWindow is MainWindow mainWindow)
        {
            mainWindow.ActivateFromSecondInstance();
            return;
        }

        _activationPending = true;
    }
}
