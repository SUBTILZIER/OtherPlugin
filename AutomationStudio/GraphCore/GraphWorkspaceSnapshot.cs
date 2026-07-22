using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Services;

namespace AutomationStudioWpf.GraphCore;

public enum GraphDependencyIssueSeverity
{
    Warning,
    Error,
}

public sealed record GraphDependencyIssue(
    GraphDependencyIssueSeverity Severity,
    string Scope,
    string Message);

public sealed record ScriptHotkeySnapshot(
    ScriptHotkeyInputKind InputKind,
    string Key,
    int PressCount,
    int TriggerWindowMs);

public sealed record ScriptRunSettingsSnapshot(
    ScriptLoopMode LoopMode,
    int LoopCount,
    int DurationHours,
    int DurationMinutes,
    int DurationSeconds,
    bool PreventDuplicateRun,
    ScriptHotkeySnapshot StartHotkey,
    ScriptHotkeySnapshot StopHotkey);

public sealed class GraphSnapshot
{
    private readonly GraphFileModel _model;

    internal GraphSnapshot(
        string id,
        string name,
        GraphAssetKind kind,
        GraphEntryRole? entryRole,
        bool isPublicToLibrary,
        GraphFileModel model)
    {
        Id = id;
        Name = name;
        Kind = kind;
        EntryRole = entryRole;
        IsPublicToLibrary = isPublicToLibrary;
        _model = GraphModelCopyMapper.Copy(model);
    }

    public string Id { get; }

    public string Name { get; }

    public GraphAssetKind Kind { get; }

    public GraphEntryRole? EntryRole { get; }

    public bool IsPublicToLibrary { get; }

    public GraphFileModel ToMutableModel() => GraphModelCopyMapper.Copy(_model);
}

public sealed class ContentAssetSnapshot
{
    internal ContentAssetSnapshot(
        string id,
        string name,
        string? parentFolderId,
        ContentAssetKind kind,
        bool isScriptEnabled,
        ScriptRunSettingsSnapshot runSettings,
        IReadOnlyList<GraphSnapshot> eventGraphs,
        IReadOnlyList<GraphSnapshot> functions)
    {
        Id = id;
        Name = name;
        ParentFolderId = parentFolderId;
        Kind = kind;
        IsScriptEnabled = isScriptEnabled;
        RunSettings = runSettings;
        EventGraphs = eventGraphs;
        Functions = functions;
    }

    public string Id { get; }

    public string Name { get; }

    public string? ParentFolderId { get; }

    public ContentAssetKind Kind { get; }

    public bool IsScriptEnabled { get; }

    public ScriptRunSettingsSnapshot RunSettings { get; }

    public IReadOnlyList<GraphSnapshot> EventGraphs { get; }

    public IReadOnlyList<GraphSnapshot> Functions { get; }

    public IEnumerable<GraphSnapshot> Graphs => EventGraphs.Concat(Functions);
}

public sealed class GraphWorkspaceSnapshot
{
    internal GraphWorkspaceSnapshot(
        IReadOnlyList<ContentAssetSnapshot> assets,
        IReadOnlyList<GraphDependencyIssue> issues)
    {
        Assets = assets;
        Issues = issues;
    }

    public IReadOnlyList<ContentAssetSnapshot> Assets { get; }

    public IReadOnlyList<GraphDependencyIssue> Issues { get; }

    public bool HasErrors => Issues.Any(issue => issue.Severity == GraphDependencyIssueSeverity.Error);
}

internal static class GraphWorkspaceSnapshotFactory
{
    public static GraphWorkspaceSnapshot Create(IEnumerable<ContentAssetViewModel> sourceAssets)
    {
        ArgumentNullException.ThrowIfNull(sourceAssets);
        var source = sourceAssets.ToList();
        var issues = new List<GraphDependencyIssue>();
        ValidateIds(source, issues);

        var assets = source.Select(asset => new ContentAssetSnapshot(
                asset.Id,
                asset.Name,
                asset.ParentFolderId,
                asset.Kind,
                asset.IsScriptEnabled,
                CopyRunSettings(asset.RunSettings),
                asset.EventGraphs.Select(CopyGraph).ToList().AsReadOnly(),
                asset.Functions.Select(CopyGraph).ToList().AsReadOnly()))
            .ToList()
            .AsReadOnly();

        return new GraphWorkspaceSnapshot(assets, issues.AsReadOnly());
    }

    private static GraphSnapshot CopyGraph(GraphListItemViewModel item) => new(
        item.Id,
        item.Name,
        item.Kind,
        item.Kind == GraphAssetKind.EventGraph ? item.EntryRole : null,
        item.IsPublicToLibrary,
        item.Graph);

    private static ScriptRunSettingsSnapshot CopyRunSettings(ScriptRunSettings? source)
    {
        source ??= new ScriptRunSettings();
        return new ScriptRunSettingsSnapshot(
            source.LoopMode,
            source.LoopCount,
            source.DurationHours,
            source.DurationMinutes,
            source.DurationSeconds,
            source.PreventDuplicateRun,
            CopyHotkey(source.StartHotkey),
            CopyHotkey(source.StopHotkey));
    }

    private static ScriptHotkeySnapshot CopyHotkey(ScriptHotkeySettings? source)
    {
        source ??= new ScriptHotkeySettings();
        return new ScriptHotkeySnapshot(
            source.InputKind,
            source.Key,
            source.PressCount,
            source.TriggerWindowMs);
    }

    private static void ValidateIds(
        IReadOnlyList<ContentAssetViewModel> assets,
        ICollection<GraphDependencyIssue> issues)
    {
        AddDuplicateIssues(
            assets.Select(asset => (asset.Id, Scope: "workspace")),
            "资产",
            issues);

        AddDuplicateIssues(
            assets.SelectMany(asset => asset.EventGraphs.Concat(asset.Functions)
                .Select(graph => (graph.Id, Scope: $"asset:{asset.Id}"))),
            "图表",
            issues);

        foreach (var asset in assets)
        {
            foreach (var graph in asset.EventGraphs.Concat(asset.Functions))
            {
                string scope = $"graph:{asset.Id}/{graph.Id}";
                AddDuplicateIssues(
                    graph.Graph.Nodes.Select(node => (node.Id, Scope: scope)),
                    "节点",
                    issues);

                foreach (var node in graph.Graph.Nodes)
                {
                    AddDuplicateIssues(
                        node.Parameters.Concat(node.InputParameters).Concat(node.OutputParameters)
                            .Select(parameter => (parameter.Id, Scope: $"{scope}/node:{node.Id}")),
                        "参数",
                        issues);
                }
            }
        }
    }

    private static void AddDuplicateIssues(
        IEnumerable<(string Id, string Scope)> values,
        string label,
        ICollection<GraphDependencyIssue> issues)
    {
        foreach (var group in values.GroupBy(value => value.Id ?? string.Empty, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(group.Key))
            {
                foreach (var item in group)
                {
                    issues.Add(new GraphDependencyIssue(
                        GraphDependencyIssueSeverity.Error,
                        item.Scope,
                        $"存在空{label} ID。"));
                }
            }
            else if (group.Count() > 1)
            {
                issues.Add(new GraphDependencyIssue(
                    GraphDependencyIssueSeverity.Error,
                    group.First().Scope,
                    $"{label} ID 重复：{group.Key}。"));
            }
        }
    }
}
