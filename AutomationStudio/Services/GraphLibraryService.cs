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

    public GraphEntryRole EntryRole
    {
        get => Graph.EntryRole ?? GraphEntryRole.MainEvent;
        set
        {
            if (Kind != GraphAssetKind.EventGraph)
            {
                if (Graph.EntryRole is null)
                    return;

                Graph.EntryRole = null;
                OnPropertyChanged();
                return;
            }

            if (Graph.EntryRole == value)
                return;

            Graph.EntryRole = value;
            OnPropertyChanged();
        }
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? string.Empty))
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

public enum ScriptLoopMode
{
    Count,
    UntilStopped,
    Duration,
}

public enum ScriptHotkeyInputKind
{
    Keyboard,
    Mouse,
}

public sealed class ScriptHotkeySettings
{
    public ScriptHotkeyInputKind InputKind { get; set; } = ScriptHotkeyInputKind.Keyboard;

    public string Key { get; set; } = string.Empty;

    public int PressCount { get; set; } = 1;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Key) && PressCount > 0;

    public ScriptHotkeySettings Clone() => new()
    {
        InputKind = InputKind,
        Key = Key,
        PressCount = PressCount,
    };

    public override string ToString()
    {
        if (!IsConfigured)
            return "未设置";

        string prefix = InputKind == ScriptHotkeyInputKind.Mouse ? "鼠标" : "键盘";
        return PressCount <= 1 ? $"{prefix} {Key}" : $"{prefix} {Key} x{PressCount}";
    }
}

public sealed class ScriptRunSettings
{
    public ScriptLoopMode LoopMode { get; set; } = ScriptLoopMode.Count;

    public int LoopCount { get; set; } = 1;

    public int DurationHours { get; set; }

    public int DurationMinutes { get; set; }

    public int DurationSeconds { get; set; }

    public bool PreventDuplicateRun { get; set; } = true;

    public ScriptHotkeySettings StartHotkey { get; set; } = new();

    public ScriptHotkeySettings StopHotkey { get; set; } = new();

    public ScriptRunSettings Clone() => new()
    {
        LoopMode = LoopMode,
        LoopCount = LoopCount,
        DurationHours = DurationHours,
        DurationMinutes = DurationMinutes,
        DurationSeconds = DurationSeconds,
        PreventDuplicateRun = PreventDuplicateRun,
        StartHotkey = StartHotkey?.Clone() ?? new ScriptHotkeySettings(),
        StopHotkey = StopHotkey?.Clone() ?? new ScriptHotkeySettings(),
    };

    public void Normalize()
    {
        LoopCount = Math.Max(1, LoopCount);
        DurationHours = Math.Clamp(DurationHours, 0, 999);
        DurationMinutes = Math.Clamp(DurationMinutes, 0, 59);
        DurationSeconds = Math.Clamp(DurationSeconds, 0, 59);
        StartHotkey ??= new ScriptHotkeySettings();
        StopHotkey ??= new ScriptHotkeySettings();
        StartHotkey.PressCount = Math.Max(1, StartHotkey.PressCount);
        StopHotkey.PressCount = Math.Max(1, StopHotkey.PressCount);
    }
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
            if (string.IsNullOrWhiteSpace(value))
                return;

            if (SetProperty(ref _name, value))
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

