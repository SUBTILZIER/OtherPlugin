using System.Windows;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Runtime;
using AutomationStudioWpf.Services;
using WpfMessageBox = System.Windows.MessageBox;

namespace AutomationStudioWpf;

public partial class MainWindow
{
    private readonly FinalCodePreviewGenerator _finalCodePreviewGenerator = new();

    private void ShowFinalCodePreview()
    {
        CommitInspectorAndSnapshotAllSessions();

        var session = GetOperationEditorSession();
        var controller = session is null ? null : GetSessionActiveAssetController(session);
        if (session is null || controller is null || controller.ActiveItem is null)
        {
            SetStatus("没有可预览的当前图表。");
            WpfMessageBox.Show(this, "没有可预览的当前图表。", "显示最终代码", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var snapshot = controller.ActiveItem.Graph;
        if (snapshot is null)
        {
            SetStatus("当前图表没有快照。");
            return;
        }

        var plan = BuildPreviewPlan(snapshot);
        var callableFunctions = _callableGraphResolver.ResolveFunctions(ContentBrowserItems, session.ContentAsset);
        var functionPlans = callableFunctions
            .GroupBy(function => function.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => BuildPreviewPlan(group.First().Graph), StringComparer.Ordinal);
        var functionNames = callableFunctions
            .GroupBy(function => function.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.Ordinal);
        var result = _finalCodePreviewGenerator.Generate(plan, session.ContentAsset, controller.AssetKind, functionPlans, functionNames);

        if (_finalCodePreviewWindow is null || _finalCodePreviewWindow.IsClosed)
        {
            _finalCodePreviewWindow = new FinalCodePreviewWindow(this);
            _finalCodePreviewWindow.Closed += (_, _) => _finalCodePreviewWindow = null;
        }

        _finalCodePreviewWindow.SetPreview(result.Text, result.ErrorMessage);
        _finalCodePreviewWindow.ActivateWindow();
    }

    private static GraphExecutionPlan BuildPreviewPlan(GraphFileModel graph)
    {
        var viewModels = graph.Nodes
            .Select(NodeSerializer.FromFileModel)
            .Where(node => node is not null)
            .Cast<NodeBaseViewModel>()
            .ToDictionary(node => node.Id, StringComparer.Ordinal);

        var nodes = viewModels.Values
            .Select(NodeSerializer.ToRuntimeNode)
            .ToList();

        var connections = graph.Connections
            .Select(conn =>
            {
                if (!viewModels.TryGetValue(conn.SourceNodeId, out var sourceNode) ||
                    !viewModels.TryGetValue(conn.TargetNodeId, out var targetNode))
                {
                    return null;
                }

                var sourcePin = sourceNode.OutputPins.FirstOrDefault(pin => pin.Name == conn.SourcePinName);
                var targetPin = targetNode.InputPins.FirstOrDefault(pin => pin.Name == conn.TargetPinName);
                if (sourcePin is null || targetPin is null)
                    return null;

                return new GraphRuntimeConnection(
                    conn.SourceNodeId,
                    conn.SourcePinName,
                    sourcePin.Kind,
                    conn.TargetNodeId,
                    conn.TargetPinName,
                    targetPin.Kind);
            })
            .Where(connection => connection is not null)
            .Cast<GraphRuntimeConnection>()
            .ToList();

        return new GraphExecutionPlan(nodes, connections);
    }
}
