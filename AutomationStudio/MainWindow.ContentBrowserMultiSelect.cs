using System;
using System.Collections.Generic;
using System.Linq;
using AutomationStudioWpf.Services;
using WpfAdorner = System.Windows.Documents.Adorner;
using WpfAdornerLayer = System.Windows.Documents.AdornerLayer;
using WpfBorder = System.Windows.Controls.Border;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfDataObject = System.Windows.DataObject;
using WpfDependencyObject = System.Windows.DependencyObject;
using WpfDragDrop = System.Windows.DragDrop;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDragDropKeyStates = System.Windows.DragDropKeyStates;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfDragEventHandler = System.Windows.DragEventHandler;
using WpfDrawingContext = System.Windows.Media.DrawingContext;
using WpfDropShadowEffect = System.Windows.Media.Effects.DropShadowEffect;
using WpfFontWeights = System.Windows.FontWeights;
using WpfGiveFeedbackEventArgs = System.Windows.GiveFeedbackEventArgs;
using WpfKey = System.Windows.Input.Key;
using WpfKeyboard = System.Windows.Input.Keyboard;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfKeyEventHandler = System.Windows.Input.KeyEventHandler;
using WpfListBoxItem = System.Windows.Controls.ListBoxItem;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfMouseButtonEventHandler = System.Windows.Input.MouseButtonEventHandler;
using WpfMouseButtonState = System.Windows.Input.MouseButtonState;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfMouseEventHandler = System.Windows.Input.MouseEventHandler;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfPen = System.Windows.Media.Pen;
using WpfPlacementMode = System.Windows.Controls.Primitives.PlacementMode;
using WpfPoint = System.Windows.Point;
using WpfPopup = System.Windows.Controls.Primitives.Popup;
using WpfRect = System.Windows.Rect;
using WpfSelectionMode = System.Windows.Controls.SelectionMode;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfSystemParameters = System.Windows.SystemParameters;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfTextTrimming = System.Windows.TextTrimming;
using WpfThickness = System.Windows.Thickness;
using WpfUIElement = System.Windows.UIElement;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;
using WpfVisualTreeHelper = System.Windows.Media.VisualTreeHelper;
using WinFormsControl = System.Windows.Forms.Control;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private const string ContentAssetDragDataFormat = "AutomationStudioWpf.ContentAssetList";

    private bool _contentBrowserEnhancedInteractionsInstalled;
    private bool _isContentBoxSelecting;
    private bool _isContentDragActive;
    private WpfPoint _contentBoxSelectionOrigin;
    private WpfPoint _contentAssetDragStartPoint;
    private ContentAssetViewModel? _contentRangeAnchor;
    private ContentAssetViewModel? _contentDragCandidate;
    private ContentSelectionAdorner? _contentSelectionAdorner;
    private WpfAdornerLayer? _contentSelectionAdornerLayer;
    private HashSet<string> _contentBoxSelectionBaseIds = [];
    private List<ContentAssetViewModel> _contentClipboardAssets = [];
    private WpfPopup? _contentDragPreviewPopup;

    private void InstallContentBrowserEnhancedInteractions()
    {
        if (_contentBrowserEnhancedInteractionsInstalled)
            return;

        _contentBrowserEnhancedInteractionsInstalled = true;
        ContentBrowserListBox.SelectionMode = WpfSelectionMode.Extended;
        ContentBrowserListBox.AllowDrop = true;
        ContentFolderListBox.AllowDrop = true;

        ContentBrowserBodyGrid.AddHandler(WpfUIElement.PreviewMouseLeftButtonDownEvent, new WpfMouseButtonEventHandler(ContentBrowserEnhanced_PreviewMouseLeftButtonDown), true);
        ContentBrowserBodyGrid.AddHandler(WpfUIElement.PreviewMouseMoveEvent, new WpfMouseEventHandler(ContentBrowserEnhanced_PreviewMouseMove), true);
        ContentBrowserBodyGrid.AddHandler(WpfUIElement.PreviewMouseLeftButtonUpEvent, new WpfMouseButtonEventHandler(ContentBrowserEnhanced_PreviewMouseLeftButtonUp), true);
        ContentBrowserBodyGrid.AddHandler(WpfDragDrop.PreviewDragOverEvent, new WpfDragEventHandler(ContentBrowserEnhanced_PreviewDragOver), true);
        ContentBrowserBodyGrid.AddHandler(WpfDragDrop.PreviewDropEvent, new WpfDragEventHandler(ContentBrowserEnhanced_PreviewDrop), true);
        ContentBrowserListBox.AddHandler(WpfKeyboard.PreviewKeyDownEvent, new WpfKeyEventHandler(ContentBrowserEnhanced_PreviewKeyDown), true);
        ContentBrowserListBox.GiveFeedback += ContentBrowserEnhanced_GiveFeedback;
    }

    private void ContentBrowserEnhanced_PreviewMouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
    {
        if (e.OriginalSource is not WpfDependencyObject source || !IsVisualInside(ContentBrowserListBox, source))
            return;
        if (HasVisualAncestor<WpfTextBox>(source))
            return;

        _contentFolderSelectionActive = false;
        ContentFolderListBox.SelectedItem = null;
        ContentBrowserListBox.Focus();

        var asset = GetContentVisibleAssetFromSource(source);
        if (asset is not null)
        {
            _contentDragCandidate = asset;
            _contentAssetDragStartPoint = e.GetPosition(ContentBrowserListBox);
            _isContentDragActive = false;

            if (e.ClickCount >= 2)
            {
                SelectContentAssetForClick(asset, WpfKeyboard.Modifiers, forceSingle: true);
                if (asset.Kind == ContentAssetKind.Folder)
                    EnterContentFolder(asset);
                else
                    OpenContentAsset(asset);

                e.Handled = true;
                return;
            }

            SelectContentAssetForClick(asset, WpfKeyboard.Modifiers, forceSingle: false);
            e.Handled = true;
            return;
        }

        _contentDragCandidate = null;
        BeginContentBoxSelection(e.GetPosition(ContentBrowserListBox), WpfKeyboard.Modifiers);
        e.Handled = true;
    }

    private void ContentBrowserEnhanced_PreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_isContentBoxSelecting)
        {
            if (e.LeftButton != WpfMouseButtonState.Pressed)
            {
                EndContentBoxSelection();
                e.Handled = true;
                return;
            }

            UpdateContentBoxSelection(e.GetPosition(ContentBrowserListBox), WpfKeyboard.Modifiers);
            e.Handled = true;
            return;
        }

        if (_contentDragCandidate is null || _isContentDragActive || e.LeftButton != WpfMouseButtonState.Pressed)
            return;

        var current = e.GetPosition(ContentBrowserListBox);
        if (Math.Abs(current.X - _contentAssetDragStartPoint.X) < WpfSystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _contentAssetDragStartPoint.Y) < WpfSystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        StartContentAssetDrag();
        e.Handled = true;
    }

    private void ContentBrowserEnhanced_PreviewMouseLeftButtonUp(object sender, WpfMouseButtonEventArgs e)
    {
        if (_isContentBoxSelecting)
        {
            EndContentBoxSelection();
            e.Handled = true;
        }

        _contentDragCandidate = null;
    }

    private void ContentBrowserEnhanced_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if ((WpfKeyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0)
            return;

        if (e.Key == WpfKey.C)
        {
            CopySelectedContentAssets();
            e.Handled = true;
        }
        else if (e.Key == WpfKey.V)
        {
            PasteContentAssetsToCurrentFolder();
            e.Handled = true;
        }
    }

    private void SelectContentAssetForClick(ContentAssetViewModel asset, System.Windows.Input.ModifierKeys modifiers, bool forceSingle)
    {
        bool ctrl = (modifiers & System.Windows.Input.ModifierKeys.Control) != 0;
        bool shift = (modifiers & System.Windows.Input.ModifierKeys.Shift) != 0;

        if (shift && _contentRangeAnchor is not null)
        {
            SelectContentAssetRange(_contentRangeAnchor, asset, keepExisting: ctrl);
            return;
        }

        if (ctrl && !forceSingle)
        {
            if (ContentBrowserListBox.SelectedItems.Contains(asset))
                ContentBrowserListBox.SelectedItems.Remove(asset);
            else
                ContentBrowserListBox.SelectedItems.Add(asset);

            _contentRangeAnchor = asset;
            return;
        }

        if (forceSingle || !ContentBrowserListBox.SelectedItems.Contains(asset))
        {
            ContentBrowserListBox.SelectedItems.Clear();
            ContentBrowserListBox.SelectedItems.Add(asset);
        }

        _contentRangeAnchor = asset;
    }

    private void SelectContentAssetRange(ContentAssetViewModel start, ContentAssetViewModel end, bool keepExisting)
    {
        int startIndex = ContentVisibleItems.IndexOf(start);
        int endIndex = ContentVisibleItems.IndexOf(end);
        if (startIndex < 0 || endIndex < 0)
        {
            ContentBrowserListBox.SelectedItems.Clear();
            ContentBrowserListBox.SelectedItems.Add(end);
            _contentRangeAnchor = end;
            return;
        }

        if (!keepExisting)
            ContentBrowserListBox.SelectedItems.Clear();

        int min = Math.Min(startIndex, endIndex);
        int max = Math.Max(startIndex, endIndex);
        for (int index = min; index <= max; index++)
        {
            var item = ContentVisibleItems[index];
            if (!ContentBrowserListBox.SelectedItems.Contains(item))
                ContentBrowserListBox.SelectedItems.Add(item);
        }
    }

    private void BeginContentBoxSelection(WpfPoint origin, System.Windows.Input.ModifierKeys modifiers)
    {
        _isContentBoxSelecting = true;
        _contentBoxSelectionOrigin = origin;
        _contentBoxSelectionBaseIds = (modifiers & (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift)) != 0
            ? GetSelectedContentAssets().Select(item => item.Id).ToHashSet()
            : [];

        if (_contentBoxSelectionBaseIds.Count == 0)
            ContentBrowserListBox.SelectedItems.Clear();

        ContentBrowserListBox.CaptureMouse();
        EnsureContentSelectionAdorner();
        _contentSelectionAdorner?.Update(new WpfRect(origin, origin));
    }

    private void UpdateContentBoxSelection(WpfPoint current, System.Windows.Input.ModifierKeys modifiers)
    {
        var selectionRect = NormalizeRect(new WpfRect(_contentBoxSelectionOrigin, current));
        _contentSelectionAdorner?.Update(selectionRect);

        var selectedByBox = new HashSet<string>();
        foreach (var asset in ContentVisibleItems)
        {
            if (ContentBrowserListBox.ItemContainerGenerator.ContainerFromItem(asset) is not WpfListBoxItem container)
                continue;

            var itemRect = container.TransformToAncestor(ContentBrowserListBox)
                .TransformBounds(new WpfRect(new WpfPoint(0, 0), container.RenderSize));

            if (selectionRect.IntersectsWith(itemRect))
                selectedByBox.Add(asset.Id);
        }

        ContentBrowserListBox.SelectedItems.Clear();
        foreach (var asset in ContentVisibleItems)
        {
            if (_contentBoxSelectionBaseIds.Contains(asset.Id) || selectedByBox.Contains(asset.Id))
                ContentBrowserListBox.SelectedItems.Add(asset);
        }
    }

    private void EndContentBoxSelection()
    {
        _isContentBoxSelecting = false;
        ContentBrowserListBox.ReleaseMouseCapture();
        if (_contentSelectionAdorner is not null)
        {
            _contentSelectionAdornerLayer?.Remove(_contentSelectionAdorner);
            _contentSelectionAdorner = null;
            _contentSelectionAdornerLayer = null;
        }
    }

    private void EnsureContentSelectionAdorner()
    {
        if (_contentSelectionAdorner is not null)
            return;

        _contentSelectionAdornerLayer = WpfAdornerLayer.GetAdornerLayer(ContentBrowserListBox);
        if (_contentSelectionAdornerLayer is null)
            return;

        _contentSelectionAdorner = new ContentSelectionAdorner(ContentBrowserListBox);
        _contentSelectionAdornerLayer.Add(_contentSelectionAdorner);
    }

    private void StartContentAssetDrag()
    {
        var selectedAssets = GetSelectedContentAssets()
            .Where(item => !ReferenceEquals(item, _rootContentFolder))
            .ToList();

        if (_contentDragCandidate is not null && !selectedAssets.Contains(_contentDragCandidate))
        {
            selectedAssets.Clear();
            selectedAssets.Add(_contentDragCandidate);
        }

        if (selectedAssets.Count == 0)
            return;

        _isContentDragActive = true;
        var data = new WpfDataObject();
        data.SetData(ContentAssetDragDataFormat, selectedAssets);

        SetContentAssetDragOpacity(selectedAssets, 0.45);
        StartContentDragPreview(selectedAssets);
        try
        {
            WpfDragDrop.DoDragDrop(ContentBrowserListBox, data, WpfDragDropEffects.Move | WpfDragDropEffects.Copy);
        }
        finally
        {
            StopContentDragPreview();
            SetContentAssetDragOpacity(ContentVisibleItems.ToList(), 1.0);
            _contentDragCandidate = null;
            _isContentDragActive = false;
        }
    }

    private void ContentBrowserEnhanced_PreviewDragOver(object sender, WpfDragEventArgs e)
    {
        if (!TryGetDraggedContentAssets(e, out var sources))
            return;

        var target = GetContentDropTargetFolder(e.OriginalSource as WpfDependencyObject);
        bool copy = (e.KeyStates & WpfDragDropKeyStates.ControlKey) != 0;
        e.Effects = target is not null && sources.Any(source => CanDropContentAsset(source, target, copy))
            ? (copy ? WpfDragDropEffects.Copy : WpfDragDropEffects.Move)
            : WpfDragDropEffects.None;
        e.Handled = true;
    }

    private void ContentBrowserEnhanced_PreviewDrop(object sender, WpfDragEventArgs e)
    {
        if (!TryGetDraggedContentAssets(e, out var sources))
            return;

        var target = GetContentDropTargetFolder(e.OriginalSource as WpfDependencyObject);
        if (target is null)
        {
            e.Effects = WpfDragDropEffects.None;
            e.Handled = true;
            return;
        }

        var choice = ShowContentDropActionDialog(sources.Count == 1 ? sources[0].Name : $"{sources.Count} 个资产");
        if (choice == ContentDropAction.Cancel)
        {
            e.Effects = WpfDragDropEffects.None;
            e.Handled = true;
            return;
        }

        bool copy = choice == ContentDropAction.Copy;
        ApplyContentAssetDrop(sources, target, copy);
        e.Effects = copy ? WpfDragDropEffects.Copy : WpfDragDropEffects.Move;
        e.Handled = true;
    }

    private void ApplyContentAssetDrop(IReadOnlyList<ContentAssetViewModel> sources, ContentAssetViewModel targetFolder, bool copy)
    {
        string? targetFolderId = ReferenceEquals(targetFolder, _rootContentFolder) ? null : targetFolder.Id;
        var topLevelSources = GetTopLevelContentAssets(sources)
            .Where(source => CanDropContentAsset(source, targetFolder, copy))
            .ToList();

        if (topLevelSources.Count == 0)
            return;

        foreach (var source in topLevelSources)
        {
            if (copy)
                CloneContentAssetDeep(source, targetFolderId);
            else
                MoveContentAsset(source, targetFolderId);
        }

        RefreshContentBrowserTreeKeepingExpansion();
        RefreshContentVisibleItemsForCurrentFolder();
        PersistAssetLibrary();
    }

    private bool CanDropContentAsset(ContentAssetViewModel source, ContentAssetViewModel targetFolder, bool copy)
    {
        if (ReferenceEquals(source, _rootContentFolder) || ReferenceEquals(source, targetFolder))
            return false;

        string? targetFolderId = ReferenceEquals(targetFolder, _rootContentFolder) ? null : targetFolder.Id;
        if (!copy && source.ParentFolderId == targetFolderId)
            return false;

        return !source.IsFolder || !IsDescendantFolder(targetFolderId, source.Id);
    }

    private List<ContentAssetViewModel> GetTopLevelContentAssets(IEnumerable<ContentAssetViewModel> sources)
    {
        var unique = sources
            .Where(source => source is not null)
            .GroupBy(source => source.Id)
            .Select(group => group.First())
            .ToList();
        var selectedIds = unique.Select(source => source.Id).ToHashSet();
        return unique.Where(source => !HasSelectedContentAncestor(source, selectedIds)).ToList();
    }

    private bool HasSelectedContentAncestor(ContentAssetViewModel asset, HashSet<string> selectedIds)
    {
        string? parentId = asset.ParentFolderId;
        while (parentId is not null)
        {
            if (selectedIds.Contains(parentId))
                return true;

            parentId = ContentBrowserItems.FirstOrDefault(item => item.Id == parentId)?.ParentFolderId;
        }

        return false;
    }

    private ContentAssetViewModel CloneContentAssetDeep(ContentAssetViewModel source, string? targetFolderId)
    {
        var clone = CreateContentAsset(source.Kind, CreateUniqueContentName($"{source.Name}_Copy", targetFolderId));
        clone.ParentFolderId = targetFolderId;
        clone.EventGraphs = new(source.EventGraphs.Select(CloneGraphItem));
        clone.Functions = new(source.Functions.Select(CloneGraphItem));
        clone.Macros = new(source.Macros.Select(CloneGraphItem));
        clone.IsDirty = true;
        ContentBrowserItems.Add(clone);

        foreach (var child in ContentBrowserItems.Where(item => item.ParentFolderId == source.Id).ToList())
            CloneContentAssetDeep(child, clone.Id);

        return clone;
    }

    private void CopySelectedContentAssets()
    {
        _contentClipboardAssets = GetSelectedContentAssets()
            .Where(item => !ReferenceEquals(item, _rootContentFolder))
            .ToList();

        if (_contentClipboardAssets.Count > 0)
            SetStatus($"已复制 {_contentClipboardAssets.Count} 个资产。");
    }

    private void PasteContentAssetsToCurrentFolder()
    {
        if (_contentClipboardAssets.Count == 0)
            return;

        foreach (var asset in GetTopLevelContentAssets(_contentClipboardAssets))
            CloneContentAssetDeep(asset, _currentContentFolderId);

        RefreshContentBrowserViews();
        PersistAssetLibrary();
        SetStatus($"已粘贴 {_contentClipboardAssets.Count} 个资产。");
    }

    private bool TryGetDraggedContentAssets(WpfDragEventArgs e, out List<ContentAssetViewModel> assets)
    {
        assets = [];
        if (!e.Data.GetDataPresent(ContentAssetDragDataFormat))
            return false;

        if (e.Data.GetData(ContentAssetDragDataFormat) is List<ContentAssetViewModel> list)
        {
            assets = list;
            return assets.Count > 0;
        }

        return false;
    }

    private ContentAssetViewModel? GetContentDropTargetFolder(WpfDependencyObject? source)
    {
        if (source is null)
            return null;

        var item = FindVisualAncestor<WpfListBoxItem>(source);
        if (item?.DataContext is ContentAssetViewModel { IsFolder: true } folder)
            return folder;

        if (IsVisualInside(ContentBrowserListBox, source))
        {
            return _currentContentFolderId is null
                ? _rootContentFolder
                : ContentBrowserItems.FirstOrDefault(item => item.Id == _currentContentFolderId && item.IsFolder);
        }

        return null;
    }

    private ContentAssetViewModel? GetContentVisibleAssetFromSource(WpfDependencyObject source)
    {
        var item = FindVisualAncestor<WpfListBoxItem>(source);
        return item?.DataContext as ContentAssetViewModel;
    }

    private List<ContentAssetViewModel> GetSelectedContentAssets() =>
        ContentBrowserListBox.SelectedItems.Cast<ContentAssetViewModel>().ToList();

    private static WpfRect NormalizeRect(WpfRect rect)
    {
        double x = Math.Min(rect.Left, rect.Right);
        double y = Math.Min(rect.Top, rect.Bottom);
        double width = Math.Abs(rect.Width);
        double height = Math.Abs(rect.Height);
        return new WpfRect(x, y, width, height);
    }

    private static bool IsVisualInside(WpfDependencyObject root, WpfDependencyObject source)
    {
        var current = source;
        while (current is not null)
        {
            if (ReferenceEquals(current, root))
                return true;

            current = WpfVisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void SetContentAssetDragOpacity(IEnumerable<ContentAssetViewModel> assets, double opacity)
    {
        foreach (var asset in assets)
        {
            if (ContentBrowserListBox.ItemContainerGenerator.ContainerFromItem(asset) is WpfUIElement element)
                element.Opacity = opacity;
        }
    }

    private void StartContentDragPreview(IReadOnlyList<ContentAssetViewModel> assets)
    {
        var title = assets.Count == 1 ? assets[0].Name : $"{assets.Count} 个资产";
        var glyph = assets.Count == 1 ? assets[0].TileGlyph : "MULTI";

        var panel = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, Margin = new WpfThickness(8, 6, 10, 6) };
        panel.Children.Add(new WpfBorder
        {
            Width = 40,
            Height = 32,
            CornerRadius = new System.Windows.CornerRadius(5),
            Background = new WpfSolidColorBrush(WpfColor.FromRgb(79, 163, 255)),
            Child = new WpfTextBlock
            {
                Text = glyph,
                Foreground = new WpfSolidColorBrush(WpfColor.FromRgb(17, 21, 26)),
                FontWeight = WpfFontWeights.Bold,
                FontSize = 10,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = WpfVerticalAlignment.Center,
            },
        });
        panel.Children.Add(new WpfTextBlock
        {
            Text = title,
            Foreground = System.Windows.Media.Brushes.White,
            FontWeight = WpfFontWeights.SemiBold,
            Margin = new WpfThickness(8, 0, 0, 0),
            VerticalAlignment = WpfVerticalAlignment.Center,
            MaxWidth = 180,
            TextTrimming = WpfTextTrimming.CharacterEllipsis,
        });

        _contentDragPreviewPopup = new WpfPopup
        {
            AllowsTransparency = true,
            IsHitTestVisible = false,
            Placement = WpfPlacementMode.Absolute,
            Child = new WpfBorder
            {
                Background = new WpfSolidColorBrush(WpfColor.FromArgb(170, 32, 36, 43)),
                BorderBrush = new WpfSolidColorBrush(WpfColor.FromArgb(210, 79, 163, 255)),
                BorderThickness = new WpfThickness(1),
                CornerRadius = new System.Windows.CornerRadius(8),
                Opacity = 0.72,
                Effect = new WpfDropShadowEffect
                {
                    BlurRadius = 12,
                    ShadowDepth = 2,
                    Opacity = 0.35,
                },
                Child = panel,
            },
        };

        UpdateContentDragPreviewPosition();
        _contentDragPreviewPopup.IsOpen = true;
    }

    private void StopContentDragPreview()
    {
        if (_contentDragPreviewPopup is null)
            return;

        _contentDragPreviewPopup.IsOpen = false;
        _contentDragPreviewPopup = null;
    }

    private void ContentBrowserEnhanced_GiveFeedback(object sender, WpfGiveFeedbackEventArgs e)
    {
        UpdateContentDragPreviewPosition();
        e.UseDefaultCursors = true;
        e.Handled = true;
    }

    private void UpdateContentDragPreviewPosition()
    {
        if (_contentDragPreviewPopup is null)
            return;

        var mouse = WinFormsControl.MousePosition;
        _contentDragPreviewPopup.HorizontalOffset = mouse.X + 14;
        _contentDragPreviewPopup.VerticalOffset = mouse.Y + 14;
    }

    private sealed class ContentSelectionAdorner(WpfUIElement adornedElement) : WpfAdorner(adornedElement)
    {
        private WpfRect _selectionRect = WpfRect.Empty;
        private readonly WpfBrush _fill = new WpfSolidColorBrush(WpfColor.FromArgb(48, 79, 163, 255));
        private readonly WpfPen _stroke = new(new WpfSolidColorBrush(WpfColor.FromArgb(210, 139, 199, 255)), 1.0);

        public void Update(WpfRect selectionRect)
        {
            _selectionRect = selectionRect;
            InvalidateVisual();
        }

        protected override void OnRender(WpfDrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            if (_selectionRect.IsEmpty || _selectionRect.Width <= 1 || _selectionRect.Height <= 1)
                return;

            drawingContext.DrawRectangle(_fill, _stroke, _selectionRect);
        }
    }
}
