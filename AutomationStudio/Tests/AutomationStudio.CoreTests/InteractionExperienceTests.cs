using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Services;
using AutomationStudioWpf.Controls;
using System.Windows.Input;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class InteractionExperienceTests
{
    [TestMethod]
    public void AppSettingsClonePreservesInspectorSectionStatesAndNormalizeHandlesNull()
    {
        var settings = new AppSettings { InspectorSectionStates = null! };
        settings.Normalize();
        Assert.IsNotNull(settings.InspectorSectionStates);

        settings.InspectorSectionStates["common:参数"] = false;
        var clone = settings.Clone();
        clone.InspectorSectionStates["common:参数"] = true;

        Assert.IsFalse(settings.InspectorSectionStates["common:参数"]);
        Assert.IsTrue(clone.InspectorSectionStates["common:参数"]);
    }

    [TestMethod]
    public void InspectorSectionStateUsesStableKeyAndPersistsChanges()
    {
        var saved = new Dictionary<string, bool>();
        var inspector = new InspectorViewModel();
        inspector.ConfigureSectionStateStore(
            key => saved.TryGetValue(key, out var value) ? value : null,
            (key, value) => saved[key] = value);
        inspector.Reset(null);

        var section = inspector.AddSection("高级");
        Assert.AreEqual("default:高级", section.SchemaKey);
        Assert.IsFalse(section.IsExpanded);

        section.IsExpanded = true;
        Assert.IsTrue(saved["default:高级"]);
    }

    [TestMethod]
    public void PaletteSelectionMovesAndWrapsWithoutOutOfRangeIndex()
    {
        Assert.AreEqual(1, NodePaletteController.MoveSelection(0, 3, Key.Down));
        Assert.AreEqual(2, NodePaletteController.MoveSelection(0, 3, Key.Up));
        Assert.AreEqual(2, NodePaletteController.MoveSelection(0, 3, Key.PageDown));
        Assert.AreEqual(0, NodePaletteController.MoveSelection(2, 3, Key.PageUp));
        Assert.AreEqual(-1, NodePaletteController.MoveSelection(0, 0, Key.Enter));
    }

    [TestMethod]
    public void NumericInputRejectsNonFiniteValues()
    {
        Assert.IsTrue(NumericUpDown.TryParseFinite("12.5", out var value));
        Assert.AreEqual(12.5, value, 0.0001);
        Assert.IsFalse(NumericUpDown.TryParseFinite("NaN", out _));
        Assert.IsFalse(NumericUpDown.TryParseFinite("Infinity", out _));
        Assert.IsFalse(NumericUpDown.TryParseFinite("abc", out _));
    }

    [TestMethod]
    public void FocusCommandsRequireControlModifier()
    {
        var router = new EditorCommandRouter();
        Assert.IsFalse(router.TryGetFocusTarget(Key.D1, ModifierKeys.None, out _));
        Assert.IsTrue(router.TryGetFocusTarget(Key.D1, ModifierKeys.Control, out var target));
        Assert.AreEqual(EditorCommandRouter.FocusTarget.Inspector, target);
    }

    [TestMethod]
    public void ShortcutBindingsNormalizeAndDetectConflicts()
    {
        var settings = new AppSettings { EditorShortcuts = new Dictionary<string, string> { [EditorShortcutAction.SearchContent.ToString()] = "bad" } };
        settings.Normalize();
        Assert.AreEqual("Ctrl+F", settings.EditorShortcuts[EditorShortcutAction.SearchContent.ToString()]);
        settings.EditorShortcuts[EditorShortcutAction.FitGraph.ToString()] = "Ctrl+F";
        var conflicts = ShortcutBindingService.FindConflicts(settings.EditorShortcuts);
        Assert.IsTrue(conflicts.ContainsKey(EditorShortcutAction.SearchContent.ToString()));
        Assert.IsTrue(conflicts.ContainsKey(EditorShortcutAction.FitGraph.ToString()));
    }

}
