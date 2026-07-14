using System.ComponentModel;

namespace AutomationStudioWpf.Interaction;

internal sealed record HookEndpointInstallResult(bool Required, bool Installed, int ErrorCode)
{
    public static HookEndpointInstallResult NotRequired { get; } = new(false, false, 0);

    public bool Failed => Required && !Installed;

    public string FormatFailure(string name)
    {
        int errorCode = ErrorCode == 0 ? -1 : ErrorCode;
        string detail = ErrorCode == 0
            ? "系统未返回错误码"
            : new Win32Exception(ErrorCode).Message;
        return $"{name}安装失败（Win32 {errorCode}：{detail}）。";
    }
}

internal sealed record HookInstallResult(
    HookEndpointInstallResult Keyboard,
    HookEndpointInstallResult Mouse)
{
    public static HookInstallResult None { get; } = new(
        HookEndpointInstallResult.NotRequired,
        HookEndpointInstallResult.NotRequired);

    public bool HasFailures => Keyboard.Failed || Mouse.Failed;
}

internal sealed record ScriptHotkeyRefreshResult(
    IReadOnlyList<string> Conflicts,
    HookInstallResult Hooks);