    public ScriptRunSettings RunSettings { get; set; } = new();

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
        get => _renameText;
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
            ToEventGraphViewModels(state.Graphs, "Unnamed Event Graph"));
    }

    public static ObservableCollection<GraphListItemViewModel> ToFunctionViewModels(GraphLibraryState state) =>
        new(ToViewModels(state.Functions, GraphAssetKind.Function, "Unnamed Function"));

    private static IEnumerable<GraphLibraryItem> ToItems(IEnumerable<GraphListItemViewModel> items) =>
        items.Select(item =>
        {
            if (item.Kind == GraphAssetKind.EventGraph)
                item.Graph.EntryRole = item.EntryRole;
            return new GraphLibraryItem
            {
                Id = item.Id,
                Name = item.Name,
                Graph = item.Graph,
                EntryRole = item.Kind == GraphAssetKind.EventGraph ? item.EntryRole : null,
                IsPublicToLibrary = item.IsPublicToLibrary,
            };
        });

    private static IEnumerable<GraphLibraryItem> ToEventItems(IEnumerable<GraphListItemViewModel> items)
    {
        var list = items.ToList();
        NormalizeEventGraphRoles(list);
        foreach (var item in list.Where(item => item.EntryRole == GraphEntryRole.AuxiliaryEvent))
            RemoveStartNodes(item.Graph);
        return ToItems(list);
    }

    private static ContentAssetModel ToContentAssetModel(ContentAssetViewModel asset) => new()
    {
        Id = asset.Id,
        ParentFolderId = asset.ParentFolderId,
        Kind = asset.Kind,
        Name = asset.Name,
        EventGraphs = ToEventItems(asset.EventGraphs).ToList(),
        Functions = ToItems(asset.Functions).ToList(),
        RunSettings = asset.Kind == ContentAssetKind.Script
            ? asset.RunSettings?.Clone() ?? new ScriptRunSettings()
            : null,
    };

    private static ContentAssetViewModel ToContentAssetViewModel(ContentAssetModel asset)
    {
        var viewModel = new ContentAssetViewModel
        {
            Id = string.IsNullOrWhiteSpace(asset.Id) ? Guid.NewGuid().ToString("N") : asset.Id,
            ParentFolderId = asset.ParentFolderId,
            Kind = asset.Kind,
            Name = string.IsNullOrWhiteSpace(asset.Name) ? "Unnamed Asset" : asset.Name,
            EventGraphs = new ObservableCollection<GraphListItemViewModel>(ToEventGraphViewModels(asset.EventGraphs, "Unnamed Event Graph")),
            Functions = new ObservableCollection<GraphListItemViewModel>(ToViewModels(asset.Functions, GraphAssetKind.Function, "Unnamed Function")),
            RunSettings = asset.RunSettings?.Clone() ?? new ScriptRunSettings(),
        };
        viewModel.RunSettings.Normalize();
        return viewModel;
    }

    private static IEnumerable<GraphListItemViewModel> ToEventGraphViewModels(
        IEnumerable<GraphLibraryItem> items,
        string fallbackName)
    {
        var result = new List<GraphListItemViewModel>();
        int index = 0;
        bool mainAssigned = false;
        foreach (var item in items)
        {
            var viewModel = ToViewModel(item, GraphAssetKind.EventGraph, fallbackName);
            GraphEntryRole role;
            if (item.EntryRole.HasValue)
            {
                role = item.EntryRole.Value;
            }
            else if (viewModel.Graph.EntryRole.HasValue)
            {
                role = viewModel.Graph.EntryRole.Value;
            }
            else
            {
                role = index == 0 ? GraphEntryRole.MainEvent : GraphEntryRole.AuxiliaryEvent;
            }

            if (role == GraphEntryRole.MainEvent && mainAssigned)
                role = GraphEntryRole.AuxiliaryEvent;

            viewModel.EntryRole = role;
            viewModel.Graph.EntryRole = role;
            if (role == GraphEntryRole.MainEvent)
                mainAssigned = true;
            index++;
            result.Add(viewModel);
        }

        if (!mainAssigned && result.Count > 0)
        {
            result[0].EntryRole = GraphEntryRole.MainEvent;
            result[0].Graph.EntryRole = GraphEntryRole.MainEvent;
        }

        return result;
    }

    public static void NormalizeEventGraphRoles(IEnumerable<GraphListItemViewModel> items)
    {
        bool mainAssigned = false;
        GraphListItemViewModel? first = null;
        foreach (var item in items.Where(item => item.Kind == GraphAssetKind.EventGraph))
        {
            first ??= item;
            var role = item.EntryRole;
            if (role == GraphEntryRole.MainEvent)
            {
                if (mainAssigned)
                    role = GraphEntryRole.AuxiliaryEvent;
                else
                    mainAssigned = true;
            }

            item.EntryRole = role;
            item.Graph.EntryRole = role;
        }

        if (!mainAssigned && first is not null)
        {
            first.EntryRole = GraphEntryRole.MainEvent;
            first.Graph.EntryRole = GraphEntryRole.MainEvent;
        }
    }

    private static IEnumerable<GraphListItemViewModel> ToViewModels(
        IEnumerable<GraphLibraryItem> items,
        GraphAssetKind kind,
        string fallbackName) =>
        items.Select(item => ToViewModel(item, kind, fallbackName));

    private static GraphListItemViewModel ToViewModel(GraphLibraryItem item, GraphAssetKind kind, string fallbackName)
    {
        var graph = item.Graph ?? new GraphFileModel();
        graph.AssetKind = kind;
        if (kind == GraphAssetKind.Function)
            graph.EntryRole = null;
        else if (item.EntryRole.HasValue)
            graph.EntryRole = item.EntryRole.Value;
        return new GraphListItemViewModel
        {
            Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id,
            Kind = kind,
            Name = string.IsNullOrWhiteSpace(item.Name) ? fallbackName : item.Name,
            Graph = graph,
            IsPublicToLibrary = item.IsPublicToLibrary,
        };
    }

    private static void RemoveStartNodes(GraphFileModel graph)
    {
        var startNodeIds = graph.Nodes
            .Where(node => node.NodeTypeKey == "start")
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (startNodeIds.Count == 0)
            return;

        graph.Nodes.RemoveAll(node => startNodeIds.Contains(node.Id));
        graph.Connections.RemoveAll(conn =>
            startNodeIds.Contains(conn.SourceNodeId) ||
            startNodeIds.Contains(conn.TargetNodeId));
    }
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

    public GraphEntryRole? EntryRole { get; set; }

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

    public ScriptRunSettings? RunSettings { get; set; }
}
