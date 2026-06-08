namespace AutomationStudioWpf.Services;

/// <summary>
/// UE4 风格的贝塞尔曲线切线计算器
/// 参考 UGraphEditorSettings::ComputeSplineTangent + ConnectionDrawingPolicy::DrawConnection
/// UE4 使用 Cubic Hermite Spline，转换为 WPF Bezier 需要除以 3
/// </summary>
public static class SplineTangentCalculator
{
    /// <summary>
    /// 计算从 Start 到 End 的贝塞尔控制点偏移量（= tangent / 3）
    /// 返回的 offset 直接用于：control1 = start + offset, control2 = end - offset
    /// </summary>
    public static System.Windows.Point ComputeBezierOffset(System.Windows.Point start, System.Windows.Point end, ConnectionSettings settings)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;

        // 判断流向：X 方向决定 forward 还是 backward
        bool goingForward = dx >= 0;

        // 根据方向选择范围和切线向量
        double hRange = goingForward ? settings.ForwardHorizontalDeltaRange : settings.BackwardHorizontalDeltaRange;
        double vRange = goingForward ? settings.ForwardVerticalDeltaRange : settings.BackwardVerticalDeltaRange;
        System.Windows.Point hTangent = goingForward ? settings.ForwardTangentFromHorizontalDelta : settings.BackwardTangentFromHorizontalDelta;
        System.Windows.Point vTangent = goingForward ? settings.ForwardTangentFromVerticalDelta : settings.BackwardTangentFromVerticalDelta;

        // Clamp 水平与垂直分量（同 UE4 ComputeSplineTangent）
        double clampedH = Math.Min(Math.Abs(dx), hRange);
        double clampedV = Math.Min(Math.Abs(dy), vRange);

        // 切线 = 水平分量 * hTangent + 垂直分量 * vTangent（同 UE4）
        double tangentX = clampedH * hTangent.X + clampedV * vTangent.X;
        double tangentY = clampedH * hTangent.Y + clampedV * vTangent.Y;

        // backward 时切线方向取反（同 UE4: P0Tangent = SplineTangent, P1Tangent = -SplineTangent）
        if (!goingForward)
        {
            tangentX = -tangentX;
            tangentY = -tangentY;
        }

        // Cubic Hermite → Cubic Bezier 转换：control offset = tangent / 3
        // 防止绕圈：offset.X 不能超过 |dx|/2，否则 control1 和 control2 会交叉
        double maxOffsetX = Math.Abs(dx) / 2.0;
        double offsetX = tangentX / 3.0;
        double offsetY = tangentY / 3.0;

        //  Clamp X 分量防止绕圈
        if (offsetX > maxOffsetX) offsetX = maxOffsetX;
        if (offsetX < -maxOffsetX) offsetX = -maxOffsetX;

        return new System.Windows.Point(offsetX, offsetY);
    }
}
