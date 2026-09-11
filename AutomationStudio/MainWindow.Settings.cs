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
        surface.ApplyLayout(
            ValidLayoutSize(_appSettings.GraphSidebarWidth, 180, 420, 224),
            ValidLayoutSize(_appSettings.InspectorWidth, 420, 720, 420));
        ApplyMainLayoutSettings();
    }

    internal void ApplyMainLayoutSettings()
    {
        LogRow.Height = new GridLength(ValidLayoutSize(_appSettings.LogPanelHeight, 180, 2000, 280));
        ContentTreeColumn.Width = new GridLength(ValidLayoutSize(_appSettings.ContentTreeWidth, 120, 420, 180));
        _layoutLoaded = true;
    }

    internal void NotifyLayoutChanged(Controls.EditorSurfaceControl? surface = null)
    {
        if (!_layoutLoaded) return;
        if (surface is not null)
        {
            var layout = surface.ReadLayout();
            _appSettings.GraphSidebarWidth = ValidLayoutSize(layout.Sidebar, 180, 420, _appSettings.GraphSidebarWidth);
            _appSettings.InspectorWidth = ValidLayoutSize(layout.Inspector, 420, 720, _appSettings.InspectorWidth);
        }
        _appSettings.LogPanelHeight = ValidLayoutSize(LogRow.ActualHeight, 180, 2000, _appSettings.LogPanelHeight);
        _appSettings.ContentTreeWidth = ValidLayoutSize(ContentTreeColumn.ActualWidth, 120, 420, _appSettings.ContentTreeWidth);
        _layoutSaveTimer.Stop();
        _layoutSaveTimer.Tick -= LayoutSaveTimer_Tick;
        _layoutSaveTimer.Tick += LayoutSaveTimer_Tick;
        _layoutSaveTimer.Start();
    }

    private static double ValidLayoutSize(double value, double minimum, double maximum, double fallback)
    {
        if (!double.IsFinite(value) || value <= 0)
            return fallback;

        return Math.Clamp(value, minimum, maximum);
    }

    private void LayoutSaveTimer_Tick(object? sender, EventArgs e)
    {
        _layoutSaveTimer.Stop();
        try { _appSettingsService.Save(_appSettings); } catch (Exception ex) { Logging.Logger.Warn($"保存界面布局失败：{ex.Message}"); }
    }

    private void GridSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        NotifyLayoutChanged(_activeEditorSession?.SurfaceContext?.Surface);
    }

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
