using System.Collections.Specialized;
using System.Windows;
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
        RefreshLogList();
    }

    private void RefreshLogList()
    {
        if (LogListBox is null) return;
        List<LogEntry> filtered = LoggingModule.Filter(Logger.Entries).ToList();
        LogListBox.ItemsSource = filtered;
        if (filtered.Count > 0)
            LogListBox.ScrollIntoView(filtered[^1]);
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
        System.Windows.Clipboard.SetText(text);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Logger.Entries.Clear();
        LogListBox.ItemsSource = null;
    }
}
