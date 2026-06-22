using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace AutomationStudioWpf.Interaction;

internal sealed class MousePickOverlayWindow : Window
{
    private static readonly SolidColorBrush BackgroundBrush = FrozenBrush(16, 20, 27, 235);
    private static readonly SolidColorBrush BorderBrushValue = FrozenBrush(79, 163, 255);
    private static readonly SolidColorBrush TextBrush = FrozenBrush(232, 237, 245);
    private static readonly SolidColorBrush MutedTextBrush = FrozenBrush(167, 177, 191);
    private static readonly SolidColorBrush ColorPreviewBorderBrush = FrozenBrush(90, 103, 126);

    private readonly TextBlock _coordinateText = new();
    private readonly TextBlock _rgbText = new();
    private readonly TextBlock _hexText = new();
    private readonly Border _colorPreview = new();
    private ScreenPickSample? _currentSample;

    public MousePickOverlayWindow(Window owner)
    {
        Owner = owner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = System.Windows.Media.Brushes.Transparent;
        Topmost = true;
        IsHitTestVisible = false;

        Content = CreateContent();
        SourceInitialized += (_, _) => ApplyToolWindowStyles();
    }

    public void Update(ScreenPickSample sample)
    {
        var previous = _currentSample;
        if (previous is null || previous.Value.X != sample.X || previous.Value.Y != sample.Y)
            _coordinateText.Text = $"坐标  {sample.X}, {sample.Y}";

        if (previous is null || previous.Value.R != sample.R || previous.Value.G != sample.G || previous.Value.B != sample.B)
        {
            _rgbText.Text = sample.RgbText;
            _hexText.Text = sample.HexText;
            _colorPreview.Background = new SolidColorBrush(WpfColor.FromRgb(sample.R, sample.G, sample.B));
        }

        _currentSample = sample;
        MoveNear(sample.X, sample.Y);
    }

    private UIElement CreateContent()
    {
        var root = new Border
        {
            Background = BackgroundBrush,
            BorderBrush = BorderBrushValue,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10, 8, 10, 8),
        };

        var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Vertical };
        _coordinateText.Foreground = TextBrush;
        _coordinateText.FontSize = 12;
        _coordinateText.FontWeight = FontWeights.SemiBold;
        panel.Children.Add(_coordinateText);

        var colorRow = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 5, 0, 0),
        };
        _colorPreview.Width = 34;
        _colorPreview.Height = 18;
        _colorPreview.BorderBrush = ColorPreviewBorderBrush;
        _colorPreview.BorderThickness = new Thickness(1);
        _colorPreview.CornerRadius = new CornerRadius(3);
        _colorPreview.Margin = new Thickness(0, 0, 8, 0);
        colorRow.Children.Add(_colorPreview);

        var colorTexts = new StackPanel { Orientation = System.Windows.Controls.Orientation.Vertical };
        _rgbText.Foreground = TextBrush;
        _rgbText.FontSize = 12;
        _hexText.Foreground = TextBrush;
        _hexText.FontSize = 12;
        colorTexts.Children.Add(_rgbText);
        colorTexts.Children.Add(_hexText);
        colorRow.Children.Add(colorTexts);
        panel.Children.Add(colorRow);

        panel.Children.Add(new TextBlock
        {
            Text = "左键复制  右键退出",
            Foreground = MutedTextBrush,
            FontSize = 11,
            Margin = new Thickness(0, 6, 0, 0),
        });

        root.Child = panel;
        return root;
    }

    private void MoveNear(int screenX, int screenY)
    {
        var screen = Screen.FromPoint(new System.Drawing.Point(screenX, screenY));
        var workArea = screen.WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = (ActualWidth > 0 ? ActualWidth : 150) * dpi.DpiScaleX;
        var height = (ActualHeight > 0 ? ActualHeight : 86) * dpi.DpiScaleY;
        double x = screenX + 18;
        double y = screenY + 22;

        if (x + width > workArea.Right)
            x = screenX - width - 18;
        if (y + height > workArea.Bottom)
            y = screenY - height - 22;

        x = Math.Max(workArea.Left + 4, Math.Min(x, workArea.Right - width - 4));
        y = Math.Max(workArea.Top + 4, Math.Min(y, workArea.Bottom - height - 4));

        Left = x / dpi.DpiScaleX;
        Top = y / dpi.DpiScaleY;
    }

    private void ApplyToolWindowStyles()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        var style = GetWindowLong(handle, GWL_EXSTYLE);
        style |= WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
        SetWindowLong(handle, GWL_EXSTYLE, style);
    }

    private static SolidColorBrush FrozenBrush(byte r, byte g, byte b, byte a = 255)
    {
        var brush = new SolidColorBrush(WpfColor.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
