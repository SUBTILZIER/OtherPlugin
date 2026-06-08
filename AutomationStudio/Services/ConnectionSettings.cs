namespace AutomationStudioWpf.Services;

/// <summary>
/// 连线贝塞尔曲线的配置参数，参考 UE4 的 UGraphEditorSettings
/// </summary>
public sealed class ConnectionSettings
{
    // Forward (左→右) 参数
    public double ForwardHorizontalDeltaRange { get; set; } = 1000.0;
    public double ForwardVerticalDeltaRange { get; set; } = 1000.0;
    public System.Windows.Point ForwardTangentFromHorizontalDelta { get; set; } = new(1.0, 0.0);
    public System.Windows.Point ForwardTangentFromVerticalDelta { get; set; } = new(1.0, 0.0);

    // Backward (右→左) 参数
    public double BackwardHorizontalDeltaRange { get; set; } = 200.0;
    public double BackwardVerticalDeltaRange { get; set; } = 200.0;
    public System.Windows.Point BackwardTangentFromHorizontalDelta { get; set; } = new(2.0, 0.0);
    public System.Windows.Point BackwardTangentFromVerticalDelta { get; set; } = new(1.5, 0.0);
}
