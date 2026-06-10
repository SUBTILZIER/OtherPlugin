using System.Collections.ObjectModel;
using AutomationStudioWpf.Controls;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Services;

namespace AutomationStudioWpf.Interaction;

public enum EditorDockMode
{
    Tab,
    Detached,
}

public sealed class EditorSessionViewModel : ObservableObject
{
    private bool _isActive;
    private bool _isDirty;
    private bool _isCompileDirty;
    private EditorDockMode _dockMode;
    private double _left = 48;
    private double _top = 42;
    private double _width = 920;
    private double _height = 560;

    public EditorSessionViewModel(ContentAssetViewModel contentAsset)
    {
        ContentAsset = contentAsset;
        GraphListItems = new ObservableCollection<GraphListItemViewModel>(contentAsset.EventGraphs);
        FunctionListItems = new ObservableCollection<GraphListItemViewModel>(contentAsset.Functions);
        MacroListItems = new ObservableCollection<GraphListItemViewModel>(contentAsset.Macros);
        RefreshDirtyState();
        EnsureSurfaceContext();
    }

    public string Id { get; } = Guid.NewGuid().ToString("N");

    public ContentAssetViewModel ContentAsset { get; }

    public GraphEditorService EditorService { get; } = new();

    public NodeFactory NodeFactory { get; } = new();

    public GraphCommandService? CommandService { get; set; }

    public EditorSurfaceControl? Surface { get; private set; }

    public EditorSurfaceContext? SurfaceContext { get; private set; }

    public ObservableCollection<GraphListItemViewModel> GraphListItems { get; }

    public ObservableCollection<GraphListItemViewModel> FunctionListItems { get; }

    public ObservableCollection<GraphListItemViewModel> MacroListItems { get; }

    public string? ActiveGraphItemId { get; set; }

    public GraphAssetKind? ActiveGraphKind { get; set; }

    public string Title => ContentAsset.Name;

    public string DisplayTitle => IsDirty ? $"{Title}*" : Title;

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
                OnPropertyChanged(nameof(DisplayTitle));
        }
    }

    public bool IsCompileDirty
    {
        get => _isCompileDirty;
        private set => SetProperty(ref _isCompileDirty, value);
    }

    public EditorDockMode DockMode
    {
        get => _dockMode;
        set => SetProperty(ref _dockMode, value);
    }

    public double Left
    {
        get => _left;
        set => SetProperty(ref _left, value);
    }

    public double Top
    {
        get => _top;
        set => SetProperty(ref _top, value);
    }

    public double Width
    {
        get => _width;
        set => SetProperty(ref _width, value);
    }

    public double Height
    {
        get => _height;
        set => SetProperty(ref _height, value);
    }

    public DetachedEditorWindow? DetachedWindow { get; set; }

    public EditorSurfaceContext EnsureSurfaceContext()
    {
        if (SurfaceContext is not null)
            return SurfaceContext;

        Surface = new EditorSurfaceControl();
        SurfaceContext = new EditorSurfaceContext(this, Surface);
        return SurfaceContext;
    }

    public void SaveToAsset()
    {
        ContentAsset.EventGraphs = new ObservableCollection<GraphListItemViewModel>(GraphListItems);
        ContentAsset.Functions = new ObservableCollection<GraphListItemViewModel>(FunctionListItems);
        ContentAsset.Macros = new ObservableCollection<GraphListItemViewModel>(MacroListItems);
        RefreshDirtyState();
    }

    public void RefreshDirtyState()
    {
        IsDirty = ContentAsset.IsDirty ||
                  GraphListItems.Concat(FunctionListItems).Concat(MacroListItems).Any(item => item.IsDirty);
        IsCompileDirty = GraphListItems.Concat(FunctionListItems).Concat(MacroListItems).Any(item => item.IsCompileDirty);
    }

    public void RememberActive(GraphListController? activeController)
    {
        ActiveGraphItemId = activeController?.ActiveItem?.Id;
        ActiveGraphKind = activeController?.AssetKind;
    }
}
