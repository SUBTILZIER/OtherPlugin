using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using AutomationStudioWpf.Services;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfControl = System.Windows.Controls.Control;
using WpfCursors = System.Windows.Input.Cursors;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfPoint = System.Windows.Point;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace AutomationStudioWpf.Interaction;

internal sealed class AccentColorPickerWindow : Window
{
    private const double PlaneWidth = 300;
    private const double PlaneHeight = 190;

    private readonly Border _colorPlane;
    private readonly WpfRectangle _hueFill;
    private readonly Ellipse _selector;
    private readonly Slider _hueSlider;
    private readonly WpfTextBox _hexTextBox;
    private readonly Border _preview;
    private bool _dragging;
    private bool _updating;
    private double _hue;
    private double _saturation;
    private double _value;

    public AccentColorPickerWindow(Window owner, string initialHex)
    {
        Owner = owner;
        Title = "选择强调色";
        Width = 430;
        Height = 430;
        MinWidth = 400;
        MinHeight = 390;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = WpfBrushes.Transparent;
        Icon = WindowIconHelper.AppIcon;

        if (!AppThemeService.TryParseColor(initialHex, out var startColor))
            AppThemeService.TryParseColor("#4FA3FF", out startColor);
        FromColor(startColor, out _hue, out _saturation, out _value);

        var root = new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect
            {
                BlurRadius = 22,
                ShadowDepth = 8,
                Opacity = 0.30,
                Color = Colors.Black,
            },
        };
        SetResource(root, Border.BackgroundProperty, "EditorPanelBackgroundBrush");
        SetResource(root, Border.BorderBrushProperty, "EditorPanelBorderBrush");

        var layout = new DockPanel { LastChildFill = true };
        root.Child = layout;

        var titleBar = BuildTitleBar();
        DockPanel.SetDock(titleBar, Dock.Top);
        layout.Children.Add(titleBar);

        var buttonBar = BuildButtonBar();
        DockPanel.SetDock(buttonBar, Dock.Bottom);
        layout.Children.Add(buttonBar);

        var content = new StackPanel
        {
            Margin = new Thickness(18, 14, 18, 12),
        };
        layout.Children.Add(content);

        _hueFill = new WpfRectangle();
        _selector = new Ellipse
        {
            Width = 16,
            Height = 16,
            StrokeThickness = 2,
            Fill = WpfBrushes.Transparent,
            IsHitTestVisible = false,
            HorizontalAlignment = WpfHorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        SetResource(_selector, Shape.StrokeProperty, "AccentForegroundBrush");

        var planeGrid = new Grid
        {
            Width = PlaneWidth,
            Height = PlaneHeight,
            ClipToBounds = true,
        };
        planeGrid.Children.Add(_hueFill);
        planeGrid.Children.Add(new WpfRectangle
        {
            Fill = new LinearGradientBrush(
                Colors.White,
                WpfColor.FromArgb(0, 255, 255, 255),
                new WpfPoint(0, 0.5),
                new WpfPoint(1, 0.5)),
        });
        planeGrid.Children.Add(new WpfRectangle
        {
            Fill = new LinearGradientBrush(
                WpfColor.FromArgb(0, 0, 0, 0),
                Colors.Black,
                new WpfPoint(0.5, 0),
                new WpfPoint(0.5, 1)),
        });
        planeGrid.Children.Add(_selector);

        _colorPlane = new Border
        {
            Width = PlaneWidth,
            Height = PlaneHeight,
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Child = planeGrid,
            Cursor = WpfCursors.Cross,
        };
        SetResource(_colorPlane, Border.BorderBrushProperty, "EditorPanelStrongBorderBrush");
        _colorPlane.PreviewMouseLeftButtonDown += ColorPlane_MouseDown;
        _colorPlane.PreviewMouseMove += ColorPlane_MouseMove;
        _colorPlane.PreviewMouseLeftButtonUp += ColorPlane_MouseUp;
        content.Children.Add(_colorPlane);

        var hueRow = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        hueRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hueRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hueRow.Children.Add(Label("色相"));
        _hueSlider = new Slider
        {
            Minimum = 0,
            Maximum = 360,
            Value = _hue,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _hueSlider.ValueChanged += (_, _) =>
        {
            if (_updating)
                return;
            _hue = _hueSlider.Value;
            UpdateVisuals();
        };
        Grid.SetColumn(_hueSlider, 1);
        hueRow.Children.Add(_hueSlider);
        content.Children.Add(hueRow);

        var hexRow = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        hexRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hexRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hexRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hexRow.Children.Add(Label("Hex"));
        _hexTextBox = new WpfTextBox
        {
            Margin = new Thickness(12, 0, 12, 0),
            MinHeight = 32,
            Padding = new Thickness(9, 5, 9, 5),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        SetResource(_hexTextBox, WpfControl.BackgroundProperty, "InputBackgroundBrush");
        SetResource(_hexTextBox, WpfControl.ForegroundProperty, "EditorTextBrush");
        SetResource(_hexTextBox, WpfControl.BorderBrushProperty, "InputBorderBrush");
        _hexTextBox.TextChanged += (_, _) =>
        {
            if (_updating || !AppThemeService.TryParseColor(_hexTextBox.Text, out var parsed))
                return;
            FromColor(parsed, out _hue, out _saturation, out _value);
            UpdateVisuals();
        };
        Grid.SetColumn(_hexTextBox, 1);
        hexRow.Children.Add(_hexTextBox);

        _preview = new Border
        {
            Width = 46,
            Height = 32,
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1),
        };
        SetResource(_preview, Border.BorderBrushProperty, "EditorPanelStrongBorderBrush");
        Grid.SetColumn(_preview, 2);
        hexRow.Children.Add(_preview);
        content.Children.Add(hexRow);

        Content = root;
        Loaded += (_, _) => UpdateVisuals();
    }

    public string? SelectedColorHex { get; private set; }

    private Border BuildTitleBar()
    {
        var titleBar = new Border
        {
            Padding = new Thickness(18, 13, 12, 11),
            CornerRadius = new CornerRadius(14, 14, 0, 0),
        };
        SetResource(titleBar, Border.BackgroundProperty, "EditorChromeBrush");
        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "选择强调色",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        SetResource(title, TextBlock.ForegroundProperty, "EditorTextBrightBrush");
        grid.Children.Add(title);

        var close = new WpfButton
        {
            Content = "×",
            Width = 32,
            Height = 28,
            Padding = new Thickness(0),
            FontSize = 17,
        };
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 1);
        grid.Children.Add(close);
        titleBar.Child = grid;
        return titleBar;
    }

