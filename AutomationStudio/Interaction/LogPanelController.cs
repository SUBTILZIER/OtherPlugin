using AutomationStudioWpf.Logging;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfRadioButton = System.Windows.Controls.RadioButton;

namespace AutomationStudioWpf.Interaction;

public sealed class LogPanelController
{
    private readonly WpfListBox _logListBox;
    private readonly WpfRadioButton _filterAllRadio;
    private readonly WpfRadioButton _filterInfoRadio;
    private readonly WpfRadioButton _filterWarnRadio;
    private readonly WpfRadioButton _filterErrorRadio;

    public LogPanelController(
        WpfListBox logListBox,
        WpfRadioButton filterAllRadio,
        WpfRadioButton filterInfoRadio,
        WpfRadioButton filterWarnRadio,
        WpfRadioButton filterErrorRadio)
    {
        _logListBox = logListBox;
        _filterAllRadio = filterAllRadio;
        _filterInfoRadio = filterInfoRadio;
        _filterWarnRadio = filterWarnRadio;
        _filterErrorRadio = filterErrorRadio;
    }

    public void Refresh()
    {
        var filtered = LoggingModule.Filter(Logger.Entries).ToList();
        _logListBox.ItemsSource = filtered;
        if (filtered.Count > 0)
            _logListBox.ScrollIntoView(filtered[^1]);
    }

    public void ApplyFilterFromUi()
    {
        LoggingModule.FilterLevel = _filterAllRadio.IsChecked == true ? null :
                                    _filterInfoRadio.IsChecked == true ? LogLevel.Info :
                                    _filterWarnRadio.IsChecked == true ? LogLevel.Warn :
                                    _filterErrorRadio.IsChecked == true ? LogLevel.Error : null;
        Refresh();
    }

    public void Clear()
    {
        Logger.Entries.Clear();
        _logListBox.ItemsSource = null;
    }
}
