using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutomationStudioWpf.Services;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfControl = System.Windows.Controls.Control;
using WpfRadioButton = System.Windows.Controls.RadioButton;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace AutomationStudioWpf.Interaction;

public sealed class ScriptPropertiesSummaryControl : WpfUserControl
{
    private static WpfBrush CardBrush => ResourceBrush("EditorPanelCardBrush");
    private static WpfBrush SectionBrush => ResourceBrush("EditorFieldCardBrush");
    private static WpfBrush InputBrush => ResourceBrush("InputBackgroundBrush");
    private static WpfBrush ChromeBorderBrush => ResourceBrush("EditorPanelBorderBrush");
    private static WpfBrush AccentBrush => ResourceBrush("AccentBrush");
    private static WpfBrush TextBrush => ResourceBrush("EditorTextBrightBrush");
    private static WpfBrush MutedBrush => ResourceBrush("EditorMutedTextBrush");
    private static WpfBrush ErrorBrush => ResourceBrush("LogErrorBrush");

    private readonly ContentAssetViewModel _asset;
    private readonly Func<ContentAssetViewModel, ScriptRunSettings, bool> _saveAction;
    private readonly ScriptRunSettings _draft;
    private readonly WpfRadioButton _countRadio = new() { Content = "按次数循环" };
    private readonly WpfRadioButton _untilStoppedRadio = new() { Content = "循环到按终止键为止" };
    private readonly WpfRadioButton _durationRadio = new() { Content = "循环一段时间" };
    private readonly WpfTextBox _loopCountBox = TextBox("1", 56);
    private readonly WpfTextBox _hoursBox = TextBox("0", 52);
    private readonly WpfTextBox _minutesBox = TextBox("0", 52);
    private readonly WpfTextBox _secondsBox = TextBox("0", 52);
    private readonly WpfCheckBox _preventDuplicateCheck = new() { Content = "禁止重复运行" };
    private readonly TextBlock _startHotkeyText = new();
    private readonly TextBlock _stopHotkeyText = new();
    private readonly WpfTextBox _startPressCountBox = TextBox("1", 52);
    private readonly WpfTextBox _stopPressCountBox = TextBox("1", 52);
    private readonly WpfTextBox _startTriggerWindowBox = TextBox("1000", 70);
    private readonly WpfTextBox _stopTriggerWindowBox = TextBox("1000", 70);
    private readonly TextBlock _statusText = new();

    public ScriptPropertiesSummaryControl(ContentAssetViewModel asset, Func<ContentAssetViewModel, ScriptRunSettings, bool> saveAction)
    {
        _asset = asset;
        _saveAction = saveAction;
        _draft = asset.RunSettings.Clone();
        _draft.Normalize();
        HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Content = Build();
        Refresh();
    }

    public ContentAssetViewModel Asset => _asset;

    public void Refresh()
    {
        CopySettings(_asset.RunSettings, _draft);
        LoadDraftToUi();
    }

    private UIElement Build()
    {
        var root = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(18),
            MaxWidth = 760,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
        };
        SetResource(root, Border.BackgroundProperty, "EditorPanelCardBrush");
        SetResource(root, Border.BorderBrushProperty, "EditorPanelBorderBrush");

        var body = new StackPanel();
        root.Child = body;
        var title = new TextBlock
        {
            Text = "脚本属性",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
        };
        SetResource(title, TextBlock.ForegroundProperty, "EditorTextBrightBrush");
        body.Children.Add(title);

