using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
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
    private static readonly SolidColorBrush ButtonHoverBackgroundBrush = FrozenBrush(45, 56, 70);
    private static readonly SolidColorBrush ButtonPressedBackgroundBrush = FrozenBrush(31, 39, 50);
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
        var windowForegroundBrush = ResourceBrush("EditorTextBrightBrush", 232, 237, 245);
        var windowBackgroundBrush = ResourceBrush("EditorPanelBackgroundBrush", 27, 32, 40);
        var windowBorderBrush = ResourceBrush("EditorPanelBorderBrush", 64, 76, 94);
        var bodyForegroundBrush = ResourceBrush("EditorTextBrush", 232, 237, 245);
        var buttonForegroundBrush = ResourceBrush("EditorTextBrightBrush", 232, 237, 245);
        var buttonBackgroundBrush = ResourceBrush("EditorToolbarGroupBrush", 36, 43, 53);
        var buttonHoverBackgroundBrush = ResourceBrush("EditorChromeHighlightBrush", 45, 56, 70);
        var buttonPressedBackgroundBrush = ResourceBrush("EditorListSelectedBrush", 31, 39, 50);
        var buttonBorderBrush = ResourceBrush("EditorPanelBorderBrush", 79, 94, 116);
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
            Foreground = windowForegroundBrush,
        };

        var root = new Border
        {
            Background = windowBackgroundBrush,
            BorderBrush = windowBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 22,
                ShadowDepth = 5,
                Opacity = 0.35,
            },
        };

        var panel = new StackPanel();
        var titleRow = new DockPanel
        {
            LastChildFill = true,
            Margin = new Thickness(0, 0, 0, string.IsNullOrWhiteSpace(message) ? 10 : 12),
        };
        titleRow.Children.Add(new Border
        {
            Width = 4,
            Height = 20,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(accent),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var titleText = new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(accent),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        titleRow.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                window.DragMove();
        };
        titleRow.Children.Add(titleText);
        panel.Children.Add(titleRow);

        if (!string.IsNullOrWhiteSpace(message))
        {
            panel.Children.Add(new TextBlock
            {
                Text = message,
                Foreground = bodyForegroundBrush,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                Margin = new Thickness(0, 0, 0, 18),
            });
        }

        var buttonPanel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, string.IsNullOrWhiteSpace(message) ? 4 : 0, 0, 0),
        };

        for (var i = 0; i < buttons.Length; i++)
        {
            var item = buttons[i];
            var normalBackground = item.IsPrimary ? new SolidColorBrush(accent) : buttonBackgroundBrush;
            var hoverBackground = item.IsPrimary ? new SolidColorBrush(Lighten(accent, 16)) : buttonHoverBackgroundBrush;
            var pressedBackground = item.IsPrimary ? new SolidColorBrush(Darken(accent, 18)) : buttonPressedBackgroundBrush;
            var button = new System.Windows.Controls.Button
            {
                Content = item.Text,
                MinWidth = 82,
                Height = 30,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                IsDefault = item.IsPrimary,
                IsCancel = item.Result == MessageBoxResult.Cancel,
                Foreground = item.IsPrimary ? ContrastBrush(accent) : buttonForegroundBrush,
                Background = normalBackground,
                BorderBrush = buttonBorderBrush,
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                Template = BuildButtonTemplate(),
            };
            button.MouseEnter += (_, _) => button.Background = hoverBackground;
            button.MouseLeave += (_, _) => button.Background = normalBackground;
            button.PreviewMouseLeftButtonDown += (_, _) => button.Background = pressedBackground;
            button.PreviewMouseLeftButtonUp += (_, _) => button.Background = button.IsMouseOver ? hoverBackground : normalBackground;
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
        window.Loaded += (_, _) =>
        {
            buttonPanel.Children.OfType<System.Windows.Controls.Button>().FirstOrDefault(button => button.IsDefault)?.Focus();
        };
        window.ShowDialog();
        return result;
    }

    private static ControlTemplate BuildButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "ButtonBorder";
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(System.Windows.Controls.Button.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(System.Windows.Controls.Button.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(System.Windows.Controls.Button.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(System.Windows.Controls.Button.PaddingProperty));
        presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(System.Windows.Controls.Button)) { VisualTree = border };
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.55, "ButtonBorder"));
        template.Triggers.Add(disabled);

        var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        focused.Setters.Add(new Setter(Border.BorderBrushProperty, FrozenBrush(116, 178, 255), "ButtonBorder"));
        template.Triggers.Add(focused);
        return template;
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

    private static WpfColor Lighten(WpfColor color, byte amount) =>
        WpfColor.FromRgb(Add(color.R, amount), Add(color.G, amount), Add(color.B, amount));

    private static WpfColor Darken(WpfColor color, byte amount) =>
        WpfColor.FromRgb(Subtract(color.R, amount), Subtract(color.G, amount), Subtract(color.B, amount));

    private static byte Add(byte value, byte amount) => (byte)System.Math.Min(255, value + amount);

    private static byte Subtract(byte value, byte amount) => (byte)System.Math.Max(0, value - amount);

    private static SolidColorBrush ResourceBrush(string key, byte fallbackR, byte fallbackG, byte fallbackB)
    {
        if (WpfApplication.Current?.TryFindResource(key) is SolidColorBrush brush)
            return brush;

        return FrozenBrush(fallbackR, fallbackG, fallbackB);
    }

    private static SolidColorBrush ContrastBrush(WpfColor background)
    {
        var luminance = (0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B);
        return luminance > 150
            ? FrozenBrush(12, 16, 22)
            : FrozenBrush(255, 255, 255);
    }

    private static SolidColorBrush FrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(WpfColor.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
