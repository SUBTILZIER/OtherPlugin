using System.Collections.Specialized;
using AutomationStudioWpf.Logging;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using WpfRichTextBox = System.Windows.Controls.RichTextBox;
using WpfRadioButton = System.Windows.Controls.RadioButton;

namespace AutomationStudioWpf.Interaction;

public sealed class LogPanelController
{
    private readonly WpfRichTextBox _logTextBox;
    private readonly WpfRadioButton _filterAllRadio;
    private readonly WpfRadioButton _filterInfoRadio;
    private readonly WpfRadioButton _filterWarnRadio;
    private readonly WpfRadioButton _filterErrorRadio;

    public LogPanelController(
        WpfRichTextBox logTextBox,
        WpfRadioButton filterAllRadio,
        WpfRadioButton filterInfoRadio,
        WpfRadioButton filterWarnRadio,
        WpfRadioButton filterErrorRadio)
    {
        _logTextBox = logTextBox;
        _filterAllRadio = filterAllRadio;
        _filterInfoRadio = filterInfoRadio;
        _filterWarnRadio = filterWarnRadio;
        _filterErrorRadio = filterErrorRadio;
        BindTextCommands();
    }

    public void Refresh()
    {
        var filtered = LoggingModule.Filter(Logger.Entries).ToList();
        var document = new FlowDocument
        {
            PagePadding = new System.Windows.Thickness(0),
            FontFamily = _logTextBox.FontFamily,
            FontSize = _logTextBox.FontSize,
        };

        foreach (var entry in filtered)
        {
            var paragraph = new Paragraph(new Run(entry.DisplayText))
            {
                Margin = new System.Windows.Thickness(0),
                Foreground = BrushFor(entry.Level),
            };
            document.Blocks.Add(paragraph);
        }

        _logTextBox.Document = document;
        if (filtered.Count > 0)
            _logTextBox.ScrollToEnd();
    }

    public void HandleEntriesChanged(NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null)
        {
            Refresh();
            return;
        }

        bool appendedAny = false;
        foreach (var item in e.NewItems)
        {
            if (item is not LogEntry entry || !MatchesFilter(entry))
                continue;

            AppendEntry(entry);
            appendedAny = true;
        }

        if (appendedAny)
            _logTextBox.ScrollToEnd();
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
        _logTextBox.Document.Blocks.Clear();
    }

    private void BindTextCommands()
    {
        _logTextBox.InputBindings.Add(new KeyBinding(ApplicationCommands.Copy, Key.C, ModifierKeys.Control));
        _logTextBox.InputBindings.Add(new KeyBinding(ApplicationCommands.SelectAll, Key.A, ModifierKeys.Control));

        _logTextBox.CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Copy,
            (_, e) =>
            {
                string text = _logTextBox.Selection.Text;
                if (!string.IsNullOrEmpty(text))
                    System.Windows.Clipboard.SetText(text);
                e.Handled = true;
            },
            (_, e) => e.CanExecute = !_logTextBox.Selection.IsEmpty));

        _logTextBox.CommandBindings.Add(new CommandBinding(
            ApplicationCommands.SelectAll,
            (_, e) =>
            {
                _logTextBox.SelectAll();
                e.Handled = true;
            },
            (_, e) => e.CanExecute = true));
    }

    private void AppendEntry(LogEntry entry)
    {
        _logTextBox.Document.Blocks.Add(new Paragraph(new Run(entry.DisplayText))
        {
            Margin = new System.Windows.Thickness(0),
            Foreground = BrushFor(entry.Level),
        });
    }

    private static bool MatchesFilter(LogEntry entry) =>
        LoggingModule.FilterLevel is null || entry.Level == LoggingModule.FilterLevel.Value;

    private static System.Windows.Media.Brush BrushFor(LogLevel level) => level switch
    {
        LogLevel.Warn => System.Windows.Media.Brushes.Gold,
        LogLevel.Error => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 107, 107)),
        _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(208, 215, 226)),
    };
}
