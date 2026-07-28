using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Services;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class GraphCompileTests
{
    [TestMethod]
    public void DuplicateNodeIdReturnsIssueAndKeepsCompileDirty()
    {
        var script = TestGraphFactory.Script();
        var graph = TestGraphFactory.Graph(
            "Main",
            GraphAssetKind.EventGraph,
            GraphEntryRole.MainEvent,
            TestGraphFactory.Node("duplicate", "start"),
            TestGraphFactory.Node("duplicate", "delay"));
        script.EventGraphs.Add(graph);

        GraphCompileResult result = new GraphCompileService().CompileAsset([script], script);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Issues.Any(issue => issue.Message.Contains("节点 ID 重复", StringComparison.Ordinal)));
        Assert.IsTrue(graph.IsCompileDirty);
    }

    [TestMethod]
    public void CompileGraphFailureRestoresCompileDirtyOnPreviouslyCleanGraph()
    {
        var script = TestGraphFactory.Script();
        var graph = TestGraphFactory.Graph(
            "Main",
            GraphAssetKind.EventGraph,
            GraphEntryRole.MainEvent,
            TestGraphFactory.Node("duplicate", "start"),
            TestGraphFactory.Node("duplicate", "delay"));
        graph.IsCompileDirty = false;
        script.EventGraphs.Add(graph);

        GraphCompileResult result = new GraphCompileService().CompileGraph([script], script, graph);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(graph.IsCompileDirty);
    }

    [TestMethod]
    public void CompileWorkspaceFailureMarksEveryParticipatingGraphCompileDirty()
    {
        var invalidScript = TestGraphFactory.Script("Invalid");
        var invalidGraph = TestGraphFactory.Graph(
            "Main",
            GraphAssetKind.EventGraph,
            GraphEntryRole.MainEvent,
            TestGraphFactory.Node("duplicate", "start"),
            TestGraphFactory.Node("duplicate", "delay"));
        invalidGraph.IsCompileDirty = false;
        invalidScript.IsDirty = true;
        invalidScript.EventGraphs.Add(invalidGraph);

        var cleanScript = TestGraphFactory.Script("Clean");
        var cleanGraph = TestGraphFactory.Graph(
            "Main",
            GraphAssetKind.EventGraph,
            GraphEntryRole.MainEvent,
            TestGraphFactory.Node("start", "start"));
        cleanGraph.IsCompileDirty = false;
        cleanScript.IsDirty = true;
        cleanScript.EventGraphs.Add(cleanGraph);

        GraphCompileResult result = new GraphCompileService().Compile([invalidScript, cleanScript]);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(invalidGraph.IsCompileDirty);
        Assert.IsTrue(cleanGraph.IsCompileDirty);
        Assert.IsTrue(invalidScript.IsDirty);
        Assert.IsTrue(cleanScript.IsDirty);
    }
}
