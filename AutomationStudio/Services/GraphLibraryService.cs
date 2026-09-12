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

    public int TriggerWindowMs { get; set; } = 1000;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Key) && PressCount > 0;

    public ScriptHotkeySettings Clone() => new()
    {
        InputKind = InputKind,
        Key = Key,
        PressCount = PressCount,
        TriggerWindowMs = TriggerWindowMs,
    };

    public override string ToString()
    {
        if (!IsConfigured)
            return "未设置";

        return PressCount <= 1 ? Key : $"{Key} x{PressCount}";
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

    public bool RequiresStopHotkey =>
        LoopMode == ScriptLoopMode.UntilStopped &&
        StartHotkey?.IsConfigured == true &&
        StopHotkey?.IsConfigured != true;

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
        StartHotkey.TriggerWindowMs = Math.Clamp(StartHotkey.TriggerWindowMs, 100, 10000);
        StopHotkey.TriggerWindowMs = Math.Clamp(StopHotkey.TriggerWindowMs, 100, 10000);
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
    private bool _isScriptEnabled = true;

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

    public bool IsScriptEnabled
    {
        get => Kind == ContentAssetKind.Script && _isScriptEnabled;
        set
        {
            bool next = Kind == ContentAssetKind.Script && value;
            if (SetProperty(ref _isScriptEnabled, next))
                OnPropertyChanged(nameof(ScriptEnabledToolTip));
        }
    }

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
    public bool ShowScriptEnableToggle => Kind == ContentAssetKind.Script;

    [System.Text.Json.Serialization.JsonIgnore]
    public string ScriptEnabledToolTip => IsScriptEnabled
        ? "是否启用：已启用。启用后可监听全局热键。"
        : "是否启用：已禁用。禁用后不会监听全局热键。";

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
    IReadOnlyList<GraphParameterFileModel> Parameters,
    string GraphId,
    GraphFileModel Graph);

public sealed class GraphLibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly GraphLibraryRepository _repository;

    public string LibraryPath { get; }

    public GraphLibraryService()
    {
        string? dir = Environment.GetEnvironmentVariable("AUTOMATION_STUDIO_LIBRARY_DIR");
        if (string.IsNullOrWhiteSpace(dir))
        {
            dir = ApplicationPaths.UserDataRoot;
        }

        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"Asset library directory is unavailable; using fallback: {ex.Message}");
            dir = ApplicationPaths.UserDataRoot;
            Directory.CreateDirectory(dir);
        }
        LibraryPath = Path.Combine(dir, "graph-library.json");
        _repository = new GraphLibraryRepository(LibraryPath, JsonOptions);
    }

    public GraphLibraryState Load()
    {
        return _repository.Load();
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
            Graphs = GraphLibraryMapper.ToItems(eventGraphs).ToList(),
            Functions = GraphLibraryMapper.ToItems(functions).ToList(),
        };

        _repository.Save(state);
    }

    public void SaveContentLibrary(IEnumerable<ContentAssetViewModel> assets, string? selectedContentId)
    {
        var state = new GraphLibraryState
        {
            LastSelectedContentId = selectedContentId,
            ContentAssets = assets.Select(GraphLibraryMapper.ToContentAssetModel).ToList(),
        };

        _repository.Save(state);
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
            .Select(GraphLibraryMapper.ToContentAssetViewModel));
    }

    public static ObservableCollection<GraphListItemViewModel> ToViewModels(GraphLibraryState state) =>
        GraphLibraryMapper.ToViewModels(state);

    public static ObservableCollection<GraphListItemViewModel> ToFunctionViewModels(GraphLibraryState state) =>
        GraphLibraryMapper.ToFunctionViewModels(state);

    public static void NormalizeEventGraphRoles(IEnumerable<GraphListItemViewModel> items) =>
        GraphLibraryMapper.NormalizeEventGraphRoles(items);
}
