using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Logging;

namespace AutomationStudioWpf.Runtime;

public sealed partial class GraphRuntimeExecutor
{
    private NodeExecutionResult ExecuteMultiThreadNode(
        GraphExecutionPlan plan,
        GraphRuntimeNode node,
        RuntimeContext context,
        string baseDirectory,
        RuntimeAssetLibrary assets,
        RuntimeExecutionState state,
        CancellationToken ct)
    {
        int threadCount = Math.Max(MultiThreadNodeViewModel.MinimumThreadOutputCount, node.ThreadOutputCount);
        var connectedBranches = Enumerable.Range(1, threadCount)
            .Select(ordinal => new
            {
                PinName = MultiThreadNodeViewModel.ThreadOutputPinName(ordinal),
                Label = MultiThreadNodeViewModel.ThreadOutputPinLabel(ordinal),
            })
            .Where(branch => plan.Index.GetExecutionConnection(node.Id, branch.PinName) is not null)
            .ToList();

        if (connectedBranches.Count == 0)
        {
            context.Set(node.Id, MultiThreadNodeViewModel.ResultPinName, true);
            Logger.Info($"多线程无连接分支：{NodeLogLabel(node)}，直接进入全部完成。");
            return NodeExecutionResult.Ok("多线程无连接分支。", MultiThreadNodeViewModel.CompletedPinName);
        }

        Logger.Info($"多线程开始：{NodeLogLabel(node)}，连接分支 {connectedBranches.Count}/{threadCount}。");
        using var branchCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        CancellationToken branchToken = branchCancellation.Token;
        IReadOnlyDictionary<string, object> baseline = context.SnapshotValues();

        var tasks = connectedBranches
            .Select(branch =>
            {
                RuntimeContext branchContext = context.Fork(baseline);
                return Task.Run(() => ExecuteMultiThreadBranchWrapper(
                    plan,
                    node,
                    branch.PinName,
                    branch.Label,
                    branchContext,
                    baseline,
                    baseDirectory,
                    assets,
                    state,
                    branchCancellation,
                    ct,
                    branchToken), CancellationToken.None);
            })
            .ToArray();

        Task.WaitAll(tasks);
        ct.ThrowIfCancellationRequested();

        MultiThreadBranchResult? failedBranch = tasks
            .Select(task => task.Result)
            .FirstOrDefault(result => result.Result is { ContinueExecution: false });
        if (failedBranch is not null)
        {
            string message = $"多线程分支失败：{NodeLogLabel(node)}：{failedBranch.Result!.Message}";
            Logger.Error(message);
            return RouteMultiThreadFailure(plan, node, context, message);
        }

        if (tasks.Any(task => task.Result.IsCanceled))
        {
            string message = $"多线程执行失败：{NodeLogLabel(node)}：分支被意外取消。";
            Logger.Error(message);
            return RouteMultiThreadFailure(plan, node, context, message);
        }

        // Stage every branch merge first. The parent context remains untouched until
        // all changes are known to be compatible.
        using var mergedContext = context.Fork(baseline);
        foreach (MultiThreadBranchResult branch in tasks.Select(task => task.Result))
        {
            if (!mergedContext.TryMergeChanges(branch.Changes, baseline, out string conflictKey))
            {
                string message = BuildConflictMessage(plan, node, branch.Label, conflictKey);
                Logger.Error(message);
                return RouteMultiThreadFailure(plan, node, context, message);
            }
        }

        IReadOnlyDictionary<string, object> mergedChanges = mergedContext.GetChangesSince(baseline);
        if (!context.TryMergeChanges(mergedChanges, baseline, out string parentConflictKey))
        {
            string message = BuildConflictMessage(plan, node, "合并", parentConflictKey);
            Logger.Error(message);
            return RouteMultiThreadFailure(plan, node, context, message);
        }

        context.Set(node.Id, MultiThreadNodeViewModel.ResultPinName, true);
        Logger.Info($"多线程全部完成：{NodeLogLabel(node)}。");
        return NodeExecutionResult.Ok("多线程全部完成。", MultiThreadNodeViewModel.CompletedPinName);
    }

