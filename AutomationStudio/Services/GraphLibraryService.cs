using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Services;

public sealed class GraphListItemViewModel : ObservableObject
{
    private string _name = string.Empty;
    private bool _isEditing;
    private bool _isDirty;
    private bool _isCompileDirty;
    private bool _isPublicToLibrary;
    private bool _showLibraryPublishOption;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public GraphAssetKind Kind { get; init; } = GraphAssetKind.EventGraph;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, string.IsNullOrWhiteSpace(value) ? "Unnamed" : value))
                OnPropertyChanged(nameof(DisplayName));
        }
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

    public bool IsCompileDirty
    {
        get => _isCompileDirty;
        set
        {
            if (SetProperty(ref _isCompileDirty, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

    public bool IsPublicToLibrary
    {
        get => _isPublicToLibrary;
        set => SetProperty(ref _isPublicToLibrary, value);
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool ShowLibraryPublishOption
    {
        get => _showLibraryPublishOption;
        set => SetProperty(ref _showLibraryPublishOption, value);
    }

    public string DisplayName => IsCompileDirty ? $"{Name} *" : Name;
}

public enum ContentAssetKind
{
    Folder,
    Script,
    FunctionLibrary,
}

public sealed class ContentAssetViewModel : ObservableObject
{
    private string _name = string.Empty;
    private string? _parentFolderId;
    private bool _isEditing;
    private bool _isDirty;
    private string _renameText = string.Empty;
    private string _renameError = string.Empty;
    private bool _eventGraphSectionExpanded;
    private bool _functionSectionExpanded;
    private bool _eventGraphSectionHasState;
    private bool _functionSectionHasState;
    private int _viewDepth;
    private bool _hasFolderChildren;
    private bool _isTreeExpanded;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string? ParentFolderId
    {
        get => _parentFolderId;
        set => SetProperty(ref _parentFolderId, value);
    }

    public ContentAssetKind Kind { get; init; } = ContentAssetKind.Script;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, string.IsNullOrWhiteSpace(value) ? "Unnamed Asset" : value))
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(TreeDisplayName));
                if (!IsEditing)
                    RenameText = _name;
            }
        }
    }

    public ObservableCollection<GraphListItemViewModel> EventGraphs { get; set; } = [];

    public ObservableCollection<GraphListItemViewModel> Functions { get; set; } = [];

    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (!SetProperty(ref _isEditing, value))
                return;

            RenameText = Name;
            RenameError = string.Empty;
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        set => SetProperty(ref _isDirty, value);
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string RenameText
    {
        get => string.IsNullOrEmpty(_renameText) ? Name : _renameText;
        set => SetProperty(ref _renameText, value ?? string.Empty);
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string RenameError
    {
        get => _renameError;
        set
        {
            if (SetProperty(ref _renameError, value ?? string.Empty))
                OnPropertyChanged(nameof(HasRenameError));
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasRenameError => !string.IsNullOrWhiteSpace(RenameError);

    public string DisplayName => Kind switch
    {
        ContentAssetKind.Folder => $"Folder {Name}",
        ContentAssetKind.Script => $"Script {Name}",
        ContentAssetKind.FunctionLibrary => $"Function Library {Name}",
        _ => Name,
    };

    [System.Text.Json.Serialization.JsonIgnore]
    public int ViewDepth
    {
        get => _viewDepth;
        set
        {
            if (SetProperty(ref _viewDepth, value))
            {
                OnPropertyChanged(nameof(TreeDisplayName));
                OnPropertyChanged(nameof(TreeIndent));
            }
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasFolderChildren
    {
        get => _hasFolderChildren;
        set
        {
            if (SetProperty(ref _hasFolderChildren, value))
                OnPropertyChanged(nameof(TreeGlyph));
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsTreeExpanded
    {
        get => _isTreeExpanded;
        set
        {
            if (SetProperty(ref _isTreeExpanded, value))
                OnPropertyChanged(nameof(TreeGlyph));
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string TreeGlyph => IsFolder && HasFolderChildren ? (IsTreeExpanded ? "v" : ">") : " ";

    [System.Text.Json.Serialization.JsonIgnore]
    public string TreeDisplayName => Name;

    [System.Text.Json.Serialization.JsonIgnore]
    public Thickness TreeIndent => new(Math.Max(0, ViewDepth) * 16, 0, 0, 0);

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsFolder => Kind == ContentAssetKind.Folder;

    [System.Text.Json.Serialization.JsonIgnore]
    public string TileGlyph => Kind switch
    {
        ContentAssetKind.Folder => "DIR",
        ContentAssetKind.Script => "SCR",
        ContentAssetKind.FunctionLibrary => "FN",
        _ => "AST",
    };

    [System.Text.Json.Serialization.JsonIgnore]
    public string TileBrush => Kind switch
    {
        ContentAssetKind.Folder => "#CDAA55",
        ContentAssetKind.Script => "#4FA3FF",
        ContentAssetKind.FunctionLibrary => "#6B5CFF",
        _ => "#8A94A6",
    };

    [System.Text.Json.Serialization.JsonIgnore]
    public string TileGlyphForeground => "#11151A";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool EventGraphSectionExpanded
    {
        get => _eventGraphSectionExpanded;
        set => SetProperty(ref _eventGraphSectionExpanded, value);
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool EventGraphSectionHasState
    {
        get => _eventGraphSectionHasState;
        set => SetProperty(ref _eventGraphSectionHasState, value);
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool FunctionSectionExpanded
    {
        get => _functionSectionExpanded;
        set => SetProperty(ref _functionSectionExpanded, value);
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool FunctionSectionHasState
    {
        get => _functionSectionHasState;
        set => SetProperty(ref _functionSectionHasState, value);
    }

}

public sealed record CallableGraphItem(
    string Id,
    string Name,
    string GroupName,
    GraphFileModel Graph);

public sealed record CallableCustomEventItem(
    string Id,
    string Name,
    string GroupName,
    IReadOnlyList<GraphParameterFileModel> Parameters);

public sealed class GraphLibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string LibraryPath { get; }

    public GraphLibraryService()
    {
        string? dir = Environment.GetEnvironmentVariable("AUTOMATION_STUDIO_LIBRARY_DIR");
        if (string.IsNullOrWhiteSpace(dir))
        {
            dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AutomationStudioWpf");
        }

        Directory.CreateDirectory(dir);
        LibraryPath = Path.Combine(dir, "graph-library.json");
    }

    public GraphLibraryState Load()
    {
        if (!File.Exists(LibraryPath))
            return new GraphLibraryState();

        string json = File.ReadAllText(LibraryPath);
        return JsonSerializer.Deserialize<GraphLibraryState>(json) ?? new GraphLibraryState();
    }

    public void Save(IEnumerable<GraphListItemViewModel> graphs, string? selectedId)
    {
        Save(
            graphs.Where(item => item.Kind == GraphAssetKind.EventGraph),
            graphs.Where(item => item.Kind == GraphAssetKind.Function),
            selectedId);
    }

    public void Save(
        IEnumerable<GraphListItemViewModel> eventGraphs,
        IEnumerable<GraphListItemViewModel> functions,
        string? selectedId)
    {
        var state = new GraphLibraryState
        {
            LastSelectedId = selectedId,
            Graphs = ToItems(eventGraphs).ToList(),
            Functions = ToItems(functions).ToList(),
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
        if (state.ContentAssets.Count == 0 && (state.Graphs.Count > 0 || state.Functions.Count > 0))
        {
            state.ContentAssets.Add(new ContentAssetModel
            {
                Id = string.IsNullOrWhiteSpace(state.LastSelectedId) ? Guid.NewGuid().ToString("N") : state.LastSelectedId,
                Name = "Default Script",
                Kind = ContentAssetKind.Script,
                EventGraphs = state.Graphs,
                Functions = state.Functions,
            });
        }

        return new ObservableCollection<ContentAssetViewModel>(state.ContentAssets
            .Where(asset => (int)asset.Kind != 3)
            .Select(ToContentAssetViewModel));
    }

    public static ObservableCollection<GraphListItemViewModel> ToViewModels(GraphLibraryState state)
    {
        return new ObservableCollection<GraphListItemViewModel>(
            ToViewModels(state.Graphs, GraphAssetKind.EventGraph, "Unnamed Event Graph"));
    }

    public static ObservableCollection<GraphListItemViewModel> ToFunctionViewModels(GraphLibraryState state) =>
        new(ToViewModels(state.Functions, GraphAssetKind.Function, "Unnamed Function"));

    private static IEnumerable<GraphLibraryItem> ToItems(IEnumerable<GraphListItemViewModel> items) =>
        items.Select(item => new GraphLibraryItem
        {
            Id = item.Id,
            Name = item.Name,
            Graph = item.Graph,
            IsPublicToLibrary = item.IsPublicToLibrary,
        });

    private static ContentAssetModel ToContentAssetModel(ContentAssetViewModel asset) => new()
    {
        Id = asset.Id,
        ParentFolderId = asset.ParentFolderId,
        Kind = asset.Kind,
        Name = asset.Name,
        EventGraphs = ToItems(asset.EventGraphs).ToList(),
        Functions = ToItems(asset.Functions).ToList(),
    };

    private static ContentAssetViewModel ToContentAssetViewModel(ContentAssetModel asset) => new()
    {
        Id = string.IsNullOrWhiteSpace(asset.Id) ? Guid.NewGuid().ToString("N") : asset.Id,
        ParentFolderId = asset.ParentFolderId,
        Kind = asset.Kind,
        Name = string.IsNullOrWhiteSpace(asset.Name) ? "Unnamed Asset" : asset.Name,
        EventGraphs = new ObservableCollection<GraphListItemViewModel>(ToViewModels(asset.EventGraphs, GraphAssetKind.EventGraph, "Unnamed Event Graph")),
        Functions = new ObservableCollection<GraphListItemViewModel>(ToViewModels(asset.Functions, GraphAssetKind.Function, "Unnamed Function")),
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
            IsPublicToLibrary = item.IsPublicToLibrary,
        });
}

public sealed class GraphLibraryState
{
    public string? LastSelectedId { get; set; }

    public string? LastSelectedContentId { get; set; }

    public List<ContentAssetModel> ContentAssets { get; set; } = [];

    public List<GraphLibraryItem> Graphs { get; set; } = [];

    public List<GraphLibraryItem> Functions { get; set; } = [];

}

public sealed class GraphLibraryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Unnamed Graph";

    public GraphFileModel Graph { get; set; } = new();

    public bool IsPublicToLibrary { get; set; }
}

public sealed class ContentAssetModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string? ParentFolderId { get; set; }

    public ContentAssetKind Kind { get; set; } = ContentAssetKind.Script;

    public string Name { get; set; } = "Unnamed Asset";

    public List<GraphLibraryItem> EventGraphs { get; set; } = [];

    public List<GraphLibraryItem> Functions { get; set; } = [];

}
