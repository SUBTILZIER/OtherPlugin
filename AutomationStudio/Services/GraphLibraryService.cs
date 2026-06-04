using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Services;

public sealed class GraphListItemViewModel : ObservableObject
{
    private string _name = string.Empty;
    private bool _isEditing;
    private bool _isDirty;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public GraphAssetKind Kind { get; init; } = GraphAssetKind.EventGraph;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public GraphFileModel Graph { get; set; } = new();

    public bool IsEditing
    {
        get => _isEditing;
        set => SetProperty(ref _isEditing, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        set => SetProperty(ref _isDirty, value);
    }
}

public enum ContentAssetKind
{
    Folder,
    Script,
    FunctionLibrary,
    MacroLibrary,
}

public sealed class ContentAssetViewModel : ObservableObject
{
    private string _name = string.Empty;
    private bool _isEditing;
    private bool _isDirty;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string? ParentFolderId { get; set; }

    public ContentAssetKind Kind { get; init; } = ContentAssetKind.Script;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

    public ObservableCollection<GraphListItemViewModel> EventGraphs { get; set; } = [];

    public ObservableCollection<GraphListItemViewModel> Functions { get; set; } = [];

    public ObservableCollection<GraphListItemViewModel> Macros { get; set; } = [];

    public bool IsEditing
    {
        get => _isEditing;
        set => SetProperty(ref _isEditing, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        set => SetProperty(ref _isDirty, value);
    }

    public string DisplayName => Kind switch
    {
        ContentAssetKind.Folder => $"[文件夹] {Name}",
        ContentAssetKind.Script => $"[脚本] {Name}",
        ContentAssetKind.FunctionLibrary => $"[函数库] {Name}",
        ContentAssetKind.MacroLibrary => $"[宏库] {Name}",
        _ => Name,
    };
}

public sealed record CallableGraphItem(
    string Id,
    string Name,
    string GroupName,
    GraphFileModel Graph);

public sealed class GraphLibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string LibraryPath { get; }

    public GraphLibraryService()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AutomationStudioWpf");
        Directory.CreateDirectory(dir);
        LibraryPath = Path.Combine(dir, "graph-library.json");
    }

    public GraphLibraryState Load()
    {
        if (!File.Exists(LibraryPath))
        {
            return new GraphLibraryState();
        }

        string json = File.ReadAllText(LibraryPath);
        return JsonSerializer.Deserialize<GraphLibraryState>(json) ?? new GraphLibraryState();
    }

    public void Save(IEnumerable<GraphListItemViewModel> graphs, string? selectedId)
    {
        Save(graphs.Where(item => item.Kind == GraphAssetKind.EventGraph),
            graphs.Where(item => item.Kind == GraphAssetKind.Function),
            graphs.Where(item => item.Kind == GraphAssetKind.Macro),
            selectedId);
    }

    public void Save(
        IEnumerable<GraphListItemViewModel> eventGraphs,
        IEnumerable<GraphListItemViewModel> functions,
        IEnumerable<GraphListItemViewModel> macros,
        string? selectedId)
    {
        var state = new GraphLibraryState
        {
            LastSelectedId = selectedId,
            Graphs = ToItems(eventGraphs).ToList(),
            Functions = ToItems(functions).ToList(),
            Macros = ToItems(macros).ToList(),
        };

        File.WriteAllText(LibraryPath, JsonSerializer.Serialize(state, JsonOptions));
    }

    public void SaveContentLibrary(IEnumerable<ContentAssetViewModel> assets, string? selectedContentId)
    {
        var state = new GraphLibraryState
        {
            LastSelectedContentId = selectedContentId,
            ContentAssets = assets.Select(ToContentAssetModel).ToList(),
        };

        File.WriteAllText(LibraryPath, JsonSerializer.Serialize(state, JsonOptions));
    }

