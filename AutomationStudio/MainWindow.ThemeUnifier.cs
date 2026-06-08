using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WpfBorder = System.Windows.Controls.Border;
using WpfBrush = System.Windows.Media.Brush;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfContextMenuEventArgs = System.Windows.Controls.ContextMenuEventArgs;
using WpfFrameworkElement = System.Windows.FrameworkElement;
using WpfItemsControl = System.Windows.Controls.ItemsControl;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfPanel = System.Windows.Controls.Panel;
using WpfRadioButton = System.Windows.Controls.RadioButton;
using WpfRichTextBox = System.Windows.Controls.RichTextBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfVisualTreeHelper = System.Windows.Media.VisualTreeHelper;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private static readonly SolidColorBrush UnifiedPanelBrush = FrozenBrush(0x20, 0x24, 0x2B);
    private static readonly SolidColorBrush UnifiedSurfaceBrush = FrozenBrush(0x1B, 0x20, 0x28);
    private static readonly SolidColorBrush UnifiedSurfaceAltBrush = FrozenBrush(0x18, 0x1B, 0x20);
    private static readonly SolidColorBrush UnifiedBorderBrush = FrozenBrush(0x30, 0x37, 0x44);
    private static readonly SolidColorBrush UnifiedStrongBorderBrush = FrozenBrush(0x38, 0x41, 0x50);
    private static readonly SolidColorBrush UnifiedSelectionBrush = FrozenBrush(0x30, 0x44, 0x5C);
    private static readonly SolidColorBrush UnifiedTextBrush = FrozenBrush(0xE8, 0xED, 0xF5);
    private static readonly SolidColorBrush UnifiedMutedTextBrush = FrozenBrush(0xA7, 0xB1, 0xBF);
    private static readonly SolidColorBrush UnifiedAccentBrush = FrozenBrush(0xD6, 0x8A, 0x22);
    private static readonly SolidColorBrush UnifiedErrorBrush = FrozenBrush(0xFF, 0x6B, 0x6B);

    private bool _unifiedThemeInstalled;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        InstallUnifiedDarkTheme();
    }

    private void InstallUnifiedDarkTheme()
    {
        if (_unifiedThemeInstalled)
            return;

        _unifiedThemeInstalled = true;
        ContextMenuOpening += MainWindow_ContextMenuOpeningTheme;
        Dispatcher.BeginInvoke(new Action(ApplyUnifiedDarkTheme), DispatcherPriority.ContextIdle);
    }

    private void MainWindow_ContextMenuOpeningTheme(object sender, WpfContextMenuEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(ApplyUnifiedContextMenuTheme), DispatcherPriority.ContextIdle);
    }

    private void ApplyUnifiedDarkTheme()
    {
        ApplyContentBrowserTheme();
        ApplyLogTheme();
        ApplyUnifiedContextMenuTheme();
    }

    private void ApplyContentBrowserTheme()
    {
        StyleListBoxSurface(ContentFolderListBox, UnifiedSurfaceBrush);
        StyleListBoxSurface(ContentBrowserListBox, UnifiedSurfaceBrush);

        ContentBrowserTreeSplitter.Background = UnifiedBorderBrush;

        foreach (var textBox in EnumerateVisualDescendants<WpfTextBox>(ContentBrowserListBox)
                     .Concat(EnumerateVisualDescendants<WpfTextBox>(ContentFolderListBox))
                     .Concat(EnumerateVisualDescendants<WpfTextBox>(ContentBrowserHeaderBar)))
        {
            StyleTextBox(textBox);
        }

        foreach (var border in EnumerateVisualDescendants<WpfBorder>(ContentBrowserListBox)
                     .Concat(EnumerateVisualDescendants<WpfBorder>(ContentFolderListBox)))
        {
            if (border.BorderThickness.Left > 0 || border.BorderThickness.Top > 0 || border.BorderThickness.Right > 0 || border.BorderThickness.Bottom > 0)
                border.BorderBrush = UnifiedStrongBorderBrush;
        }
    }

    private void ApplyLogTheme()
    {
        if (LogRichTextBox is WpfRichTextBox log)
        {
            log.Background = UnifiedSurfaceBrush;
            log.Foreground = UnifiedTextBrush;
            log.BorderBrush = UnifiedBorderBrush;
            log.SelectionBrush = UnifiedSelectionBrush;
        }

        if (FindParentBorder(LogRichTextBox) is { } logBorder)
        {
            logBorder.Background = UnifiedPanelBrush;
            logBorder.BorderBrush = UnifiedBorderBrush;
        }

        if (FindParentPanel(FilterAllRadio) is { } filterPanel)
            filterPanel.Background = UnifiedBorderBrush;

        StyleRadioButton(FilterAllRadio, UnifiedTextBrush);
        StyleRadioButton(FilterInfoRadio, UnifiedTextBrush);
        StyleRadioButton(FilterWarnRadio, UnifiedAccentBrush);
        StyleRadioButton(FilterErrorRadio, UnifiedErrorBrush);
    }

    private void ApplyUnifiedContextMenuTheme()
    {
        foreach (var menu in EnumerateContextMenus(this))
            StyleContextMenu(menu);
    }

    private static void StyleListBoxSurface(WpfListBox listBox, WpfBrush background)
    {
        listBox.Background = background;
        listBox.Foreground = UnifiedTextBrush;
        listBox.BorderBrush = UnifiedBorderBrush;
    }

    private static void StyleTextBox(WpfTextBox textBox)
    {
        textBox.Background = UnifiedSurfaceAltBrush;
        textBox.Foreground = UnifiedTextBrush;
        textBox.BorderBrush = UnifiedStrongBorderBrush;
        textBox.CaretBrush = UnifiedTextBrush;
        textBox.SelectionBrush = UnifiedSelectionBrush;
    }

    private static void StyleRadioButton(WpfRadioButton radioButton, WpfBrush foreground)
    {
        radioButton.Foreground = foreground;
        radioButton.Background = UnifiedSurfaceBrush;
        radioButton.BorderBrush = UnifiedStrongBorderBrush;
    }

    private static void StyleContextMenu(WpfContextMenu menu)
    {
        menu.Background = UnifiedPanelBrush;
        menu.BorderBrush = UnifiedBorderBrush;
        menu.Foreground = UnifiedTextBrush;
        menu.Padding = new Thickness(5);

        foreach (var item in EnumerateMenuItems(menu))
            StyleMenuItem(item);
    }

    private static void StyleMenuItem(WpfMenuItem item)
    {
        item.Background = Brushes.Transparent;
        item.Foreground = item.IsEnabled ? UnifiedTextBrush : UnifiedMutedTextBrush;
        item.Padding = new Thickness(10, 6, 10, 6);
        item.MinWidth = Math.Max(item.MinWidth, 130);
        item.BorderBrush = Brushes.Transparent;
    }

    private static IEnumerable<WpfMenuItem> EnumerateMenuItems(WpfItemsControl root)
    {
        foreach (var rawItem in root.Items)
        {
            if (rawItem is not WpfMenuItem menuItem)
                continue;

            yield return menuItem;
            foreach (var child in EnumerateMenuItems(menuItem))
                yield return child;
        }
    }

    private static IEnumerable<WpfContextMenu> EnumerateContextMenus(DependencyObject root)
    {
        foreach (var element in EnumerateVisualDescendants<WpfFrameworkElement>(root))
        {
            if (element.ContextMenu is { } menu)
                yield return menu;
        }
    }

    private static IEnumerable<T> EnumerateVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        int childCount = WpfVisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            var child = WpfVisualTreeHelper.GetChild(root, index);
            if (child is T typedChild)
                yield return typedChild;

            foreach (var descendant in EnumerateVisualDescendants<T>(child))
                yield return descendant;
        }
    }

    private static WpfBorder? FindParentBorder(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is WpfBorder border)
                return border;

            current = WpfVisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static WpfPanel? FindParentPanel(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is WpfPanel panel)
                return panel;

            current = WpfVisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static SolidColorBrush FrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
