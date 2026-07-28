using AutomationStudioWpf.Runtime;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class GraphExecutionResultTests
{
    [TestMethod]
    public void CompletedAndFatalStatesExposeConsistentFlags()
    {
        GraphExecutionResult completed = GraphExecutionResult.Completed("done");
        GraphExecutionResult fatal = GraphExecutionResult.Fatal("failed");

        Assert.AreEqual(GraphExecutionStatus.Completed, completed.Status);
        Assert.IsTrue(completed.Success);
        Assert.IsTrue(completed.ContinueExecution);
        Assert.AreEqual(GraphExecutionStatus.FatalStop, fatal.Status);
        Assert.IsFalse(fatal.Success);
        Assert.IsFalse(fatal.ContinueExecution);
    }

    [TestMethod]
    public void LegacyConstructorRejectsContradictoryFlags()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new GraphExecutionResult(true, "invalid", false));
        Assert.ThrowsExactly<ArgumentException>(() => new GraphExecutionResult(false, "invalid", true));
    }

    [TestMethod]
    public void WarningNodeContinuesExecutionWithoutBecomingFatal()
    {
        NodeExecutionResult warning = NodeExecutionResult.Warn("warning");

        Assert.AreEqual(NodeExecutionStatus.WarnButContinue, warning.Status);
        Assert.IsTrue(warning.ContinueExecution);
        Assert.IsFalse(warning.Success);
    }
}
