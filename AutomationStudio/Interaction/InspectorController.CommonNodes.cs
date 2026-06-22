using System.Windows;
using System.Windows.Controls;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Services;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace AutomationStudioWpf.Interaction;

public sealed partial class InspectorController
{
    private void ApplyCommonNodeChanges(CommonNodeViewModel commonNode)
    {
        if (commonNode.CanAddVariadicInput)
        {
            ApplyVariadicDefaultRows(commonNode);
        }

        if (_commonTextBox.Visibility == Visibility.Visible && _commonTextBox.IsEnabled)
            commonNode.Text = ReadCommonTextValue(commonNode.NodeKind, 1, _commonTextBox);
        if (_commonText2Box.Visibility == Visibility.Visible && _commonText2Box.IsEnabled)
            commonNode.Text2 = ReadCommonTextValue(commonNode.NodeKind, 2, _commonText2Box);
        if (IsEnumCommonNode(commonNode.NodeKind) && _commonEnumComboBox.IsEnabled)
            commonNode.Text2 = GetSelectedComboTag(_commonEnumComboBox, GetCommonEnumFallback(commonNode.NodeKind));
        if (_commonText3Box.Visibility == Visibility.Visible && _commonText3Box.IsEnabled)
            commonNode.Text3 = ReadCommonTextValue(commonNode.NodeKind, 3, _commonText3Box);

        if (_commonNumberBox.Visibility == Visibility.Visible && _commonNumberBox.IsEnabled &&
            double.TryParse(_commonNumberBox.Text.Trim(), out double number))
            commonNode.Number = number;
        if (_commonNumber2Box.Visibility == Visibility.Visible && _commonNumber2Box.IsEnabled &&
            double.TryParse(_commonNumber2Box.Text.Trim(), out double number2))
            commonNode.Number2 = number2;
        if (_commonNumber3Box.Visibility == Visibility.Visible && _commonNumber3Box.IsEnabled &&
            double.TryParse(_commonNumber3Box.Text.Trim(), out double number3))
            commonNode.Number3 = number3;
        if (_commonNumber4Box.Visibility == Visibility.Visible && _commonNumber4Box.IsEnabled &&
            double.TryParse(_commonNumber4Box.Text.Trim(), out double number4))
            commonNode.Number4 = number4;

        if (_commonFlagCheckBox.Visibility == Visibility.Visible && _commonFlagCheckBox.IsEnabled)
            commonNode.Flag = _commonFlagCheckBox.IsChecked == true;

        if (IsWindowCommonNode(commonNode.NodeKind) && _commonModeComboBox.IsEnabled)
            commonNode.Text2 = GetSelectedComboTag(_commonModeComboBox, WindowInputMode.Manual.ToString());
    }

    private void LoadCommonNode(CommonNodeViewModel node)
    {
        _commonTextBox.Text = node.Text;
        _commonText2Box.Text = node.Text2;
        _commonText3Box.Text = node.Text3;
        _commonNumberBox.Text = node.Number.ToString("0.##");
        _commonNumber2Box.Text = node.Number2.ToString("0.##");
        _commonNumber3Box.Text = node.Number3.ToString("0.##");
        _commonNumber4Box.Text = node.Number4.ToString("0.##");
        _commonFlagCheckBox.IsChecked = node.Flag;
        PopulateKeyComboBox(_commonKeyChordKeyComboBox, string.Empty);
        SetComboSingleValue(_commonWindowComboBox, IsWindowCommonNode(node.NodeKind) ? node.Text : string.Empty);
        SelectCommonMode(node.Text2);
        ConfigureCommonLabels(node.NodeKind);
        ConfigureCommonAuxiliaryControls(node);
        RebuildVariadicDefaultRows(node);
    }

