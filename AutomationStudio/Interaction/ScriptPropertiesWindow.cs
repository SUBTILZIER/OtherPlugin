using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AutomationStudioWpf.Services;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using DockPanel = System.Windows.Controls.DockPanel;
using Border = System.Windows.Controls.Border;
using Grid = System.Windows.Controls.Grid;
using ColumnDefinition = System.Windows.Controls.ColumnDefinition;
using RadioButton = System.Windows.Controls.RadioButton;
using StackPanel = System.Windows.Controls.StackPanel;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Dock = System.Windows.Controls.Dock;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace AutomationStudioWpf.Interaction;

public sealed class ScriptPropertiesWindow : Window
{
    private static readonly SolidColorBrush WindowBackgroundBrush = Brush(0x10, 0x14, 0x1B);
    private static readonly SolidColorBrush PanelBrush = Brush(0x16, 0x1B, 0x23);
    private static readonly SolidColorBrush CardBrush = Brush(0x1B, 0x22, 0x2C);
    private static readonly SolidColorBrush CardBorderBrush = Brush(0x2D, 0x37, 0x47);
    private static readonly SolidColorBrush AccentBrush = Brush(0x4F, 0xA3, 0xFF);
    private static readonly SolidColorBrush AccentHoverBrush = Brush(0x66, 0xB3, 0xFF);
    private static readonly SolidColorBrush MutedTextBrush = Brush(0x9A, 0xA6, 0xB6);
    private static readonly SolidColorBrush InputBrush = Brush(0x21, 0x28, 0x34);
    private static readonly SolidColorBrush InputBorderBrush = Brush(0x34, 0x3E, 0x4E);

    private readonly ScriptRunSettings _settings;
    private readonly RadioButton _countRadio = new() { Content = "按次数循环" };
    private readonly RadioButton _untilStoppedRadio = new() { Content = "循环到按终止键为止" };
    private readonly RadioButton _durationRadio = new() { Content = "循环一段时间" };
    private readonly TextBox _loopCountBox = CreateTextBox("1", 56);
    private readonly TextBox _hoursBox = CreateTextBox("0", 52);
    private readonly TextBox _minutesBox = CreateTextBox("0", 52);
    private readonly TextBox _secondsBox = CreateTextBox("0", 52);
    private readonly CheckBox _preventDuplicateCheck = new() { Content = "禁止重复运行" };
    private readonly TextBlock _startHotkeyText = new();
    private readonly TextBlock _stopHotkeyText = new();
    private readonly TextBox _startPressCountBox = CreateTextBox("1", 52);
    private readonly TextBox _stopPressCountBox = CreateTextBox("1", 52);
    private readonly TextBox _startTriggerWindowBox = CreateTextBox("1000", 64);
    private readonly TextBox _stopTriggerWindowBox = CreateTextBox("1000", 64);
    private readonly TextBlock _errorText = new();

    public ScriptPropertiesWindow(Window owner, string assetName, ScriptRunSettings settings)
    {
        Owner = owner;
        Title = $"脚本属性 - {assetName}";
        Width = 660;
        MinWidth = 620;
        Height = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = WindowBackgroundBrush;
        Foreground = Brushes.White;
        _settings = settings.Clone();
        _settings.Normalize();
        Content = BuildContent(assetName);
        LoadSettings();
    }

    public ScriptRunSettings Result => _settings.Clone();

    private UIElement BuildContent(string assetName)
    {
        var shell = new Border
        {
            Background = PanelBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
        };
        var root = new DockPanel();
        shell.Child = root;

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var title = new TextBlock
        {
            Text = "脚本属性",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4),
        };
        var subtitle = new TextBlock
        {
            Text = $"脚本：{assetName}",
            FontSize = 12,
            Foreground = MutedTextBrush,
        };
        header.Children.Add(title);
        header.Children.Add(subtitle);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var save = CreateButton("保存设置", 96);
        save.Click += (_, _) => SaveAndClose();
        var cancel = CreateButton("取消", 72);
        cancel.Click += (_, _) => DialogResult = false;
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var panel = new StackPanel();
        root.Children.Add(panel);

        // ---- ToolTips ----
        _countRadio.ToolTip = "按设定次数重复执行脚本";
        _untilStoppedRadio.ToolTip = "持续运行直到按下终止键为止";
        _durationRadio.ToolTip = "运行指定时长后自动停止";
        _loopCountBox.ToolTip = "脚本执行的循环次数";
        _hoursBox.ToolTip = "循环时长 - 小时";
        _minutesBox.ToolTip = "循环时长 - 分钟";
        _secondsBox.ToolTip = "循环时长 - 秒";
        _preventDuplicateCheck.ToolTip = "运行中再次触发启动热键时将忽略此次触发";
        _startPressCountBox.ToolTip = "在触发时间阈值内累计按下次数，达到此次数后触发";
        _stopPressCountBox.ToolTip = "在触发时间阈值内累计按下次数，达到此次数后触发";
        _startTriggerWindowBox.ToolTip = "按下热键的判定时间窗，在此时间内累计按下次数（毫秒）";
        _stopTriggerWindowBox.ToolTip = "按下热键的判定时间窗，在此时间内累计按下次数（毫秒）";

