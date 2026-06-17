using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace AutomationStudioWpf.Interaction;

public sealed record ThemedDialogButton(string Text, MessageBoxResult Result, bool IsPrimary = false);

public static class ThemedDialog
{
    private static readonly SolidColorBrush WindowForegroundBrush = FrozenBrush(232, 237, 245);
    private static readonly SolidColorBrush WindowBackgroundBrush = FrozenBrush(27, 32, 40);
    private static readonly SolidColorBrush WindowBorderBrush = FrozenBrush(64, 76, 94);
    private static readonly SolidColorBrush BodyForegroundBrush = FrozenBrush(232, 237, 245);
    private static readonly SolidColorBrush ButtonForegroundBrush = FrozenBrush(232, 237, 245);
    private static readonly SolidColorBrush PrimaryButtonForegroundBrush = FrozenBrush(12, 16, 22);
    private static readonly SolidColorBrush ButtonBackgroundBrush = FrozenBrush(36, 43, 53);
    private static readonly SolidColorBrush ButtonBorderBrush = FrozenBrush(79, 94, 116);

    public static MessageBoxResult Show(Window? owner, string message, string title, MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
    {
        return ShowCustom(owner, message, title, image, BuildButtons(buttons));
    }

    public static MessageBoxResult ShowCustom(Window? owner, string message, string title, MessageBoxImage image, params ThemedDialogButton[] buttons)
    {
        if (buttons.Length == 0)
            buttons = [new ThemedDialogButton("确定", MessageBoxResult.OK, true)];

        var result = GetFallbackResult(buttons);
        var accent = GetAccentColor(image);
        var window = new Window
        {
            Owner = owner,
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = WindowForegroundBrush,
        };

        var root = new Border
        {
            Background = WindowBackgroundBrush,
            BorderBrush = WindowBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18),
        };

        var panel = new StackPanel();
        var titleText = new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(accent),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
        };
        titleText.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                window.DragMove();
        };
        panel.Children.Add(titleText);

        panel.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = BodyForegroundBrush,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
            Margin = new Thickness(0, 0, 0, 18),
        });

        var buttonPanel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
        };

        foreach (var item in buttons)
        {
            var button = new System.Windows.Controls.Button
            {
                Content = item.Text,
                MinWidth = 82,
                Height = 30,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = item.IsPrimary,
                IsCancel = item.Result == MessageBoxResult.Cancel,
                Foreground = item.IsPrimary ? PrimaryButtonForegroundBrush : ButtonForegroundBrush,
                Background = item.IsPrimary ? new SolidColorBrush(accent) : ButtonBackgroundBrush,
                BorderBrush = ButtonBorderBrush,
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            button.Click += (_, _) =>
            {
                result = item.Result;
                window.Close();
            };
            buttonPanel.Children.Add(button);
        }

        panel.Children.Add(buttonPanel);
        root.Child = panel;
        window.Content = root;
        window.ShowDialog();
        return result;
    }

    private static ThemedDialogButton[] BuildButtons(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.OK => [new("确定", MessageBoxResult.OK, true)],
        MessageBoxButton.OKCancel => [new("确定", MessageBoxResult.OK, true), new("取消", MessageBoxResult.Cancel)],
        MessageBoxButton.YesNo => [new("是", MessageBoxResult.Yes, true), new("否", MessageBoxResult.No)],
        MessageBoxButton.YesNoCancel => [new("是", MessageBoxResult.Yes, true), new("否", MessageBoxResult.No), new("取消", MessageBoxResult.Cancel)],
        _ => [new("确定", MessageBoxResult.OK, true)],
    };

    private static MessageBoxResult GetFallbackResult(IReadOnlyList<ThemedDialogButton> buttons) =>
        buttons.FirstOrDefault(button => button.Result == MessageBoxResult.Cancel)?.Result
        ?? buttons.FirstOrDefault(button => button.Result == MessageBoxResult.No)?.Result
        ?? buttons[0].Result;

    private static WpfColor GetAccentColor(MessageBoxImage image) => image switch
    {
        MessageBoxImage.Error => WpfColor.FromRgb(255, 107, 107),
        MessageBoxImage.Warning => WpfColor.FromRgb(214, 138, 34),
        MessageBoxImage.Question => WpfColor.FromRgb(79, 163, 255),
        MessageBoxImage.Information => WpfColor.FromRgb(79, 163, 255),
        _ => WpfColor.FromRgb(167, 177, 191),
    };

    private static SolidColorBrush FrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(WpfColor.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
