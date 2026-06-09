using System.Linq;
using AutomationStudioWpf.Services;
using WpfKey = System.Windows.Input.Key;
using WpfKeyboard = System.Windows.Input.Keyboard;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfModifierKeys = System.Windows.Input.ModifierKeys;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    protected override void OnPreviewKeyDown(WpfKeyEventArgs e)
    {
        if (TryHandleContentBrowserGlobalShortcut(e))
            return;

        base.OnPreviewKeyDown(e);
    }

    private bool TryHandleContentBrowserGlobalShortcut(WpfKeyEventArgs e)
    {
        if (e.Handled || WpfKeyboard.FocusedElement is WpfTextBox)
            return false;

        bool ctrl = (WpfKeyboard.Modifiers & WpfModifierKeys.Control) != 0;
        bool isContentShortcut =
            e.Key == WpfKey.Delete ||
            e.Key == WpfKey.F2 ||
            (ctrl && (e.Key == WpfKey.C || e.Key == WpfKey.V));

        if (!isContentShortcut || !ShouldRouteShortcutToContentBrowser())
            return false;

        if (ctrl && e.Key == WpfKey.C)
        {
            CopySelectedContentAssets();
            e.Handled = true;
            return true;
        }

        if (ctrl && e.Key == WpfKey.V)
        {
            PasteContentAssetsToCurrentFolder();
            e.Handled = true;
            return true;
        }

        if (e.Key == WpfKey.Delete)
        {
            DeleteSelectedContentAssets();
            e.Handled = true;
            return true;
        }

        if (e.Key == WpfKey.F2)
        {
            StartRenameSelectedContentAssetEnhanced();
            e.Handled = true;
            return true;
        }

        return false;
    }

    private bool ShouldRouteShortcutToContentBrowser()
    {
        if (IsFocusInside(ContentBrowserListBox) || IsFocusInside(ContentFolderListBox))
            return true;

        // 框选/Shift/Ctrl 多选后，焦点可能被菜单、画布或其它控件拿走。
        // 只要右侧资产浏览器仍有多个选中项，Delete/Ctrl+C 仍按资产集合处理。
        return ContentBrowserListBox.SelectedItems
            .Cast<ContentAssetViewModel>()
            .Count(item => !ReferenceEquals(item, _rootContentFolder)) > 1;
    }
}
