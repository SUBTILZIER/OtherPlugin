using System.Windows;

namespace AutomationStudioWpf.Services;

/// <summary>集中处理窗口布局读取、校正与快照，避免 UI 事件直接操作设置模型。</summary>
public sealed class LayoutStateService
{
    public AppSettings Normalize(AppSettings settings)
    {
        var copy = settings.Clone();
        copy.Normalize();
        return copy;
    }

    public void Apply(AppSettings settings, FrameworkElement? owner)
    {
        if (owner is null) return;
        owner.DataContext = settings;
    }
}
