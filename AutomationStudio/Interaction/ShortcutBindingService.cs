using System.Globalization;
using System.Windows.Input;

namespace AutomationStudioWpf.Interaction;

public enum EditorShortcutAction
{
    SearchContent,
    FocusInspector,
    FocusGraphList,
    FocusContentBrowser,
    FocusLog,
    FitGraph,
    ResetZoom,
    ResetView,
    ToggleGraphSidebar,
    ToggleInspector,
    ToggleLog,
    Undo,
    Redo,
    CopyContent,
    PasteContent,
    DeleteContent,
    RenameContent,
    ShowScriptProperties,
    CopyNodes,
    PasteNodes,
    DeleteNodes,
}

public sealed record EditorShortcutDefinition(EditorShortcutAction Action, string DisplayName, string DefaultGesture);
public sealed record ShortcutGesture(Key Key, ModifierKeys Modifiers);

public static class ShortcutBindingService
{
    public static IReadOnlyList<EditorShortcutDefinition> Definitions { get; } =
    [
        new(EditorShortcutAction.SearchContent, "搜索内容", "Ctrl+F"),
        new(EditorShortcutAction.FocusInspector, "聚焦 Inspector", "Ctrl+1"),
        new(EditorShortcutAction.FocusGraphList, "聚焦节点列表", "Ctrl+2"),
        new(EditorShortcutAction.FocusContentBrowser, "聚焦内容浏览器", "Ctrl+3"),
        new(EditorShortcutAction.FocusLog, "聚焦日志", "Ctrl+4"),
        new(EditorShortcutAction.FitGraph, "画布全览", "F"),
        new(EditorShortcutAction.ResetZoom, "画布 100%", "0"),
        new(EditorShortcutAction.ResetView, "重置画布视图", "Ctrl+0"),
        new(EditorShortcutAction.ToggleGraphSidebar, "切换左栏", "Alt+1"),
        new(EditorShortcutAction.ToggleInspector, "切换 Inspector", "Alt+2"),
        new(EditorShortcutAction.ToggleLog, "切换日志", "Alt+3"),
        new(EditorShortcutAction.Undo, "撤销", "Ctrl+Z"),
        new(EditorShortcutAction.Redo, "重做", "Ctrl+Y"),
        new(EditorShortcutAction.CopyContent, "复制内容资产", "Ctrl+C"),
        new(EditorShortcutAction.PasteContent, "粘贴内容资产", "Ctrl+V"),
        new(EditorShortcutAction.DeleteContent, "删除内容资产", "Delete"),
        new(EditorShortcutAction.RenameContent, "重命名内容资产", "F2"),
        new(EditorShortcutAction.ShowScriptProperties, "打开脚本属性", "Alt+Enter"),
        new(EditorShortcutAction.CopyNodes, "复制节点", "Ctrl+C"),
        new(EditorShortcutAction.PasteNodes, "粘贴节点", "Ctrl+V"),
        new(EditorShortcutAction.DeleteNodes, "删除节点", "Delete"),
    ];

    public static Dictionary<string, string> CreateDefaults() =>
        Definitions.ToDictionary(d => d.Action.ToString(), d => d.DefaultGesture, StringComparer.OrdinalIgnoreCase);

    public static void Normalize(IDictionary<string, string>? bindings)
    {
        if (bindings is null) return;
        var defaults = CreateDefaults();
        foreach (var definition in Definitions)
        {
            var key = definition.Action.ToString();
            if (!bindings.TryGetValue(key, out var gesture) || !TryParse(gesture, out _))
                bindings[key] = defaults[key];
        }
    }

    public static bool TryParse(string? text, out ShortcutGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            var parts = text.Trim().Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return false;
            var modifiers = ModifierKeys.None;
            for (int i = 0; i < parts.Length - 1; i++)
                modifiers |= parts[i].ToUpperInvariant() switch { "CTRL" or "CONTROL" => ModifierKeys.Control, "ALT" => ModifierKeys.Alt, "SHIFT" => ModifierKeys.Shift, "WIN" or "WINDOWS" => ModifierKeys.Windows, _ => (ModifierKeys)(-1) };
            if ((int)modifiers < 0) return false;
            var keyText = parts[^1];
            if (keyText == "0") keyText = "D0";
            if (!Enum.TryParse<Key>(keyText, true, out var key) || key == Key.None)
            {
                if (keyText.Length == 1 && char.IsLetterOrDigit(keyText[0]))
                {
                    var upper = keyText.ToUpperInvariant()[0];
                    key = upper switch
                    {
                        >= 'A' and <= 'Z' => (Key)((int)Key.A + upper - 'A'),
                        >= '1' and <= '9' => (Key)((int)Key.D1 + upper - '1'),
                        '0' => Key.D0,
                        _ => Key.None,
                    };
                }
                if (key == Key.None) return false;
            }
            gesture = new ShortcutGesture(key, modifiers);
            return true;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
    }

