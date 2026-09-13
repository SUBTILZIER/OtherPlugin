using System.Windows;

namespace AutomationStudioWpf.Services;

/// <summary>集中处理窗口布局读取、校正与快照，避免 UI 事件直接操作设置模型。</summary>
public sealed class LayoutStateService
{
    public readonly record struct LayoutSnapshot(double Sidebar, double Inspector, double LogHeight, double ContentTree);
    public double ClampSize(double value, double minimum, double maximum, double fallback)
    {
        return !double.IsFinite(value) || value <= 0 ? fallback : Math.Clamp(value, minimum, maximum);
    }

    public AppSettings Normalize(AppSettings settings)
    {
        var copy = settings.Clone();
        copy.Normalize();
        return copy;
    }

    public LayoutSnapshot ReadSnapshot(double sidebar, double inspector, double logHeight, double contentTree) =>
        new(ClampSize(sidebar, 180, 420, 224), ClampSize(inspector, 420, 720, 420), ClampSize(logHeight, 180, 2000, 280), ClampSize(contentTree, 120, 420, 180));

    public void ApplySnapshot(AppSettings settings, Action<double, double, double, double> apply)
    {
        var snapshot = ReadSnapshot(settings.GraphSidebarWidth, settings.InspectorWidth, settings.LogPanelHeight, settings.ContentTreeWidth);
        apply(snapshot.Sidebar, snapshot.Inspector, snapshot.LogHeight, snapshot.ContentTree);
    }

    public void UpdateSettings(AppSettings settings, LayoutSnapshot snapshot)
    {
        settings.GraphSidebarWidth = snapshot.Sidebar;
        settings.InspectorWidth = snapshot.Inspector;
        settings.LogPanelHeight = snapshot.LogHeight;
        settings.ContentTreeWidth = snapshot.ContentTree;
    }

    public void Apply(AppSettings settings, FrameworkElement? owner)
    {
        if (owner is null) return;
        owner.DataContext = settings;
    }
}
