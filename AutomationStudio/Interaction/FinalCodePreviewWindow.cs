using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfColor = System.Windows.Media.Color;
using WpfFontFamily = System.Windows.Media.FontFamily;

namespace AutomationStudioWpf.Interaction;

internal sealed class FinalCodePreviewWindow : Window
{
    private static readonly SolidColorBrush WindowBackgroundBrush = FrozenBrush(23, 28, 36);
    private static readonly SolidColorBrush WindowBorderBrush = FrozenBrush(58, 70, 86);
    private static readonly SolidColorBrush TitleBrush = FrozenBrush(232, 237, 245);
    private static readonly SolidColorBrush ErrorBrush = FrozenBrush(255, 107, 107);
    private static readonly SolidColorBrush TextBrush = FrozenBrush(232, 237, 245);
    private static readonly SolidColorBrush MutedBrush = FrozenBrush(167, 177, 191);
    private static readonly SolidColorBrush TextBoxBackgroundBrush = FrozenBrush(18, 22, 29);
    private static readonly SolidColorBrush TextBoxBorderBrush = FrozenBrush(69, 82, 100);

    private readonly WpfTextBox _textBox = new();
    private readonly TextBlock _statusText = new();

    public FinalCodePreviewWindow(Window owner)
    {
        Owner = owner;
        Title = "显示最终代码";
        Width = 980;
        Height = 760;
        MinWidth = 720;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = WindowBackgroundBrush;
        Foreground = TextBrush;
        ShowInTaskbar = false;

        Content = BuildContent();
        Closed += (_, _) => IsClosed = true;
    }

    public bool IsClosed { get; private set; }

    public void SetPreview(string text, string? errorMessage)
    {
        _textBox.Text = text;
        _statusText.Text = string.IsNullOrWhiteSpace(errorMessage)
            ? "只读预览"
            : $"生成失败：{errorMessage}";
        _statusText.Foreground = string.IsNullOrWhiteSpace(errorMessage) ? MutedBrush : ErrorBrush;
        Dispatcher.BeginInvoke(new Action(() => _textBox.CaretIndex = 0));
    }

    public void ActivateWindow()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Show();
        Activate();
        Focus();
        _textBox.Focus();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel
        {
            Margin = new Thickness(14),
        };

        var header = new DockPanel
        {
            LastChildFill = true,
            Margin = new Thickness(0, 0, 0, 10),
        };

        header.Children.Add(new TextBlock
        {
            Text = "显示最终代码",
            Foreground = TitleBrush,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 10, 0),
        });

        DockPanel.SetDock(_statusText, Dock.Right);
        _statusText.Text = "只读预览";
        _statusText.VerticalAlignment = VerticalAlignment.Center;
        _statusText.Foreground = MutedBrush;
        header.Children.Add(_statusText);

        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        _textBox.IsReadOnly = true;
        _textBox.AcceptsReturn = true;
        _textBox.AcceptsTab = true;
        _textBox.TextWrapping = TextWrapping.Wrap;
        _textBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _textBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _textBox.FontFamily = new WpfFontFamily("Consolas");
        _textBox.FontSize = 13;
        _textBox.Background = TextBoxBackgroundBrush;
        _textBox.Foreground = TextBrush;
        _textBox.BorderBrush = TextBoxBorderBrush;
        _textBox.BorderThickness = new Thickness(1);
        _textBox.Padding = new Thickness(12);
        _textBox.CaretBrush = TextBrush;
        _textBox.SelectionBrush = new SolidColorBrush(WpfColor.FromRgb(79, 163, 255));
        _textBox.SelectionOpacity = 0.35;
        _textBox.IsUndoEnabled = false;
        _textBox.SpellCheck.IsEnabled = false;
        _textBox.ContextMenu = BuildContextMenu();

        root.Children.Add(_textBox);
        return root;
    }

    private WpfContextMenu BuildContextMenu()
    {
        var menu = new WpfContextMenu();
        menu.Items.Add(CreateMenuItem("全选", (_, _) => _textBox.SelectAll()));
        menu.Items.Add(CreateMenuItem("复制", (_, _) => _textBox.Copy()));
        return menu;
    }

    private static WpfMenuItem CreateMenuItem(string header, RoutedEventHandler click)
    {
        var item = new WpfMenuItem { Header = header };
        item.Click += click;
        return item;
    }

    private static SolidColorBrush FrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(WpfColor.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
