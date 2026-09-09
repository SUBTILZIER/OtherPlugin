using System.Windows;
using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Services;
using System.Windows.Threading;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private readonly AppSettingsService _appSettingsService = new();
    private AppSettings _appSettings = new();
    private readonly DispatcherTimer _layoutSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _layoutLoaded;

    private void LoadAppSettings()
    {
        _appSettings = _appSettingsService.Load();
        AppThemeService.Apply(_appSettings);
    }

    internal void ApplyLayoutSettings(Controls.EditorSurfaceControl surface)
    {
        surface.ApplyLayout(_appSettings.GraphSidebarWidth, _appSettings.InspectorWidth);
        LogRow.Height = new GridLength(_appSettings.LogPanelHeight);
        ContentTreeColumn.Width = new GridLength(_appSettings.ContentTreeWidth);
        _layoutLoaded = true;
    }

    internal void NotifyLayoutChanged(Controls.EditorSurfaceControl surface)
    {
        if (!_layoutLoaded) return;
        var layout = surface.ReadLayout();
        _appSettings.GraphSidebarWidth = layout.Sidebar;
        _appSettings.InspectorWidth = layout.Inspector;
        _appSettings.LogPanelHeight = LogRow.ActualHeight;
        _appSettings.ContentTreeWidth = ContentTreeColumn.ActualWidth;
        _layoutSaveTimer.Stop();
        _layoutSaveTimer.Tick -= LayoutSaveTimer_Tick;
        _layoutSaveTimer.Tick += LayoutSaveTimer_Tick;
        _layoutSaveTimer.Start();
    }

    private void LayoutSaveTimer_Tick(object? sender, EventArgs e)
    {
        _layoutSaveTimer.Stop();
        try { _appSettingsService.Save(_appSettings); } catch (Exception ex) { Logging.Logger.Warn($"保存界面布局失败：{ex.Message}"); }
    }

    private void LayoutSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (_activeEditorSession?.SurfaceContext?.Surface is { } surface) NotifyLayoutChanged(surface);
    }

    private void ToggleLogPanel_Click(object sender, RoutedEventArgs e) => LogRow.Height = LogRow.Height.Value > 1 ? new GridLength(0) : new GridLength(_appSettings.LogPanelHeight);
    private void ToggleSidebar_Click(object sender, RoutedEventArgs e) { if (_activeEditorSession?.SurfaceContext?.Surface is { } s) { s.ToggleSidebar(); NotifyLayoutChanged(s); } }
    private void ToggleInspector_Click(object sender, RoutedEventArgs e) { if (_activeEditorSession?.SurfaceContext?.Surface is { } s) { s.ToggleInspector(); NotifyLayoutChanged(s); } }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(this, _appSettings, ApplyAndSaveAppSettings);
        dialog.ShowDialog();
    }

    private bool ApplyAndSaveAppSettings(AppSettings settings)
    {
        try
        {
            settings.Normalize();
            AppThemeService.Apply(settings);
            _appSettings = settings.Clone();
            _appSettingsService.Save(_appSettings);
            SetStatus("设置已保存。");
            return true;
        }
        catch (Exception ex)
        {
            ThemedDialog.Show(this, $"保存设置失败：{ex.Message}", "设置", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void OnAppThemeChanged(object? sender, EventArgs e)
    {
        if (!IsLoaded)
            return;

        Dispatcher.InvokeAsync(() =>
        {
            ApplyContextMenuResourceReferences();
            RefreshVisibleScriptPropertiesSummaries();
            _logPanelController?.Refresh();
            _finalCodePreviewWindow?.RefreshTheme();
            foreach (var session in _editorSessions)
                session.DetachedWindow?.RefreshTheme();
        });
    }
}
