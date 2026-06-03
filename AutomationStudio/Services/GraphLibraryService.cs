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
