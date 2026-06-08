using System.Collections.Specialized;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
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
        if (LogRichTextBox is null) return;
        List<LogEntry> filtered = LoggingModule.Filter(Logger.Entries).ToList();
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = LogRichTextBox.FontFamily,
            FontSize = LogRichTextBox.FontSize,
        };
        foreach (var entry in filtered)
        {
            document.Blocks.Add(new Paragraph(new Run(entry.DisplayText))
            {
                Margin = new Thickness(0),
                Foreground = BrushFor(entry.Level),
            });
        }

        LogRichTextBox.Document = document;
        if (filtered.Count > 0)
            LogRichTextBox.ScrollToEnd();
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
        LogRichTextBox.Document.Blocks.Clear();
    }

    private static System.Windows.Media.Brush BrushFor(LogLevel level) => level switch
    {
        LogLevel.Warn => System.Windows.Media.Brushes.Gold,
        LogLevel.Error => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 107, 107)),
        _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(208, 215, 226)),
    };
}
