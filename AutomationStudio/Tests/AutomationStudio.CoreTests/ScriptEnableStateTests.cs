using System.Windows;
using System.Windows.Controls;
using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Services;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class ScriptEnableStateTests
{
    [STATestMethod]
    public void ExternalChangesRefreshInPlaceWithoutOverwritingDraft()
    {
        using var fixture = new WorkbenchFixture();
        int requests = 0;
        var summary = fixture.Create(_ => { requests++; return false; });
        summary.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var root = summary.Content;
        var checkBox = EnableCheckBox(summary);
        var status = StatusText(summary);
        var draftField = Descendants<TextBox>(summary).First();
        draftField.Text = "123";
        draftField.CaretIndex = 2;

        fixture.Asset.IsScriptEnabled = false;

        Assert.IsFalse(checkBox.IsChecked == true);
        Assert.AreEqual("已停用", status.Text);
        Assert.AreEqual(fixture.Asset.ScriptEnabledToolTip, checkBox.ToolTip);
        Assert.AreEqual("123", draftField.Text);
        Assert.AreEqual(2, draftField.CaretIndex);
        Assert.AreSame(root, summary.Content);
        Assert.AreEqual(0, requests);

        summary.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        fixture.Asset.IsScriptEnabled = true;
        Assert.IsFalse(checkBox.IsChecked == true, "Unloaded controls must detach from the asset.");
        summary.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Assert.IsTrue(checkBox.IsChecked == true);
        Assert.AreEqual("已启用", status.Text);
        Assert.AreEqual("123", draftField.Text);
    }

    [STATestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void RejectedChangeRestoresRealStateWithoutReenteringCallback(bool initiallyEnabled)
    {
        using var fixture = new WorkbenchFixture();
        fixture.Asset.IsScriptEnabled = initiallyEnabled;
        int requests = 0;
        var summary = fixture.Create(requested =>
        {
            requests++;
            Assert.AreNotEqual(initiallyEnabled, requested);
            Assert.AreEqual(initiallyEnabled, fixture.Asset.IsScriptEnabled);
            return false;
        });
        summary.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var checkBox = EnableCheckBox(summary);
        checkBox.IsChecked = !initiallyEnabled;

        Assert.AreEqual(initiallyEnabled, checkBox.IsChecked);
        Assert.AreEqual(initiallyEnabled, fixture.Asset.IsScriptEnabled);
        Assert.AreEqual(initiallyEnabled ? "已启用" : "已停用", StatusText(summary).Text);
        Assert.AreEqual(1, requests);
    }

    [STATestMethod]
    public void WorkbenchRequestsGoThroughCallbackAndSynchronizeOtherViews()
    {
        using var fixture = new WorkbenchFixture();
        int requests = 0;
        bool ChangeEnabled(bool enabled)
        {
            requests++;
            Assert.AreNotEqual(enabled, fixture.Asset.IsScriptEnabled);
            fixture.Asset.IsScriptEnabled = enabled;
            return true;
        }

        var overview = fixture.Create(ChangeEnabled);
        var inspector = fixture.Create(ChangeEnabled);
        overview.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        overview.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        inspector.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

        EnableCheckBox(overview).IsChecked = false;
        Assert.IsFalse(fixture.Asset.IsScriptEnabled);
        Assert.IsFalse(EnableCheckBox(inspector).IsChecked == true);
        Assert.AreEqual("已停用", StatusText(inspector).Text);

        EnableCheckBox(inspector).IsChecked = true;
        Assert.IsTrue(EnableCheckBox(overview).IsChecked == true);
        Assert.AreEqual("已启用", StatusText(overview).Text);
        Assert.AreEqual(2, requests);
    }

    private static CheckBox EnableCheckBox(ScriptPropertiesSummaryControl summary) =>
        Descendants<CheckBox>(summary).Single(control => Equals(control.Content, "启用脚本"));

    private static TextBlock StatusText(ScriptPropertiesSummaryControl summary) =>
        Descendants<TextBlock>(summary).Single(control => control.Text is "已启用" or "已停用");

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    private sealed class WorkbenchFixture : IDisposable
    {
        private readonly Window _owner = new();
        private readonly ScriptHotkeyService _hotkeys;
        private readonly List<ScriptPropertiesSummaryControl> _summaries = [];
        public ContentAssetViewModel Asset { get; } = new() { Name = "EnableSync" };

        public WorkbenchFixture() => _hotkeys = new ScriptHotkeyService(_owner, _ => { });

        public ScriptPropertiesSummaryControl Create(Func<bool, bool> changeEnabled)
        {
            var capture = new HotkeyCaptureCoordinator(_owner, _hotkeys, () => false, _ => { });
            var summary = new ScriptPropertiesSummaryControl(Asset, (_, _) => true, capture, changeEnabled);
            _summaries.Add(summary);
            return summary;
        }

        public void Dispose()
        {
            foreach (var summary in _summaries)
                summary.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            _hotkeys.Dispose();
            _owner.Close();
        }
    }
}
