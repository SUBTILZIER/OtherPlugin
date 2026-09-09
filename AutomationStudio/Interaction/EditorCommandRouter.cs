using System.Windows.Input;

namespace AutomationStudioWpf.Interaction;

/// <summary>统一编辑器快捷键判定，供窗口与嵌入式编辑面板复用。</summary>
public sealed class EditorCommandRouter
{
    public bool IsFocusCommand(Key key, ModifierKeys modifiers) =>
        modifiers.HasFlag(ModifierKeys.Control) && key is Key.D1 or Key.D2 or Key.D3 or Key.D4;

    public bool IsPanelToggle(Key key, ModifierKeys modifiers) =>
        modifiers.HasFlag(ModifierKeys.Alt) && key is Key.D1 or Key.D2 or Key.D3;
}
