using AutomationStudioWpf.Graph;
using AutomationStudioWpf.GraphCore;
using AutomationStudioWpf.Services;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class GraphCompilePipelineTests
{
    [TestMethod]
    public void ValidationReadsSnapshotWithoutRepairingLiveGraph()
    {
        var script = TestGraphFactory.Script();
        var auxiliary = TestGraphFactory.Graph(
            "Aux",
            GraphAssetKind.EventGraph,
            GraphEntryRole.AuxiliaryEvent,
            TestGraphFactory.Node("start", "start"));
        script.EventGraphs.Add(GraphStructureNormalizer.CreateMainEventGraph("Main"));
        script.EventGraphs.Add(auxiliary);
        GraphWorkspaceSnapshot snapshot = GraphWorkspaceSnapshotFactory.Create([script]);
        GraphDependencyIndex index = new GraphDependencyIndexBuilder().Build(snapshot);

        IReadOnlyList<GraphValidationIssue> issues = new GraphValidationService()
            .ValidateWorkspace(snapshot, index);

        Assert.IsTrue(issues.Any(issue => issue.Message.Contains("辅助事件图不能包含开始节点", StringComparison.Ordinal)));
        Assert.AreEqual(1, auxiliary.Graph.Nodes.Count(node => node.NodeTypeKey == "start"));
    }

    [TestMethod]
    public void PreparationMarksRepairedAssetDirtyAndReportsRepair()
    {
        var script = TestGraphFactory.Script();
        var main = TestGraphFactory.Graph(
            "Main",
            GraphAssetKind.EventGraph,
            GraphEntryRole.MainEvent,
            TestGraphFactory.Node("start_a", "start"),
            TestGraphFactory.Node("start_b", "start"));
        main.IsDirty = false;
        main.IsCompileDirty = false;
        script.IsDirty = false;
        script.EventGraphs.Add(main);

        GraphPreparationResult result = new GraphPreparationService().PrepareAsset(script);

        Assert.IsTrue(result.ChangedAssetIds.Contains(script.Id));
        Assert.IsNotEmpty(result.RepairMessages);
        Assert.IsTrue(script.IsDirty);
        Assert.IsTrue(main.IsDirty);
        Assert.IsTrue(main.IsCompileDirty);
        Assert.AreEqual(1, main.Graph.Nodes.Count(node => node.NodeTypeKey == "start"));
    }

    [TestMethod]
    public void FatalSnapshotIssueSkipsCallReferenceMutation()
    {
        var script = TestGraphFactory.Script();
        var main = GraphStructureNormalizer.CreateMainEventGraph("Main");
        main.Graph.Nodes.Add(TestGraphFactory.Node("duplicate", "delay"));
        main.Graph.Nodes.Add(TestGraphFactory.Node("duplicate", "print_log"));
        main.Graph.Nodes.Add(new NodeFileModel
        {
            Id = "call",
            NodeTypeKey = "function_call",
            Title = "Old title",
            FunctionId = "function_target",
        });
        script.EventGraphs.Add(main);
        script.Functions.Add(TestGraphFactory.ValidFunction("Current title", "function_target"));

        GraphCompileResult result = new GraphCompileService().CompileAsset([script], script);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("Old title", main.Graph.Nodes.Single(node => node.Id == "call").Title);
    }

    [TestMethod]
    public void SuccessfulRepairKeepsSaveDirtyAndClearsCompileDirty()
    {
        var script = TestGraphFactory.Script();
        var main = TestGraphFactory.Graph(
            "Main",
            GraphAssetKind.EventGraph,
            GraphEntryRole.MainEvent,
            TestGraphFactory.Node("start_a", "start"),
            TestGraphFactory.Node("start_b", "start"));
        script.EventGraphs.Add(main);
        script.IsDirty = false;

        GraphCompileResult result = new GraphCompileService().CompileAsset([script], script);

        Assert.IsTrue(result.Success);
        Assert.IsTrue(script.IsDirty);
        Assert.IsFalse(main.IsCompileDirty);
        Assert.IsNotEmpty(result.RepairMessages);
    }

    [TestMethod]
    public void MetadataRepairDoesNotMakeCompileButtonDirty()
    {
        var script = TestGraphFactory.Script();
        var main = GraphStructureNormalizer.CreateMainEventGraph("Main");
        var target = TestGraphFactory.Node("target", "delay", "等待");
        var todo = TestGraphFactory.Node("todo", "todo", "待办");
        todo.TargetNodeId = target.Id;
        main.Graph.Nodes.Add(target);
        main.Graph.Nodes.Add(todo);
        main.IsDirty = false;
        main.IsCompileDirty = false;
        script.EventGraphs.Add(main);
        script.IsDirty = false;

        GraphPreparationResult result = new GraphPreparationService().PrepareAsset(script);

        Assert.IsTrue(result.ChangedAssetIds.Contains(script.Id));
        Assert.IsTrue(script.IsDirty);
        Assert.IsTrue(main.IsDirty);
        Assert.IsFalse(main.IsCompileDirty);
        Assert.AreEqual("N002", target.NodeNumber);
        Assert.AreEqual("等待", todo.TargetNodeTitle);
        Assert.AreEqual("N002", todo.TargetNodeNumber);
    }

    [TestMethod]
    public void CompilingFunctionLibrarySynchronizesAffectedScriptCallers()
    {
        var library = TestGraphFactory.FunctionLibrary("Library");
        var function = TestGraphFactory.ValidFunction("Format", "function_format");
        function.IsPublicToLibrary = true;
        function.Graph.Nodes.Single(node => node.NodeTypeKey == "function_entry").Parameters.Add(new GraphParameterFileModel
        {
            Id = "message",
            Name = "消息",
            Type = GraphParameterType.String,
            DefaultValue = "default",
        });
        library.Functions.Add(function);

        var script = TestGraphFactory.Script("Caller");
        var main = GraphStructureNormalizer.CreateMainEventGraph("Main");
        var call = new NodeFileModel
        {
            Id = "call",
            NodeTypeKey = "function_call",
            Title = "Old",
            FunctionId = function.Id,
            InputParameters =
            [
                new GraphParameterFileModel
                {
                    Id = "old",
                    Name = "旧参数",
                    Type = GraphParameterType.String,
                    DefaultValue = "keep",
                },
            ],
        };
        main.Graph.Nodes.Add(call);
        script.EventGraphs.Add(main);

        GraphCompileResult result = new GraphCompileService().CompileAsset([script, library], library);

        Assert.IsTrue(result.Success);
        Assert.IsTrue(result.AffectedAssetIds.SetEquals([library.Id, script.Id]));
        Assert.IsTrue(result.ChangedAssetIds.Contains(script.Id));
        Assert.IsEmpty(result.InvalidatedAssetIds);
        Assert.AreEqual("Format", call.Title);
        Assert.AreEqual("message", call.InputParameters.Single().Id);
        Assert.IsFalse(main.IsCompileDirty);
        Assert.IsTrue(script.IsDirty);

        Assert.IsNotNull(result.ReadModel);
        GraphSnapshot compiledGraph = result.ReadModel.DependencyIndex.FindGraph(main.Id)!;
        Assert.AreEqual(
            "message",
            compiledGraph.ToMutableModel().Nodes.Single(node => node.Id == call.Id).InputParameters.Single().Id);

        call.InputParameters.Single().Id = "mutated_after_compile";
        Assert.AreEqual(
            "message",
            compiledGraph.ToMutableModel().Nodes.Single(node => node.Id == call.Id).InputParameters.Single().Id);
    }

    [TestMethod]
    public void UnrelatedBrokenAssetDoesNotBlockFunctionCallerCompilation()
    {
        var library = TestGraphFactory.FunctionLibrary("Library");
        var function = TestGraphFactory.ValidFunction("Work", "function_work");
        function.IsPublicToLibrary = true;
        library.Functions.Add(function);

        var caller = TestGraphFactory.Script("Caller");
        var callerMain = GraphStructureNormalizer.CreateMainEventGraph("Main");
        callerMain.Graph.Nodes.Add(new NodeFileModel
        {
            Id = "call",
            NodeTypeKey = "function_call",
            FunctionId = function.Id,
        });
        caller.EventGraphs.Add(callerMain);

        var unrelated = TestGraphFactory.Script("Broken");
        var brokenMain = GraphStructureNormalizer.CreateMainEventGraph("BrokenMain");
        brokenMain.Graph.Nodes.Add(TestGraphFactory.Node("duplicate", "delay"));
        brokenMain.Graph.Nodes.Add(TestGraphFactory.Node("duplicate", "print_log"));
        unrelated.EventGraphs.Add(brokenMain);

        GraphCompileResult result = new GraphCompileService().CompileAsset([library, caller, unrelated], library);

        Assert.IsTrue(result.Success);
        Assert.IsFalse(result.AffectedAssetIds.Contains(unrelated.Id));
        Assert.IsTrue(brokenMain.IsCompileDirty);
    }

    [TestMethod]
    public void ReverseDependencyClosureTraversesFunctionCallers()
    {
        var library = TestGraphFactory.FunctionLibrary("Library");
        var leaf = TestGraphFactory.ValidFunction("Leaf", "function_leaf");
        leaf.IsPublicToLibrary = true;
        library.Functions.Add(leaf);

        var middleLibrary = TestGraphFactory.FunctionLibrary("Middle");
        var middle = TestGraphFactory.ValidFunction("Middle", "function_middle");
        middle.IsPublicToLibrary = true;
        middle.Graph.Nodes.Add(new NodeFileModel
        {
            Id = "call_leaf",
            NodeTypeKey = "function_call",
            FunctionId = leaf.Id,
        });
        middleLibrary.Functions.Add(middle);

        var script = TestGraphFactory.Script("Caller");
        var main = GraphStructureNormalizer.CreateMainEventGraph("Main");
        main.Graph.Nodes.Add(new NodeFileModel
        {
            Id = "call_middle",
            NodeTypeKey = "function_call",
            FunctionId = middle.Id,
        });
        script.EventGraphs.Add(main);

        GraphDependencyIndex index = new GraphDependencyIndexBuilder().Build(
            GraphWorkspaceSnapshotFactory.Create([library, middleLibrary, script]));
        IReadOnlySet<string> affected = index.GetAffectedCallerAssetIds([leaf.Id]);

        Assert.IsTrue(affected.SetEquals([middleLibrary.Id, script.Id]));
    }
}
