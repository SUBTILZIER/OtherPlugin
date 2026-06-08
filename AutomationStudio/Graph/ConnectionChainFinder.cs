using System.Collections.ObjectModel;
using AutomationStudioWpf.Services;
using Point = System.Windows.Point;

namespace AutomationStudioWpf.Graph;

/// <summary>
/// 追踪连线路径，从链的起点到终点，经过所有路由节点，构建完整的路径点链。
/// 链的顺序按照连接拓扑关系确定：从非路由节点的输出引脚开始，沿着连接链向前追踪。
/// </summary>
public static class ConnectionChainFinder
{
    /// <summary>
    /// 给定一个连接，尝试构建它所属的完整连线路径链。
    /// 先回溯找到链的起点，再向前追踪到终点。
    /// 如果连线不经过路由节点，返回 null。
    /// </summary>
    public static ConnectionChain? TryBuildChain(
        PinViewModel sourcePin,
        PinViewModel targetPin,
        IReadOnlyList<NodeBaseViewModel> nodes,
        IReadOnlyList<ConnectionViewModel> connections,
        ConnectionSettings settings)
    {
        // 第一步：找到链的起点（回溯）
        var (chainStartPin, chainEndPin, allConnectionsInChain) = FindFullChain(
            sourcePin, targetPin, connections);

        if (allConnectionsInChain.Count < 2)
            return null;  // 只有单段连接，不需要链渲染

        // 第二步：从起点构建路径点序列
        var waypoints = BuildWaypoints(chainStartPin, allConnectionsInChain);

        if (waypoints.Count < 2)
            return null;

        return new ConnectionChain(waypoints.AsReadOnly(), allConnectionsInChain.AsReadOnly(), settings);
    }

    /// <summary>
    /// 从给定连接出发，回溯找到链起点，向前找到链终点，返回完整的链信息。
    /// </summary>
    private static (PinViewModel startPin, PinViewModel endPin, List<ConnectionViewModel> connections)
        FindFullChain(
            PinViewModel sourcePin,
            PinViewModel targetPin,
            IReadOnlyList<ConnectionViewModel> connections)
    {
        var chainConns = new List<ConnectionViewModel>();

        // 向前回溯：找到链的真正起点
        var currentSource = sourcePin;
        while (true)
        {
            var incomingConn = FindIncomingConnection(currentSource, connections);
            if (incomingConn is null)
                break;
            currentSource = incomingConn.SourcePin;
        }

        // 向前追踪：从起点到终点
        var visited = new HashSet<PinViewModel>();
        var startPin = currentSource;

        // 从起点的输出引脚开始，找到第一条连接
        var currentTarget = FindOutgoingConnectionTarget(currentSource, connections);
        if (currentTarget is null)
            return (startPin, targetPin, chainConns);

        currentSource = startPin;

        while (currentTarget is not null && !visited.Contains(currentTarget))
        {
            visited.Add(currentTarget);

            var conn = FindConnection(connections, currentSource, currentTarget);
            if (conn is null)
                break;

            chainConns.Add(conn);

            var targetOwner = currentTarget.Owner;
            if (targetOwner is RerouteNodeViewModel reroute)
            {
                // 路由节点的输出引脚
                var rerouteOutPin = reroute.OutputPins.FirstOrDefault(p => p.Name == "out");
                if (rerouteOutPin is null)
                    break;

                currentSource = rerouteOutPin;
                currentTarget = FindOutgoingConnectionTarget(rerouteOutPin, connections);
            }
            else
            {
                // 普通节点，到达终点
                break;
            }
        }

        var endPin = currentTarget ?? targetPin;
        return (startPin, endPin, chainConns);
    }

    /// <summary>查找连接到给定引脚的输入连接</summary>
    private static ConnectionViewModel? FindIncomingConnection(
        PinViewModel pin,
        IReadOnlyList<ConnectionViewModel> connections)
    {
        if (pin.Direction != PinDirection.Input)
            return null;

        return connections.FirstOrDefault(c => c.TargetPin == pin);
    }

    /// <summary>查找从给定引脚出发的连接的目标引脚</summary>
    private static PinViewModel? FindOutgoingConnectionTarget(
        PinViewModel pin,
        IReadOnlyList<ConnectionViewModel> connections)
    {
        if (pin.Direction != PinDirection.Output)
            return null;

        var conn = connections.FirstOrDefault(c => c.SourcePin == pin);
        return conn?.TargetPin;
    }

    /// <summary>从路径点序列构建路径点列表</summary>
    private static List<Point> BuildWaypoints(
        PinViewModel startPin,
        List<ConnectionViewModel> chainConnections)
    {
        var waypoints = new List<Point>();

        // 起点：源引脚锚点
        waypoints.Add(GetAbsolutePinAnchor(startPin));

        // 中间点：每个路由节点的中心
        foreach (var conn in chainConnections)
        {
            var targetOwner = conn.TargetPin.Owner;
            if (targetOwner is RerouteNodeViewModel reroute)
            {
                // 路由节点中心
                waypoints.Add(new Point(
                    reroute.X + reroute.Width / 2.0,
                    reroute.Y + reroute.Height / 2.0));
            }
            else
            {
                // 终点：目标引脚锚点
                waypoints.Add(GetAbsolutePinAnchor(conn.TargetPin));
            }
        }

        return waypoints;
    }

    private static ConnectionViewModel? FindConnection(
        IReadOnlyList<ConnectionViewModel> connections,
        PinViewModel sourcePin,
        PinViewModel targetPin)
    {
        return connections.FirstOrDefault(c => c.SourcePin == sourcePin && c.TargetPin == targetPin);
    }

    private static Point GetAbsolutePinAnchor(PinViewModel pin)
    {
        var anchor = pin.Owner.GetPinAnchor(pin);
        return new Point(pin.Owner.X + anchor.X, pin.Owner.Y + anchor.Y);
    }
}