    public static string Format(ShortcutGesture gesture)
    {
        var prefix = string.Join("+", new[] { gesture.Modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl" : null, gesture.Modifiers.HasFlag(ModifierKeys.Alt) ? "Alt" : null, gesture.Modifiers.HasFlag(ModifierKeys.Shift) ? "Shift" : null, gesture.Modifiers.HasFlag(ModifierKeys.Windows) ? "Win" : null }.Where(value => value is not null));
        var key = gesture.Key == Key.D0 ? "0" : gesture.Key.ToString();
        return string.IsNullOrEmpty(prefix) ? key : $"{prefix}+{key}";
    }

    public static IReadOnlyDictionary<string, string> FindConflicts(IReadOnlyDictionary<string, string> bindings)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var parsed = new Dictionary<string, (string Key, string DisplayName)>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in Definitions)
        {
            if (!bindings.TryGetValue(definition.Action.ToString(), out var text) || string.IsNullOrWhiteSpace(text)) continue;
            if (!TryParse(text, out var gesture) || gesture is null) { result[definition.Action.ToString()] = "快捷键格式无效"; continue; }
            if (IsReserved(gesture) && definition.Action is not (EditorShortcutAction.CopyContent or EditorShortcutAction.PasteContent or EditorShortcutAction.CopyNodes or EditorShortcutAction.PasteNodes or EditorShortcutAction.Undo or EditorShortcutAction.Redo))
            {
                result[definition.Action.ToString()] = "系统或文本编辑保留快捷键";
                continue;
            }
            var normalized = Format(gesture);
            if (parsed.TryGetValue(normalized, out var owner))
            {
                if (IsContextualPair(owner.Key, definition.Action))
                    continue;
                result[definition.Action.ToString()] = $"与“{owner.DisplayName}”冲突";
                result[owner.Key] = $"与“{definition.DisplayName}”冲突";
            }
            else parsed[normalized] = (definition.Action.ToString(), definition.DisplayName);
        }
        return result;
    }

    private static bool IsReserved(ShortcutGesture gesture) =>
        gesture.Key == Key.F4 && gesture.Modifiers.HasFlag(ModifierKeys.Alt)
        || gesture.Key is Key.C or Key.V or Key.X or Key.A or Key.Z or Key.Y
            && gesture.Modifiers.HasFlag(ModifierKeys.Control);

    private static bool IsContextualPair(string ownerKey, EditorShortcutAction action)
    {
        _ = Enum.TryParse<EditorShortcutAction>(ownerKey, true, out var owner);
        return (owner == EditorShortcutAction.CopyContent && action == EditorShortcutAction.CopyNodes)
            || (owner == EditorShortcutAction.CopyNodes && action == EditorShortcutAction.CopyContent)
            || (owner == EditorShortcutAction.PasteContent && action == EditorShortcutAction.PasteNodes)
            || (owner == EditorShortcutAction.PasteNodes && action == EditorShortcutAction.PasteContent)
            || (owner == EditorShortcutAction.DeleteContent && action == EditorShortcutAction.DeleteNodes)
            || (owner == EditorShortcutAction.DeleteNodes && action == EditorShortcutAction.DeleteContent);
    }

    public static bool TryGetAction(Key key, ModifierKeys modifiers, IReadOnlyDictionary<string, string> bindings, out EditorShortcutAction action)
    {
        action = default;
        foreach (var definition in Definitions)
        {
            if (!bindings.TryGetValue(definition.Action.ToString(), out var text) || !TryParse(text, out var gesture) || gesture is null) continue;
            if (gesture.Key == key && gesture.Modifiers == modifiers) { action = definition.Action; return true; }
        }
        return false;
    }
}
