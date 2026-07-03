using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Windows;
using AutomationStudioWpf.Interaction;
using WpfApplication = System.Windows.Application;
using WinFormsCursor = System.Windows.Forms.Cursor;
using WinFormsMouseButtons = System.Windows.Forms.MouseButtons;
using WinFormsNotifyIcon = System.Windows.Forms.NotifyIcon;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private WinFormsNotifyIcon? _notifyIcon;
    private TrayMenuWindow? _trayMenuWindow;

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

    private void RestoreFromTray()
    {
        _trayMenuWindow?.Close();
        _trayMenuWindow = null;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _isReallyClosing = true;
        _trayMenuWindow?.Close();
        _trayMenuWindow = null;
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        _trayMenuWindow?.Close();
        _trayMenuWindow = null;
        Close();
        WpfApplication.Current.Shutdown();
        Environment.Exit(0);
    }

    private void MinimizeToTray()
    {
        if (_notifyIcon is not null)
            _notifyIcon.Visible = true;
        Hide();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _mousePickController.Stop();
        _scriptRunManager.StopAll();
        _executionController.ReleaseAllKeys();
        if (_isClosing) return;

        CommitAllSessionsToAssets(applyInspectorForActive: true);
        if (ContentBrowserItems.Any(item => item.IsDirty) ||
            GraphListItems.Concat(FunctionListItems).Any(item => item.IsDirty))
        {
            var result = ThemedDialog.ShowCustom(
                this,
                "存在未保存资产，是否保存？",
                "是否保存",
                MessageBoxImage.Question,
                new ThemedDialogButton("保存", MessageBoxResult.Yes, true),
                new ThemedDialogButton("不保存", MessageBoxResult.No),
                new ThemedDialogButton("取消", MessageBoxResult.Cancel));
            if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (result == MessageBoxResult.Yes)
                SaveAllAssets();
        }

        if (e.Cancel)
            return;

        _isClosing = true;
        _finalCodePreviewWindow?.Close();
        _finalCodePreviewWindow = null;
        foreach (var session in _editorSessions.ToList())
        {
            session.DetachedWindow?.CloseFromOwner();
            session.DetachedWindow = null;
        }

        _mousePickController.Dispose();
        _scriptRunManager.Dispose();
        _scriptHotkeyService.Dispose();
        _trayMenuWindow?.Close();
        _trayMenuWindow = null;
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }
}