    private void ConfigureCommonLabels(NodeKind kind)
    {
        SetCommonVisibility(true, true, true, true, true, true, true, true);
        _commonTextLabel.Text = "文本 1";
        _commonText2Label.Text = "文本 2";
        _commonText3Label.Text = "文本 3";
        _commonNumberLabel.Text = "数值 1";
        _commonNumber2Label.Text = "数值 2";
        _commonNumber3Label.Text = "数值 3";
        _commonNumber4Label.Text = "数值 4";
        _commonFlagCheckBox.Content = "布尔值";
        _commonHelpTextBlock.Text = string.Empty;
        ConfigureCommonTextModes(kind);

        switch (kind)
        {
            case NodeKind.MouseDoubleClick:
                SetCommonVisibility(false, false, false, true, true, false, false, false);
                _commonNumberLabel.Text = "点击位置 X";
                _commonNumber2Label.Text = "点击位置 Y";
                _commonHelpTextBlock.Text = "也可连接 Vector2D 到 position 输入，引脚输入优先。";
                break;
            case NodeKind.KeyChord:
                SetCommonVisibility(true, false, false, true, false, false, false, false);
                _commonTextLabel.Text = "组合预览";
                _commonNumberLabel.Text = "按住时长 ms";
                _commonHelpTextBlock.Text = "从上方下拉选择按键并点击增加，也可以手动编辑，例如 Ctrl+C、Ctrl+Shift+Esc。";
                break;
            case NodeKind.WaitImage:
            case NodeKind.WaitImageDisappear:
                SetCommonVisibility(true, false, true, true, true, true, false, false);
                _commonTextLabel.Text = "查找目标";
                _commonText3Label.Text = "查找源图像路径";
                _commonNumberLabel.Text = "超时 ms（0=不超时）";
                _commonNumber2Label.Text = "检测间隔 ms";
                _commonNumber3Label.Text = "相似度阈值 %";
                _commonHelpTextBlock.Text = kind == NodeKind.WaitImage
                    ? "查找源模式用下拉框选择。输出 result、center、image_path。"
                    : "查找源模式用下拉框选择。";
                break;
            case NodeKind.Compare:
                SetCommonVisibility(true, true, true, false, false, false, false, false);
                _commonTextLabel.Text = "左值，变量用 $变量名";
                _commonText2Label.Text = "右值，变量用 $变量名";
                _commonText3Label.Text = "操作：Equal/NotEqual/Contains/>/<";
                break;
            case NodeKind.BooleanAnd:
            case NodeKind.BooleanOr:
                SetCommonVisibility(false, false, false, false, false, false, false, false);
                break;
            case NodeKind.BooleanNot:
                SetCommonVisibility(false, false, false, false, false, false, false, true);
                _commonFlagCheckBox.Content = "输入值";
                break;
            case NodeKind.StringConcat:
                SetCommonVisibility(false, false, false, false, false, false, false, false);
                break;
            case NodeKind.WaitWindow:
                SetCommonVisibility(true, false, false, true, true, false, false, false);
                _commonTextLabel.Text = "进程名";
                _commonNumberLabel.Text = "超时 ms（0=不超时）";
                _commonNumber2Label.Text = "检测间隔 ms";
                break;
            case NodeKind.CloseWindow:
            case NodeKind.WindowExists:
                SetCommonVisibility(true, false, false, false, false, false, false, false);
                _commonTextLabel.Text = "进程名";
                break;
            case NodeKind.GetForegroundWindow:
                SetCommonVisibility(false, false, false, false, false, false, false, false);
                _commonHelpTextBlock.Text = "输出 process_name、window_title、result，可接后续窗口/日志/比较节点。";
                break;
            case NodeKind.SaveScreenshot:
                SetCommonVisibility(true, false, false, true, true, true, true, false);
                _commonTextLabel.Text = "保存路径 .png（手动模式使用）";
                _commonNumberLabel.Text = "区域 X（0=全屏）";
                _commonNumber2Label.Text = "区域 Y";
                _commonNumber3Label.Text = "区域宽（0=全屏）";
                _commonNumber4Label.Text = "区域高";
                _commonHelpTextBlock.Text = "默认 Auto，自动保存到项目临时目录 Temp/Screenshots，并输出 image_path。Manual 模式使用手动路径。";
                break;
            case NodeKind.ShowMessage:
                SetCommonVisibility(true, true, false, false, false, false, false, false);
                _commonTextLabel.Text = "消息内容";
                _commonText2Label.Text = "窗口标题";
                break;
            case NodeKind.GetMousePosition:
                SetCommonVisibility(false, false, false, false, false, false, false, false);
                _commonHelpTextBlock.Text = "输出 position(Vector2D)，可接鼠标点击/鼠标移动的位置输入。";
                break;
        }
    }