        panel.Children.Add(Section("运行设置",
            Row(_countRadio, _loopCountBox, Label("次")),
            Row(_untilStoppedRadio),
            Row(_durationRadio, _hoursBox, Label("小时"), _minutesBox, Label("分钟"), _secondsBox, Label("秒")),
            _preventDuplicateCheck));

        panel.Children.Add(Section("热键",
            HotkeyRow("启动热键", _settings.StartHotkey, _startHotkeyText, _startPressCountBox, _startTriggerWindowBox,
                () => CaptureHotkey(_settings.StartHotkey, _startHotkeyText)),
            HotkeyRow("终止热键", _settings.StopHotkey, _stopHotkeyText, _stopPressCountBox, _stopTriggerWindowBox,
                () => CaptureHotkey(_settings.StopHotkey, _stopHotkeyText))));

        _errorText.Foreground = Brushes.OrangeRed;
        _errorText.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(_errorText);
        return shell;
    }

    private void LoadSettings()
    {
        _countRadio.IsChecked = _settings.LoopMode == ScriptLoopMode.Count;
        _untilStoppedRadio.IsChecked = _settings.LoopMode == ScriptLoopMode.UntilStopped;
        _durationRadio.IsChecked = _settings.LoopMode == ScriptLoopMode.Duration;
        _loopCountBox.Text = _settings.LoopCount.ToString();
        _hoursBox.Text = _settings.DurationHours.ToString();
        _minutesBox.Text = _settings.DurationMinutes.ToString();
        _secondsBox.Text = _settings.DurationSeconds.ToString();
        _preventDuplicateCheck.IsChecked = _settings.PreventDuplicateRun;
        _startPressCountBox.Text = Math.Max(1, _settings.StartHotkey.PressCount).ToString();
        _stopPressCountBox.Text = Math.Max(1, _settings.StopHotkey.PressCount).ToString();
        _startTriggerWindowBox.Text = _settings.StartHotkey.TriggerWindowMs.ToString();
        _stopTriggerWindowBox.Text = _settings.StopHotkey.TriggerWindowMs.ToString();
        RefreshHotkeyText(_settings.StartHotkey, _startHotkeyText);
        RefreshHotkeyText(_settings.StopHotkey, _stopHotkeyText);
    }

    private void SaveAndClose()
    {
        _errorText.Text = string.Empty;
        _settings.LoopMode = _untilStoppedRadio.IsChecked == true
            ? ScriptLoopMode.UntilStopped
            : _durationRadio.IsChecked == true
                ? ScriptLoopMode.Duration
                : ScriptLoopMode.Count;
        _settings.LoopCount = ParseInt(_loopCountBox.Text, 1);
        _settings.DurationHours = ParseInt(_hoursBox.Text, 0);
        _settings.DurationMinutes = ParseInt(_minutesBox.Text, 0);
        _settings.DurationSeconds = ParseInt(_secondsBox.Text, 0);
        _settings.PreventDuplicateRun = _preventDuplicateCheck.IsChecked == true;
        _settings.StartHotkey.PressCount = ParseInt(_startPressCountBox.Text, 1);
        _settings.StopHotkey.PressCount = ParseInt(_stopPressCountBox.Text, 1);
        _settings.StartHotkey.TriggerWindowMs = ParseInt(_startTriggerWindowBox.Text, 1000);
        _settings.StopHotkey.TriggerWindowMs = ParseInt(_stopTriggerWindowBox.Text, 1000);
        _settings.Normalize();

        if (_settings.LoopMode == ScriptLoopMode.Duration &&
            _settings.DurationHours == 0 &&
            _settings.DurationMinutes == 0 &&
            _settings.DurationSeconds == 0)
        {
            _errorText.Text = "循环时长不能为 0";
            return;
        }

        if (_settings.StartHotkey.IsConfigured &&
            _settings.StopHotkey.IsConfigured &&
            ScriptHotkeyService.SameHotkey(_settings.StartHotkey, _settings.StopHotkey))
        {
            _errorText.Text = "启动热键与终止热键不能相同";
            return;
        }

        DialogResult = true;
    }

    private void CaptureHotkey(ScriptHotkeySettings target, TextBlock label)
    {
        var window = new ScriptHotkeyCaptureWindow(this);
        if (window.ShowDialog() != true || window.Result is null)
            return;

        target.InputKind = window.Result.InputKind;
        target.Key = window.Result.Key;
        target.PressCount = Math.Max(1, window.Result.PressCount);
        RefreshHotkeyText(target, label);
    }

    private static void RefreshHotkeyText(ScriptHotkeySettings settings, TextBlock label)
    {
        if (!settings.IsConfigured)
        {
            label.Text = "无";
            return;
        }
        label.Text = settings.Key;
    }

    private static int ParseInt(string text, int fallback) =>
        int.TryParse(text, out var value) ? value : fallback;

    private static TextBox CreateTextBox(string text, double width) => new()
    {
        Text = text,
        Width = width,
        Margin = new Thickness(4, 0, 4, 0),
        Background = InputBrush,
        Foreground = Brushes.White,
        BorderBrush = InputBorderBrush,
        Padding = new Thickness(6, 3, 6, 3),
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    private static TextBlock Label(string text, bool bold = false, double? width = null)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Margin = new Thickness(0, 3, 4, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = MutedTextBrush,
        };
        if (width.HasValue) tb.Width = width.Value;
        return tb;
    }

    private static Border Section(string text, params UIElement[] children)
    {
        var body = new StackPanel();
        foreach (var child in children)
            body.Children.Add(child);

        return new Border
        {
            Background = CardBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = text,
                        FontWeight = FontWeights.Bold,
                        FontSize = 13,
                        Margin = new Thickness(0, 0, 0, 10),
                        Foreground = Brushes.White,
                    },
                    body,
                }
            }
        };
    }

    private static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        foreach (var child in children)
            row.Children.Add(child);
        return row;
    }

    private static UIElement HotkeyRow(
        string title,
        ScriptHotkeySettings settings,
        TextBlock label,
        TextBox pressCountBox,
        TextBox triggerWindowBox,
        Action capture)
    {
        label.Width = 130;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Foreground = Brushes.White;
        var keyBadge = new Border
        {
            Background = InputBrush,
            BorderBrush = InputBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6, 10, 6),
            MinWidth = 110,
            Child = label,
        };

        var change = CreateButton("修改", 58);
        change.Click += (_, _) => capture();
        var clear = CreateButton("清空", 50);
        clear.Click += (_, _) =>
        {
            settings.Key = string.Empty;
            settings.PressCount = 1;
            settings.TriggerWindowMs = 1000;
            label.Text = "无";
            pressCountBox.Text = "1";
            triggerWindowBox.Text = "1000";
        };

        var headerRow = Row(
            Label(title, true),
            Label("按键", width: 32),
            keyBadge,
            change,
            Label("按下次数", width: 56),
            pressCountBox,
            clear);

        var thresholdRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 6),
        };
        var thresholdLabel = Label("触发时间阈值");
        var msLabel = Label("ms");
        triggerWindowBox.ToolTip = "在此时间窗内累计按下次数，达到目标次数后触发（毫秒）";
        thresholdRow.Children.Add(thresholdLabel);
        thresholdRow.Children.Add(triggerWindowBox);
        thresholdRow.Children.Add(msLabel);

        var wrapper = new StackPanel();
        wrapper.Children.Add(headerRow);
        wrapper.Children.Add(thresholdRow);
        return wrapper;
    }

    private static Button CreateButton(string text, double width) => new()
    {
        Content = text,
        Width = width,
        Margin = new Thickness(4, 0, 0, 0),
        Padding = new Thickness(8, 4, 8, 4),
        Background = AccentBrush,
        Foreground = Brushes.White,
        BorderBrush = AccentHoverBrush,
        BorderThickness = new Thickness(1),
        FontWeight = FontWeights.SemiBold,
    };

    private static SolidColorBrush Brush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

