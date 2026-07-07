using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace AutomationStudioWpf.Interaction;

internal sealed class FinalCodePreviewWindow : Window
{
    private readonly WpfTextBox _textBox = new();
    private readonly TextBlock _statusText = new();
    private Border _shell = null!;
    private Border _header = null!;
    private TextBlock _titleText = null!;
    private bool _hasError;

    public FinalCodePreviewWindow(Window owner)
    {
        Owner = owner;
        Title = "显示最终代码";
        Width = 980;
        Height = 760;
        MinWidth = 720;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Icon = WindowIconHelper.AppIcon;

        Content = BuildContent();
        RefreshTheme();
        Closed += (_, _) => IsClosed = true;
    }

    public bool IsClosed { get; private set; }

    public void SetPreview(string text, string? errorMessage)
    {
        _textBox.Text = text;
        _hasError = !string.IsNullOrWhiteSpace(errorMessage);
        _statusText.Text = _hasError
            ? $"生成失败：{errorMessage}"
            : "只读预览";
        ThemeResourceHelper.SetResource(_statusText, TextBlock.ForegroundProperty, _hasError ? "LogErrorBrush" : "EditorMutedTextBrush");
        Dispatcher.BeginInvoke(new Action(() => _textBox.CaretIndex = 0));
    }

    public void RefreshTheme()
    {
        ThemeResourceHelper.SetResource(this, BackgroundProperty, "EditorPanelBackgroundBrush");
        ThemeResourceHelper.SetResource(this, ForegroundProperty, "EditorTextBrush");
        ThemeResourceHelper.SetResource(_shell, Border.BackgroundProperty, "EditorPanelBackgroundBrush");
        ThemeResourceHelper.SetResource(_shell, Border.BorderBrushProperty, "EditorPanelBorderBrush");
        ThemeResourceHelper.SetResource(_header, Border.BackgroundProperty, "EditorPanelElevatedBrush");
        ThemeResourceHelper.SetResource(_header, Border.BorderBrushProperty, "EditorPanelBorderBrush");
        ThemeResourceHelper.SetResource(_titleText, TextBlock.ForegroundProperty, "EditorTextBrightBrush");
        ThemeResourceHelper.SetResource(_statusText, TextBlock.ForegroundProperty, _hasError ? "LogErrorBrush" : "EditorMutedTextBrush");
        ThemeResourceHelper.SetResource(_textBox, WpfTextBox.BackgroundProperty, "EditorChromeBrush");
        ThemeResourceHelper.SetResource(_textBox, WpfTextBox.ForegroundProperty, "EditorTextBrush");
        ThemeResourceHelper.SetResource(_textBox, WpfTextBox.BorderBrushProperty, "EditorPanelBorderBrush");
        ThemeResourceHelper.SetResource(_textBox, WpfTextBox.CaretBrushProperty, "EditorTextBrush");
        ThemeResourceHelper.SetResource(_textBox, WpfTextBox.SelectionBrushProperty, "EditorListSelectedBrush");
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
        _shell = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14),
        };

        var root = new DockPanel
        {
            LastChildFill = true,
        };
        _shell.Child = root;

        _header = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 10),
        };
        var headerDock = new DockPanel { LastChildFill = true };
        _header.Child = headerDock;

        _titleText = new TextBlock
        {
            Text = "显示最终代码",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 10, 0),
        };
        headerDock.Children.Add(_titleText);

        DockPanel.SetDock(_statusText, Dock.Right);
        _statusText.Text = "只读预览";
        _statusText.VerticalAlignment = VerticalAlignment.Center;
        headerDock.Children.Add(_statusText);

        DockPanel.SetDock(_header, Dock.Top);
        root.Children.Add(_header);

        _textBox.IsReadOnly = true;
        _textBox.AcceptsReturn = true;
        _textBox.AcceptsTab = true;
        _textBox.TextWrapping = TextWrapping.Wrap;
        _textBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _textBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _textBox.FontFamily = new WpfFontFamily("Consolas");
        _textBox.FontSize = 13;
        _textBox.BorderThickness = new Thickness(1);
        _textBox.Padding = new Thickness(12);
        _textBox.SelectionOpacity = 0.35;
        _textBox.IsUndoEnabled = false;
        _textBox.SpellCheck.IsEnabled = false;
        _textBox.ContextMenu = BuildContextMenu();

        root.Children.Add(_textBox);
        return _shell;
    }

    private WpfContextMenu BuildContextMenu()
    {
        var menu = new WpfContextMenu();
        ThemeResourceHelper.SetResource(menu, WpfContextMenu.BackgroundProperty, "DropdownBackgroundBrush");
        ThemeResourceHelper.SetResource(menu, WpfContextMenu.BorderBrushProperty, "DropdownBorderBrush");
        ThemeResourceHelper.SetResource(menu, WpfContextMenu.ForegroundProperty, "DropdownTextBrush");
        menu.Items.Add(CreateMenuItem("全选", (_, _) => _textBox.SelectAll()));
        menu.Items.Add(CreateMenuItem("复制", (_, _) => _textBox.Copy()));
        return menu;
    }

    private static WpfMenuItem CreateMenuItem(string header, RoutedEventHandler click)
    {
        var item = new WpfMenuItem { Header = header };
        ThemeResourceHelper.SetResource(item, WpfMenuItem.ForegroundProperty, "DropdownTextBrush");
        item.Click += click;
        return item;
    }
}
