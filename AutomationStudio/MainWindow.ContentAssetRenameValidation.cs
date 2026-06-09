using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AutomationStudioWpf.Services;
using WpfBorder = System.Windows.Controls.Border;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfPopup = System.Windows.Controls.Primitives.Popup;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private bool _contentAssetRenameValidationInstalled;
    private readonly Dictionary<WpfTextBox, WpfPopup> _contentRenameErrorPopups = [];
    private readonly Dictionary<WpfTextBox, WpfBrush?> _contentRenameOriginalBorderBrushes = [];

    private void InstallContentAssetRenameValidation()
    {
        if (_contentAssetRenameValidationInstalled)
            return;

        _contentAssetRenameValidationInstalled = true;

        AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(ContentAssetRenameTextBox_Loaded), true);
        AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(ContentAssetRenameTextBox_TextChangedValidated), true);
        AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(ContentAssetRenameTextBox_PreviewKeyDownValidated), true);
        AddHandler(Keyboard.PreviewLostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(ContentAssetRenameTextBox_PreviewLostKeyboardFocusValidated), true);
        PreviewMouseDown += ContentAssetRenameValidation_WindowPreviewMouseDown;

        foreach (var textBox in EnumerateVisualDescendants<WpfTextBox>(this))
            ConfigureContentAssetRenameTextBox(textBox);
    }

    private void ContentAssetRenameTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is WpfTextBox textBox)
            ConfigureContentAssetRenameTextBox(textBox);
    }

    private void ConfigureContentAssetRenameTextBox(WpfTextBox textBox)
    {
        if (textBox.DataContext is not ContentAssetViewModel)
            return;

        var binding = BindingOperations.GetBinding(textBox, WpfTextBox.TextProperty);
        if (binding?.Path.Path != nameof(ContentAssetViewModel.RenameText))
        {
            textBox.SetBinding(WpfTextBox.TextProperty, new Binding(nameof(ContentAssetViewModel.RenameText))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });
        }

        if (!_contentRenameOriginalBorderBrushes.ContainsKey(textBox))
            _contentRenameOriginalBorderBrushes[textBox] = textBox.BorderBrush;

        textBox.Unloaded -= ContentAssetRenameTextBox_Unloaded;
        textBox.Unloaded += ContentAssetRenameTextBox_Unloaded;
        UpdateContentAssetRenameErrorVisual(textBox);
    }

    private void ContentAssetRenameTextBox_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfTextBox textBox)
            return;

        if (_contentRenameErrorPopups.Remove(textBox, out var popup))
            popup.IsOpen = false;
        _contentRenameOriginalBorderBrushes.Remove(textBox);
    }

    private void ContentAssetRenameTextBox_TextChangedValidated(object sender, TextChangedEventArgs e)
    {
        if (e.OriginalSource is not WpfTextBox { DataContext: ContentAssetViewModel item } textBox || !item.IsEditing)
            return;

        ConfigureContentAssetRenameTextBox(textBox);
        ValidateContentAssetRenameInput(item, textBox.Text, showEmptyError: false);
        UpdateContentAssetRenameErrorVisual(textBox);
    }

    private void ContentAssetRenameTextBox_PreviewKeyDownValidated(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is not WpfTextBox { DataContext: ContentAssetViewModel item } textBox || !item.IsEditing)
            return;

        if (e.Key == Key.Enter)
        {
            TryCommitContentAssetRenameValidated(item, textBox, keepFocusOnError: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelContentAssetRenameValidated(item, textBox);
            e.Handled = true;
        }
    }

    private void ContentAssetRenameTextBox_PreviewLostKeyboardFocusValidated(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.OriginalSource is not WpfTextBox { DataContext: ContentAssetViewModel item } textBox || !item.IsEditing)
            return;

        if (TryCommitContentAssetRenameValidated(item, textBox, keepFocusOnError: true))
            return;

        e.Handled = true;
    }

    private void ContentAssetRenameValidation_WindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.FocusedElement is not WpfTextBox { DataContext: ContentAssetViewModel item } textBox || !item.IsEditing)
            return;

        if (e.OriginalSource is DependencyObject source && IsVisualAncestor(textBox, source))
            return;

        if (TryCommitContentAssetRenameValidated(item, textBox, keepFocusOnError: true))
            return;

        e.Handled = true;
    }

    private bool TryCommitContentAssetRenameValidated(ContentAssetViewModel item, WpfTextBox? textBox, bool keepFocusOnError)
    {
        if (_isCommittingContentAssetRename)
            return false;

        _isCommittingContentAssetRename = true;
        try
        {
            string newName = ((textBox?.Text ?? item.RenameText) ?? string.Empty).Trim();
            if (!ValidateContentAssetRenameInput(item, newName, showEmptyError: true))
            {
                SetStatus(item.RenameError);
                if (textBox is not null)
                {
                    UpdateContentAssetRenameErrorVisual(textBox);
                    if (keepFocusOnError)
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            textBox.Focus();
                            textBox.CaretIndex = textBox.Text.Length;
                        }), System.Windows.Threading.DispatcherPriority.Input);
                    }
                }
                return false;
            }

            item.RenameError = string.Empty;
            item.RenameText = newName;

            if (!string.Equals(item.Name, newName, StringComparison.Ordinal))
            {
                item.Name = newName;
                item.IsDirty = true;
                PersistAssetLibrary();
                SetStatus($"已重命名资产：{newName}");
            }

            item.IsEditing = false;
            if (textBox is not null)
                UpdateContentAssetRenameErrorVisual(textBox);
            RefreshContentBrowserViews();
            return true;
        }
        finally
        {
            _isCommittingContentAssetRename = false;
        }
    }

    private bool ValidateContentAssetRenameInput(ContentAssetViewModel item, string? text, bool showEmptyError)
    {
        string candidate = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            item.RenameError = showEmptyError ? "名称不能为空。" : string.Empty;
            return false;
        }

        if (HasSameLevelContentName(candidate, item.ParentFolderId, item))
        {
            item.RenameError = "同层级已存在同名资产。";
            return false;
        }

        item.RenameError = string.Empty;
        return true;
    }

    private void CancelContentAssetRenameValidated(ContentAssetViewModel item, WpfTextBox textBox)
    {
        item.RenameError = string.Empty;
        item.RenameText = item.Name;
        item.IsEditing = false;
        textBox.Text = item.Name;
        UpdateContentAssetRenameErrorVisual(textBox);
    }

    private void UpdateContentAssetRenameErrorVisual(WpfTextBox textBox)
    {
        if (textBox.DataContext is not ContentAssetViewModel item)
            return;

        bool hasError = item.IsEditing && item.HasRenameError;
        if (hasError)
        {
            textBox.BorderBrush = new WpfSolidColorBrush(WpfColor.FromRgb(255, 107, 107));
            textBox.BorderThickness = new Thickness(2);
            textBox.ToolTip = item.RenameError;
            ShowContentAssetRenameErrorPopup(textBox, item.RenameError);
            return;
        }

        if (_contentRenameOriginalBorderBrushes.TryGetValue(textBox, out var originalBrush) && originalBrush is not null)
            textBox.BorderBrush = originalBrush;
        else
            textBox.BorderBrush = UnifiedStrongBorderBrush;
        textBox.BorderThickness = new Thickness(1);
        textBox.ToolTip = null;
        HideContentAssetRenameErrorPopup(textBox);
    }

    private void ShowContentAssetRenameErrorPopup(WpfTextBox textBox, string message)
    {
        if (!_contentRenameErrorPopups.TryGetValue(textBox, out var popup))
        {
            var text = new WpfTextBlock
            {
                Foreground = new WpfSolidColorBrush(WpfColor.FromRgb(255, 120, 120)),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                MaxWidth = 220,
            };

            popup = new WpfPopup
            {
                PlacementTarget = textBox,
                Placement = PlacementMode.Bottom,
                AllowsTransparency = true,
                StaysOpen = true,
                IsHitTestVisible = false,
                Child = new WpfBorder
                {
                    Background = new WpfSolidColorBrush(WpfColor.FromRgb(39, 24, 26)),
                    BorderBrush = new WpfSolidColorBrush(WpfColor.FromRgb(255, 107, 107)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 3, 6, 3),
                    Margin = new Thickness(0, 2, 0, 0),
                    Child = text,
                },
            };
            _contentRenameErrorPopups[textBox] = popup;
        }

        if (popup.Child is WpfBorder { Child: WpfTextBlock textBlock })
            textBlock.Text = message;
        popup.IsOpen = textBox.IsVisible && textBox.IsKeyboardFocusWithin;
    }

    private void HideContentAssetRenameErrorPopup(WpfTextBox textBox)
    {
        if (_contentRenameErrorPopups.TryGetValue(textBox, out var popup))
            popup.IsOpen = false;
    }
}
