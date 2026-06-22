using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace AutomationStudioWpf.Interaction;

internal sealed class MousePickChoiceWindow : Window
{
    private static readonly SolidColorBrush WindowBackgroundBrush = FrozenBrush(27, 32, 40);
    private static readonly SolidColorBrush WindowBorderBrush = FrozenBrush(79, 163, 255);
    private static readonly SolidColorBrush TextBrush = FrozenBrush(232, 237, 245);
    private static readonly SolidColorBrush MutedTextBrush = FrozenBrush(167, 177, 191);
    private static readonly SolidColorBrush ButtonBackgroundBrush = FrozenBrush(36, 43, 53);
    private static readonly SolidColorBrush ButtonBorderBrush = FrozenBrush(79, 94, 116);
    private static readonly SolidColorBrush PrimaryButtonBrush = FrozenBrush(79, 163, 255);
    private static readonly SolidColorBrush PrimaryButtonTextBrush = FrozenBrush(12, 16, 22);

    private readonly ScreenPickSample _sample;
    private readonly Action<MessageBoxResult> _completed;
    private bool _isClosing;
    private MessageBoxResult _result = MessageBoxResult.Cancel;

    private MousePickChoiceWindow(Window owner, ScreenPickSample sample, Action<MessageBoxResult> completed)
    {
        _sample = sample;
        _completed = completed;
        Owner = owner;
        Title = "鼠标拾取";
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = System.Windows.Media.Brushes.Transparent;
        Content = CreateContent();
        Closed += (_, _) => _completed(_result);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Complete(MessageBoxResult.Cancel);
        };
    }

    public static MousePickChoiceWindow ShowAt(Window owner, ScreenPickSample sample, int screenX, int screenY, Action<MessageBoxResult> completed)
    {
        var window = new MousePickChoiceWindow(owner, sample, completed);
        window.PositionNear(owner, screenX, screenY);
        window.Show();
        window.Activate();
        return window;
    }

    private UIElement CreateContent()
    {
        var root = new Border
        {
            Background = WindowBackgroundBrush,
            BorderBrush = WindowBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            MinWidth = 236,
        };

        var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Vertical };
        panel.Children.Add(new TextBlock
        {
            Text = "鼠标拾取",
            Foreground = WindowBorderBrush,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        });

        panel.Children.Add(new TextBlock
        {
            Text = $"坐标：{_sample.CoordinateText}\n颜色：{_sample.RgbText} / {_sample.HexText}",
            Foreground = TextBrush,
            FontSize = 12,
            LineHeight = 19,
            Margin = new Thickness(0, 0, 0, 10),
        });

        panel.Children.Add(new TextBlock
        {
            Text = "选择要复制的内容",
            Foreground = MutedTextBrush,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 10),
        });

        var row = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
        };
        row.Children.Add(CreateButton("复制坐标", MessageBoxResult.Yes, true));
        row.Children.Add(CreateButton("复制颜色", MessageBoxResult.No, false));
        row.Children.Add(CreateButton("取消", MessageBoxResult.Cancel, false));
        panel.Children.Add(row);

        root.Child = panel;
        return root;
    }

    private System.Windows.Controls.Button CreateButton(string text, MessageBoxResult result, bool primary)
    {
        var button = new System.Windows.Controls.Button
        {
            Content = text,
            MinWidth = 72,
            Height = 28,
            Margin = new Thickness(7, 0, 0, 0),
            Padding = new Thickness(10, 0, 10, 0),
            IsDefault = primary,
            IsCancel = result == MessageBoxResult.Cancel,
            Cursor = System.Windows.Input.Cursors.Hand,
            Foreground = primary ? PrimaryButtonTextBrush : TextBrush,
            Background = primary ? PrimaryButtonBrush : ButtonBackgroundBrush,
            BorderBrush = ButtonBorderBrush,
            BorderThickness = new Thickness(1),
        };
        button.Click += (_, _) => Complete(result);
        return button;
    }

    private void Complete(MessageBoxResult result)
    {
        if (_isClosing)
            return;

        _isClosing = true;
        _result = result;
        Close();
    }

    private void PositionNear(Window owner, int screenX, int screenY)
    {
        var dpi = VisualTreeHelper.GetDpi(owner);
        var desired = MeasureContent();
        var widthPx = desired.Width * dpi.DpiScaleX;
        var heightPx = desired.Height * dpi.DpiScaleY;
        var workArea = Screen.FromPoint(new System.Drawing.Point(screenX, screenY)).WorkingArea;

        double x = screenX + 18;
        double y = screenY + 18;

        if (x + widthPx > workArea.Right)
            x = screenX - widthPx - 18;
        if (y + heightPx > workArea.Bottom)
            y = screenY - heightPx - 18;

        x = Math.Max(workArea.Left + 4, Math.Min(x, workArea.Right - widthPx - 4));
        y = Math.Max(workArea.Top + 4, Math.Min(y, workArea.Bottom - heightPx - 4));

        Left = x / dpi.DpiScaleX;
        Top = y / dpi.DpiScaleY;
    }

    private System.Windows.Size MeasureContent()
    {
        var content = (UIElement)Content;
        content.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = content.DesiredSize;
        return new System.Windows.Size(Math.Max(desired.Width, 236), Math.Max(desired.Height, 128));
    }

    private static SolidColorBrush FrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(WpfColor.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
