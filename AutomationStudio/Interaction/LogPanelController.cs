using System.Collections.Specialized;
using AutomationStudioWpf.Logging;
using System.Windows.Documents;
using System.Windows;
using System.Windows.Input;
using WpfRichTextBox = System.Windows.Controls.RichTextBox;
using WpfRadioButton = System.Windows.Controls.RadioButton;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfButton = System.Windows.Controls.Button;
using System.Windows.Media;

namespace AutomationStudioWpf.Interaction;

public sealed class LogPanelController
{
    private readonly WpfRichTextBox _logTextBox;
    private readonly WpfRadioButton _filterAllRadio;
    private readonly WpfRadioButton _filterInfoRadio;
    private readonly WpfRadioButton _filterWarnRadio;
    private readonly WpfRadioButton _filterErrorRadio;
    private readonly WpfTextBlock? _unreadText;
    private readonly WpfButton? _jumpButton;
    private int _unread;

    public LogPanelController(
        WpfRichTextBox logTextBox,
        WpfRadioButton filterAllRadio,
        WpfRadioButton filterInfoRadio,
        WpfRadioButton filterWarnRadio,
        WpfRadioButton filterErrorRadio,
        WpfTextBlock? unreadText = null,
        WpfButton? jumpButton = null)
    {
        _logTextBox = logTextBox;
        _filterAllRadio = filterAllRadio;
        _filterInfoRadio = filterInfoRadio;
        _filterWarnRadio = filterWarnRadio;
        _filterErrorRadio = filterErrorRadio;
        _unreadText = unreadText; _jumpButton = jumpButton;
        if (_jumpButton is not null) _jumpButton.Click += (_, _) => JumpToEnd();
        BindTextCommands();
    }

    public void Refresh()
    {
        bool wasBottom = IsNearBottom();
        var filtered = LoggingModule.Filter(Logger.Entries).ToList();
        var document = new FlowDocument
        {
            PagePadding = new System.Windows.Thickness(0),
            FontFamily = _logTextBox.FontFamily,
            FontSize = _logTextBox.FontSize,
        };

        foreach (var entry in filtered)
            document.Blocks.Add(LogEntryDocumentRenderer.CreateBlock(entry, _logTextBox));

        _logTextBox.Document = document;
        if (filtered.Count > 0 && wasBottom) { _logTextBox.ScrollToEnd(); _unread = 0; UpdateUnread(); }
    }

    public void HandleEntriesChanged(NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null)
        {
            Refresh();
            return;
        }

        bool appendedAny = false; bool wasBottom = IsNearBottom();
        foreach (var item in e.NewItems)
        {
            if (item is not LogEntry entry || !MatchesFilter(entry))
                continue;

            AppendEntry(entry);
            appendedAny = true;
        }

        if (appendedAny && wasBottom) { _logTextBox.ScrollToEnd(); _unread = 0; UpdateUnread(); }
        else if (appendedAny) { _unread += e.NewItems.OfType<LogEntry>().Count(MatchesFilter); UpdateUnread(); }
    }

    public void ApplyFilterFromUi()
    {
        LoggingModule.FilterLevel = _filterAllRadio.IsChecked == true ? null :
                                    _filterInfoRadio.IsChecked == true ? LogLevel.Info :
                                    _filterWarnRadio.IsChecked == true ? LogLevel.Warn :
                                    _filterErrorRadio.IsChecked == true ? LogLevel.Error : null;
        bool wasBottom = IsNearBottom();
        Refresh();
        if (wasBottom) { _unread = 0; UpdateUnread(); }
    }

    public void Clear()
    {
        Logger.ClearUiEntries();
        _logTextBox.Document.Blocks.Clear();
        _unread = 0; UpdateUnread();
    }

    private bool IsNearBottom()
    {
        var viewer = FindVisualChild<System.Windows.Controls.ScrollViewer>(_logTextBox);
        return viewer is null || viewer.ScrollableHeight - viewer.VerticalOffset <= 24;
    }

    public void JumpToEnd() { _logTextBox.ScrollToEnd(); _unread = 0; UpdateUnread(); }
    private void UpdateUnread() { if (_unreadText is not null) _unreadText.Text = _unread > 0 ? $"有 {_unread} 条新日志" : string.Empty; if (_jumpButton is not null) _jumpButton.Visibility = _unread > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed; }
    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject { for (int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){ var c=VisualTreeHelper.GetChild(root,i); if(c is T t)return t; var r=FindVisualChild<T>(c); if(r is not null)return r;} return null; }

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
                    ClipboardHelper.TrySetText(text);
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
        _logTextBox.Document.Blocks.Add(LogEntryDocumentRenderer.CreateBlock(entry, _logTextBox));
    }

    private static bool MatchesFilter(LogEntry entry) =>
        LoggingModule.FilterLevel is null || entry.Level == LoggingModule.FilterLevel.Value;
}
