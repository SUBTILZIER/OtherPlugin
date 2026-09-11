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
    private readonly AppSettings _baselineSettings;
    private readonly WpfRadioButton _darkThemeRadio;
    private readonly WpfRadioButton _lightThemeRadio;
    private readonly WpfTextBox _accentTextBox;
    private readonly WpfRectangle _accentPreview;
    private readonly WpfTextBox _accentOpacityTextBox;
    private readonly Border _darkThemeCard;
    private readonly Border _lightThemeCard;
    private readonly WpfRadioButton _closeMinimizeRadio;
    private readonly WpfRadioButton _closeExitRadio;
    private readonly TextBlock _accentValidationText;
    private TextBlock? _settingsStatusText;
    private WpfButton? _applyButton;

    public SettingsWindow(Window owner, AppSettings currentSettings, Func<AppSettings, bool> apply)
    {
        _apply = apply;
        _draft = currentSettings.Clone();
        _draft.Normalize();
        _baselineSettings = _draft.Clone();

        Owner = owner;
        Title = "设置";
        Width = 900;
        Height = 680;
        MinWidth = 760;
        MinHeight = 560;
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

        var contentLayout = new Grid { Margin = new Thickness(16, 12, 16, 14) };
        contentLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        contentLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        contentLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var interfacePage = new StackPanel();
        interfacePage.Children.Add(BuildPageHeader("界面", "主题、强调色与编辑器的视觉层级"));
        var windowPage = new StackPanel();
        windowPage.Children.Add(BuildPageHeader("窗口行为", "控制关闭窗口后的运行策略"));

        var pageHost = new Border
        {
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 0, 0, 0),
        };
        SetResource(pageHost, Border.BackgroundProperty, "EditorPanelBackgroundBrush");
        var pageScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        pageHost.Child = pageScroll;

        var categoryNavigation = BuildCategoryNavigation();
        Grid.SetColumn(categoryNavigation, 0);
        contentLayout.Children.Add(categoryNavigation);
        Grid.SetColumn(pageHost, 2);
        contentLayout.Children.Add(pageHost);
        layout.Children.Add(contentLayout);

        var themeCard = BuildCard("主题", "选择编辑器的整体明暗风格，切换后立即预览。", out var themeCardBody);
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
        _darkThemeCard = BuildThemeChoiceCard("暗色", "UE 风格石墨灰", "#17191C", "#292C30", "#3E9BB5", _darkThemeRadio);
        Grid.SetColumn(_darkThemeCard, 0);
        themeRows.Children.Add(_darkThemeCard);
        _lightThemeCard = BuildThemeChoiceCard("亮色", "Codex 中性灰白", "#F2F3F1", "#F7F8F6", "#2F6FEB", _lightThemeRadio);
        Grid.SetColumn(_lightThemeCard, 2);
        themeRows.Children.Add(_lightThemeCard);
        themeCardBody.Children.Add(themeRows);
        interfacePage.Children.Add(themeCard);

        var accentCard = BuildCard("强调色", "用于选中态、焦点边框、提示和主要操作按钮。", out var accentCardBody);
        var accentGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        accentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        accentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        accentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        accentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        accentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        accentGrid.Children.Add(BuildLabel("颜色"));
        _accentTextBox = new WpfTextBox
        {
            Text = _draft.AccentColor,
            MinWidth = 180,
            Margin = new Thickness(0, 0, 10, 0),
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
            Content = "选择颜色",
            Padding = new Thickness(12, 4, 12, 4),
            MinHeight = 30,
            MinWidth = 84,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "打开自定义颜色选择器",
        };
        pickButton.Click += (_, _) => OpenAccentColorPicker();
        Grid.SetColumn(pickButton, 3);
        accentGrid.Children.Add(pickButton);

        var resetButton = new WpfButton
        {
            Content = "恢复默认",
            Padding = new Thickness(12, 4, 12, 4),
            MinHeight = 30,
            MinWidth = 84,
        };
        resetButton.Click += (_, _) =>
        {
            _accentTextBox.Text = "#3E9BB5";
            SetOpacityPercent(62);
            ApplyLivePreviewIfValid();
        };
        Grid.SetColumn(resetButton, 4);
        accentGrid.Children.Add(resetButton);

        _accentTextBox.TextChanged += (_, _) => ApplyLivePreviewIfValid();
        accentCardBody.Children.Add(accentGrid);

        _accentValidationText = new TextBlock
        {
            Text = "请输入 #RRGGBB 格式的颜色",
            FontSize = 11,
            Margin = new Thickness(72, 6, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        SetResource(_accentValidationText, TextBlock.ForegroundProperty, "ValidationErrorBrush");
        accentCardBody.Children.Add(_accentValidationText);

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
            Text = "范围 18-100%，数值越低越柔和",
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        SetResource(opacityHint, TextBlock.ForegroundProperty, "EditorMutedTextBrush");
        Grid.SetColumn(opacityHint, 3);
        opacityGrid.Children.Add(opacityHint);
        accentCardBody.Children.Add(opacityGrid);

        var presetRow = new WrapPanel
        {
            Margin = new Thickness(0, 12, 0, 0),
        };
        AddPresetButton(presetRow, "雾蓝", "#7C8DFF");
        AddPresetButton(presetRow, "蓝", "#4FA3FF");
        AddPresetButton(presetRow, "湖青", "#5FB7C8");
        AddPresetButton(presetRow, "鼠尾草", "#7AA874");
        AddPresetButton(presetRow, "柔紫", "#B8A7FF");
        AddPresetButton(presetRow, "玫瑰", "#D98BA6");
        accentCardBody.Children.Add(presetRow);
        interfacePage.Children.Add(accentCard);

        var closeSection = BuildFlatSection("关闭窗口时", "选择点击右上角关闭按钮后的行为。", out var closeSectionBody);
        _closeMinimizeRadio = BuildOptionRadio(
            "最小化到托盘",
            "窗口隐藏到托盘，程序继续运行，脚本热键仍然有效。",
            _draft.WindowCloseAction == AppWindowCloseAction.MinimizeToTray);
        _closeExitRadio = BuildOptionRadio(
            "退出应用",
            "退出程序；如果存在未保存内容，关闭前仍会提示保存。",
            _draft.WindowCloseAction == AppWindowCloseAction.ExitApplication);
        closeSectionBody.Children.Add(_closeMinimizeRadio);
        closeSectionBody.Children.Add(_closeExitRadio);
        windowPage.Children.Add(closeSection);

        var pageContent = new Grid();
        pageContent.Children.Add(interfacePage);
        pageContent.Children.Add(windowPage);
        pageScroll.Content = pageContent;
        windowPage.Visibility = Visibility.Collapsed;
        SelectCategory(0, categoryNavigation, interfacePage, windowPage);
        var categoryButtons = (categoryNavigation.Child as StackPanel)?.Children.OfType<WpfButton>().ToList() ?? [];
        if (categoryButtons.Count >= 2)
        {
            categoryButtons[0].Click += (_, _) => SelectCategory(0, categoryNavigation, interfacePage, windowPage);
            categoryButtons[1].Click += (_, _) => SelectCategory(1, categoryNavigation, interfacePage, windowPage);
        }

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
        UpdateApplyButtonState();
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
            Text = "主题、强调色与窗口行为",
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
            Padding = new Thickness(18, 10, 18, 14),
            BorderThickness = new Thickness(0, 1, 0, 0),
        };
        SetResource(bar, Border.BackgroundProperty, "EditorChromeBrush");
        SetResource(bar, Border.BorderBrushProperty, "EditorSectionHeaderBrush");

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _settingsStatusText = new TextBlock
        {
            Text = "修改会实时预览，点击应用并关闭保存。",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };
        SetResource(_settingsStatusText, TextBlock.ForegroundProperty, "EditorMutedTextBrush");
        layout.Children.Add(_settingsStatusText);

        var applyButton = new WpfButton
        {
            Content = "应用并关闭",
            MinWidth = 112,
            Height = 32,
            Margin = new Thickness(16, 0, 0, 0),
        };
        applyButton.Click += (_, _) =>
        {
            if (ApplyDraft())
            {
                Close();
            }
        };
        _applyButton = applyButton;
        Grid.SetColumn(applyButton, 1);
        layout.Children.Add(applyButton);

        bar.Child = layout;
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

    private StackPanel BuildFlatSection(string title, string description, out StackPanel body)
    {
        body = new StackPanel
        {
            Margin = new Thickness(0, 10, 0, 0),
        };

        var section = new StackPanel();
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
        };
        SetResource(titleText, TextBlock.ForegroundProperty, "EditorTextBrightBrush");
        section.Children.Add(titleText);

        var descriptionText = new TextBlock
        {
            Text = description,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };
        SetResource(descriptionText, TextBlock.ForegroundProperty, "EditorMutedTextBrush");
        section.Children.Add(descriptionText);
        section.Children.Add(body);
        return section;
    }

    private StackPanel BuildPageHeader(string title, string description)
    {
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
        };
        SetResource(titleText, TextBlock.ForegroundProperty, "EditorTextBrightBrush");
        header.Children.Add(titleText);

        var descriptionText = new TextBlock
        {
            Text = description,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };
        SetResource(descriptionText, TextBlock.ForegroundProperty, "EditorMutedTextBrush");
        header.Children.Add(descriptionText);
        return header;
    }

    private Border BuildCategoryNavigation()
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
        };
        SetResource(border, Border.BackgroundProperty, "EditorPanelBackgroundBrush");
        SetResource(border, Border.BorderBrushProperty, "EditorPanelBorderBrush");

        var navigation = new StackPanel();
        var heading = new TextBlock
        {
            Text = "设置",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 8, 10, 8),
        };
        SetResource(heading, TextBlock.ForegroundProperty, "EditorMutedTextBrush");
        navigation.Children.Add(heading);
        navigation.Children.Add(CreateCategoryButton("界面", "主题与颜色", "\uE790"));
        navigation.Children.Add(CreateCategoryButton("窗口行为", "关闭与后台运行", "\uE713"));
        border.Child = navigation;
        return border;
    }

    private WpfButton CreateCategoryButton(string title, string description, string glyph)
    {
        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons"),
            FontSize = 15,
            Margin = new Thickness(2, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        SetResource(icon, TextBlock.ForegroundProperty, "AccentBrush");

        var titleText = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Left,
            HorizontalAlignment = WpfHorizontalAlignment.Left,
        };
        var descriptionText = new TextBlock
        {
            Text = description,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
            TextAlignment = TextAlignment.Left,
            HorizontalAlignment = WpfHorizontalAlignment.Left,
        };
        SetResource(titleText, TextBlock.ForegroundProperty, "EditorTextBrush");
        SetResource(descriptionText, TextBlock.ForegroundProperty, "EditorMutedTextBrush");

        var text = new StackPanel
        {
            HorizontalAlignment = WpfHorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.Children.Add(titleText);
        text.Children.Add(descriptionText);

        var content = new Grid { HorizontalAlignment = WpfHorizontalAlignment.Stretch };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var indicator = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = WpfBrushes.Transparent,
        };
        Grid.SetColumn(indicator, 0);
        Grid.SetColumn(icon, 1);
        Grid.SetColumn(text, 2);
        content.Children.Add(indicator);
        content.Children.Add(icon);
        content.Children.Add(text);
        var button = new WpfButton
        {
            Content = content,
            HorizontalContentAlignment = WpfHorizontalAlignment.Stretch,
            MinHeight = 52,
            Padding = new Thickness(10, 7, 8, 7),
            Margin = new Thickness(0, 0, 0, 6),
            Tag = title,
        };
        SetResource(button, WpfButton.BackgroundProperty, "EditorPanelBackgroundBrush");
        SetResource(button, WpfButton.BorderBrushProperty, "EditorPanelBackgroundBrush");
        return button;
    }

    private void SelectCategory(int index, Border navigation, UIElement interfacePage, UIElement windowPage)
    {
        var buttons = (navigation.Child as StackPanel)?.Children.OfType<WpfButton>().ToList() ?? [];
        for (int i = 0; i < buttons.Count; i++)
        {
            bool selected = i == index;
            SetResource(buttons[i], WpfButton.BackgroundProperty, selected ? "EditorListSelectedBrush" : "EditorPanelBackgroundBrush");
            SetResource(buttons[i], WpfButton.BorderBrushProperty, selected ? "EditorSelectedBorderBrush" : "EditorPanelBackgroundBrush");
            SetResource(buttons[i], WpfButton.ForegroundProperty, selected ? "EditorSelectionTextBrush" : "EditorTextBrush");

            if (buttons[i].Content is Grid content && content.Children.Count >= 3)
            {
                if (content.Children[0] is Border indicator)
                    SetResource(indicator, Border.BackgroundProperty, selected ? "EditorSelectedBorderBrush" : "EditorPanelBackgroundBrush");
                if (content.Children[1] is TextBlock icon)
                    SetResource(icon, TextBlock.ForegroundProperty, selected ? "EditorSelectionTextBrush" : "AccentBrush");
                if (content.Children[2] is StackPanel text && text.Children.Count >= 2)
                {
                    SetResource(text.Children[0], TextBlock.ForegroundProperty, selected ? "EditorSelectionTextBrush" : "EditorTextBrush");
                    SetResource(text.Children[1], TextBlock.ForegroundProperty, selected ? "EditorSelectionTextBrush" : "EditorMutedTextBrush");
                }
            }
        }

        interfacePage.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        windowPage.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;

    }

    private void UpdateThemeCards()
    {
        SetResource(_darkThemeCard, Border.BorderBrushProperty, _darkThemeRadio.IsChecked == true ? "EditorSelectedAccentBrush" : "EditorPanelBorderBrush");
        SetResource(_lightThemeCard, Border.BorderBrushProperty, _lightThemeRadio.IsChecked == true ? "EditorSelectedAccentBrush" : "EditorPanelBorderBrush");
        SetResource(_darkThemeCard, Border.BackgroundProperty, _darkThemeRadio.IsChecked == true ? "EditorSectionHeaderAccentBrush" : "EditorFieldCardBrush");
        SetResource(_lightThemeCard, Border.BackgroundProperty, _lightThemeRadio.IsChecked == true ? "EditorSectionHeaderAccentBrush" : "EditorFieldCardBrush");
    }

    private TextBlock BuildLabel(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = WpfHorizontalAlignment.Left,
        };
        SetResource(label, TextBlock.ForegroundProperty, "EditorTextBrush");
        return label;
    }

    private WpfRadioButton BuildOptionRadio(string title, string description, bool isChecked)
    {
        var indicator = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = WpfBrushes.Transparent,
        };
        var stack = new StackPanel();
        var titleText = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Left,
            HorizontalAlignment = WpfHorizontalAlignment.Left,
        };
        SetResource(titleText, TextBlock.ForegroundProperty, "EditorTextBrush");
        var descriptionText = new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 3, 0, 0),
            TextAlignment = TextAlignment.Left,
            HorizontalAlignment = WpfHorizontalAlignment.Left,
        };
        SetResource(descriptionText, TextBlock.ForegroundProperty, "EditorMutedTextBrush");
        stack.Children.Add(titleText);
        stack.Children.Add(descriptionText);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(indicator, 0);
        Grid.SetColumn(stack, 2);
        content.Children.Add(indicator);
        content.Children.Add(stack);

        var radio = new WpfRadioButton
        {
            Content = content,
            IsChecked = isChecked,
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(8, 8, 8, 8),
            MinHeight = 56,
            ToolTip = description,
            HorizontalContentAlignment = WpfHorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Tag = indicator,
        };
        SetResource(radio, WpfRadioButton.BackgroundProperty, "EditorPanelBackgroundBrush");
        SetResource(radio, WpfRadioButton.BorderBrushProperty, "EditorPanelBackgroundBrush");
        SetResource(radio, System.Windows.Controls.Control.ForegroundProperty, "EditorTextBrush");
        radio.Checked += (_, _) => UpdateOptionRadioVisual(radio);
        radio.Unchecked += (_, _) => UpdateOptionRadioVisual(radio);
        UpdateOptionRadioVisual(radio);
        return radio;
    }

    private void UpdateOptionRadioVisual(WpfRadioButton radio)
    {
        bool selected = radio.IsChecked == true;
        SetResource(radio, WpfRadioButton.BackgroundProperty, selected ? "EditorListSelectedBrush" : "EditorPanelBackgroundBrush");
        SetResource(radio, WpfRadioButton.BorderBrushProperty, selected ? "EditorSelectedBorderBrush" : "EditorPanelBackgroundBrush");
        if (radio.Tag is Border indicator)
            SetResource(indicator, Border.BackgroundProperty, selected ? "EditorSelectedBorderBrush" : "EditorPanelBackgroundBrush");

        if (radio.Content is Grid content && content.Children.OfType<StackPanel>().FirstOrDefault() is { } text
            && text.Children.Count >= 2)
        {
            SetResource(text.Children[0], TextBlock.ForegroundProperty, selected ? "EditorSelectionTextBrush" : "EditorTextBrush");
            SetResource(text.Children[1], TextBlock.ForegroundProperty, selected ? "EditorSelectionTextBrush" : "EditorMutedTextBrush");
        }
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
            SetAccentValidation(true);
            _accentTextBox.Focus();
            return false;
        }

        SetAccentValidation(false);
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
        {
            SetAccentValidation(true);
            UpdateApplyButtonState();
            return;
        }

        SetAccentValidation(false);
        _draft.ThemeMode = _lightThemeRadio.IsChecked == true ? AppThemeMode.Light : AppThemeMode.Dark;
        _draft.AccentColor = AppThemeService.ToHex(accent);
        _draft.AccentOpacity = ReadAccentOpacity();
        _draft.WindowCloseAction = ReadWindowCloseAction();
        _draft.Normalize();
        _apply(_draft.Clone());
        UpdateApplyButtonState();
    }

    private void UpdateApplyButtonState()
    {
        if (_applyButton is null)
            return;

        bool validColor = AppThemeService.TryParseColor(_accentTextBox.Text, out _);
        bool changed = validColor
            && (_draft.ThemeMode != _baselineSettings.ThemeMode
                || !string.Equals(_draft.AccentColor, _baselineSettings.AccentColor, StringComparison.OrdinalIgnoreCase)
                || Math.Abs(_draft.AccentOpacity - _baselineSettings.AccentOpacity) > 0.0001
                || _draft.WindowCloseAction != _baselineSettings.WindowCloseAction);
        _applyButton.IsEnabled = changed;
        _applyButton.ToolTip = !validColor
            ? "请先修正强调色格式"
            : changed ? "应用设置并关闭" : "没有待应用的设置变更";
        if (_settingsStatusText is not null)
        {
            _settingsStatusText.Text = !validColor
                ? "强调色格式无效，请修正后应用。"
                : changed ? "修改会实时预览，点击应用并关闭保存。" : "当前设置已保存。";
            SetResource(_settingsStatusText, TextBlock.ForegroundProperty, !validColor ? "ValidationErrorBrush" : "EditorMutedTextBrush");
        }
    }

    private void SetAccentValidation(bool invalid)
    {
        _accentValidationText.Visibility = invalid ? Visibility.Visible : Visibility.Collapsed;
        SetResource(_accentTextBox, System.Windows.Controls.Control.BorderBrushProperty, invalid ? "ValidationErrorBrush" : "InputBorderBrush");
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
