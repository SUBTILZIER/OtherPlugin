using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using AutomationStudioWpf.Services;
using WinFormsControl = System.Windows.Forms.Control;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private const string ContentAssetDragDataFormat = "AutomationStudioWpf.ContentAssetList";

    private bool _contentBrowserEnhancedInteractionsInstalled;
    private bool _isContentBoxSelecting;
    private bool _isContentDragActive;
    private Point _contentBoxSelectionOrigin;
    private Point _contentAssetDragStartPoint;
    private ContentAssetViewModel? _contentRangeAnchor;
    private ContentAssetViewModel? _contentDragCandidate;
    private ContentSelectionAdorner? _contentSelectionAdorner;
    private AdornerLayer? _contentSelectionAdornerLayer;
    private HashSet<string> _contentBoxSelectionBaseIds = [];
    private List<ContentAssetViewModel> _contentClipboardAssets = [];
    private Popup? _contentDragPreviewPopup;

    private void InstallContentBrowserEnhancedInteractions()
    {
        if (_contentBrowserEnhancedInteractionsInstalled)
            return;

        _contentBrowserEnhancedInteractionsInstalled = true;
        ContentBrowserListBox.SelectionMode = SelectionMode.Extended;
        ContentBrowserListBox.AllowDrop = true;
        ContentFolderListBox.AllowDrop = true;

        ContentBrowserBodyGrid.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(ContentBrowserEnhanced_PreviewMouseLeftButtonDown), true);
        ContentBrowserBodyGrid.AddHandler(UIElement.PreviewMouseMoveEvent, new MouseEventHandler(ContentBrowserEnhanced_PreviewMouseMove), true);
        ContentBrowserBodyGrid.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(ContentBrowserEnhanced_PreviewMouseLeftButtonUp), true);
        ContentBrowserBodyGrid.AddHandler(DragDrop.PreviewDragOverEvent, new DragEventHandler(ContentBrowserEnhanced_PreviewDragOver), true);
        ContentBrowserBodyGrid.AddHandler(DragDrop.PreviewDropEvent, new DragEventHandler(ContentBrowserEnhanced_PreviewDrop), true);
        ContentBrowserListBox.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(ContentBrowserEnhanced_PreviewKeyDown), true);
        ContentBrowserListBox.GiveFeedback += ContentBrowserEnhanced_GiveFeedback;
    }

    private void ContentBrowserEnhanced_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || !IsVisualInside(ContentBrowserListBox, source))
            return;
        if (HasVisualAncestor<TextBox>(source))
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
                SelectContentAssetForClick(asset, Keyboard.Modifiers, forceSingle: true);
                if (asset.Kind == ContentAssetKind.Folder)
                    EnterContentFolder(asset);
                else
                    OpenContentAsset(asset);

                e.Handled = true;
                return;
            }

            SelectContentAssetForClick(asset, Keyboard.Modifiers, forceSingle: false);
            e.Handled = true;
            return;
        }

        _contentDragCandidate = null;
        BeginContentBoxSelection(e.GetPosition(ContentBrowserListBox), Keyboard.Modifiers);
        e.Handled = true;
    }

    private void ContentBrowserEnhanced_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isContentBoxSelecting)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndContentBoxSelection();
                e.Handled = true;
                return;
            }

            UpdateContentBoxSelection(e.GetPosition(ContentBrowserListBox), Keyboard.Modifiers);
            e.Handled = true;
            return;
        }

        if (_contentDragCandidate is null || _isContentDragActive || e.LeftButton != MouseButtonState.Pressed)
            return;

        var current = e.GetPosition(ContentBrowserListBox);
        if (Math.Abs(current.X - _contentAssetDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _contentAssetDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        StartContentAssetDrag();
        e.Handled = true;
    }

    private void ContentBrowserEnhanced_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isContentBoxSelecting)
        {
            EndContentBoxSelection();
            e.Handled = true;
        }

        _contentDragCandidate = null;
    }

    private void ContentBrowserEnhanced_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            return;

        if (e.Key == Key.C)
        {
            CopySelectedContentAssets();
            e.Handled = true;
        }
        else if (e.Key == Key.V)
        {
            PasteContentAssetsToCurrentFolder();
            e.Handled = true;
        }
    }

    private void SelectContentAssetForClick(ContentAssetViewModel asset, ModifierKeys modifiers, bool forceSingle)
    {
        bool ctrl = (modifiers & ModifierKeys.Control) != 0;
        bool shift = (modifiers & ModifierKeys.Shift) != 0;

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

    private void BeginContentBoxSelection(Point origin, ModifierKeys modifiers)
    {
        _isContentBoxSelecting = true;
        _contentBoxSelectionOrigin = origin;
        _contentBoxSelectionBaseIds = (modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0
            ? GetSelectedContentAssets().Select(item => item.Id).ToHashSet()
            : [];

        if (_contentBoxSelectionBaseIds.Count == 0)
            ContentBrowserListBox.SelectedItems.Clear();

        ContentBrowserListBox.CaptureMouse();
        EnsureContentSelectionAdorner();
        _contentSelectionAdorner?.Update(new Rect(origin, origin));
    }

    private void UpdateContentBoxSelection(Point current, ModifierKeys modifiers)
    {
        var selectionRect = NormalizeRect(new Rect(_contentBoxSelectionOrigin, current));
        _contentSelectionAdorner?.Update(selectionRect);

        var selectedByBox = new HashSet<string>();
        foreach (var asset in ContentVisibleItems)
        {
            if (ContentBrowserListBox.ItemContainerGenerator.ContainerFromItem(asset) is not ListBoxItem container)
                continue;

            var itemRect = container.TransformToAncestor(ContentBrowserListBox)
                .TransformBounds(new Rect(new Point(0, 0), container.RenderSize));

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

        _contentSelectionAdornerLayer = AdornerLayer.GetAdornerLayer(ContentBrowserListBox);
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
        var data = new DataObject();
        data.SetData(ContentAssetDragDataFormat, selectedAssets);

        SetContentAssetDragOpacity(selectedAssets, 0.45);
        StartContentDragPreview(selectedAssets);
        try
        {
            DragDrop.DoDragDrop(ContentBrowserListBox, data, DragDropEffects.Move | DragDropEffects.Copy);
        }
        finally
        {
            StopContentDragPreview();
            SetContentAssetDragOpacity(ContentVisibleItems.ToList(), 1.0);
            _contentDragCandidate = null;
            _isContentDragActive = false;
        }
    }

    private void ContentBrowserEnhanced_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!TryGetDraggedContentAssets(e, out var sources))
            return;

        var target = GetContentDropTargetFolder(e.OriginalSource as DependencyObject);
        bool copy = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
        e.Effects = target is not null && sources.Any(source => CanDropContentAsset(source, target, copy))
            ? (copy ? DragDropEffects.Copy : DragDropEffects.Move)
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void ContentBrowserEnhanced_PreviewDrop(object sender, DragEventArgs e)
    {
        if (!TryGetDraggedContentAssets(e, out var sources))
            return;

        var target = GetContentDropTargetFolder(e.OriginalSource as DependencyObject);
        if (target is null)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        bool copy = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
        ApplyContentAssetDrop(sources, target, copy);
        e.Effects = copy ? DragDropEffects.Copy : DragDropEffects.Move;
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

    private bool TryGetDraggedContentAssets(DragEventArgs e, out List<ContentAssetViewModel> assets)
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

    private ContentAssetViewModel? GetContentDropTargetFolder(DependencyObject? source)
    {
        if (source is null)
            return null;

        var item = FindVisualAncestor<ListBoxItem>(source);
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

    private ContentAssetViewModel? GetContentVisibleAssetFromSource(DependencyObject source)
    {
        var item = FindVisualAncestor<ListBoxItem>(source);
        return item?.DataContext as ContentAssetViewModel;
    }

    private List<ContentAssetViewModel> GetSelectedContentAssets() =>
        ContentBrowserListBox.SelectedItems.Cast<ContentAssetViewModel>().ToList();

    private static Rect NormalizeRect(Rect rect)
    {
        double x = Math.Min(rect.Left, rect.Right);
        double y = Math.Min(rect.Top, rect.Bottom);
        double width = Math.Abs(rect.Width);
        double height = Math.Abs(rect.Height);
        return new Rect(x, y, width, height);
    }

    private static bool IsVisualInside(DependencyObject root, DependencyObject source)
    {
        var current = source;
        while (current is not null)
        {
            if (ReferenceEquals(current, root))
                return true;

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void SetContentAssetDragOpacity(IEnumerable<ContentAssetViewModel> assets, double opacity)
    {
        foreach (var asset in assets)
        {
            if (ContentBrowserListBox.ItemContainerGenerator.ContainerFromItem(asset) is UIElement element)
                element.Opacity = opacity;
        }
    }

    private void StartContentDragPreview(IReadOnlyList<ContentAssetViewModel> assets)
    {
        var title = assets.Count == 1 ? assets[0].Name : $"{assets.Count} 个资产";
        var glyph = assets.Count == 1 ? assets[0].TileGlyph : "MULTI";

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6, 10, 6) };
        panel.Children.Add(new Border
        {
            Width = 40,
            Height = 32,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.FromRgb(79, 163, 255)),
            Child = new TextBlock
            {
                Text = glyph,
                Foreground = new SolidColorBrush(Color.FromRgb(17, 21, 26)),
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
        panel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 180,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        _contentDragPreviewPopup = new Popup
        {
            AllowsTransparency = true,
            IsHitTestVisible = false,
            Placement = PlacementMode.Absolute,
            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(170, 32, 36, 43)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(210, 79, 163, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Opacity = 0.72,
                Effect = new DropShadowEffect
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

    private void ContentBrowserEnhanced_GiveFeedback(object sender, GiveFeedbackEventArgs e)
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

    private sealed class ContentSelectionAdorner(UIElement adornedElement) : Adorner(adornedElement)
    {
        private Rect _selectionRect = Rect.Empty;
        private readonly Brush _fill = new SolidColorBrush(Color.FromArgb(48, 79, 163, 255));
        private readonly Pen _stroke = new(new SolidColorBrush(Color.FromArgb(210, 139, 199, 255)), 1.0);

        public void Update(Rect selectionRect)
        {
            _selectionRect = selectionRect;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            if (_selectionRect.IsEmpty || _selectionRect.Width <= 1 || _selectionRect.Height <= 1)
                return;

            drawingContext.DrawRectangle(_fill, _stroke, _selectionRect);
        }
    }
}
