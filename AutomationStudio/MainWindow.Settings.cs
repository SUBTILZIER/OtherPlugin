using System.Windows;
using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Services;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private readonly AppSettingsService _appSettingsService = new();
    private AppSettings _appSettings = new();

    private void LoadAppSettings()
    {
        _appSettings = _appSettingsService.Load();
        AppThemeService.Apply(_appSettings);
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
