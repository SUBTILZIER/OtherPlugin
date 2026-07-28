using System.ComponentModel;
using System.Windows;
using AutomationStudioWpf.Adapters;
using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Services;
using WpfApplication = System.Windows.Application;
using WinFormsCursor = System.Windows.Forms.Cursor;
using WinFormsMouseButtons = System.Windows.Forms.MouseButtons;
using WinFormsNotifyIcon = System.Windows.Forms.NotifyIcon;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private WinFormsNotifyIcon? _notifyIcon;
    private TrayMenuWindow? _trayMenuWindow;
    private int _exitCleanupStarted;

    private void SetupNotifyIcon()
    {
        _notifyIcon = new WinFormsNotifyIcon
        {
            Icon = WindowIconHelper.TrayIcon,
            Text = "AutomationStudio",
            Visible = true,
        };

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == WinFormsMouseButtons.Left)
                RestoreFromTray();
            else if (e.Button == WinFormsMouseButtons.Right)
                ShowTrayMenu();
        };
    }

    private void ShowTrayMenu()
    {
        _trayMenuWindow?.Close();
        _trayMenuWindow = new TrayMenuWindow(RestoreFromTray, ExitApplication);
        _trayMenuWindow.Closed += (_, _) => _trayMenuWindow = null;
        _trayMenuWindow.ShowNear(WinFormsCursor.Position, this);
    }

    private void RestoreFromTray() => ActivatePrimaryWindow();

    internal void ActivateFromSecondInstance() => ActivatePrimaryWindow();

    internal void RequestShutdownForUpdate()
    {
        _isReallyClosing = true;
        Close();
        if (!_isClosing)
            _isReallyClosing = false;
    }

    private void ActivatePrimaryWindow()
    {
        _trayMenuWindow?.Close();
        _trayMenuWindow = null;
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ExitApplication()
    {
        _isReallyClosing = true;
        _trayMenuWindow?.Close();
        _trayMenuWindow = null;
        Close();
        if (_isClosing)
            WpfApplication.Current.Shutdown();

        _isReallyClosing = false;
        if (_notifyIcon is not null)
            _notifyIcon.Visible = true;
    }

    private void MinimizeToTray()
    {
        if (_notifyIcon is not null)
            _notifyIcon.Visible = true;
        Hide();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isClosing)
            return;

        if (!_isReallyClosing && _appSettings.WindowCloseAction == AppWindowCloseAction.MinimizeToTray)
        {
            e.Cancel = true;
            MinimizeToTray();
            return;
        }

        CommitInspectorAndSnapshotAllSessions();
        if (ContentBrowserItems.Any(item => item.IsDirty) ||
            GraphListItems.Concat(FunctionListItems).Any(item => item.IsDirty))
        {
            bool resumeMousePickOnCancel = _mousePickController.IsActive;
            if (resumeMousePickOnCancel)
                _mousePickController.Stop();

            MessageBoxResult result = ThemedDialog.ShowCustom(
                this,
                "存在未保存的资产，是否保存？",
                "是否保存",
                MessageBoxImage.Question,
                new ThemedDialogButton("保存", MessageBoxResult.Yes, true),
                new ThemedDialogButton("不保存", MessageBoxResult.No),
                new ThemedDialogButton("取消", MessageBoxResult.Cancel));
            if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                if (resumeMousePickOnCancel)
                    _mousePickController.Start();
                return;
            }

            if (result == MessageBoxResult.Yes && !SaveAllAssets())
            {
                e.Cancel = true;
                if (resumeMousePickOnCancel)
                    _mousePickController.Start();
                return;
            }
        }

        if (e.Cancel)
            return;

        _isReallyClosing = true;
        _isClosing = true;
        CleanupForApplicationExit();
    }

    private void CleanupForApplicationExit()
    {
        if (Interlocked.Exchange(ref _exitCleanupStarted, 1) != 0)
            return;

        RuntimeShutdownGate.BeginShutdown();
        _mousePickController.Stop();
        _scriptRunManager.BeginShutdown();
        _executionController.Cancel(ExecutionStopReason.ApplicationExit);
        _pythonEnvironmentService.TerminateAllProcessesImmediately();
        _executionController.ReleaseAllInputs();
        DisposeWindowSubscriptions();
        _finalCodePreviewWindow?.Close();
        _finalCodePreviewWindow = null;
        foreach (EditorSessionViewModel session in _editorSessions.ToList())
        {
            session.DetachedWindow?.CloseFromOwner();
            session.DetachedWindow = null;
            session.Dispose();
        }
        _editorSessions.Clear();
        _mainEditorSessions.Clear();

        _mousePickController.Dispose();
        _scriptRunManager.Dispose();
        _scriptHotkeyService.Dispose();
        _pythonEnvironmentService.Dispose();
        _trayMenuWindow?.Close();
        _trayMenuWindow = null;
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }

    internal void EmergencyStopRuntimeForApplicationExit()
    {
        _isReallyClosing = true;
        _isClosing = true;
        CleanupForApplicationExit();
    }

    internal void EmergencyStopRuntimeWithoutUi()
    {
        RuntimeShutdownGate.BeginShutdown();
        try { _scriptRunManager.BeginShutdown(); } catch { }
        try { _executionController.CancelWithoutUi(); } catch { }
        try { _pythonEnvironmentService.TerminateAllProcessesImmediately(); } catch { }
        try { _executionController.ReleaseAllInputs(); } catch { }
        try { _mousePickController.EmergencyStopWithoutUi(); } catch { }
        try { _scriptHotkeyService.EmergencyStopWithoutUi(); } catch { }
    }
}
