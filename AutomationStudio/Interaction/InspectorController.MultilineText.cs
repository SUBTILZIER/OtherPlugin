using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AutomationStudioWpf.Graph;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace AutomationStudioWpf.Interaction;

public sealed partial class InspectorController
{
    private const int MultilineTextBoxMinLines = 1;
    private const int MultilineTextBoxMaxLines = 8;
    private const double MultilineTextBoxChromePadding = 14.0;

    private void ConfigureStaticFreeTextEditors()
    {
        ConfigureFreeTextBox(_printLogMessageTextBox, multiline: true);
    }

    private void ConfigureCommonTextModes(NodeKind kind)
    {
        ConfigureFreeTextBox(_commonTextBox, IsCommonTextMultiline(kind, 1));
        ConfigureFreeTextBox(_commonText2Box, IsCommonTextMultiline(kind, 2));
        ConfigureFreeTextBox(_commonText3Box, IsCommonTextMultiline(kind, 3));
    }

    private static string ReadCommonTextValue(NodeKind kind, int index, WpfTextBox textBox)
    {
        return IsCommonTextMultiline(kind, index)
            ? textBox.Text
            : textBox.Text.Trim();
    }

    private static bool IsCommonTextMultiline(NodeKind kind, int index)
    {
        return kind switch
        {
            NodeKind.Compare => index is 1 or 2,
            NodeKind.ShowMessage => index == 1,
            _ => false,
        };
    }

    private static void ConfigureFreeTextBox(WpfTextBox textBox, bool multiline)
    {
        textBox.TextChanged -= MultilineTextBox_TextChanged;
        textBox.Loaded -= MultilineTextBox_Loaded;
        textBox.SizeChanged -= MultilineTextBox_SizeChanged;

        if (!multiline)
        {
            textBox.AcceptsReturn = false;
            textBox.AcceptsTab = false;
            textBox.TextWrapping = TextWrapping.NoWrap;
            textBox.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            textBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            textBox.ClearValue(FrameworkElement.HeightProperty);
            textBox.ClearValue(FrameworkElement.MinHeightProperty);
            textBox.ClearValue(FrameworkElement.MaxHeightProperty);
            return;
        }

        textBox.AcceptsReturn = true;
        textBox.AcceptsTab = false;
        textBox.TextWrapping = TextWrapping.Wrap;
        textBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        textBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        textBox.TextChanged += MultilineTextBox_TextChanged;
        textBox.Loaded += MultilineTextBox_Loaded;
        textBox.SizeChanged += MultilineTextBox_SizeChanged;
        QueueMultilineTextBoxHeightUpdate(textBox);
    }

    private static void MultilineTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is WpfTextBox textBox)
            QueueMultilineTextBoxHeightUpdate(textBox);
    }

    private static void MultilineTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is WpfTextBox textBox)
            QueueMultilineTextBoxHeightUpdate(textBox);
    }

    private static void MultilineTextBox_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is WpfTextBox textBox && Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5)
            QueueMultilineTextBoxHeightUpdate(textBox);
    }

    private static void QueueMultilineTextBoxHeightUpdate(WpfTextBox textBox)
    {
        if (!textBox.IsLoaded)
        {
            UpdateMultilineTextBoxHeight(textBox);
            return;
        }

        textBox.Dispatcher.BeginInvoke(
            () => UpdateMultilineTextBoxHeight(textBox),
            DispatcherPriority.Background);
    }

    private static void UpdateMultilineTextBoxHeight(WpfTextBox textBox)
    {
        if (!textBox.AcceptsReturn)
            return;

        int lineCount = CountTextLines(textBox.Text);
        if (textBox.IsLoaded && textBox.LineCount > 0)
            lineCount = Math.Max(lineCount, textBox.LineCount);

        int visibleLines = Math.Clamp(lineCount, MultilineTextBoxMinLines, MultilineTextBoxMaxLines);
        double lineHeight = Math.Max(16.0, textBox.FontSize * 1.45);
        double minHeight = Math.Ceiling(lineHeight * MultilineTextBoxMinLines + MultilineTextBoxChromePadding);
        double maxHeight = Math.Ceiling(lineHeight * MultilineTextBoxMaxLines + MultilineTextBoxChromePadding);
        double targetHeight = Math.Ceiling(lineHeight * visibleLines + MultilineTextBoxChromePadding);

        textBox.MinHeight = minHeight;
        textBox.MaxHeight = maxHeight;
        textBox.Height = Math.Min(maxHeight, Math.Max(minHeight, targetHeight));
    }

    private static int CountTextLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 1;

        int count = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                count++;
                continue;
            }

            if (text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n'))
                count++;
        }

        return count;
    }
}
