using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Services;

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

    public bool? IsScriptEnabled { get; set; }
}
