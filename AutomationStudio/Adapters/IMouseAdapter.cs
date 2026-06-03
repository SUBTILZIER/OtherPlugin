using System.Drawing;
using AutomationStudioWpf.Graph;

namespace AutomationStudioWpf.Adapters;

public interface IMouseAdapter
{
    void MoveTo(Point point);

    void ExecuteButton(MouseButton button, PressReleaseMode mode);

    void DoubleClick(MouseButton button);

    Point GetPosition();

    void ExecuteScroll(ScrollWheelAction action, int speed, int intervalMs, int durationMs, CancellationToken ct);
}
