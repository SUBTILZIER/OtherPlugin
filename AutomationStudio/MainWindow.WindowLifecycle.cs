using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using WpfMessageBox = System.Windows.MessageBox;
using WpfApplication = System.Windows.Application;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private NotifyIcon? _notifyIcon;

    private void SetupNotifyIcon()
    {
        System.Drawing.Icon? icon = null;
        try
        {
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "2.png");
            if (System.IO.File.Exists(iconPath))
            {
                using var bmp = new Bitmap(iconPath);
                icon = System.Drawing.Icon.FromHandle(bmp.GetHicon());
            }
        }
        catch { }
        icon ??= SystemIcons.Application;

        _notifyIcon = new NotifyIcon
        {
            Icon = icon,
            Text = "AutomationStudio",
            Visible = true,
        };

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                RestoreFromTray();
        };

        _notifyIcon.ContextMenuStrip = new ContextMenuStrip();
        _notifyIcon.ContextMenuStrip.Items.Add("打开面板", null, (_, _) => RestoreFromTray());
        _notifyIcon.ContextMenuStrip.Items.Add("退出程序", null, (_, _) => ExitApplication());
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _isReallyClosing = true;
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
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
            var result = WpfMessageBox.Show(
                this,
                "存在未保存资产，是否保存？",
                "是否保存",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
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
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }
}