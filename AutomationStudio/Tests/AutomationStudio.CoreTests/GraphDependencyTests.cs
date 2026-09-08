using AutomationStudioWpf.Graph;
using AutomationStudioWpf.GraphCore;
using AutomationStudioWpf.Services;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class GraphDependencyTests
{
    [TestMethod]
    public void ScriptCanResolveCustomEventFromAuxiliaryGraph()
    {
        var script = TestGraphFactory.Script("A");
        var main = GraphStructureNormalizer.CreateMainEventGraph("Main");
        var auxiliary = TestGraphFactory.Graph(
            "Aux",
            GraphAssetKind.EventGraph,
            GraphEntryRole.AuxiliaryEvent,
            new NodeFileModel
            {
                Id = "event_node",
                NodeTypeKey = "custom_event",
                Title = "Work",
                CustomEventId = "event_a",
            });
        script.EventGraphs.Add(main);
        script.EventGraphs.Add(auxiliary);

        var events = new CustomEventResolver().Resolve(script);

        Assert.IsTrue(events.Any(item => item.Id == "event_a" && item.GraphId == auxiliary.Id));
    }

    [TestMethod]
    public void CustomEventsNeverCrossAssetBoundary()
    {
        var scriptA = TestGraphFactory.Script("A");
        var scriptB = TestGraphFactory.Script("B");
        scriptA.EventGraphs.Add(GraphStructureNormalizer.CreateMainEventGraph("MainA"));
        scriptB.EventGraphs.Add(TestGraphFactory.Graph(
            "AuxB",
            GraphAssetKind.EventGraph,
            GraphEntryRole.AuxiliaryEvent,
            new NodeFileModel
            {
                Id = "event_b_node",
                NodeTypeKey = "custom_event",
                Title = "BEvent",
                CustomEventId = "event_b",
            }));

        var events = new CustomEventResolver().Resolve(scriptA);

        Assert.IsFalse(events.Any(item => item.Id == "event_b"));
    }

    [TestMethod]
    [Timeout(3000)]
    public void RecursiveCallsDoNotHangCompilation()
    {
        var script = TestGraphFactory.Script("Recursive");
        script.EventGraphs.Add(GraphStructureNormalizer.CreateMainEventGraph("Main"));
        var function = TestGraphFactory.ValidFunction("RecursiveFunction", "function_recursive");
        function.Graph.Nodes.Add(new NodeFileModel
        {
            Id = "self_call",
            NodeTypeKey = "function_call",
            Title = "RecursiveFunction",
            FunctionId = function.Id,
        });
        script.Functions.Add(function);

        GraphCompileResult result = new GraphCompileService().CompileAsset([script], script);

        Assert.IsNotNull(result);
    }

    [TestMethod]
    public void ReachabilityTraversesEventsAndRecursiveFunctionsOnce()
    {
        var script = TestGraphFactory.Script("Script");
        var main = GraphStructureNormalizer.CreateMainEventGraph("Main");
        main.Graph.Nodes.Add(new NodeFileModel
        {
            Id = "call_event",
            NodeTypeKey = "custom_event_call",
            CustomEventId = "event_work",
        });
        var auxiliary = TestGraphFactory.Graph(
            "Aux",
            GraphAssetKind.EventGraph,
            GraphEntryRole.AuxiliaryEvent,
            new NodeFileModel
            {
                Id = "event_entry",
                NodeTypeKey = "custom_event",
                Title = "Work",
                CustomEventId = "event_work",
            },
            new NodeFileModel
            {
                Id = "call_function",
                NodeTypeKey = "function_call",
                FunctionId = "public_function",
            });
        script.EventGraphs.Add(main);
        script.EventGraphs.Add(auxiliary);

        var library = TestGraphFactory.FunctionLibrary("Library");
        var function = TestGraphFactory.ValidFunction("Public", "public_function");
        function.IsPublicToLibrary = true;
        function.Graph.Nodes.Add(new NodeFileModel
        {
            Id = "recursive_call",
            NodeTypeKey = "function_call",
            FunctionId = function.Id,
        });
        function.Graph.Nodes.Add(new NodeFileModel
        {
            Id = "image",
            NodeTypeKey = "wait_image",
        });
        library.Functions.Add(function);

        GraphWorkspaceSnapshot snapshot = GraphWorkspaceSnapshotFactory.Create([script, library]);
        GraphDependencyIndex index = new GraphDependencyIndexBuilder().Build(snapshot);
        GraphReachabilityResult reachability = index.GetMainEventReachability(script.Id);

        Assert.IsTrue(reachability.GraphIds.SetEquals([main.Id, auxiliary.Id, function.Id]));
        Assert.IsTrue(reachability.RequiresPython);
        Assert.IsEmpty(reachability.Issues);
    }

    [TestMethod]
    public void PublicFunctionFromAnotherScriptIsNotVisible()
    {
        var scriptA = TestGraphFactory.Script("A");
        scriptA.EventGraphs.Add(GraphStructureNormalizer.CreateMainEventGraph("MainA"));
        var scriptB = TestGraphFactory.Script("B");
        scriptB.EventGraphs.Add(GraphStructureNormalizer.CreateMainEventGraph("MainB"));
        var function = TestGraphFactory.ValidFunction("PrivateToScript", "script_b_function");
        function.IsPublicToLibrary = true;
        scriptB.Functions.Add(function);

        GraphDependencyIndex index = new GraphDependencyIndexBuilder().Build(
            GraphWorkspaceSnapshotFactory.Create([scriptA, scriptB]));

        Assert.IsFalse(index.ResolveFunctions(scriptA.Id).Any(target => target.Id == function.Id));
    }

    [TestMethod]
    public void EmptyCustomEventIdUpdatesClearlyMatchedCallsAcrossGraphs()
    {
        var script = TestGraphFactory.Script("Script");
        var main = GraphStructureNormalizer.CreateMainEventGraph("Main");
        var call = new NodeFileModel
        {
            Id = "call",
            NodeTypeKey = "custom_event_call",
            CustomEventId = "event_node",
        };
        main.Graph.Nodes.Add(call);
        var auxiliary = TestGraphFactory.Graph(
            "Aux",
            GraphAssetKind.EventGraph,
            GraphEntryRole.AuxiliaryEvent,
            new NodeFileModel
            {
                Id = "event_node",
                NodeTypeKey = "custom_event",
                Title = "Work",
                CustomEventId = string.Empty,
            });
        script.EventGraphs.Add(main);
        script.EventGraphs.Add(auxiliary);

        bool changed = GraphStructureNormalizer.NormalizeContentAsset(script);
        string eventId = auxiliary.Graph.Nodes.Single().CustomEventId!;

        Assert.IsTrue(changed);
        Assert.IsFalse(string.IsNullOrWhiteSpace(eventId));
        Assert.AreEqual(eventId, call.CustomEventId);
        Assert.IsTrue(main.IsCompileDirty);
        Assert.IsTrue(auxiliary.IsCompileDirty);
    }

    [TestMethod]
    public void DuplicateCustomEventIdsAreNotRedirectedAndBlockCompilation()
    {
        var script = TestGraphFactory.Script("Script");
        var main = GraphStructureNormalizer.CreateMainEventGraph("Main");
        var call = new NodeFileModel
        {
            Id = "call",
            NodeTypeKey = "custom_event_call",
            CustomEventId = "duplicate_event",
        };
        main.Graph.Nodes.Add(call);
        script.EventGraphs.Add(main);
        script.EventGraphs.Add(TestGraphFactory.Graph(
            "Aux1",
            GraphAssetKind.EventGraph,
            GraphEntryRole.AuxiliaryEvent,
            new NodeFileModel
            {
                Id = "event_1",
                NodeTypeKey = "custom_event",
                CustomEventId = "duplicate_event",
            }));
        script.EventGraphs.Add(TestGraphFactory.Graph(
            "Aux2",
            GraphAssetKind.EventGraph,
            GraphEntryRole.AuxiliaryEvent,
            new NodeFileModel
            {
                Id = "event_2",
                NodeTypeKey = "custom_event",
                CustomEventId = "duplicate_event",
            }));

        GraphCompileResult result = new GraphCompileService().CompileAsset([script], script);

        Assert.IsFalse(result.Success);
        Assert.AreEqual("duplicate_event", call.CustomEventId);
        Assert.IsTrue(script.EventGraphs.Skip(1)
            .SelectMany(graph => graph.Graph.Nodes)
            .All(node => node.CustomEventId == "duplicate_event"));
        Assert.IsTrue(result.Issues.Any(issue => issue.Message.Contains("自定义事件 ID 重复", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void DuplicateFunctionIdsAreReportedByDependencyIndex()
    {
        var script = TestGraphFactory.Script("Script");
        script.EventGraphs.Add(GraphStructureNormalizer.CreateMainEventGraph("Main"));
        var libraryA = TestGraphFactory.FunctionLibrary("LibraryA");
        libraryA.Functions.Add(TestGraphFactory.ValidFunction("Same", "duplicate_function"));
        var libraryB = TestGraphFactory.FunctionLibrary("LibraryB");
        libraryB.Functions.Add(TestGraphFactory.ValidFunction("Same", "duplicate_function"));

        GraphDependencyIndex index = new GraphDependencyIndexBuilder().Build(
            GraphWorkspaceSnapshotFactory.Create([script, libraryA, libraryB]));

        Assert.IsTrue(index.Issues.Any(issue =>
            issue.Message.Contains("函数 ID 重复：duplicate_function", StringComparison.Ordinal)));
        Assert.IsNull(index.FindFunction("duplicate_function"));
    }

    [TestMethod]
    public async Task ReadModelReachabilityCacheSupportsConcurrentReads()
    {
        var script = TestGraphFactory.Script("Script");
        var main = GraphStructureNormalizer.CreateMainEventGraph("Main");
        script.EventGraphs.Add(main);
        GraphWorkspaceReadModel readModel = GraphWorkspaceReadModel.Create([script]);

        GraphReachabilityResult[] results = await Task.WhenAll(
            Enumerable.Range(0, 32)
                .Select(_ => Task.Run(() => readModel.GetMainEventReachability(script.Id))));

        Assert.AreEqual(32, results.Length);
        Assert.IsTrue(results.All(result => result.GraphIds.SetEquals([main.Id])));
    }
}
