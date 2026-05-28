using System.Collections.Specialized;
using System.Text;
using System.Windows;
using AutomationStudioWpf.Logging;

namespace AutomationStudioWpf;

public partial class LogWindow : Window
{
    public LogWindow()
    {
        InitializeComponent();
        LogDirText.Text = $"日志目录：{Logger.GetLogDirectory()}";
        RebuildText();
        Logger.Entries.CollectionChanged += OnEntriesChanged;
        Closed += (_, _) => Logger.Entries.CollectionChanged -= OnEntriesChanged;
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildText();
    }

    private void RebuildText()
    {
        StringBuilder sb = new();
        foreach (LogEntry entry in Logger.Entries)
        {
            sb.AppendLine($"[{entry.Timestamp}] [{entry.Level}] {entry.Message}");
        }
        LogTextBox.Text = sb.ToString();
        LogTextBox.ScrollToEnd();
    }

    private void CopyAll_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText(LogTextBox.Text);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Logger.Entries.Clear();
        LogTextBox.Text = string.Empty;
    }
}