    private void SetCommonVisibility(
        bool text,
        bool text2,
        bool text3,
        bool number,
        bool number2,
        bool number3,
        bool number4,
        bool flag)
    {
        SetVisible(_commonTextLabel, _commonTextBox, text);
        SetVisible(_commonText2Label, _commonText2Box, text2);
        SetVisible(_commonText3Label, _commonText3Box, text3);
        SetVisible(_commonNumberLabel, _commonNumberBox, number);
        SetVisible(_commonNumber2Label, _commonNumber2Box, number2);
        SetVisible(_commonNumber3Label, _commonNumber3Box, number3);
        SetVisible(_commonNumber4Label, _commonNumber4Box, number4);
        _commonFlagCheckBox.Visibility = flag ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RebuildVariadicDefaultRows(CommonNodeViewModel node)
    {
        var panel = EnsureVariadicDefaultsPanel();
        panel.Children.Clear();
        panel.Visibility = node.CanAddVariadicInput ? Visibility.Visible : Visibility.Collapsed;
        if (!node.CanAddVariadicInput)
            return;

        for (int i = 1; i <= node.VariadicInputCount; i++)
        {
            string pinName = CommonNodeViewModel.VariadicInputName(i);
            bool connected = IsInputPinConnected(node, pinName);
            panel.Children.Add(CreateVariadicDefaultRow(node, pinName, i, connected));
        }
    }

    private StackPanel EnsureVariadicDefaultsPanel()
    {
        if (_commonVariadicDefaultsPanel is not null)
            return _commonVariadicDefaultsPanel;

        _commonVariadicDefaultsPanel = new StackPanel
        {
            Margin = new Thickness(0, 8, 0, 10),
        };

        int insertIndex = Math.Max(1, _commonInspectorPanel.Children.IndexOf(_commonTextLabel));
        _commonInspectorPanel.Children.Insert(insertIndex, _commonVariadicDefaultsPanel);
        return _commonVariadicDefaultsPanel;
    }

    private FrameworkElement CreateVariadicDefaultRow(CommonNodeViewModel node, string pinName, int ordinal, bool connected)
    {
        var root = new StackPanel
        {
            Margin = new Thickness(0, ordinal == 1 ? 0 : 8, 0, 0),
        };
        root.Children.Add(new TextBlock
        {
            Text = node.VariadicInputLabel(ordinal),
            Foreground = System.Windows.Media.Brushes.White,
        });

        if (node.NodeKind is NodeKind.BooleanAnd or NodeKind.BooleanOr)
        {
            var combo = new WpfComboBox
            {
                Margin = new Thickness(0, 4, 0, 0),
                Tag = pinName,
                IsEnabled = !connected,
            };
            combo.Items.Add(new WpfComboBoxItem { Content = "False", Tag = "False" });
            combo.Items.Add(new WpfComboBoxItem { Content = "True", Tag = "True" });
            combo.SelectedIndex = bool.TryParse(node.GetVariadicInputDefault(pinName), out bool value) && value ? 1 : 0;
            if (connected)
            {
                combo.Items.Clear();
                combo.Items.Add(new WpfComboBoxItem { Content = "前置输入", Tag = node.GetVariadicInputDefault(pinName) });
                combo.SelectedIndex = 0;
            }
            combo.SelectionChanged += VariadicDefaultComboBox_SelectionChanged;
            root.Children.Add(combo);
        }
        else
        {
            var textBox = new WpfTextBox
            {
                Margin = new Thickness(0, 4, 0, 0),
                Text = connected ? "前置输入" : node.GetVariadicInputDefault(pinName),
                Tag = pinName,
                IsEnabled = !connected,
            };
            ConfigureFreeTextBox(textBox, multiline: !connected && node.NodeKind == NodeKind.StringConcat);
            textBox.TextChanged += VariadicDefaultTextBox_TextChanged;
            root.Children.Add(textBox);
        }

        return root;
    }

    private void VariadicDefaultTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading ||
            sender is not WpfTextBox { Tag: string pinName, IsEnabled: true } textBox ||
            _editorService.Nodes.FirstOrDefault(node => node.IsSelected) is not CommonNodeViewModel commonNode ||
            !commonNode.CanAddVariadicInput)
        {
            return;
        }

