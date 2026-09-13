using System.Windows.Input;

namespace AutomationStudioWpf.Interaction;

/// <summary>统一编辑器快捷键判定，供窗口与嵌入式编辑面板复用。</summary>
public sealed class EditorCommandRouter
{
    public enum FocusTarget
    {
        Inspector,
        GraphList,
        ContentBrowser,
        Log,
    }

    public bool TryGetFocusTarget(Key key, ModifierKeys modifiers, out FocusTarget target)
    {
        target = key switch
        {
            Key.D1 => FocusTarget.Inspector,
            Key.D2 => FocusTarget.GraphList,
            Key.D3 => FocusTarget.ContentBrowser,
            Key.D4 => FocusTarget.Log,
            _ => default,
        };
        return modifiers.HasFlag(ModifierKeys.Control) && key is Key.D1 or Key.D2 or Key.D3 or Key.D4;
    }

    public bool IsFocusCommand(Key key, ModifierKeys modifiers) =>
        TryGetFocusTarget(key, modifiers, out _);


}
