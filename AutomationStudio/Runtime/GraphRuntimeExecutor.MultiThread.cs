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
                var branchContext = context.Fork(baseline);
                return Task.Run(
                    () =>
                    {
                        try
                        {
                            GraphExecutionResult result = ExecuteMultiThreadBranch(
                                plan,
                                node,
                                branch.PinName,
                                branch.Label,
                                branchContext,
                                baseDirectory,
                                assets,
                                state.CreateBranchState(),
                                branchToken);
                            if (!result.ContinueExecution)
                            {
                                branchCancellation.Cancel();
                                return MultiThreadBranchResult.Failed(branch.Label, result);
                            }

                            IReadOnlyDictionary<string, object> changes = branchContext.GetChangesSince(baseline);
                            if (!context.TryMergeChanges(changes, baseline, out string conflictKey))
                            {
                                branchCancellation.Cancel();
                                string outputLabel = FormatRuntimeOutputKey(plan, conflictKey);
                                string message = $"多线程分支输出冲突：{NodeLogLabel(node)} / {branch.Label} 写入 {outputLabel}，但该输出已被其他分支写入不同值。";
                                Logger.Error(message);
                                return MultiThreadBranchResult.Failed(
                                    branch.Label,
                                    GraphExecutionResult.Fatal(message));
                            }

                            Logger.Info($"多线程 {NodeLogLabel(node)} / {branch.Label} 完成。");
                            return MultiThreadBranchResult.Completed(branch.Label, result);
                        }
                        catch (OperationCanceledException)
                        {
                            return MultiThreadBranchResult.Canceled(branch.Label, ct.IsCancellationRequested);
                        }
                        catch (Exception ex)
                        {
                            branchCancellation.Cancel();
                            string message = $"多线程 {NodeLogLabel(node)} / {branch.Label} 异常：{ex.Message}";
                            Logger.Error(message);
                            return MultiThreadBranchResult.Failed(
                                branch.Label,
                                GraphExecutionResult.Fatal(message));
                        }
                        finally
                        {
                            branchContext.Dispose();
                        }
                    },
                    CancellationToken.None);
            })
            .ToArray();

        Task.WaitAll(tasks);
        ct.ThrowIfCancellationRequested();

        foreach (var task in tasks)
        {
            MultiThreadBranchResult branchResult = task.Result;
            if (branchResult.IsExternalCancellation)
                ct.ThrowIfCancellationRequested();

            if (branchResult.Result is { ContinueExecution: false } result)
            {
                string message = $"多线程分支失败：{NodeLogLabel(node)}：{result.Message}";
                Logger.Error(message);
                return NodeExecutionResult.Fatal(message);
            }
        }

        if (tasks.Any(task => task.Result.IsCanceled))
        {
            string message = $"多线程执行失败：{NodeLogLabel(node)}：分支被意外取消。";
            Logger.Error(message);
            return NodeExecutionResult.Fatal(message);
        }

        Logger.Info($"多线程全部完成：{NodeLogLabel(node)}。");
        return NodeExecutionResult.Ok("多线程全部完成。", MultiThreadNodeViewModel.CompletedPinName);
    }

    private sealed record MultiThreadBranchResult(
        string Label,
        GraphExecutionResult? Result,
        bool IsCanceled,
        bool IsExternalCancellation)
    {
        public static MultiThreadBranchResult Completed(string label, GraphExecutionResult result) =>
            new(label, result, false, false);

        public static MultiThreadBranchResult Failed(string label, GraphExecutionResult result) =>
            new(label, result, false, false);

        public static MultiThreadBranchResult Canceled(string label, bool externalCancellation) =>
            new(label, null, true, externalCancellation);
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

        return result;
    }
}
