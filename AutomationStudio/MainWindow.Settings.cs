using System.Windows;
using AutomationStudioWpf.Graph;
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

    private bool? GetInspectorSectionState(string key) =>
        _appSettings.InspectorSectionStates.TryGetValue(key, out var value) ? value : null;

    private string GetInspectorSchemaKey(NodeBaseViewModel node) =>
        _nodeRegistry.TryGetDefinition(node.NodeKind, out var definition)
            ? definition.InspectorSchemaKey
            : node.NodeKind.ToString();

    private void SetInspectorSectionState(string key, bool value)
    {
        _appSettings.InspectorSectionStates[key] = value;
        _layoutSaveTimer.Stop();
        _layoutSaveTimer.Tick -= LayoutSaveTimer_Tick;
        _layoutSaveTimer.Tick += LayoutSaveTimer_Tick;
        _layoutSaveTimer.Start();
    }

    private void LoadAppSettings()
    {
        _appSettings = _layoutStateService.Normalize(_appSettingsService.Load());
        AppThemeService.Apply(_appSettings);
    }

    internal void ApplyLayoutSettings(Controls.EditorSurfaceControl surface)
    {
        _layoutStateService.ApplySnapshot(_appSettings, (sidebar, inspector, log, tree) =>
        {
            surface.ApplyLayout(sidebar, inspector);
            LogRow.Height = new GridLength(log);
            ContentTreeColumn.Width = new GridLength(tree);
        });
        _layoutLoaded = true;
    }

    internal void ApplyMainLayoutSettings()
    {
        _layoutStateService.ApplySnapshot(_appSettings, (_, _, log, tree) =>
        {
            LogRow.Height = new GridLength(log);
            ContentTreeColumn.Width = new GridLength(tree);
        });
        _layoutLoaded = true;
    }

    internal void NotifyLayoutChanged(Controls.EditorSurfaceControl? surface = null)
    {
        if (!_layoutLoaded) return;
        var current = surface?.ReadLayout() ?? (_appSettings.GraphSidebarWidth, _appSettings.InspectorWidth);
        _layoutStateService.UpdateSettings(_appSettings, _layoutStateService.ReadSnapshot(current.Sidebar, current.Inspector, LogRow.ActualHeight, ContentTreeColumn.ActualWidth));
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
