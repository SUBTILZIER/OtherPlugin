using System.Windows;
using System.Windows.Input;
using WpfButton = System.Windows.Controls.Button;
using WpfListBoxItem = System.Windows.Controls.ListBoxItem;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private void ContentFolderItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            (HasVisualAncestor<WpfButton>(source) || HasVisualAncestor<WpfTextBox>(source)))
            return;

        if (sender is not WpfListBoxItem { DataContext: ContentAssetViewModel { IsFolder: true } folder })
            return;

        _contentFolderSelectionActive = true;
        ContentFolderListBox.SelectedItem = folder;
        ContentBrowserListBox.SelectedItem = null;
        ContentFolderListBox.Focus();

        if (e.ClickCount >= 2)
        {
            EnterContentFolder(ReferenceEquals(folder, _rootContentFolder) ? null : folder);
            e.Handled = true;
        }
    }
}
