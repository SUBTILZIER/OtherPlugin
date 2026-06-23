using System.Collections.Specialized;
using System.Windows;
using System.Windows.Documents;
using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Logging;

namespace AutomationStudioWpf;

public partial class LogWindow : Window
{
    public LogWindow()
    {
        InitializeComponent();
        LogDirText.Text = $"日志目录：{Logger.GetLogDirectory()}";
        RefreshLogList();
        Logger.Entries.CollectionChanged += OnEntriesChanged;
        Closed += (_, _) => Logger.Entries.CollectionChanged -= OnEntriesChanged;
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null)
        {
            RefreshLogList();
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
            LogRichTextBox.ScrollToEnd();
    }

    private void RefreshLogList()
    {
        if (LogRichTextBox is null) return;
        List<LogEntry> filtered = LoggingModule.Filter(Logger.Entries).ToList();
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = LogRichTextBox.FontFamily,
            FontSize = LogRichTextBox.FontSize,
        };
        foreach (var entry in filtered)
            document.Blocks.Add(LogEntryDocumentRenderer.CreateBlock(entry, LogRichTextBox));

        LogRichTextBox.Document = document;
        if (filtered.Count > 0)
            LogRichTextBox.ScrollToEnd();
    }

    private void AppendEntry(LogEntry entry)
    {
        if (LogRichTextBox is null) return;

        LogRichTextBox.Document.Blocks.Add(LogEntryDocumentRenderer.CreateBlock(entry, LogRichTextBox));
    }

    private void FilterRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (FilterAllRadio.IsChecked == true)
            LoggingModule.FilterLevel = null;
        else if (FilterInfoRadio.IsChecked == true)
            LoggingModule.FilterLevel = LogLevel.Info;
        else if (FilterWarnRadio.IsChecked == true)
            LoggingModule.FilterLevel = LogLevel.Warn;
        else if (FilterErrorRadio.IsChecked == true)
            LoggingModule.FilterLevel = LogLevel.Error;

        RefreshLogList();
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        string text = string.Join(Environment.NewLine, LoggingModule.Filter(Logger.Entries).Select(e => e.DisplayText));
        ClipboardHelper.TrySetText(text);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Logger.Entries.Clear();
        LogRichTextBox.Document.Blocks.Clear();
    }

    private static bool MatchesFilter(LogEntry entry) =>
        LoggingModule.FilterLevel is null || entry.Level == LoggingModule.FilterLevel.Value;
}
