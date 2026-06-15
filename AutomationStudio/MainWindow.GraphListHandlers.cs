using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private void AddGraphListItem_Click(object sender, RoutedEventArgs e)
    {
        SnapshotActiveAsset();
        SetSessionActiveGraphController(GetOperationEditorSession(), _graphListController, remember: false);
        _graphListController.AddAndRename(snapshotCurrent: false);
        SetSessionActiveGraphController(GetOperationEditorSession(), _graphListController);
        SaveSectionExpansionForActiveAsset(_graphListController);
        MarkCurrentContentDirty();
        UpdateGraphSectionVisibility();
    }

    private void GraphListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_graphListController.TryGetItemFromMouse(e, out var item))
        {
            LoadGraphItem(_graphListController, item, snapshotCurrent: true);
            e.Handled = true;
            UpdateGraphSectionVisibility();
        }
    }

    private void GraphListBox_KeyDown(object sender, KeyEventArgs e)
    {
        _graphListController.HandleKeyDown(e);
        SaveSectionExpansionForActiveAsset(_graphListController);
        UpdateGraphSectionVisibility();
    }

    private void RenameGraphMenuItem_Click(object sender, RoutedEventArgs e)
    {
        (_activeAssetController ?? _graphListController).RenameSelected();
    }

    private void DeleteGraphMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var controller = _activeAssetController ?? _graphListController;
        controller.DeleteSelected();
        SaveSectionExpansionForActiveAsset(controller);
        UpdateGraphSectionVisibility();
    }

    private void AddFunctionListItem_Click(object sender, RoutedEventArgs e)
    {
        SnapshotActiveAsset();
        SetSessionActiveGraphController(GetOperationEditorSession(), _functionListController, remember: false);
        _functionListController.AddAndRename(snapshotCurrent: false);
        SetSessionActiveGraphController(GetOperationEditorSession(), _functionListController);
        SaveSectionExpansionForActiveAsset(_functionListController);
        MarkCurrentContentDirty();
        UpdateGraphSectionVisibility();
    }

    private void FunctionListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_functionListController.TryGetItemFromMouse(e, out var item))
        {
            LoadGraphItem(_functionListController, item, snapshotCurrent: true);
            e.Handled = true;
            UpdateGraphSectionVisibility();
        }
    }

    private void FunctionListBox_KeyDown(object sender, KeyEventArgs e)
    {
        _functionListController.HandleKeyDown(e);
        SaveSectionExpansionForActiveAsset(_functionListController);
        UpdateGraphSectionVisibility();
    }

    private void FunctionListItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _functionListController.SelectRightClickedItem(sender);
        ActivateGraphListItem(_functionListController, sender, e);
    }

    private void FunctionListItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ActivateGraphListItem(_functionListController, sender, e);
    }

    private void GraphListItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _graphListController.SelectRightClickedItem(sender);
        ActivateGraphListItem(_graphListController, sender, e);
        e.Handled = false;
    }

    private void GraphListItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ActivateGraphListItem(_graphListController, sender, e);
    }

    private void ActivateGraphListItem(GraphListController controller, object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: GraphListItemViewModel item } ||
            item.IsEditing)
        {
            return;
        }

        var source = e.OriginalSource as DependencyObject ?? sender as DependencyObject;
        if (source is not null && HasVisualAncestor<WpfTextBox>(source))
            return;
        if (source is not null && HasVisualAncestor<System.Windows.Controls.Primitives.ToggleButton>(source))
            return;

        if (ReferenceEquals(_activeAssetController, controller) &&
            ReferenceEquals(controller.ActiveItem, item))
        {
            return;
        }

        LoadGraphItem(controller, item, snapshotCurrent: true);
        UpdateGraphSectionVisibility();
    }

    private void LoadGraphItem(GraphListController controller, GraphListItemViewModel item, bool snapshotCurrent)
    {
        if (snapshotCurrent)
            SnapshotActiveAsset();

        SetSessionActiveGraphController(GetOperationEditorSession(), controller, remember: false);
        controller.LoadItem(item, snapshotCurrent: false);
        var session = GetOperationEditorSession();
        SetSessionActiveGraphController(session, controller);
        _graphCommandService.Clear();
    }

    private void GraphNameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is WpfTextBox { DataContext: GraphListItemViewModel item } tb)
            GetControllerFor(item).HandleRenameKeyDown(tb, e);
    }

    private void GraphNameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is WpfTextBox { DataContext: GraphListItemViewModel item } tb)
            GetControllerFor(item).HandleRenameLostFocus(tb);
    }

    private void LibraryPublishCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox { DataContext: GraphListItemViewModel item })
            return;
        if (_activeContentAsset?.Kind is not ContentAssetKind.FunctionLibrary)
            return;

        item.IsDirty = true;
        MarkCurrentContentDirty();
        PersistAssetLibrary();
        SetStatus(item.IsPublicToLibrary
            ? $"已公开到库：{item.Name}"
            : $"已取消公开到库：{item.Name}");
    }

    private GraphListController GetControllerFor(GraphListItemViewModel item) =>
        item.Kind switch
        {
            GraphAssetKind.Function => _functionListController,
            _ => _graphListController,
        };

    private void ToggleEventGraphSection_Click(object sender, RoutedEventArgs e)
    {
        _graphListController.SetSectionExpanded(!_graphListController.IsSectionExpanded);
        SaveSectionExpansionForActiveAsset(_graphListController);
        UpdateGraphSectionVisibility();
    }

    private void ToggleFunctionSection_Click(object sender, RoutedEventArgs e)
    {
        _functionListController.SetSectionExpanded(!_functionListController.IsSectionExpanded);
        SaveSectionExpansionForActiveAsset(_functionListController);
        UpdateGraphSectionVisibility();
    }

    private void SaveSectionExpansionForActiveAsset(GraphListController controller)
    {
        if (_activeContentAsset is null)
            return;

        if (ReferenceEquals(controller, _graphListController))
        {
            _activeContentAsset.EventGraphSectionExpanded = controller.IsSectionExpanded;
            _activeContentAsset.EventGraphSectionHasState = true;
        }
        else if (ReferenceEquals(controller, _functionListController))
        {
            _activeContentAsset.FunctionSectionExpanded = controller.IsSectionExpanded;
            _activeContentAsset.FunctionSectionHasState = true;
        }
    }

    private void UpdateGraphSectionVisibility()
    {
        if (_graphListController is null || _functionListController is null)
            return;

        bool showEvent = _activeContentAsset?.Kind == ContentAssetKind.Script;
        bool showFunction = _activeContentAsset?.Kind is ContentAssetKind.Script or ContentAssetKind.FunctionLibrary;
        if (TryGetActiveEditorSurface() is not { } surface)
        {
            UpdateCompileButtonState();
            return;
        }

        UpdateGraphSection(_graphListController, surface.EventGraphPanel, surface.EventGraphSection, surface.EventGraphSectionToggle, surface.GraphListBox, showEvent);
        UpdateGraphSection(_functionListController, surface.FunctionPanel, surface.FunctionSection, surface.FunctionSectionToggle, surface.FunctionListBox, showFunction);
        surface.EventGraphDirtyBadge.Visibility = showEvent && _graphListController.HasCompileDirtyItems ? Visibility.Visible : Visibility.Collapsed;
        surface.FunctionDirtyBadge.Visibility = showFunction && _functionListController.HasCompileDirtyItems ? Visibility.Visible : Visibility.Collapsed;
        UpdateLibraryPublishOptionVisibility();
        UpdateCompileButtonState();
    }

    private void UpdateLibraryPublishOptionVisibility()
    {
        bool showFunctions = _activeContentAsset?.Kind == ContentAssetKind.FunctionLibrary;

        foreach (var item in FunctionListItems)
            item.ShowLibraryPublishOption = showFunctions;
    }

    private static void UpdateGraphSection(
        GraphListController controller,
        FrameworkElement panel,
        FrameworkElement header,
        System.Windows.Controls.Button toggle,
        FrameworkElement list,
        bool showSection)
    {
        controller.RefreshSectionExpansion();
        panel.Visibility = showSection ? Visibility.Visible : Visibility.Collapsed;
        header.Visibility = showSection ? Visibility.Visible : Visibility.Collapsed;
        toggle.Content = controller.IsSectionExpanded ? "v" : ">";
        list.Visibility = showSection && controller.IsSectionExpanded && controller.ItemCount > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
