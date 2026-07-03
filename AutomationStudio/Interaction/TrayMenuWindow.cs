using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace AutomationStudioWpf.Interaction;

public sealed class TrayMenuWindow : Window
{
    private static readonly SolidColorBrush BackgroundBrush = FrozenBrush(24, 30, 38);
    private static readonly SolidColorBrush ChromeBorderBrush = FrozenBrush(58, 72, 90);
    private static readonly SolidColorBrush HoverBrush = FrozenBrush(45, 58, 74);
    private static readonly SolidColorBrush PressedBrush = FrozenBrush(36, 48, 63);
    private static readonly SolidColorBrush TextBrush = FrozenBrush(232, 237, 245);
    private static readonly SolidColorBrush MutedBrush = FrozenBrush(150, 162, 178);
    private bool _isClosing;

    public TrayMenuWindow(Action openPanel, Action exitApplication)
    {
        Width = 168;
        Height = 150;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = WpfBrushes.Transparent;
        Topmost = true;
        Focusable = true;

        var root = new Border
        {
            Background = BackgroundBrush,
            BorderBrush = ChromeBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 18,
                ShadowDepth = 4,
                Opacity = 0.35,
            },
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "AutomationStudio",
            Foreground = TextBrush,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 4, 8, 8),
        });
        panel.Children.Add(new Border
        {
            Height = 1,
            Background = ChromeBorderBrush,
            Opacity = 0.75,
            Margin = new Thickness(4, 0, 4, 8),
        });
        panel.Children.Add(CreateMenuButton("打开面板", "恢复主窗口", openPanel));
        panel.Children.Add(CreateMenuButton("退出程序", "关闭 AutomationStudio", exitApplication, isDanger: true));
        root.Child = panel;
        Content = root;

        Deactivated += (_, _) => CloseSafe();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CloseSafe();
            }
        };
    }

    public void ShowNear(DrawingPoint screenPoint, Window owner)
    {
        var point = ToDip(owner, screenPoint);
        var workArea = ToDip(owner, System.Windows.Forms.Screen.FromPoint(screenPoint).WorkingArea);
        Left = Math.Clamp(point.X - Width + 12, workArea.Left + 6, workArea.Right - Width - 6);
        Top = Math.Clamp(point.Y - Height - 8, workArea.Top + 6, workArea.Bottom - Height - 6);
        Show();
        Activate();
    }

    private WpfButton CreateMenuButton(string title, string subtitle, Action action, bool isDanger = false)
    {
        var icon = new WpfRectangle
        {
            Width = 8,
            Height = 8,
            RadiusX = 4,
            RadiusY = 4,
            Fill = isDanger ? FrozenBrush(255, 107, 107) : FrozenBrush(79, 163, 255),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 10, 0),
        };

        var textPanel = new StackPanel();
        textPanel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = TextBrush,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = MutedBrush,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
        });

        var content = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Children = { icon, textPanel },
        };

        var button = new WpfButton
        {
            Content = content,
            Height = 44,
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(8, 0, 8, 0),
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
            Background = WpfBrushes.Transparent,
            BorderBrush = WpfBrushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = BuildButtonTemplate(),
        };
        button.Click += (_, _) =>
        {
            CloseSafe();
            action();
        };
        return button;
    }

    private static ControlTemplate BuildButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Root";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(WpfButton.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(WpfButton.PaddingProperty));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Left);
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(WpfButton)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, HoverBrush, "Root"));
        template.Triggers.Add(hover);
        var pressed = new Trigger { Property = WpfButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, PressedBrush, "Root"));
        template.Triggers.Add(pressed);
        return template;
    }

    private void CloseSafe()
    {
        if (_isClosing)
            return;

        _isClosing = true;
        Close();
    }

    private static WpfPoint ToDip(Window owner, DrawingPoint point)
    {
        var source = PresentationSource.FromVisual(owner);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return transform.Transform(new WpfPoint(point.X, point.Y));
    }

    private static Rect ToDip(Window owner, DrawingRectangle rectangle)
    {
        var source = PresentationSource.FromVisual(owner);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new WpfPoint(rectangle.Left, rectangle.Top));
        var bottomRight = transform.Transform(new WpfPoint(rectangle.Right, rectangle.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private static SolidColorBrush FrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(WpfColor.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
