namespace AutomationStudioWpf.Runtime;

public enum NodeExecutionStatus
{
    Success,
    WarnButContinue,
    FatalStop,
}

public sealed record NodeExecutionResult(
    NodeExecutionStatus Status,
    string Message,
    string? NextPinName = "exec_out")
{
    public bool ContinueExecution => Status != NodeExecutionStatus.FatalStop;

    public bool Success => Status == NodeExecutionStatus.Success;

    public static NodeExecutionResult Ok(string message, string? nextPinName = "exec_out") =>
        new(NodeExecutionStatus.Success, message, nextPinName);

    public static NodeExecutionResult Warn(string message, string? nextPinName = "exec_out") =>
        new(NodeExecutionStatus.WarnButContinue, message, nextPinName);

    public static NodeExecutionResult Fatal(string message) =>
        new(NodeExecutionStatus.FatalStop, message, null);
}

