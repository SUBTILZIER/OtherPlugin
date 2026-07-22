using AutomationStudioWpf.Graph;
using AutomationStudioWpf.GraphCore;
using AutomationStudioWpf.Services;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class GraphExecutionPreflightTests
{
    [TestMethod]
    public void CleanGraphWithDuplicateNodeIdsCannotExecute()
    {
        ContentAssetViewModel script = TestGraphFactory.Script("BadScript");
        GraphListItemViewModel main = GraphStructureNormalizer.CreateMainEventGraph("Main");
        main.IsCompileDirty = false;
        main.Graph.Nodes.Add(TestGraphFactory.Node("duplicate", "delay"));
        main.Graph.Nodes.Add(TestGraphFactory.Node("duplicate", "print_log"));
        script.EventGraphs.Add(main);

        GraphExecutionPreflightResult result = new GraphExecutionPreflightService()
            .Prepare(GraphWorkspaceReadModel.Create([script]), script.Id);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Issues.Any(issue => issue.Message.Contains("节点 ID 重复", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ReachableMalformedFunctionCannotExecute()
    {
        ContentAssetViewModel script = TestGraphFactory.Script("Script");
        GraphListItemViewModel main = GraphStructureNormalizer.CreateMainEventGraph("Main");
        main.Graph.Nodes.Add(new NodeFileModel
        {
            Id = "call",
            NodeTypeKey = "function_call",
            Title = "Call",
            FunctionId = "function_bad",
        });
        script.EventGraphs.Add(main);

        ContentAssetViewModel library = TestGraphFactory.FunctionLibrary("Library");
        GraphListItemViewModel function = TestGraphFactory.ValidFunction("Bad", "function_bad");
        function.IsPublicToLibrary = true;
        function.Graph.Nodes.Add(TestGraphFactory.Node("duplicate", "delay"));
        function.Graph.Nodes.Add(TestGraphFactory.Node("duplicate", "print_log"));
        library.Functions.Add(function);

        GraphExecutionPreflightResult result = new GraphExecutionPreflightService()
            .Prepare(GraphWorkspaceReadModel.Create([script, library]), script.Id);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Issues.Any(issue => issue.Message.Contains("节点 ID 重复", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void PublicLibraryFunctionCanReachPrivateHelperFunction()
    {
        ContentAssetViewModel script = TestGraphFactory.Script("Script");
        GraphListItemViewModel main = GraphStructureNormalizer.CreateMainEventGraph("Main");
        main.Graph.Nodes.Add(new NodeFileModel
        {
            Id = "call_public",
            NodeTypeKey = "function_call",
            Title = "Public",
            FunctionId = "function_public",
        });
        script.EventGraphs.Add(main);

        ContentAssetViewModel library = TestGraphFactory.FunctionLibrary("Library");
        GraphListItemViewModel publicFunction = TestGraphFactory.ValidFunction("Public", "function_public");
        publicFunction.IsPublicToLibrary = true;
        publicFunction.Graph.Nodes.Add(new NodeFileModel
        {
            Id = "call_private",
            NodeTypeKey = "function_call",
            Title = "Private",
            FunctionId = "function_private",
        });
        GraphListItemViewModel privateFunction = TestGraphFactory.ValidFunction("Private", "function_private");
        privateFunction.IsPublicToLibrary = false;
        library.Functions.Add(publicFunction);
        library.Functions.Add(privateFunction);

        GraphExecutionPreflightResult result = new GraphExecutionPreflightService()
            .Prepare(GraphWorkspaceReadModel.Create([script, library]), script.Id);

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message)));
        Assert.IsTrue(result.AssetLibrary!.Functions.ContainsKey("function_public"));
        Assert.IsTrue(result.AssetLibrary.Functions.ContainsKey("function_private"));
    }

    [TestMethod]
    public void UnrelatedMalformedAssetDoesNotBlockCurrentScript()
    {
        ContentAssetViewModel script = TestGraphFactory.Script("Runnable");
        script.EventGraphs.Add(GraphStructureNormalizer.CreateMainEventGraph("Main"));

        ContentAssetViewModel unrelated = TestGraphFactory.Script("Unrelated");
        GraphListItemViewModel badGraph = GraphStructureNormalizer.CreateMainEventGraph("BadMain");
        badGraph.Graph.Nodes.Add(TestGraphFactory.Node("duplicate", "delay"));
        badGraph.Graph.Nodes.Add(TestGraphFactory.Node("duplicate", "print_log"));
        unrelated.EventGraphs.Add(badGraph);

        GraphExecutionPreflightResult result = new GraphExecutionPreflightService()
            .Prepare(GraphWorkspaceReadModel.Create([script, unrelated]), script.Id);

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message)));
    }

    [TestMethod]
    public void InvalidReachableConnectionCannotExecute()
    {
        ContentAssetViewModel script = TestGraphFactory.Script("BadConnection");
        GraphListItemViewModel main = GraphStructureNormalizer.CreateMainEventGraph("Main");
        main.Graph.Connections.Add(new ConnectionFileModel
        {
            SourceNodeId = main.Graph.Nodes[0].Id,
            SourcePinName = "exec_out",
            TargetNodeId = "missing",
            TargetPinName = "exec_in",
        });
        script.EventGraphs.Add(main);

        GraphExecutionPreflightResult result = new GraphExecutionPreflightService()
            .Prepare(GraphWorkspaceReadModel.Create([script]), script.Id);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Issues.Any(issue => issue.Message.Contains("连线目标节点不存在", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void MainEventCanReachCustomEventInAuxiliaryGraph()
    {
        ContentAssetViewModel script = TestGraphFactory.Script("Script");
        GraphListItemViewModel main = GraphStructureNormalizer.CreateMainEventGraph("Main");
        main.Graph.Nodes.Add(new NodeFileModel
        {
            Id = "call_event",
            NodeTypeKey = "custom_event_call",
            Title = "Work",
            CustomEventId = "event_work",
        });
        GraphListItemViewModel auxiliary = TestGraphFactory.Graph(
            "Aux",
            GraphAssetKind.EventGraph,
            GraphEntryRole.AuxiliaryEvent,
            new NodeFileModel
            {
                Id = "event_entry",
                NodeTypeKey = "custom_event",
                Title = "Work",
                CustomEventId = "event_work",
            });
        script.EventGraphs.Add(main);
        script.EventGraphs.Add(auxiliary);

        GraphExecutionPreflightResult result = new GraphExecutionPreflightService()
            .Prepare(GraphWorkspaceReadModel.Create([script]), script.Id);

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message)));
        Assert.AreEqual(main.Id, result.Reachability.GraphIds.Single(id => id == main.Id));
        Assert.IsTrue(result.Reachability.GraphIds.Contains(auxiliary.Id));
        Assert.IsTrue(result.AssetLibrary!.CustomEvents.ContainsKey("event_work"));
    }

    [TestMethod]
    public void UnknownReachableNodeCannotExecuteEvenWhenGraphIsClean()
    {
        ContentAssetViewModel script = TestGraphFactory.Script("UnknownNode");
        GraphListItemViewModel main = GraphStructureNormalizer.CreateMainEventGraph("Main");
        main.IsCompileDirty = false;
        main.Graph.Nodes.Add(TestGraphFactory.Node("unknown", "removed_node_type"));
        script.EventGraphs.Add(main);

        GraphExecutionPreflightResult result = new GraphExecutionPreflightService()
            .Prepare(GraphWorkspaceReadModel.Create([script]), script.Id);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Issues.Any(issue => issue.Severity == GraphValidationSeverity.Error));
    }

    [TestMethod]
    public void MissingReachableFunctionCannotExecute()
    {
        ContentAssetViewModel script = TestGraphFactory.Script("MissingFunction");
        GraphListItemViewModel main = GraphStructureNormalizer.CreateMainEventGraph("Main");
        main.Graph.Nodes.Add(new NodeFileModel
        {
            Id = "missing_call",
            NodeTypeKey = "function_call",
            FunctionId = "missing_function",
        });
        script.EventGraphs.Add(main);

        GraphExecutionPreflightResult result = new GraphExecutionPreflightService()
            .Prepare(GraphWorkspaceReadModel.Create([script]), script.Id);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Issues.Any(issue => issue.Severity == GraphValidationSeverity.Error));
    }

    [TestMethod]
    public void FunctionLibraryCannotEnterScriptExecutionPreflight()
    {
        ContentAssetViewModel library = TestGraphFactory.FunctionLibrary("Library");
        library.Functions.Add(TestGraphFactory.ValidFunction("Work", "function_work"));

        GraphExecutionPreflightResult result = new GraphExecutionPreflightService()
            .Prepare(GraphWorkspaceReadModel.Create([library]), library.Id);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Issues.Any(issue => issue.Message.Contains("只能执行脚本资产", StringComparison.Ordinal)));
    }
}