        var assetName = new TextBlock
        {
            Text = _asset.Name,
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 16),
        };
        SetResource(assetName, TextBlock.ForegroundProperty, "EditorMutedTextBrush");
        body.Children.Add(assetName);

        _countRadio.ToolTip = "按设定次数重复执行脚本。";
        _untilStoppedRadio.ToolTip = "持续运行直到按下终止热键。";
        _durationRadio.ToolTip = "运行指定时长后自动停止。";
        _preventDuplicateCheck.ToolTip = "运行中再次触发启动热键时，将忽略重复触发。";

        body.Children.Add(Section("运行设置",
            Row(_countRadio, _loopCountBox, Label("次")),
            Row(_untilStoppedRadio),
            Row(_durationRadio, _hoursBox, Label("小时"), _minutesBox, Label("分钟"), _secondsBox, Label("秒")),
            _preventDuplicateCheck));

        body.Children.Add(Section("热键",
            HotkeyRow("启动热键", _draft.StartHotkey, _startHotkeyText, _startPressCountBox, _startTriggerWindowBox),
            HotkeyRow("终止热键", _draft.StopHotkey, _stopHotkeyText, _stopPressCountBox, _stopTriggerWindowBox)));

        SetResource(_statusText, TextBlock.ForegroundProperty, "EditorMutedTextBrush");
        _statusText.Margin = new Thickness(0, 0, 0, 10);
        body.Children.Add(_statusText);

        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
        };
        var save = Button("保存设置", 96);
        save.Click += (_, _) => Save();
        var reset = Button("还原", 72);
        SetResource(reset, WpfControl.BackgroundProperty, "InputBackgroundBrush");
        reset.Click += (_, _) => Refresh();
        buttons.Children.Add(save);
        buttons.Children.Add(reset);
        body.Children.Add(buttons);
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = root,
        };
    }

    private void Save()
    {
        SetResource(_statusText, TextBlock.ForegroundProperty, "EditorMutedTextBrush");
        _statusText.Text = string.Empty;
        ReadUiToDraft();
        if (_draft.LoopMode == ScriptLoopMode.Duration &&
            _draft.DurationHours == 0 &&
            _draft.DurationMinutes == 0 &&
            _draft.DurationSeconds == 0)
        {
            SetResource(_statusText, TextBlock.ForegroundProperty, "LogErrorBrush");
            _statusText.Text = "循环时长不能为 0。";
            return;
        }

        if (_draft.StartHotkey.IsConfigured &&
            _draft.StopHotkey.IsConfigured &&
            ScriptHotkeyService.SameHotkey(_draft.StartHotkey, _draft.StopHotkey))
        {
            SetResource(_statusText, TextBlock.ForegroundProperty, "LogErrorBrush");
            _statusText.Text = "启动热键与终止热键不能相同。";
            return;
        }

        if (_saveAction(_asset, _draft.Clone()))
        {
            SetResource(_statusText, TextBlock.ForegroundProperty, "AccentBrush");
            _statusText.Text = "已保存。";
            CopySettings(_asset.RunSettings, _draft);
            LoadDraftToUi();
        }
    }

    private void LoadDraftToUi()
    {
        _draft.Normalize();
        _countRadio.IsChecked = _draft.LoopMode == ScriptLoopMode.Count;
        _untilStoppedRadio.IsChecked = _draft.LoopMode == ScriptLoopMode.UntilStopped;
        _durationRadio.IsChecked = _draft.LoopMode == ScriptLoopMode.Duration;
        _loopCountBox.Text = _draft.LoopCount.ToString();
        _hoursBox.Text = _draft.DurationHours.ToString();
        _minutesBox.Text = _draft.DurationMinutes.ToString();
        _secondsBox.Text = _draft.DurationSeconds.ToString();
        _preventDuplicateCheck.IsChecked = _draft.PreventDuplicateRun;
        _startPressCountBox.Text = Math.Max(1, _draft.StartHotkey.PressCount).ToString();
        _stopPressCountBox.Text = Math.Max(1, _draft.StopHotkey.PressCount).ToString();
        _startTriggerWindowBox.Text = _draft.StartHotkey.TriggerWindowMs.ToString();
        _stopTriggerWindowBox.Text = _draft.StopHotkey.TriggerWindowMs.ToString();
        RefreshHotkeyText(_draft.StartHotkey, _startHotkeyText);
        RefreshHotkeyText(_draft.StopHotkey, _stopHotkeyText);
    }

    private void ReadUiToDraft()
    {
        _draft.LoopMode = _untilStoppedRadio.IsChecked == true
            ? ScriptLoopMode.UntilStopped
            : _durationRadio.IsChecked == true
                ? ScriptLoopMode.Duration
                : ScriptLoopMode.Count;
        _draft.LoopCount = ParseInt(_loopCountBox.Text, 1);
        _draft.DurationHours = ParseInt(_hoursBox.Text, 0);
        _draft.DurationMinutes = ParseInt(_minutesBox.Text, 0);
        _draft.DurationSeconds = ParseInt(_secondsBox.Text, 0);
        _draft.PreventDuplicateRun = _preventDuplicateCheck.IsChecked == true;
        _draft.StartHotkey.PressCount = ParseInt(_startPressCountBox.Text, 1);
        _draft.StopHotkey.PressCount = ParseInt(_stopPressCountBox.Text, 1);
        _draft.StartHotkey.TriggerWindowMs = ParseInt(_startTriggerWindowBox.Text, 1000);
        _draft.StopHotkey.TriggerWindowMs = ParseInt(_stopTriggerWindowBox.Text, 1000);
        _draft.Normalize();
    }

    private UIElement HotkeyRow(string title, ScriptHotkeySettings settings, TextBlock keyText, WpfTextBox pressCount, WpfTextBox triggerWindow)
    {
        keyText.Text = settings.IsConfigured ? settings.Key : "无";
        SetResource(keyText, TextBlock.ForegroundProperty, "EditorTextBrush");
        keyText.VerticalAlignment = VerticalAlignment.Center;
        keyText.TextTrimming = TextTrimming.CharacterEllipsis;

        var keyBadge = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6, 10, 6),
            MinWidth = 120,
            Child = keyText,
        };
        SetResource(keyBadge, Border.BackgroundProperty, "InputBackgroundBrush");
        SetResource(keyBadge, Border.BorderBrushProperty, "EditorPanelBorderBrush");

        var change = Button("修改", 58);
        change.Click += (_, _) => CaptureHotkey(settings, keyText);
        var clear = Button("清空", 58);
        SetResource(clear, WpfControl.BackgroundProperty, "InputBackgroundBrush");
        clear.Click += (_, _) =>
        {
            settings.Key = string.Empty;
            settings.PressCount = 1;
            settings.TriggerWindowMs = 1000;
            keyText.Text = "无";
            pressCount.Text = "1";
            triggerWindow.Text = "1000";
        };

        var stack = new StackPanel();
        stack.Children.Add(Label(title, null, true));

        var keyRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AddToGrid(keyRow, Label("按键", 42), 0);
        AddToGrid(keyRow, keyBadge, 1);
        AddToGrid(keyRow, change, 2);
        AddToGrid(keyRow, clear, 3);
        stack.Children.Add(keyRow);

        var triggerRow = new WrapPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 0),
        };
        triggerRow.Children.Add(Label("按下次数", 66));
        triggerRow.Children.Add(pressCount);
        triggerRow.Children.Add(Label("触发时间阈值", 94));
        triggerRow.Children.Add(triggerWindow);
        triggerRow.Children.Add(Label("ms", 24));
        stack.Children.Add(triggerRow);

        var hotkeyCard = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 2, 0, 10),
            Child = stack,
        };
        SetResource(hotkeyCard, Border.BackgroundProperty, "EditorPanelCardBrush");
        SetResource(hotkeyCard, Border.BorderBrushProperty, "EditorPanelBorderBrush");
        return hotkeyCard;
    }

    private void CaptureHotkey(ScriptHotkeySettings target, TextBlock label)
    {
        var owner = Window.GetWindow(this);
        var window = owner is null ? new ScriptHotkeyCaptureWindow(new Window()) : new ScriptHotkeyCaptureWindow(owner);
        if (window.ShowDialog() != true || window.Result is null)
            return;

        target.InputKind = window.Result.InputKind;
        target.Key = window.Result.Key;
        label.Text = target.Key;
    }

    private static Border Section(string title, params UIElement[] fields)
    {
        var stack = new StackPanel();
        var header = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 8),
        };
        SetResource(header, TextBlock.ForegroundProperty, "EditorTextBrush");
        stack.Children.Add(header);

        foreach (var field in fields)
            stack.Children.Add(field);

        var section = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            Child = stack,
        };
        SetResource(section, Border.BackgroundProperty, "EditorFieldCardBrush");
        SetResource(section, Border.BorderBrushProperty, "EditorPanelBorderBrush");
        return section;
    }

    private static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        foreach (var child in children)
            row.Children.Add(child);
        return row;
    }

    private static TextBlock Label(string text, double? width = null, bool bold = false)
    {
        var label = new TextBlock
        {
            Text = text,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Margin = new Thickness(0, 6, 6, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        SetResource(label, TextBlock.ForegroundProperty, "EditorMutedTextBrush");
        if (width.HasValue)
            label.Width = width.Value;
        return label;
    }

    private static void AddToGrid(Grid grid, UIElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private static WpfTextBox TextBox(string text, double width)
    {
        var textBox = new WpfTextBox
        {
            Text = text,
            Width = width,
            MinHeight = 30,
            Margin = new Thickness(4, 0, 8, 0),
            Padding = new Thickness(8, 5, 8, 5),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        SetResource(textBox, WpfControl.BackgroundProperty, "InputBackgroundBrush");
        SetResource(textBox, WpfControl.ForegroundProperty, "EditorTextBrush");
        SetResource(textBox, WpfControl.BorderBrushProperty, "EditorPanelBorderBrush");
        return textBox;
    }

    private static WpfButton Button(string text, double width)
    {
        var button = new WpfButton
        {
            Content = text,
            Width = width,
            MinHeight = 32,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(10, 5, 10, 5),
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        SetResource(button, WpfControl.BackgroundProperty, "AccentBrush");
        SetResource(button, WpfControl.ForegroundProperty, "AccentForegroundBrush");
        SetResource(button, WpfControl.BorderBrushProperty, "AccentBrush");
        return button;
    }

    private static void RefreshHotkeyText(ScriptHotkeySettings settings, TextBlock label)
    {
        label.Text = settings.IsConfigured ? settings.Key : "无";
    }

    private static int ParseInt(string text, int fallback) =>
        int.TryParse(text, out var value) ? value : fallback;

    private static void CopySettings(ScriptRunSettings source, ScriptRunSettings target)
    {
        var clone = source.Clone();
        clone.Normalize();
        target.LoopMode = clone.LoopMode;
        target.LoopCount = clone.LoopCount;
        target.DurationHours = clone.DurationHours;
        target.DurationMinutes = clone.DurationMinutes;
        target.DurationSeconds = clone.DurationSeconds;
        target.PreventDuplicateRun = clone.PreventDuplicateRun;
        target.StartHotkey = clone.StartHotkey.Clone();
        target.StopHotkey = clone.StopHotkey.Clone();
    }

    private static void SetResource(DependencyObject target, DependencyProperty property, string key)
    {
        if (target is FrameworkElement element)
            element.SetResourceReference(property, key);
        else if (target is FrameworkContentElement contentElement)
            contentElement.SetResourceReference(property, key);
    }

    private static WpfBrush ResourceBrush(string key)
    {
        var resources = WpfApplication.Current?.Resources;
        if (resources is not null && resources.Contains(key) && resources[key] is WpfBrush brush)
            return brush;
        return WpfBrushes.Transparent;
    }
}
