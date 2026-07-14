using System.Windows;
using AutomationStudioWpf.Services;

namespace AutomationStudioWpf.Interaction;

internal sealed class HotkeyCaptureCoordinator
{
    private readonly Window _defaultOwner;
    private readonly ScriptHotkeyService _hotkeyService;
    private readonly Func<bool> _isExecutionActive;
    private readonly Action<string> _setStatus;

    public HotkeyCaptureCoordinator(
        Window defaultOwner,
        ScriptHotkeyService hotkeyService,
        Func<bool> isExecutionActive,
        Action<string> setStatus)
    {
        _defaultOwner = defaultOwner;
        _hotkeyService = hotkeyService;
        _isExecutionActive = isExecutionActive;
        _setStatus = setStatus;
    }

    public ScriptHotkeySettings? Capture(Window? owner)
    {
        owner ??= _defaultOwner;
        if (_isExecutionActive())
        {
            const string message = "脚本运行期间不能捕获热键。请先停止全部运行任务。";
            _setStatus(message);
            ThemedDialog.Show(owner, message, "无法修改热键", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        using var suspension = _hotkeyService.SuspendTriggers();
        try
        {
            var window = new ScriptHotkeyCaptureWindow(owner);
            return window.ShowDialog() == true ? window.Result : null;
        }
        finally
        {
            _setStatus("热键监听已恢复。");
        }
    }
}
