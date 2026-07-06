using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using AutomationStudioWpf.Services;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfPanel = System.Windows.Controls.Panel;
using WpfRadioButton = System.Windows.Controls.RadioButton;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace AutomationStudioWpf.Interaction;

public sealed class SettingsWindow : Window
{
    private readonly Func<AppSettings, bool> _apply;
    private readonly AppSettings _draft;
    private readonly WpfRadioButton _darkThemeRadio;
    private readonly WpfRadioButton _lightThemeRadio;
    private readonly WpfTextBox _accentTextBox;
    private readonly WpfRectangle _accentPreview;

    public SettingsWindow(Window owner, AppSettings currentSettings, Func<AppSettings, bool> apply)
    {
        _apply = apply;
        _draft = currentSettings.Clone();
        _draft.Normalize();

        Owner = owner;
        Title = "设置";
        Width = 560;
        Height = 430;
        MinWidth = 520;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = WpfBrushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Icon = WindowIconHelper.AppIcon;

        var root = new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            Effect = new DropShadowEffect
            {
                BlurRadius = 24,
                ShadowDepth = 8,
                Opacity = 0.34,
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

        var contentScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(18, 4, 18, 10),
        };

        var content = new StackPanel();
        contentScroll.Content = content;
        layout.Children.Add(contentScroll);

        var themeCard = BuildCard("主题颜色", "暗色沿用当前编辑器视觉；亮色接近 Codex 的清爽浅色界面。", out var themeCardBody);
        var themeRows = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        _darkThemeRadio = new WpfRadioButton
        {
            Content = "暗色主题",
            IsChecked = _draft.ThemeMode == AppThemeMode.Dark,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _lightThemeRadio = new WpfRadioButton
        {
            Content = "亮色主题（Codex 风格）",
            IsChecked = _draft.ThemeMode == AppThemeMode.Light,
        };
        themeRows.Children.Add(_darkThemeRadio);
        themeRows.Children.Add(_lightThemeRadio);
        themeCardBody.Children.Add(themeRows);
        content.Children.Add(themeCard);

        var accentCard = BuildCard("强调色", "影响选中态、焦点边框、提示框描边和主要按钮色。格式：#RRGGBB。", out var accentCardBody);
        var accentGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        accentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        accentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        accentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        accentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        accentGrid.Children.Add(BuildLabel("颜色"));
        _accentTextBox = new WpfTextBox
        {
            Text = _draft.AccentColor,
            MinWidth = 180,
            Margin = new Thickness(10, 0, 10, 0),
        };
        Grid.SetColumn(_accentTextBox, 1);
        accentGrid.Children.Add(_accentTextBox);

        _accentPreview = new WpfRectangle
        {
            Width = 34,
            Height = 26,
            RadiusX = 7,
            RadiusY = 7,
            StrokeThickness = 1,
            Margin = new Thickness(0, 0, 10, 0),
        };
        SetResource(_accentPreview, Shape.StrokeProperty, "EditorPanelBorderBrush");
        Grid.SetColumn(_accentPreview, 2);
        accentGrid.Children.Add(_accentPreview);

        var resetButton = new WpfButton
        {
            Content = "还原默认",
            Padding = new Thickness(12, 4, 12, 4),
            MinHeight = 30,
        };
        resetButton.Click += (_, _) =>
        {
            _accentTextBox.Text = _lightThemeRadio.IsChecked == true ? "#2F6FEB" : "#4FA3FF";
            RefreshAccentPreview();
        };
        Grid.SetColumn(resetButton, 3);
        accentGrid.Children.Add(resetButton);

        _accentTextBox.TextChanged += (_, _) => RefreshAccentPreview();
        accentCardBody.Children.Add(accentGrid);

        var presetRow = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            Margin = new Thickness(0, 12, 0, 0),
        };
        AddPresetButton(presetRow, "蓝", "#4FA3FF");
        AddPresetButton(presetRow, "绿", "#22C55E");
        AddPresetButton(presetRow, "橙", "#F59E0B");
        AddPresetButton(presetRow, "粉", "#EC4899");
        accentCardBody.Children.Add(presetRow);
        content.Children.Add(accentCard);

        Content = root;
        RefreshAccentPreview();
    }