    private MultiThreadBranchResult ExecuteMultiThreadBranchWrapper(
        GraphExecutionPlan plan,
        GraphRuntimeNode node,
        string pinName,
        string label,
        RuntimeContext branchContext,
        IReadOnlyDictionary<string, object> baseline,
        string baseDirectory,
        RuntimeAssetLibrary assets,
        RuntimeExecutionState parentState,
        CancellationTokenSource branchCancellation,
        CancellationToken externalToken,
        CancellationToken branchToken)
    {
        try
        {
            GraphExecutionResult result = ExecuteMultiThreadBranch(
                plan,
                node,
                pinName,
                label,
                branchContext,
                baseDirectory,
                assets,
                parentState.CreateBranchState(),
                branchToken);

            if (!result.ContinueExecution)
            {
                branchCancellation.Cancel();
                return MultiThreadBranchResult.Failed(label, result);
            }

            IReadOnlyDictionary<string, object> changes = branchContext.GetChangesSince(baseline);
            return MultiThreadBranchResult.Completed(label, result, changes);
        }
        catch (OperationCanceledException)
        {
            return MultiThreadBranchResult.Canceled(label, externalToken.IsCancellationRequested);
        }
        catch (Exception ex)
        {
            branchCancellation.Cancel();
            string message = $"多线程 {NodeLogLabel(node)} / {label} 异常：{ex.Message}";
            Logger.Error(message);
            return MultiThreadBranchResult.Failed(label, GraphExecutionResult.Fatal(message));
        }
        finally
        {
            branchContext.Dispose();
        }
    }

    private NodeExecutionResult RouteMultiThreadFailure(
        GraphExecutionPlan plan,
        GraphRuntimeNode node,
        RuntimeContext context,
        string message)
    {
        context.Set(node.Id, MultiThreadNodeViewModel.ResultPinName, false);
        if (plan.Index.GetExecutionConnection(node.Id, MultiThreadNodeViewModel.FailedPinName) is not null)
            return NodeExecutionResult.HandledFailure(message, MultiThreadNodeViewModel.FailedPinName);

        return NodeExecutionResult.Fatal(message);
    }

    private static string BuildConflictMessage(
        GraphExecutionPlan plan,
        GraphRuntimeNode node,
        string branchLabel,
        string conflictKey)
    {
        string outputLabel = FormatRuntimeOutputKey(plan, conflictKey);
        return $"多线程分支输出冲突：{NodeLogLabel(node)} / {branchLabel} 写入 {outputLabel}，但该输出已被其他分支写入不同值。";
    }

    private sealed record MultiThreadBranchResult(
        string Label,
        GraphExecutionResult? Result,
        IReadOnlyDictionary<string, object> Changes,
        bool IsCanceled,
        bool IsExternalCancellation)
    {
        public static MultiThreadBranchResult Completed(
            string label,
            GraphExecutionResult result,
            IReadOnlyDictionary<string, object> changes) =>
            new(label, result, changes, false, false);

        public static MultiThreadBranchResult Failed(string label, GraphExecutionResult result) =>
            new(label, result, new Dictionary<string, object>(StringComparer.Ordinal), false, false);

        public static MultiThreadBranchResult Canceled(string label, bool externalCancellation) =>
            new(label, null, new Dictionary<string, object>(StringComparer.Ordinal), true, externalCancellation);
    }

    private GraphExecutionResult ExecuteMultiThreadBranch(
        GraphExecutionPlan plan,
        GraphRuntimeNode node,
        string pinName,
        string label,
        RuntimeContext context,
        string baseDirectory,
        RuntimeAssetLibrary assets,
        RuntimeExecutionState branchState,
        CancellationToken ct)
    {
        Logger.Info($"多线程 {NodeLogLabel(node)} / {label} 开始。");
        GraphExecutionResult result = ExecuteChain(plan, node.Id, pinName, context, baseDirectory, assets, branchState, ct, out _);
        if (!result.ContinueExecution)
            Logger.Error($"多线程 {NodeLogLabel(node)} / {label} 失败：{result.Message}");
        else
            Logger.Info($"多线程 {NodeLogLabel(node)} / {label} 完成。");

        return result;
    }
}