        commonNode.SetVariadicInputDefault(pinName, textBox.Text);
        commonNode.RefreshDescription();
        _markDirty();
        _setStatus($"节点已自动保存：{commonNode.Title}");
    }

    private void VariadicDefaultComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading ||
            sender is not WpfComboBox { Tag: string pinName, IsEnabled: true } comboBox ||
            comboBox.SelectedItem is not WpfComboBoxItem { Tag: string value } ||
            _editorService.Nodes.FirstOrDefault(node => node.IsSelected) is not CommonNodeViewModel commonNode ||
            !commonNode.CanAddVariadicInput)
        {
            return;
        }

        commonNode.SetVariadicInputDefault(pinName, value);
        commonNode.RefreshDescription();
        _markDirty();
        _setStatus($"节点已自动保存：{commonNode.Title}");
    }

    private void ApplyVariadicDefaultRows(CommonNodeViewModel node)
    {
        if (_commonVariadicDefaultsPanel is null)
            return;

        foreach (var child in _commonVariadicDefaultsPanel.Children.OfType<StackPanel>())
        {
            if (child.Children.OfType<WpfTextBox>().FirstOrDefault() is { Tag: string textPin, IsEnabled: true } textBox)
                node.SetVariadicInputDefault(textPin, textBox.Text);
            if (child.Children.OfType<WpfComboBox>().FirstOrDefault() is { Tag: string comboPin, IsEnabled: true } comboBox &&
                comboBox.SelectedItem is WpfComboBoxItem { Tag: string value })
            {
                node.SetVariadicInputDefault(comboPin, value);
            }
        }
    }

    private void ConfigureCommonAuxiliaryControls(CommonNodeViewModel node)
    {
        bool isWindowNode = IsWindowCommonNode(node.NodeKind);
        WindowInputMode mode = GetCommonMode();
        _commonKeyChordAddPanel.Visibility = node.NodeKind == NodeKind.KeyChord ? Visibility.Visible : Visibility.Collapsed;
        _commonModePanel.Visibility = isWindowNode ? Visibility.Visible : Visibility.Collapsed;
        _commonWindowPickerPanel.Visibility = isWindowNode && mode == WindowInputMode.Auto ? Visibility.Visible : Visibility.Collapsed;
        _commonEnumPanel.Visibility = IsEnumCommonNode(node.NodeKind) ? Visibility.Visible : Visibility.Collapsed;
        ConfigureCommonEnumComboBox(node);
        UpdateCommonEnumDependentVisibility(node);
        bool manualScreenshot = node.NodeKind == NodeKind.SaveScreenshot &&
                                GetCommonScreenshotSaveMode() == ScreenshotSaveMode.Manual;
        _commonBrowseFileButton.Visibility = node.NodeKind is NodeKind.WaitImage or NodeKind.WaitImageDisappear ||
                                             manualScreenshot ||
                                             (isWindowNode && mode == WindowInputMode.ExePath)
            ? Visibility.Visible
            : Visibility.Collapsed;
        _commonBrowseFileButton.Content = isWindowNode ? "浏览 exe" : "浏览";
    }

    private void ConfigureCommonEnumComboBox(CommonNodeViewModel node)
    {
        _commonEnumComboBox.Items.Clear();
        if (node.NodeKind is NodeKind.WaitImage or NodeKind.WaitImageDisappear)
        {
            _commonEnumLabel.Text = "查找源";
            AddCommonEnumOption("实时截屏", ImageSearchSourceMode.RealtimeScreenshot.ToString());
            AddCommonEnumOption("手动配置图像", ImageSearchSourceMode.ManualImage.ToString());
            SelectCommonEnumValue(ParseImageSearchSourceMode(node.Text2).ToString());
            return;
        }

        if (node.NodeKind == NodeKind.SaveScreenshot)
        {
            _commonEnumLabel.Text = "保存路径模式";
            AddCommonEnumOption("自动保存", ScreenshotSaveMode.Auto.ToString());
            AddCommonEnumOption("手动配置", ScreenshotSaveMode.Manual.ToString());
            SelectCommonEnumValue(ParseScreenshotSaveMode(node.Text2).ToString());
        }
    }

    private void AddCommonEnumOption(string content, string tag)
    {
        _commonEnumComboBox.Items.Add(new WpfComboBoxItem { Content = content, Tag = tag });
    }

    private void SelectCommonEnumValue(string tag)
    {
        foreach (var item in _commonEnumComboBox.Items.OfType<WpfComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                _commonEnumComboBox.SelectedItem = item;
                return;
            }
        }

        if (_commonEnumComboBox.Items.Count > 0)
            _commonEnumComboBox.SelectedIndex = 0;
    }

    private void UpdateCommonEnumDependentVisibility(CommonNodeViewModel node)
    {
        if (node.NodeKind is NodeKind.WaitImage or NodeKind.WaitImageDisappear)
        {
            var mode = GetCommonImageSearchSourceMode();
            bool manual = mode == ImageSearchSourceMode.ManualImage;
            _commonText3Label.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
            _commonText3Box.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
        }

        if (node.NodeKind == NodeKind.SaveScreenshot)
        {
            bool manual = GetCommonScreenshotSaveMode() == ScreenshotSaveMode.Manual;
            _commonTextLabel.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
            _commonTextBox.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
            _commonBrowseFileButton.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static void SetVisible(TextBlock label, WpfTextBox textBox, bool visible)
    {
        label.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        textBox.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public void CommonModeChanged()
    {
        if (_isLoading)
            return;

        if (_editorService.Nodes.FirstOrDefault(node => node.IsSelected) is CommonNodeViewModel commonNode)
        {
            ConfigureCommonAuxiliaryControls(commonNode);
            ApplyChanges();
        }
    }

    public void CommonEnumChanged()
    {
        if (_isLoading)
            return;

        if (_editorService.Nodes.FirstOrDefault(node => node.IsSelected) is CommonNodeViewModel commonNode)
        {
            ClearConnectionsForHiddenCommonEnumPins(commonNode);
            UpdateCommonEnumDependentVisibility(commonNode);
            ApplyChanges();
            LockCommonInputs(commonNode);
        }
    }

    private void ClearConnectionsForHiddenCommonEnumPins(CommonNodeViewModel node)
    {
        if (node.NodeKind is NodeKind.WaitImage or NodeKind.WaitImageDisappear &&
            GetCommonImageSearchSourceMode() == ImageSearchSourceMode.RealtimeScreenshot)
        {
            ClearInputConnections(node, "source_image_path");
        }

        if (node.NodeKind == NodeKind.SaveScreenshot &&
            GetCommonScreenshotSaveMode() == ScreenshotSaveMode.Auto)
        {
            ClearInputConnections(node, "path");
        }
    }

    public void CommonWindowChanged()
    {
        if (_isLoading)
            return;

        if (_editorService.Nodes.FirstOrDefault(node => node.IsSelected) is not CommonNodeViewModel commonNode ||
            !IsWindowCommonNode(commonNode.NodeKind) ||
            _commonWindowComboBox.SelectedItem is not string processName)
            return;

        _commonTextBox.Text = processName;
        ApplyChanges();
    }

    public void RefreshCommonWindowList()
    {
        PopulateCommonWindowComboBox();
    }

    public void BrowseCommonFile()
    {
        if (_editorService.Nodes.FirstOrDefault(node => node.IsSelected) is not CommonNodeViewModel commonNode)
            return;

        if (IsWindowCommonNode(commonNode.NodeKind))
        {
            var dialog = new WpfOpenFileDialog
            {
                Title = "选择应用程序",
                Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            };
            if (dialog.ShowDialog(_owner) == true)
            {
                _commonText3Box.Text = dialog.FileName;
                _commonTextBox.Text = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
                SelectCommonMode(WindowInputMode.ExePath.ToString());
                ApplyChanges();
            }
            return;
        }

        if (commonNode.NodeKind is NodeKind.WaitImage or NodeKind.WaitImageDisappear)
        {
            var dialog = new WpfOpenFileDialog
            {
                Title = "选择图片文件",
                Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|所有文件 (*.*)|*.*",
            };
            if (dialog.ShowDialog(_owner) == true)
            {
                _commonTextBox.Text = dialog.FileName;
                ApplyChanges();
            }
            return;
        }

        if (commonNode.NodeKind == NodeKind.SaveScreenshot)
        {
            var dialog = new WpfSaveFileDialog
            {
                Title = "选择截图保存路径",
                Filter = "PNG 图片 (*.png)|*.png|所有文件 (*.*)|*.*",
                DefaultExt = ".png",
            };
            if (dialog.ShowDialog(_owner) == true)
            {
                SelectCommonEnumValue(ScreenshotSaveMode.Manual.ToString());
                _commonTextBox.Text = dialog.FileName;
                ApplyChanges();
            }
        }
    }

}
