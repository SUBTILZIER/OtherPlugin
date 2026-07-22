using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Services;

namespace AutomationStudio.CoreTests;

internal static class TestGraphFactory
{
    public static ContentAssetViewModel Script(string name = "Script") => new()
    {
        Kind = ContentAssetKind.Script,
        Name = name,
    };

    public static ContentAssetViewModel FunctionLibrary(string name = "Library") => new()
    {
        Kind = ContentAssetKind.FunctionLibrary,
        Name = name,
    };

    public static GraphListItemViewModel Graph(
        string name,
        GraphAssetKind kind,
        GraphEntryRole? role,
        params NodeFileModel[] nodes)
    {
        var model = new GraphFileModel
        {
            Name = name,
            AssetKind = kind,
            EntryRole = role,
            Nodes = [.. nodes],
        };
        return new GraphListItemViewModel
        {
            Kind = kind,
            Name = name,
            EntryRole = role ?? GraphEntryRole.MainEvent,
            Graph = model,
            IsDirty = true,
            IsCompileDirty = true,
        };
    }

    public static NodeFileModel Node(string id, string typeKey, string title = "") => new()
    {
        Id = id,
        NodeTypeKey = typeKey,
        Title = string.IsNullOrWhiteSpace(title) ? typeKey : title,
    };

    public static GraphListItemViewModel ValidFunction(string name, string? id = null)
    {
        var item = Graph(
            name,
            GraphAssetKind.Function,
            null,
            Node("entry", "function_entry", "函数开始"),
            Node("return", "function_return", "函数返回"));
        if (id is null)
            return item;

        return new GraphListItemViewModel
        {
            Id = id,
            Kind = item.Kind,
            Name = item.Name,
            Graph = item.Graph,
            IsDirty = item.IsDirty,
            IsCompileDirty = item.IsCompileDirty,
        };
    }
}