    private Border BuildTitleBar()
    {
        var titleBar = new Border
        {
            Padding = new Thickness(18, 14, 14, 12),
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

        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock
        {
            Text = "设置",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = "主题、颜色和界面显示选项",
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0),
        });
        SetResource(titleStack.Children[0], TextBlock.ForegroundProperty, "EditorTextBrightBrush");
        SetResource(titleStack.Children[1], TextBlock.ForegroundProperty, "EditorMutedTextBrush");
        grid.Children.Add(titleStack);

        var closeButton = new WpfButton
        {
            Content = "×",
            Width = 34,
            Height = 30,
            Padding = new Thickness(0),
            FontSize = 18,
            ToolTip = "关闭设置",
        };
        closeButton.Click += (_, _) => Close();
        Grid.SetColumn(closeButton, 1);
        grid.Children.Add(closeButton);

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

        var saveButton = new WpfButton
        {
            Content = "保存设置",
            MinWidth = 96,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
        };
        saveButton.Click += (_, _) =>
        {
            if (ApplyDraft())
                Close();
        };
        buttons.Children.Add(saveButton);

        var applyButton = new WpfButton
        {
            Content = "应用",
            MinWidth = 76,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
        };
        applyButton.Click += (_, _) => ApplyDraft();
        buttons.Children.Add(applyButton);

        var cancelButton = new WpfButton
        {
            Content = "取消",
            MinWidth = 76,
            Height = 32,
            IsCancel = true,
        };
        cancelButton.Click += (_, _) => Close();
        buttons.Children.Add(cancelButton);

        bar.Child = buttons;
        return bar;
    }

    private StackPanel BuildCard(string title, string description, out StackPanel body)
    {
        body = new StackPanel
        {
            Margin = new Thickness(0, 12, 0, 0),
        };

        var border = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
            Child = body,
        };
        SetResource(border, Border.BackgroundProperty, "EditorPanelCardBrush");
        SetResource(border, Border.BorderBrushProperty, "EditorPanelBorderBrush");

        var wrapper = new StackPanel();
        wrapper.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
        });
        SetResource(wrapper.Children[0], TextBlock.ForegroundProperty, "EditorTextBrightBrush");
        wrapper.Children.Add(new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 5, 0, 0),
        });
        SetResource(wrapper.Children[1], TextBlock.ForegroundProperty, "EditorMutedTextBrush");
        wrapper.Children.Add(border);

        var outer = new StackPanel();
        outer.Children.Add(wrapper);
        return outer;
    }

    private TextBlock BuildLabel(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
        };
        SetResource(label, TextBlock.ForegroundProperty, "EditorTextBrush");
        return label;
    }

    private void AddPresetButton(WpfPanel parent, string label, string color)
    {
        var button = new WpfButton
        {
            Content = label,
            MinWidth = 46,
            Height = 28,
            Margin = new Thickness(parent.Children.Count == 0 ? 0 : 8, 0, 0, 0),
            ToolTip = color,
        };
        button.Click += (_, _) =>
        {
            _accentTextBox.Text = color;
            RefreshAccentPreview();
        };
        parent.Children.Add(button);
    }

    private bool ApplyDraft()
    {
        if (!AppThemeService.TryParseColor(_accentTextBox.Text, out var accent))
        {
            ThemedDialog.Show(this, "请输入 #RRGGBB 格式的颜色，例如 #4FA3FF。", "颜色格式无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _draft.ThemeMode = _lightThemeRadio.IsChecked == true ? AppThemeMode.Light : AppThemeMode.Dark;
        _draft.AccentColor = AppThemeService.ToHex(accent);
        _draft.Normalize();
        _accentTextBox.Text = _draft.AccentColor;
        return _apply(_draft.Clone());
    }

    private void RefreshAccentPreview()
    {
        if (AppThemeService.TryParseColor(_accentTextBox.Text, out var color))
            _accentPreview.Fill = new SolidColorBrush(color);
        else
            _accentPreview.Fill = WpfBrushes.Transparent;
    }

    private static void SetResource(DependencyObject target, DependencyProperty property, string key)
    {
        if (target is FrameworkElement element)
        {
            element.SetResourceReference(property, key);
        }
        else if (target is FrameworkContentElement contentElement)
        {
            contentElement.SetResourceReference(property, key);
        }
    }
}
