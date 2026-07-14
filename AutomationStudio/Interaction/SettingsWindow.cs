using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using AutomationStudioWpf.Services;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfCursors = System.Windows.Input.Cursors;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfPanel = System.Windows.Controls.Panel;
using WpfRadioButton = System.Windows.Controls.RadioButton;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace AutomationStudioWpf.Interaction;

public sealed class SettingsWindow : Window
{
    private readonly Func<AppSettings, bool> _apply;
    private readonly AppSettings _draft;
    private readonly WpfRadioButton _darkThemeRadio;
    private readonly WpfRadioButton _lightThemeRadio;
    private readonly WpfTextBox _accentTextBox;
    private readonly WpfRectangle _accentPreview;
    private readonly WpfTextBox _accentOpacityTextBox;
    private readonly Border _darkThemeCard;
    private readonly Border _lightThemeCard;
    private readonly WpfRadioButton _closeMinimizeRadio;
    private readonly WpfRadioButton _closeExitRadio;

    public SettingsWindow(Window owner, AppSettings currentSettings, Func<AppSettings, bool> apply)
    {
        _apply = apply;
        _draft = currentSettings.Clone();
        _draft.Normalize();

        Owner = owner;
        Title = "设置";
        Width = 720;
        Height = 680;
        MinWidth = 640;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = WpfBrushes.Transparent;
        ResizeMode = ResizeMode.CanResizeWithGrip;
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
        var themeRows = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        themeRows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        themeRows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        themeRows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _darkThemeRadio = new WpfRadioButton
        {
            Content = "暗色主题",
            IsChecked = _draft.ThemeMode == AppThemeMode.Dark,
            Visibility = Visibility.Collapsed,
        };
        _lightThemeRadio = new WpfRadioButton
        {
            Content = "亮色主题（Codex 风格）",
            IsChecked = _draft.ThemeMode == AppThemeMode.Light,
            Visibility = Visibility.Collapsed,
        };
        themeRows.Children.Add(_darkThemeRadio);
        themeRows.Children.Add(_lightThemeRadio);
        _darkThemeCard = BuildThemeChoiceCard("暗色", "高对比编辑器", "#10151D", "#151B23", "#4FA3FF", _darkThemeRadio);
        Grid.SetColumn(_darkThemeCard, 0);
        themeRows.Children.Add(_darkThemeCard);
        _lightThemeCard = BuildThemeChoiceCard("亮色", "Codex 中性灰白", "#ECEEED", "#F7F8F7", "#4FA3FF", _lightThemeRadio);
        Grid.SetColumn(_lightThemeCard, 2);
        themeRows.Children.Add(_lightThemeCard);
        themeCardBody.Children.Add(themeRows);
        content.Children.Add(themeCard);

        var accentCard = BuildCard("强调色", "影响选中态、焦点边框、提示框描边和主要按钮色。格式：#RRGGBB；透明度会让颜色更柔和。", out var accentCardBody);
        var accentGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        accentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        accentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        accentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
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
            Cursor = WpfCursors.Hand,
            ToolTip = "点击打开自定义颜色选择器",
        };
        SetResource(_accentPreview, Shape.StrokeProperty, "EditorPanelBorderBrush");
        _accentPreview.MouseLeftButtonDown += (_, _) => OpenAccentColorPicker();
        Grid.SetColumn(_accentPreview, 2);
        accentGrid.Children.Add(_accentPreview);

        var pickButton = new WpfButton
        {
            Content = "色盘",
            Padding = new Thickness(12, 4, 12, 4),
            MinHeight = 30,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "打开自定义颜色选择器",
        };
        pickButton.Click += (_, _) => OpenAccentColorPicker();
        Grid.SetColumn(pickButton, 3);
        accentGrid.Children.Add(pickButton);

        var resetButton = new WpfButton
        {
            Content = "还原默认",
            Padding = new Thickness(12, 4, 12, 4),
            MinHeight = 30,
        };
        resetButton.Click += (_, _) =>
        {
            _accentTextBox.Text = "#4FA3FF";
            SetOpacityPercent(62);
            ApplyLivePreviewIfValid();
        };
        Grid.SetColumn(resetButton, 4);
        accentGrid.Children.Add(resetButton);

        _accentTextBox.TextChanged += (_, _) => ApplyLivePreviewIfValid();
        accentCardBody.Children.Add(accentGrid);

        var opacityGrid = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        opacityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        opacityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        opacityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        opacityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        opacityGrid.Children.Add(BuildLabel("透明度"));

        _accentOpacityTextBox = new WpfTextBox
        {
            Text = Math.Round(_draft.AccentOpacity * 100).ToString("0"),
            Width = 72,
            MinHeight = 30,
            Margin = new Thickness(10, 0, 6, 0),
            ToolTip = "强调色透明度百分比，范围 18-100。按 Enter 或移开焦点后应用。",
        };
        Grid.SetColumn(_accentOpacityTextBox, 1);
        opacityGrid.Children.Add(_accentOpacityTextBox);

