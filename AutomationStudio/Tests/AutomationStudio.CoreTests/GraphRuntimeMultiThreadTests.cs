using System.Diagnostics;
using System.Drawing;
using AutomationStudioWpf.Adapters;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Nodes;
using AutomationStudioWpf.Runtime;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class GraphRuntimeMultiThreadTests
{
    [TestMethod]
    public async Task BusinessFalseDoesNotFailBranchesAndCompletedRunsOnce()
    {
        var process = new ProbeProcessAdapter();
        GraphRuntimeExecutor executor = CreateExecutor(process);
        GraphExecutionPlan plan = CreatePlan(
            [
                GraphRuntimeNode.ForIf("false", "False", false),
                GraphRuntimeNode.ForIf("true1", "True 1", true),
                GraphRuntimeNode.ForIf("true2", "True 2", true),
            ],
            [
                Exec("multi", "exec_thread_1", "false"),
                Exec("multi", "exec_thread_2", "true1"),
                Exec("multi", "exec_thread_3", "true2"),
            ]);

        GraphExecutionResult result = await Task.Run(() => executor.Execute(plan, Environment.CurrentDirectory));

        Assert.AreEqual(GraphExecutionStatus.Completed, result.Status);
        Assert.AreEqual(1, process.StartCount);
    }

    [TestMethod]
    public async Task FatalBranchCancelsLongSiblingAndSkipsCompleted()
    {
        var process = new ProbeProcessAdapter();
        GraphRuntimeExecutor executor = CreateExecutor(process);
        GraphExecutionPlan plan = CreatePlan(
            [
                GraphRuntimeNode.ForFunctionCall("fatal", "Missing function", "missing"),
                GraphRuntimeNode.ForDelay("slow", "Slow", 10000),
            ],
            [
                Exec("multi", "exec_thread_1", "fatal"),
                Exec("multi", "exec_thread_2", "slow"),
            ]);
        var stopwatch = Stopwatch.StartNew();

        GraphExecutionResult result = await Task.Run(() => executor.Execute(plan, Environment.CurrentDirectory));

        stopwatch.Stop();
        Assert.AreEqual(GraphExecutionStatus.FatalStop, result.Status);
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"Sibling cancellation took {stopwatch.Elapsed}.");
        Assert.AreEqual(0, process.StartCount);
    }

    [TestMethod]
    public async Task FatalBranchUsesFailureOutputWhenConnected()
    {
        var process = new ProbeProcessAdapter();
        GraphRuntimeExecutor executor = CreateExecutor(process);
        GraphExecutionPlan plan = CreatePlan(
            [GraphRuntimeNode.ForFunctionCall("fatal", "Missing function", "missing")],
            [Exec("multi", "exec_thread_1", "fatal")],
            connectFailure: true);

        GraphExecutionResult result = await Task.Run(() => executor.Execute(plan, Environment.CurrentDirectory));

        Assert.AreEqual(GraphExecutionStatus.Completed, result.Status);
        Assert.AreEqual(1, process.StartCount);
    }

    [TestMethod]
    public async Task ExternalCancellationPropagatesInsteadOfBecomingFatal()
    {
        var process = new ProbeProcessAdapter();
        GraphRuntimeExecutor executor = CreateExecutor(process);
        GraphExecutionPlan plan = CreatePlan(
            [
                GraphRuntimeNode.ForDelay("slow1", "Slow 1", 10000),
                GraphRuntimeNode.ForDelay("slow2", "Slow 2", 10000),
            ],
            [
                Exec("multi", "exec_thread_1", "slow1"),
                Exec("multi", "exec_thread_2", "slow2"),
            ]);
        using var cancellation = new CancellationTokenSource();
        Task<GraphExecutionResult> running = Task.Run(() => executor.Execute(
            plan,
            Environment.CurrentDirectory,
            cancellation.Token));

        await Task.Delay(50);
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => await running);
        Assert.AreEqual(0, process.StartCount);
    }

    [TestMethod]
    public void BranchOutputsMergeFalseTrueTrueWithoutConflict()
    {
        using var parent = new RuntimeContext();
        IReadOnlyDictionary<string, object> baseline = parent.SnapshotValues();

        Assert.IsTrue(Merge(parent, baseline, "branch1", false));
        Assert.IsTrue(Merge(parent, baseline, "branch2", true));
        Assert.IsTrue(Merge(parent, baseline, "branch3", true));
        Assert.IsTrue(parent.TryGet("branch1", "result", out bool first));
        Assert.IsFalse(first);
        Assert.IsTrue(parent.TryGet("branch2", "result", out bool second) && second);
        Assert.IsTrue(parent.TryGet("branch3", "result", out bool third) && third);
    }

    [TestMethod]
    public void SameOutputValueCanMergeButDifferentValueConflicts()
    {
        using var parent = new RuntimeContext();
        IReadOnlyDictionary<string, object> baseline = parent.SnapshotValues();

        Assert.IsTrue(Merge(parent, baseline, "shared", false));
        Assert.IsTrue(Merge(parent, baseline, "shared", false));
        Assert.IsFalse(Merge(parent, baseline, "shared", true));
    }

    private static bool Merge(
        RuntimeContext parent,
        IReadOnlyDictionary<string, object> baseline,
        string nodeId,
        bool value)
    {
        using RuntimeContext branch = parent.Fork(baseline);
        branch.Set(nodeId, "result", value);
        return parent.TryMergeChanges(branch.GetChangesSince(baseline), baseline, out _);
    }

    private static GraphExecutionPlan CreatePlan(
        IReadOnlyList<GraphRuntimeNode> branchNodes,
        IReadOnlyList<GraphRuntimeConnection> branchConnections,
        bool connectFailure = false)
    {
        GraphRuntimeNode start = GraphRuntimeNode.ForStart("start", "Start");
        GraphRuntimeNode multi = GraphRuntimeNode.ForMultiThread("multi", "Multi", 3);
        GraphRuntimeNode completed = GraphRuntimeNode.ForStartProgram(
            "completed",
            "Completed",
            "probe.exe",
            0,
            ProgramStartFailureAction.None,
            0);
        var connections = new List<GraphRuntimeConnection>
        {
            Exec("start", "exec_out", "multi"),
        };
        connections.AddRange(branchConnections);
        connections.Add(Exec("multi", "exec_completed", "completed"));
        if (connectFailure)
            connections.Add(Exec("multi", MultiThreadNodeViewModel.FailedPinName, "completed"));

        return new GraphExecutionPlan([start, multi, .. branchNodes, completed], connections);
    }

    private static GraphRuntimeConnection Exec(string sourceNodeId, string sourcePinName, string targetNodeId) =>
        new(sourceNodeId, sourcePinName, PinKind.Execution, targetNodeId, "exec_in", PinKind.Execution);

    private static GraphRuntimeExecutor CreateExecutor(ProbeProcessAdapter process) => new(
        new RuntimeAdapters(
            new NoOpMouseAdapter(),
            new NoOpKeyboardAdapter(),
            new NoOpWindowAdapter(),
            process,
            new NoOpPythonAdapter(),
            new NoOpScreenshotAdapter()),
        NodeRegistry.CreateDefault());

    private sealed class ProbeProcessAdapter : IProcessAdapter
    {
        private int _startCount;

        public int StartCount => Volatile.Read(ref _startCount);

        public ProcessStartResult StartProgram(
            string programPath,
            int waitTimeoutMs,
            ProgramStartFailureAction failureAction,
            int retryCount,
            CancellationToken ct)
        {
            Interlocked.Increment(ref _startCount);
            return new ProcessStartResult(true, "probe", "probe");
        }
    }

    private sealed class NoOpMouseAdapter : IMouseAdapter
    {
        public void MoveTo(Point point) { }
        public void ExecuteButton(MouseButton button, PressReleaseMode mode) { }
        public Point GetPosition() => Point.Empty;
        public void ExecuteScroll(ScrollWheelAction action, int speed, int intervalMs, int durationMs, CancellationToken ct) { }
    }

    private sealed class NoOpKeyboardAdapter : IKeyboardAdapter
    {
        public void ExecuteKey(string key, PressReleaseMode mode) { }
        public void ReleaseAllKeys() { }
    }

    private sealed class NoOpWindowAdapter : IWindowAdapter
    {
        public WindowSelectionResult SelectWindowByProcessName(string processName) => new(false, processName, string.Empty);
        public WindowSelectionResult WaitWindowByProcessName(string processName, int timeoutMs, int intervalMs, CancellationToken ct) => new(false, processName, string.Empty);
        public WindowSelectionResult CloseWindowByProcessName(string processName) => new(false, processName, string.Empty);
        public WindowSelectionResult WindowExists(string processName) => new(false, processName, string.Empty);
        public WindowInfoResult GetForegroundWindowInfo() => new(false, string.Empty, string.Empty, string.Empty);
        public List<string> GetRunningWindowNames() => [];
    }

    private sealed class NoOpPythonAdapter : IPythonScriptAdapter
    {
        public PythonScriptResult RunJsonScript(string scriptPath, object payload, TimeSpan timeout, CancellationToken ct) =>
            new(false, -1, string.Empty, string.Empty, string.Empty);
    }

    private sealed class NoOpScreenshotAdapter : IScreenshotAdapter
    {
        public ScreenshotResult SaveScreenshot(string path, int x, int y, int width, int height) =>
            new(false, path, string.Empty);
    }
}
