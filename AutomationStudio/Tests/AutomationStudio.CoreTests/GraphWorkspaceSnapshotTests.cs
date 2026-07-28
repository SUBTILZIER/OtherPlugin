using AutomationStudioWpf.Graph;
using AutomationStudioWpf.GraphCore;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class GraphWorkspaceSnapshotTests
{
    [TestMethod]
    public void SnapshotDoesNotRetainLiveGraphReferences()
    {
        var script = TestGraphFactory.Script("Before");
        var main = TestGraphFactory.Graph(
            "Main",
            GraphAssetKind.EventGraph,
            GraphEntryRole.MainEvent,
            TestGraphFactory.Node("start", "start", "BeforeNode"));
        script.EventGraphs.Add(main);

        GraphWorkspaceSnapshot snapshot = GraphWorkspaceSnapshotFactory.Create([script]);
        script.Name = "After";
        main.Graph.Nodes[0].Title = "AfterNode";
        string indexedNodeTitle = snapshot.Assets[0].EventGraphs[0].Nodes[0].Title;
        GraphFileModel firstCopy = snapshot.Assets[0].EventGraphs[0].ToMutableModel();
        firstCopy.Nodes[0].Title = "MutatedCopy";
        GraphFileModel secondCopy = snapshot.Assets[0].EventGraphs[0].ToMutableModel();

        Assert.AreEqual("Before", snapshot.Assets[0].Name);
        Assert.AreEqual("BeforeNode", indexedNodeTitle);
        Assert.AreEqual("BeforeNode", secondCopy.Nodes[0].Title);
    }

    [TestMethod]
    public void DuplicateAndEmptyIdsBecomeIssuesInsteadOfExceptions()
    {
        var script = TestGraphFactory.Script();
        var main = TestGraphFactory.Graph(
            "Main",
            GraphAssetKind.EventGraph,
            GraphEntryRole.MainEvent,
            TestGraphFactory.Node("duplicate", "start"),
            TestGraphFactory.Node("duplicate", "delay"),
            TestGraphFactory.Node(string.Empty, "print_log"));
        script.EventGraphs.Add(main);

        GraphWorkspaceSnapshot snapshot = GraphWorkspaceSnapshotFactory.Create([script]);

        Assert.IsTrue(snapshot.HasErrors);
        Assert.IsTrue(snapshot.Issues.Any(issue => issue.Message.Contains("节点 ID 重复", StringComparison.Ordinal)));
        Assert.IsTrue(snapshot.Issues.Any(issue => issue.Message.Contains("空节点 ID", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ConnectionAndNodeCopiesRemainIsolatedFromLiveGraph()
    {
        var script = TestGraphFactory.Script();
        var main = TestGraphFactory.Graph(
            "Main",
            GraphAssetKind.EventGraph,
            GraphEntryRole.MainEvent,
            TestGraphFactory.Node("start", "start"),
            TestGraphFactory.Node("print", "print_log"));
        main.Graph.Connections.Add(new ConnectionFileModel
        {
            SourceNodeId = "start",
            SourcePinName = "exec_out",
            TargetNodeId = "print",
            TargetPinName = "exec_in",
        });
        script.EventGraphs.Add(main);

        GraphSnapshot graph = GraphWorkspaceSnapshotFactory.Create([script]).Assets[0].EventGraphs[0];
        main.Graph.Nodes[1].Title = "Changed";
        main.Graph.Connections[0].TargetNodeId = "changed";

        NodeFileModel nodeCopy = graph.Nodes[1].ToMutableModel();
        nodeCopy.Title = "MutatedCopy";

        Assert.AreNotEqual("Changed", graph.Nodes[1].Title);
        Assert.AreNotEqual("MutatedCopy", graph.Nodes[1].ToMutableModel().Title);
        Assert.AreEqual("print", graph.Connections[0].TargetNodeId);
    }
}