        var percentText = new TextBlock
        {
            Text = "%",
            VerticalAlignment = VerticalAlignment.Center,
        };
        SetResource(percentText, TextBlock.ForegroundProperty, "EditorMutedTextBrush");
        Grid.SetColumn(percentText, 2);
        opacityGrid.Children.Add(percentText);

        var opacityHint = new TextBlock
        {
            Text = "18-100，数值越低越柔和",
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        SetResource(opacityHint, TextBlock.ForegroundProperty, "EditorMutedTextBrush");
        Grid.SetColumn(opacityHint, 3);
        opacityGrid.Children.Add(opacityHint);
        accentCardBody.Children.Add(opacityGrid);

        var presetRow = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            Margin = new Thickness(0, 12, 0, 0),
        };
        AddPresetButton(presetRow, "雾蓝", "#7C8DFF");
        AddPresetButton(presetRow, "蓝", "#4FA3FF");
        AddPresetButton(presetRow, "湖青", "#5FB7C8");
        AddPresetButton(presetRow, "鼠尾草", "#7AA874");
        AddPresetButton(presetRow, "柔紫", "#B8A7FF");
        AddPresetButton(presetRow, "玫瑰", "#D98BA6");
        accentCardBody.Children.Add(presetRow);
        content.Children.Add(accentCard);

        var closeCard = BuildCard("关闭窗口时", "控制点击主窗口右上角 × 时的行为。默认最小化到托盘，不再每次弹窗询问。", out var closeCardBody);
        _closeMinimizeRadio = BuildOptionRadio(
            "最小化到托盘（默认）",
            "点击关闭按钮时隐藏主窗口，程序仍在托盘运行，脚本热键继续可用。",
            _draft.WindowCloseAction == AppWindowCloseAction.MinimizeToTray);
        _closeExitRadio = BuildOptionRadio(
            "关闭软件",
            "点击关闭按钮时直接退出程序；如有未保存资产，仍会先询问是否保存。",
            _draft.WindowCloseAction == AppWindowCloseAction.ExitApplication);
        closeCardBody.Children.Add(_closeMinimizeRadio);
        closeCardBody.Children.Add(_closeExitRadio);
        content.Children.Add(closeCard);

        Content = root;
        _darkThemeRadio.Checked += (_, _) => ApplyLivePreviewIfValid();
        _lightThemeRadio.Checked += (_, _) => ApplyLivePreviewIfValid();
        _closeMinimizeRadio.Checked += (_, _) => ApplyLivePreviewIfValid();
        _closeExitRadio.Checked += (_, _) => ApplyLivePreviewIfValid();
        _accentOpacityTextBox.LostFocus += (_, _) => CommitOpacityText();
        _accentOpacityTextBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;

