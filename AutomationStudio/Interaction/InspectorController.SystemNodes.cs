using System;
using System.Linq;
using System.Windows;
using AutomationStudioWpf.Graph;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace AutomationStudioWpf.Interaction;

public sealed partial class InspectorController
{
    public void BrowseFindImagePath()
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "选择图片文件",
            Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|所有文件(*.*)|*.*",
        };

        if (dialog.ShowDialog(_owner) == true)
        {
            _findImagePathTextBox.Text = dialog.FileName;
            ApplyChanges();
        }
    }

    public void BrowseFindImageSourcePath()
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "选择查找源图像",
            Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|所有文件(*.*)|*.*",
        };

        if (dialog.ShowDialog(_owner) == true)
        {
            _findImageSourcePathTextBox.Text = dialog.FileName;
            ApplyChanges();
        }
    }

    public void FindImageSourceModeChanged()
    {
        if (_isLoading)
            return;

        var mode = GetFindImageSourceMode();
        if (mode == ImageSearchSourceMode.RealtimeScreenshot &&
            _editorService.Nodes.FirstOrDefault(node => node.IsSelected) is FindImageNodeViewModel findImageNode)
        {
            ClearInputConnections(findImageNode, "source_image_path");
        }

        UpdateFindImageSourcePathVisibility(mode);
        ApplyChanges();
    }

    public void BrowseStartProgramPath()
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "选择应用程序",
            Filter = "可执行文件 (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|所有文件(*.*)|*.*",
        };

        if (dialog.ShowDialog(_owner) == true)
        {
            _startProgramPathTextBox.Text = dialog.FileName;
            ApplyChanges();
        }
    }

    public void SelectWindowInputModeChanged()
    {
        if (_isLoading)
            return;

        var mode = _selectWindowInputModeComboBox.SelectedIndex == 1 ? WindowInputMode.Auto : WindowInputMode.Manual;
        var node = _editorService.Nodes.OfType<SelectWindowNodeViewModel>().FirstOrDefault(n => n.IsSelected);
        if (node is null)
            return;

        bool locked = IsInputPinConnected(node, "process_name");
        node.InputMode = mode;
        UpdateSelectWindowModeVisibility(mode, locked);

        if (mode == WindowInputMode.Auto && !locked)
            SetComboSingleValue(_selectWindowAutoComboBox, node.ProcessName);

        _markDirty();
    }

    public void SelectWindowAutoChanged()
    {
        if (!_isLoading)
            ApplyChanges();
    }

    public void RefreshWindowList()
    {
        PopulateWindowListComboBox();
        var node = _editorService.Nodes.OfType<SelectWindowNodeViewModel>().FirstOrDefault(n => n.IsSelected);
        if (node is not null)
            _selectWindowAutoComboBox.SelectedItem = node.ProcessName;
    }

    public void AddCommonKeyChordKey()
    {
        string key = GetEditableComboValue(_commonKeyChordKeyComboBox);
        if (string.IsNullOrWhiteSpace(key))
            return;

        string current = _commonTextBox.Text.Trim();
        _commonTextBox.Text = string.IsNullOrWhiteSpace(current) ? key : $"{current}+{key}";
        ApplyChanges();
    }

    private void PopulateWindowListComboBox()
    {
        var names = _windowAdapter.GetRunningWindowNames();
        _selectWindowAutoComboBox.Items.Clear();
        foreach (var name in names)
            _selectWindowAutoComboBox.Items.Add(name);
    }

    private void UpdateSelectWindowModeVisibility(WindowInputMode mode, bool locked)
    {
        bool isAuto = mode == WindowInputMode.Auto;
        _selectWindowManualPanel.Visibility = locked ? Visibility.Visible : (isAuto ? Visibility.Collapsed : Visibility.Visible);
        _selectWindowAutoPanel.Visibility = isAuto && !locked ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PopulateCommonWindowComboBox()
    {
        string selected = _commonTextBox.Text.Trim();
        _commonWindowComboBox.Items.Clear();
        foreach (var name in _windowAdapter.GetRunningWindowNames())
        {
            _commonWindowComboBox.Items.Add(name);
            if (string.Equals(name, selected, StringComparison.OrdinalIgnoreCase))
                _commonWindowComboBox.SelectedItem = name;
        }
    }

    private static void SetComboSingleValue(WpfComboBox comboBox, string? value)
    {
        comboBox.Items.Clear();
        comboBox.SelectedItem = null;
        comboBox.Text = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
            return;

        comboBox.Items.Add(value);
        comboBox.SelectedItem = value;
    }

    private void SelectCommonMode(string? mode)
    {
        string target = Enum.TryParse<WindowInputMode>(mode, true, out var parsed)
            ? parsed.ToString()
            : WindowInputMode.Manual.ToString();
        foreach (var item in _commonModeComboBox.Items.OfType<WpfComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), target, StringComparison.OrdinalIgnoreCase))
            {
                _commonModeComboBox.SelectedItem = item;
                return;
            }
        }

        _commonModeComboBox.SelectedIndex = 0;
    }

    private void SelectFindImageSourceMode(ImageSearchSourceMode mode)
    {
        foreach (var item in _findImageSourceModeComboBox.Items.OfType<WpfComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), mode.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                _findImageSourceModeComboBox.SelectedItem = item;
                return;
            }
        }

        _findImageSourceModeComboBox.SelectedIndex = 1;
    }

    private ImageSearchSourceMode GetFindImageSourceMode()
    {
        string tag = GetSelectedComboTag(_findImageSourceModeComboBox, ImageSearchSourceMode.RealtimeScreenshot.ToString());
        return Enum.TryParse(tag, true, out ImageSearchSourceMode mode) ? mode : ImageSearchSourceMode.RealtimeScreenshot;
    }

    private void UpdateFindImageSourcePathVisibility(ImageSearchSourceMode mode)
    {
        var visibility = mode == ImageSearchSourceMode.ManualImage ? Visibility.Visible : Visibility.Collapsed;
        _findImageSourcePathLabel.Visibility = visibility;
        _findImageSourcePathPanel.Visibility = visibility;
    }

    private WindowInputMode GetCommonMode()
    {
        string tag = GetSelectedComboTag(_commonModeComboBox, WindowInputMode.Manual.ToString());
        return Enum.TryParse(tag, true, out WindowInputMode mode) ? mode : WindowInputMode.Manual;
    }

    private ImageSearchSourceMode GetCommonImageSearchSourceMode()
    {
        string tag = GetSelectedComboTag(_commonEnumComboBox, ImageSearchSourceMode.RealtimeScreenshot.ToString());
        return Enum.TryParse(tag, true, out ImageSearchSourceMode mode) ? mode : ImageSearchSourceMode.RealtimeScreenshot;
    }

    private ScreenshotSaveMode GetCommonScreenshotSaveMode()
    {
        string tag = GetSelectedComboTag(_commonEnumComboBox, ScreenshotSaveMode.Auto.ToString());
        return Enum.TryParse(tag, true, out ScreenshotSaveMode mode) ? mode : ScreenshotSaveMode.Auto;
    }

    private static ImageSearchSourceMode ParseImageSearchSourceMode(string? mode) =>
        Enum.TryParse(mode, true, out ImageSearchSourceMode parsed)
            ? parsed
            : ImageSearchSourceMode.RealtimeScreenshot;

    private static ScreenshotSaveMode ParseScreenshotSaveMode(string? mode) =>
        Enum.TryParse(mode, true, out ScreenshotSaveMode parsed)
            ? parsed
            : ScreenshotSaveMode.Auto;

    private static string GetCommonEnumFallback(NodeKind kind) =>
        kind switch
        {
            NodeKind.WaitImage or NodeKind.WaitImageDisappear => ImageSearchSourceMode.RealtimeScreenshot.ToString(),
            NodeKind.SaveScreenshot => ScreenshotSaveMode.Auto.ToString(),
            _ => string.Empty,
        };

    private static bool IsWindowCommonNode(NodeKind kind) =>
        kind is NodeKind.WaitWindow or NodeKind.CloseWindow or NodeKind.WindowExists;

    private static bool IsEnumCommonNode(NodeKind kind) =>
        kind is NodeKind.WaitImage or NodeKind.WaitImageDisappear or NodeKind.SaveScreenshot;

    private static string GetSelectedComboTag(WpfComboBox comboBox, string fallback)
    {
        return comboBox.SelectedItem is WpfComboBoxItem item
            ? item.Tag?.ToString() ?? fallback
            : fallback;
    }

    private static string GetEditableComboValue(WpfComboBox comboBox)
    {
        if (comboBox.SelectedItem is WpfComboBoxItem selectedItem)
            return selectedItem.Tag?.ToString() ?? selectedItem.Content?.ToString() ?? string.Empty;
        return comboBox.Text.Trim();
    }

    private void SetWhileMaxIterationsVisible(bool visible)
    {
        _whileMaxIterationsLabel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _whileMaxIterationsTextBox.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PopulateKeyboardKeyComboBox(string selectedKey)
    {
        PopulateKeyComboBox(_keyboardKeyComboBox, selectedKey);
    }

    private static void PopulateKeyComboBox(WpfComboBox comboBox, string selectedKey)
    {
        comboBox.Items.Clear();
        foreach (var key in GetKeyboardKeys())
        {
            var item = new WpfComboBoxItem { Content = key, Tag = key };
            comboBox.Items.Add(item);
            if (key == selectedKey)
                comboBox.SelectedItem = item;
        }
    }

    private static string[] GetKeyboardKeys() =>
    [
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
        "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
        "D0", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "Enter", "Escape", "Space", "Tab", "Backspace",
        "Shift", "Control", "Alt",
        "Left", "Up", "Right", "Down",
        "Insert", "DeleteKey", "Home", "End", "PageUp", "PageDown",
        "NumPad0", "NumPad1", "NumPad2", "NumPad3", "NumPad4",
        "NumPad5", "NumPad6", "NumPad7", "NumPad8", "NumPad9",
        "Add", "Subtract", "Multiply", "Divide",
        "LWin", "RWin",
    ];
}
