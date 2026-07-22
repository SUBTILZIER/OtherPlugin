using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private static void ApplyEntryNodeTitle(IEnumerable<NodeBaseViewModel> nodes, GraphAssetKind kind, string graphName)
    {
        string title = $"{graphName}开始";
        foreach (var node in nodes)
        {
            if (kind == GraphAssetKind.Function && node is FunctionEntryNodeViewModel)
            {
                node.Title = title;
            }
        }
    }

    private static void ApplyEntryNodeTitle(GraphFileModel graph, GraphAssetKind kind, string graphName)
    {
        string? entryKey = kind switch
        {
            GraphAssetKind.Function => "function_entry",
            _ => null,
        };
        if (entryKey is null)
            return;

        foreach (var node in graph.Nodes.Where(node => node.NodeTypeKey == entryKey))
            node.Title = $"{graphName}开始";
    }

}