            CommitOpacityText();
            e.Handled = true;
        };
        RefreshAccentPreview();
        UpdateThemeCards();
    }

    private Border BuildThemeChoiceCard(string title, string subtitle, string rootColor, string panelColor, string accentColor, WpfRadioButton targetRadio)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Cursor = WpfCursors.Hand,
            ToolTip = $"切换到{title}主题",
        };
        SetResource(card, Border.BackgroundProperty, "EditorFieldCardBrush");
        SetResource(card, Border.BorderBrushProperty, "EditorPanelBorderBrush");
        card.MouseLeftButtonDown += (_, _) =>
        {
            targetRadio.IsChecked = true;
            ApplyLivePreviewIfValid();
        };

        var stack = new StackPanel();
        var header = new DockPanel { LastChildFill = true };
        var check = new TextBlock
        {
            Text = "●",
            FontSize = 14,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = WpfVerticalAlignment.Center,
        };
        SetResource(check, TextBlock.ForegroundProperty, "EditorSelectedAccentBrush");
        header.Children.Add(check);
        var titleText = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
        };
        SetResource(titleText, TextBlock.ForegroundProperty, "EditorTextBrightBrush");
        header.Children.Add(titleText);
        stack.Children.Add(header);

        var subtitleText = new TextBlock
        {
            Text = subtitle,
            FontSize = 11,
            Margin = new Thickness(22, 2, 0, 10),
        };
        SetResource(subtitleText, TextBlock.ForegroundProperty, "EditorMutedTextBrush");
        stack.Children.Add(subtitleText);

        var preview = new Border
        {
            Height = 82,
            CornerRadius = new CornerRadius(10),
            Background = BrushFrom(rootColor),
            Padding = new Thickness(10),
        };
        var previewGrid = new Grid();
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        previewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        previewGrid.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(7),
            Background = BrushFrom(panelColor),
        });

        var lines = new StackPanel
        {
            VerticalAlignment = WpfVerticalAlignment.Center,
        };
        lines.Children.Add(new Border { Height = 8, Width = 72, CornerRadius = new CornerRadius(4), Background = BrushFrom(accentColor), HorizontalAlignment = WpfHorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) });
        lines.Children.Add(new Border { Height = 6, Width = 120, CornerRadius = new CornerRadius(3), Background = BrushFrom("#9AA4B2"), HorizontalAlignment = WpfHorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 7) });
        lines.Children.Add(new Border { Height = 6, Width = 92, CornerRadius = new CornerRadius(3), Background = BrushFrom("#C4CBD5"), HorizontalAlignment = WpfHorizontalAlignment.Left });
        Grid.SetColumn(lines, 2);
        previewGrid.Children.Add(lines);
        preview.Child = previewGrid;
        stack.Children.Add(preview);

        card.Child = stack;
        return card;
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

        var applyButton = new WpfButton
        {
            Content = "应用",
            MinWidth = 96,
            Height = 32,
        };
        applyButton.Click += (_, _) =>
        {
            if (ApplyDraft())
            {
                Close();
            }
        };
        buttons.Children.Add(applyButton);

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

    private void UpdateThemeCards()
    {
        SetResource(_darkThemeCard, Border.BorderBrushProperty, _darkThemeRadio.IsChecked == true ? "EditorSelectedAccentBrush" : "EditorPanelBorderBrush");
        SetResource(_lightThemeCard, Border.BorderBrushProperty, _lightThemeRadio.IsChecked == true ? "EditorSelectedAccentBrush" : "EditorPanelBorderBrush");
        _darkThemeCard.Opacity = _darkThemeRadio.IsChecked == true ? 1 : 0.72;
        _lightThemeCard.Opacity = _lightThemeRadio.IsChecked == true ? 1 : 0.72;
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

    private WpfRadioButton BuildOptionRadio(string title, string description, bool isChecked)
    {
        var stack = new StackPanel();
        var titleText = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
        };
        SetResource(titleText, TextBlock.ForegroundProperty, "EditorTextBrush");
        var descriptionText = new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 3, 0, 0),
        };
        SetResource(descriptionText, TextBlock.ForegroundProperty, "EditorMutedTextBrush");
        stack.Children.Add(titleText);
        stack.Children.Add(descriptionText);

        var radio = new WpfRadioButton
        {
            Content = stack,
            IsChecked = isChecked,
            Margin = new Thickness(0, 0, 0, 12),
            ToolTip = description,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        SetResource(radio, System.Windows.Controls.Control.ForegroundProperty, "EditorTextBrush");
        return radio;
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
        _draft.AccentOpacity = ReadAccentOpacity();
        _draft.WindowCloseAction = ReadWindowCloseAction();
        _draft.Normalize();
        _accentTextBox.Text = _draft.AccentColor;
        SetOpacityPercent(_draft.AccentOpacity * 100);
        return _apply(_draft.Clone());
    }

    private void ApplyLivePreviewIfValid()
    {
        RefreshAccentPreview();
        UpdateThemeCards();
        if (!AppThemeService.TryParseColor(_accentTextBox.Text, out var accent))
            return;

        _draft.ThemeMode = _lightThemeRadio.IsChecked == true ? AppThemeMode.Light : AppThemeMode.Dark;
        _draft.AccentColor = AppThemeService.ToHex(accent);
        _draft.AccentOpacity = ReadAccentOpacity();
        _draft.WindowCloseAction = ReadWindowCloseAction();
        _draft.Normalize();
        _apply(_draft.Clone());
    }

    private void RefreshAccentPreview()
    {
        if (AppThemeService.TryParseColor(_accentTextBox.Text, out var color))
        {
            byte alpha = (byte)Math.Round(255 * ReadAccentOpacity());
            _accentPreview.Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, color.R, color.G, color.B));
        }
        else
        {
            _accentPreview.Fill = WpfBrushes.Transparent;
        }
    }

    private void OpenAccentColorPicker()
    {
        var picker = new AccentColorPickerWindow(this, _accentTextBox.Text);
        if (picker.ShowDialog() == true && !string.IsNullOrWhiteSpace(picker.SelectedColorHex))
        {
            _accentTextBox.Text = picker.SelectedColorHex;
            ApplyLivePreviewIfValid();
        }
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

    private static SolidColorBrush BrushFrom(string value)
    {
        return AppThemeService.TryParseColor(value, out var color)
            ? new SolidColorBrush(color)
            : new SolidColorBrush(Colors.Transparent);
    }

    private double ReadAccentOpacity()
    {
        if (double.TryParse(_accentOpacityTextBox.Text.Trim(), out var textPercent))
            return Math.Clamp(textPercent / 100.0, 0.18, 1.0);

        return Math.Clamp(_draft.AccentOpacity, 0.18, 1.0);
    }

    private AppWindowCloseAction ReadWindowCloseAction()
    {
        return _closeExitRadio.IsChecked == true
            ? AppWindowCloseAction.ExitApplication
            : AppWindowCloseAction.MinimizeToTray;
    }

    private void SetOpacityPercent(double percent)
    {
        var clamped = Math.Clamp(percent, 18, 100);
        _accentOpacityTextBox.Text = Math.Round(clamped).ToString("0");
    }

    private void CommitOpacityText()
    {
        if (!double.TryParse(_accentOpacityTextBox.Text.Trim(), out var percent))
        {
            SetOpacityPercent(_draft.AccentOpacity * 100);
            RefreshAccentPreview();
            return;
        }

        SetOpacityPercent(percent);
        ApplyLivePreviewIfValid();
    }
}
