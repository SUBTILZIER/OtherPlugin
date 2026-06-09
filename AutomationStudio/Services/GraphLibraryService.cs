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
    MacroLibrary,
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
    private bool _macroSectionExpanded;
    private bool _eventGraphSectionHasState;
    private bool _functionSectionHasState;
    private bool _macroSectionHasState;
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

    public ObservableCollection<GraphListItemViewModel> Macros { get; set; } = [];

    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (!SetProperty(ref _isEditing, value))
                return;

            if (value)
            {
                RenameText = Name;
                RenameError = string.Empty;
            }
            else
            {
                RenameText = Name;
                RenameError = string.Empty;
            }
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
        ContentAssetKind.MacroLibrary => $"Macro Library {Name}",
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
        ContentAssetKind.MacroLibrary => "MAC",
        _ => "AST",
    };

    [System.Text.Json.Serialization.JsonIgnore]
    public string TileBrush => Kind switch
    {
        ContentAssetKind.Folder => "#CDAA55",
        ContentAssetKind.Script => "#4FA3FF",
        ContentAssetKind.FunctionLibrary => "#6B5CFF",
        ContentAssetKind.MacroLibrary => "#D8DCE3",
        _ => "#8A94A6",
    };

    [System.Text.Json.Serialization.JsonIgnore]
    public string TileGlyphForeground => Kind == ContentAssetKind.MacroLibrary
        ? "#161A20"
        : "#11151A";

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

    [System.Text.Json.Serialization.JsonIgnore]
    public bool MacroSectionExpanded
    {
        get => _macroSectionExpanded;
        set => SetProperty(ref _macroSectionExpanded, value);
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool MacroSectionHasState
    {
        get => _macroSectionHasState;
        set => SetProperty(ref _macroSectionHasState, value);
    }
}
