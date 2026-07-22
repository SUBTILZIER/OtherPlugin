using System.Collections.ObjectModel;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.GraphCore;

namespace AutomationStudioWpf.Services;

/// <summary>
/// Converts persistence DTOs and editor ViewModels without performing file I/O.
/// Compatibility normalization stays at this boundary.
/// </summary>
internal static class GraphLibraryMapper
{
    public static IEnumerable<GraphLibraryItem> ToItems(IEnumerable<GraphListItemViewModel> items) =>
        items.Select(item =>
        {
            var graph = GraphModelCopyMapper.Copy(item.Graph);
            if (item.Kind == GraphAssetKind.EventGraph)
                graph.EntryRole = item.EntryRole;

            return new GraphLibraryItem
            {
                Id = item.Id,
                Name = item.Name,
                Graph = graph,
                EntryRole = item.Kind == GraphAssetKind.EventGraph ? item.EntryRole : null,
                IsPublicToLibrary = item.IsPublicToLibrary,
            };
        });

    public static IEnumerable<GraphLibraryItem> ToEventItems(IEnumerable<GraphListItemViewModel> items)
    {
        var list = items.ToList();
        var roles = ResolveEventGraphRoles(list);
        return list.Select(item =>
        {
            GraphEntryRole role = roles[item];
            var graph = GraphModelCopyMapper.Copy(item.Graph);
            graph.EntryRole = role;
            if (role == GraphEntryRole.AuxiliaryEvent)
                RemoveStartNodes(graph);

            return new GraphLibraryItem
            {
                Id = item.Id,
                Name = item.Name,
                Graph = graph,
                EntryRole = role,
                IsPublicToLibrary = item.IsPublicToLibrary,
            };
        });
    }

    public static ContentAssetModel ToContentAssetModel(ContentAssetViewModel asset) => new()
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
        IsScriptEnabled = asset.Kind == ContentAssetKind.Script ? asset.IsScriptEnabled : null,
    };

    public static ContentAssetViewModel ToContentAssetViewModel(ContentAssetModel asset)
    {
        var viewModel = new ContentAssetViewModel
        {
            Id = string.IsNullOrWhiteSpace(asset.Id) ? Guid.NewGuid().ToString("N") : asset.Id,
            ParentFolderId = asset.ParentFolderId,
            Kind = asset.Kind,
            Name = string.IsNullOrWhiteSpace(asset.Name) ? "Unnamed Asset" : asset.Name,
            EventGraphs = new ObservableCollection<GraphListItemViewModel>(
                ToEventGraphViewModels(asset.EventGraphs, "Unnamed Event Graph")),
            Functions = new ObservableCollection<GraphListItemViewModel>(
                ToViewModels(asset.Functions, GraphAssetKind.Function, "Unnamed Function")),
            RunSettings = asset.RunSettings?.Clone() ?? new ScriptRunSettings(),
            IsScriptEnabled = asset.Kind == ContentAssetKind.Script && (asset.IsScriptEnabled ?? true),
        };

        viewModel.RunSettings.Normalize();
        GraphStructureNormalizer.NormalizeContentAsset(viewModel);
        return viewModel;
    }

    public static ObservableCollection<GraphListItemViewModel> ToViewModels(GraphLibraryState state) =>
        new(ToEventGraphViewModels(state.Graphs, "Unnamed Event Graph"));

    public static ObservableCollection<GraphListItemViewModel> ToFunctionViewModels(GraphLibraryState state) =>
        new(ToViewModels(state.Functions, GraphAssetKind.Function, "Unnamed Function"));

    public static IEnumerable<GraphListItemViewModel> ToEventGraphViewModels(
        IEnumerable<GraphLibraryItem> items,
        string fallbackName)
    {
        var result = new List<GraphListItemViewModel>();
        int index = 0;
        bool mainAssigned = false;

        foreach (var item in items)
        {
            var viewModel = ToViewModel(item, GraphAssetKind.EventGraph, fallbackName);
            GraphEntryRole role = item.EntryRole
                ?? viewModel.Graph.EntryRole
                ?? (index == 0 ? GraphEntryRole.MainEvent : GraphEntryRole.AuxiliaryEvent);

            if (role == GraphEntryRole.MainEvent && mainAssigned)
                role = GraphEntryRole.AuxiliaryEvent;

            viewModel.EntryRole = role;
            viewModel.Graph.EntryRole = role;
            mainAssigned |= role == GraphEntryRole.MainEvent;
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
            GraphEntryRole role = item.EntryRole;
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

    private static Dictionary<GraphListItemViewModel, GraphEntryRole> ResolveEventGraphRoles(
        IReadOnlyList<GraphListItemViewModel> items)
    {
        var roles = new Dictionary<GraphListItemViewModel, GraphEntryRole>();
        bool mainAssigned = false;
        GraphListItemViewModel? first = null;

        for (int index = 0; index < items.Count; index++)
        {
            GraphListItemViewModel item = items[index];
            first ??= item;
            GraphEntryRole role = item.EntryRole;
            if (role == GraphEntryRole.MainEvent && mainAssigned)
                role = GraphEntryRole.AuxiliaryEvent;
            else if (role == GraphEntryRole.MainEvent)
                mainAssigned = true;

            roles[item] = role;
        }

        if (!mainAssigned && first is not null)
            roles[first] = GraphEntryRole.MainEvent;

        return roles;
    }

    private static IEnumerable<GraphListItemViewModel> ToViewModels(
        IEnumerable<GraphLibraryItem> items,
        GraphAssetKind kind,
        string fallbackName) =>
        items.Select(item => ToViewModel(item, kind, fallbackName));

    private static GraphListItemViewModel ToViewModel(
        GraphLibraryItem item,
        GraphAssetKind kind,
        string fallbackName)
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
        graph.Connections.RemoveAll(connection =>
            startNodeIds.Contains(connection.SourceNodeId) ||
            startNodeIds.Contains(connection.TargetNodeId));
    }
}
