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
}