internal sealed class ScriptHotkeyCaptureWindow : Window
{
    public ScriptHotkeySettings? Result { get; private set; }
    private bool _captured;

    public ScriptHotkeyCaptureWindow(Window owner)
    {
        Owner = owner;
        Title = "修改热键";
        Width = 340;
        Height = 160;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x16, 0x1B, 0x23));
        Foreground = Brushes.White;
        Content = new TextBlock
        {
            Text = "按下键盘键或鼠标键作为热键。\n支持鼠标滚轮前滚/后滚。\nEsc 取消。",
            Margin = new Thickness(18),
            TextWrapping = TextWrapping.Wrap,
        };
        KeyDown += CaptureKeyDown;
        MouseDown += CaptureMouseDown;
        MouseWheel += CaptureMouseWheel;
    }

    private void CaptureKeyDown(object sender, KeyEventArgs e)
    {
        if (_captured) return;
        _captured = true;
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        Result = new ScriptHotkeySettings
        {
            InputKind = ScriptHotkeyInputKind.Keyboard,
            Key = key.ToString(),
            PressCount = 1,
        };
        DialogResult = true;
    }

    private void CaptureMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_captured) return;
        _captured = true;
        Result = new ScriptHotkeySettings
        {
            InputKind = ScriptHotkeyInputKind.Mouse,
            Key = e.ChangedButton.ToString(),
            PressCount = 1,
        };
        DialogResult = true;
    }

    private void CaptureMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_captured) return;
        _captured = true;
        Result = new ScriptHotkeySettings
        {
            InputKind = ScriptHotkeyInputKind.Mouse,
            Key = e.Delta > 0 ? "WheelForward" : "WheelBackward",
            PressCount = 1,
        };
        DialogResult = true;
    }
}