    public ObservableCollection<ContentAssetViewModel> LoadContentLibrary()
    {
        var state = Load();
        if (state.ContentAssets.Count == 0 && (state.Graphs.Count > 0 || state.Functions.Count > 0 || state.Macros.Count > 0))
        {
            state.ContentAssets.Add(new ContentAssetModel
            {
                Id = string.IsNullOrWhiteSpace(state.LastSelectedId) ? Guid.NewGuid().ToString("N") : state.LastSelectedId,
                Name = "默认脚本",
                Kind = ContentAssetKind.Script,
                EventGraphs = state.Graphs,
                Functions = state.Functions,
                Macros = state.Macros,
            });
        }

        return new ObservableCollection<ContentAssetViewModel>(state.ContentAssets.Select(ToContentAssetViewModel));
    }

    public static ObservableCollection<GraphListItemViewModel> ToViewModels(GraphLibraryState state)
    {
        return new ObservableCollection<GraphListItemViewModel>(
            ToViewModels(state.Graphs, GraphAssetKind.EventGraph, "未命名事件图"));
    }

    public static ObservableCollection<GraphListItemViewModel> ToFunctionViewModels(GraphLibraryState state) =>
        new(ToViewModels(state.Functions, GraphAssetKind.Function, "未命名函数"));

    public static ObservableCollection<GraphListItemViewModel> ToMacroViewModels(GraphLibraryState state) =>
        new(ToViewModels(state.Macros, GraphAssetKind.Macro, "未命名宏"));

    private static IEnumerable<GraphLibraryItem> ToItems(IEnumerable<GraphListItemViewModel> items) =>
        items.Select(item => new GraphLibraryItem
        {
            Id = item.Id,
            Name = item.Name,
            Graph = item.Graph,
        });

    private static ContentAssetModel ToContentAssetModel(ContentAssetViewModel asset) => new()
    {
        Id = asset.Id,
        ParentFolderId = asset.ParentFolderId,
        Kind = asset.Kind,
        Name = asset.Name,
        EventGraphs = ToItems(asset.EventGraphs).ToList(),
        Functions = ToItems(asset.Functions).ToList(),
        Macros = ToItems(asset.Macros).ToList(),
    };

    private static ContentAssetViewModel ToContentAssetViewModel(ContentAssetModel asset) => new()
    {
        Id = string.IsNullOrWhiteSpace(asset.Id) ? Guid.NewGuid().ToString("N") : asset.Id,
        ParentFolderId = asset.ParentFolderId,
        Kind = asset.Kind,
        Name = string.IsNullOrWhiteSpace(asset.Name) ? "未命名资产" : asset.Name,
        EventGraphs = new ObservableCollection<GraphListItemViewModel>(ToViewModels(asset.EventGraphs, GraphAssetKind.EventGraph, "未命名事件图")),
        Functions = new ObservableCollection<GraphListItemViewModel>(ToViewModels(asset.Functions, GraphAssetKind.Function, "未命名函数")),
        Macros = new ObservableCollection<GraphListItemViewModel>(ToViewModels(asset.Macros, GraphAssetKind.Macro, "未命名宏")),
    };

    private static IEnumerable<GraphListItemViewModel> ToViewModels(
        IEnumerable<GraphLibraryItem> items,
        GraphAssetKind kind,
        string fallbackName) =>
        items.Select(item => new GraphListItemViewModel
        {
            Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id,
            Kind = kind,
            Name = string.IsNullOrWhiteSpace(item.Name) ? fallbackName : item.Name,
            Graph = item.Graph ?? new GraphFileModel(),
        });
}

public sealed class GraphLibraryState
{
    public string? LastSelectedId { get; set; }

    public string? LastSelectedContentId { get; set; }

    public List<ContentAssetModel> ContentAssets { get; set; } = [];

    public List<GraphLibraryItem> Graphs { get; set; } = [];

    public List<GraphLibraryItem> Functions { get; set; } = [];

    public List<GraphLibraryItem> Macros { get; set; } = [];
}

public sealed class GraphLibraryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "未命名图谱";

    public GraphFileModel Graph { get; set; } = new();
}

public sealed class ContentAssetModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string? ParentFolderId { get; set; }

    public ContentAssetKind Kind { get; set; } = ContentAssetKind.Script;

    public string Name { get; set; } = "未命名资产";

    public List<GraphLibraryItem> EventGraphs { get; set; } = [];

    public List<GraphLibraryItem> Functions { get; set; } = [];

    public List<GraphLibraryItem> Macros { get; set; } = [];
}
