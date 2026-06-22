using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using AutomationStudioWpf.Logging;
using WpfRichTextBox = System.Windows.Controls.RichTextBox;

namespace AutomationStudioWpf.Interaction;

internal static class LogEntryDocumentRenderer
{
    public static Block CreateBlock(LogEntry entry, WpfRichTextBox owner)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0),
            Foreground = LoggingModule.GetLevelBrush(entry.Level),
        };

        string prefix = $"[{entry.Timestamp}] [{LoggingModule.LevelLabel(entry.Level)}] ";
        string message = NormalizeLineEndings(entry.Message ?? string.Empty);
        string[] lines = message.Split('\n');
        if (lines.Length == 0)
            lines = [string.Empty];

        if (lines.Length > 1)
        {
            string continuationPrefix = prefix + GetMessageContinuationPrefix(lines[0]);
            double indent = MeasureTextWidth(continuationPrefix, owner);
            if (indent > 0)
            {
                paragraph.Margin = new Thickness(indent, 0, 0, 0);
                paragraph.TextIndent = -indent;
            }
        }

        paragraph.Inlines.Add(new Run(prefix));
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new Run(lines[i]));
        }

        return paragraph;
    }

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string GetMessageContinuationPrefix(string firstMessageLine)
    {
        int chineseColon = firstMessageLine.LastIndexOf('：');
        if (chineseColon >= 0)
            return firstMessageLine[..(chineseColon + 1)];

        int asciiColon = firstMessageLine.LastIndexOf(": ", StringComparison.Ordinal);
        return asciiColon >= 0
            ? firstMessageLine[..(asciiColon + 2)]
            : string.Empty;
    }

    private static double MeasureTextWidth(string text, WpfRichTextBox owner)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var dpi = VisualTreeHelper.GetDpi(owner);
        var typeface = new Typeface(owner.FontFamily, owner.FontStyle, owner.FontWeight, owner.FontStretch);
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            owner.FlowDirection,
            typeface,
            owner.FontSize,
            System.Windows.Media.Brushes.White,
            dpi.PixelsPerDip);
        return Math.Ceiling(formatted.WidthIncludingTrailingWhitespace);
    }
}