    private Border BuildButtonBar()
    {
        var bar = new Border
        {
            Padding = new Thickness(18, 12, 18, 16),
            BorderThickness = new Thickness(0, 1, 0, 0),
        };
        SetResource(bar, Border.BackgroundProperty, "EditorChromeBrush");
        SetResource(bar, Border.BorderBrushProperty, "EditorSectionHeaderBrush");

        var buttons = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right,
        };

        var ok = new WpfButton
        {
            Content = "确定",
            MinWidth = 82,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
        };
        ok.Click += (_, _) =>
        {
            SelectedColorHex = AppThemeService.ToHex(ToColor(_hue, _saturation, _value));
            DialogResult = true;
        };
        buttons.Children.Add(ok);

        var cancel = new WpfButton
        {
            Content = "取消",
            MinWidth = 82,
            Height = 32,
            IsCancel = true,
        };
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(cancel);

        bar.Child = buttons;
        return bar;
    }

    private void ColorPlane_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _colorPlane.CaptureMouse();
        UpdateFromPoint(e.GetPosition(_colorPlane));
    }

    private void ColorPlane_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_dragging)
            UpdateFromPoint(e.GetPosition(_colorPlane));
    }

    private void ColorPlane_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        _colorPlane.ReleaseMouseCapture();
        UpdateFromPoint(e.GetPosition(_colorPlane));
    }

    private void UpdateFromPoint(WpfPoint point)
    {
        var width = _colorPlane.ActualWidth > 0 ? _colorPlane.ActualWidth : PlaneWidth;
        var height = _colorPlane.ActualHeight > 0 ? _colorPlane.ActualHeight : PlaneHeight;
        _saturation = Math.Clamp(point.X / width, 0, 1);
        _value = 1 - Math.Clamp(point.Y / height, 0, 1);
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        _updating = true;
        var hueColor = ToColor(_hue, 1, 1);
        _hueFill.Fill = new SolidColorBrush(hueColor);
        var selected = ToColor(_hue, _saturation, _value);
        _preview.Background = new SolidColorBrush(selected);
        _hexTextBox.Text = AppThemeService.ToHex(selected);
        _hueSlider.Value = _hue;

        var width = _colorPlane.ActualWidth > 0 ? _colorPlane.ActualWidth : PlaneWidth;
        var height = _colorPlane.ActualHeight > 0 ? _colorPlane.ActualHeight : PlaneHeight;
        _selector.Margin = new Thickness(
            (_saturation * width) - (_selector.Width / 2),
            ((1 - _value) * height) - (_selector.Height / 2),
            0,
            0);
        _updating = false;
    }

    private static TextBlock Label(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            Width = 44,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        SetResource(label, TextBlock.ForegroundProperty, "EditorTextBrush");
        return label;
    }

    private static void SetResource(DependencyObject target, DependencyProperty property, string key)
    {
        if (target is FrameworkElement element)
            element.SetResourceReference(property, key);
        else if (target is FrameworkContentElement contentElement)
            contentElement.SetResourceReference(property, key);
    }

    private static WpfColor ToColor(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);

        var c = value * saturation;
        var x = c * (1 - Math.Abs((hue / 60 % 2) - 1));
        var m = value - c;
        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };

        return WpfColor.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private static void FromColor(WpfColor color, out double hue, out double saturation, out double value)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        hue = delta == 0 ? 0 :
            max == r ? 60 * (((g - b) / delta) % 6) :
            max == g ? 60 * (((b - r) / delta) + 2) :
            60 * (((r - g) / delta) + 4);
        if (hue < 0)
            hue += 360;
        saturation = max == 0 ? 0 : delta / max;
        value = max;
    }
}
