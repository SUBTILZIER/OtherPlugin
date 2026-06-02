using System.Drawing;
using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Adapters;

public interface IMouseAdapter
{
    void MoveTo(Point point);

    void ExecuteButton(MouseButton button, PressReleaseMode mode);

    void ExecuteScroll(ScrollWheelAction action, int speed, int intervalMs, int durationMs, CancellationToken ct);
}

