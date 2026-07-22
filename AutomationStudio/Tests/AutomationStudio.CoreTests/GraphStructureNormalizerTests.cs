using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Services;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class GraphStructureNormalizerTests
{
    [TestMethod]
    public void EmptyScriptCreatesMainGraphWithSingleStart()
    {
        var asset = TestGraphFactory.Script();

        bool changed = GraphStructureNormalizer.NormalizeContentAsset(asset);

        Assert.IsTrue(changed);
        Assert.HasCount(1, asset.EventGraphs);
        Assert.AreEqual(GraphEntryRole.MainEvent, asset.EventGraphs[0].EntryRole);
        Assert.AreEqual(1, CountType(asset.EventGraphs[0].Graph, "start"));
    }

    [TestMethod]
    public void MainAndAuxiliaryGraphsEnforceStartInvariant()
    {
        var asset = TestGraphFactory.Script();
        var main = TestGraphFactory.Graph(
            "Main",
            GraphAssetKind.EventGraph,
            GraphEntryRole.MainEvent,
            TestGraphFactory.Node("start_a", "start"),
            TestGraphFactory.Node("start_b", "start"));
        var auxiliary = TestGraphFactory.Graph(
            "Aux",
            GraphAssetKind.EventGraph,
            GraphEntryRole.AuxiliaryEvent,
            TestGraphFactory.Node("aux_start", "start"),
            TestGraphFactory.Node("aux_log", "print_log"));
        auxiliary.Graph.Connections.Add(new ConnectionFileModel
        {
            SourceNodeId = "aux_start",
            SourcePinName = "exec_out",
            TargetNodeId = "aux_log",
            TargetPinName = "exec_in",
        });
        asset.EventGraphs.Add(main);
        asset.EventGraphs.Add(auxiliary);

        GraphStructureNormalizer.NormalizeContentAsset(asset);

        Assert.AreEqual(1, CountType(main.Graph, "start"));
        Assert.AreEqual(0, CountType(auxiliary.Graph, "start"));
        Assert.IsFalse(auxiliary.Graph.Connections.Any(connection =>
            connection.SourceNodeId == "aux_start" || connection.TargetNodeId == "aux_start"));
    }

    [TestMethod]
    public void FunctionKeepsSingleEntryAndReturn()
    {
        var asset = TestGraphFactory.FunctionLibrary();
        var function = TestGraphFactory.Graph(
            "Function",
            GraphAssetKind.Function,
            null,
            TestGraphFactory.Node("entry_a", "function_entry"),
            TestGraphFactory.Node("entry_b", "function_entry"),
            TestGraphFactory.Node("return_a", "function_return"),
            TestGraphFactory.Node("return_b", "function_return"));
        asset.Functions.Add(function);

        GraphStructureNormalizer.NormalizeContentAsset(asset);

        Assert.AreEqual(1, CountType(function.Graph, "function_entry"));
        Assert.AreEqual(1, CountType(function.Graph, "function_return"));
    }

    [TestMethod]
    public void NewFunctionHasBoundaryNodesAndDefaultExecutionConnection()
    {
        GraphListItemViewModel function = GraphStructureNormalizer.CreateFunctionGraph("LocalFunction");

        NodeFileModel entry = function.Graph.Nodes.Single(node => node.NodeTypeKey == "function_entry");
        NodeFileModel ret = function.Graph.Nodes.Single(node => node.NodeTypeKey == "function_return");
        Assert.AreEqual("LocalFunction开始", entry.Title);
        Assert.AreEqual("Fun001", entry.NodeNumber);
        Assert.AreEqual("Fun002", ret.NodeNumber);
        Assert.IsTrue(function.Graph.Connections.Any(connection =>
            connection.SourceNodeId == entry.Id &&
            connection.SourcePinName == "exec_out" &&
            connection.TargetNodeId == ret.Id &&
            connection.TargetPinName == "exec_in"));
    }

    [TestMethod]
    public void FunctionBoundaryNodesCannotBeDeleted()
    {
        GraphListItemViewModel function = GraphStructureNormalizer.CreateFunctionGraph("LocalFunction");
        var editor = new GraphEditorService();
        editor.LoadFromModel(function.Graph);
        var entry = editor.Nodes.Single(node => node.NodeKind == NodeKind.FunctionEntry);
        var ret = editor.Nodes.Single(node => node.NodeKind == NodeKind.FunctionReturn);

        editor.RemoveNode(entry);
        editor.RemoveNode(ret);

        Assert.AreEqual(1, editor.Nodes.Count(node => node.NodeKind == NodeKind.FunctionEntry));
        Assert.AreEqual(1, editor.Nodes.Count(node => node.NodeKind == NodeKind.FunctionReturn));
    }

    private static int CountType(GraphFileModel graph, string typeKey) =>
        graph.Nodes.Count(node => string.Equals(node.NodeTypeKey, typeKey, StringComparison.OrdinalIgnoreCase));
}
