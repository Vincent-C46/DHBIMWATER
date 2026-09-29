using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>수동 선 분할(docs/179 § H-1·H-2). 교차 정리와 같은 <c>SplitEdgeAt</c>를 쓰므로
/// 관경 상속·부속 재분배 규칙이 저절로 같다는 것을 여기서 못박는다.</summary>
public sealed class PipeNetworkSplitTests
{
    private const string ValveTypeKey = "배관 밸브류";

    private static (PipeNetwork Network, PipeEdge Edge) Line(double lengthMm = 1000, double diameterMm = 200)
    {
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(0, 0), new Point2D(lengthMm, 0), diameterMm);
        return (network, network.Edges.Single());
    }

    [Fact]
    public void SplitSegment_MakesTwoEdges_InheritingDiameter()
    {
        var (network, edge) = Line(diameterMm: 250);

        var split = network.SplitSegment(edge.Id, 0.5);

        Assert.NotNull(split);
        Assert.Equal(2, network.Edges.Count);
        Assert.All(network.Edges, x => Assert.Equal(250, x.DiameterMm));
        Assert.Contains(network.Edges, x => x.Id == split!.Value.First);
        Assert.Contains(network.Edges, x => x.Id == split!.Value.Second);
    }

    [Fact]
    public void SplitSegment_MiddleNode_IsInline_SoNoElbowIsPlaced()
    {
        // 배치 경계(RevitPipeSegmentCommandRepo)는 Elbow/Tee/Cross가 아니면 절점 부속을 건너뛴다.
        // 분할 자리에 곡관이 생기지 않는 근거가 이 분류다(docs/179 § H-0).
        var (network, edge) = Line();

        network.SplitSegment(edge.Id, 0.5);

        var middle = network.Nodes.Single(x => x.Position.DistanceTo(new Point2D(500, 0)) < 1);
        Assert.Equal(NodeKind.Inline, middle.NodeKind);
    }

    [Fact]
    public void SplitSegment_DistributesInlineFittings_AndRescalesT()
    {
        var (network, edge) = Line();
        network.AddInlineFitting(edge.Id, ValveTypeKey, "밸브 : DN200", 0.25);
        network.AddInlineFitting(edge.Id, ValveTypeKey, "밸브 : DN200", 0.75);

        var split = network.SplitSegment(edge.Id, 0.5);

        var first = network.Edges.Single(x => x.Id == split!.Value.First);
        var second = network.Edges.Single(x => x.Id == split!.Value.Second);
        // 0.25 / 0.5 = 0.5, (0.75 - 0.5) / 0.5 = 0.5 — 각 조각 안에서 다시 잰 위치다.
        Assert.Equal(0.5, Assert.Single(first.InlineFittings).T, 6);
        Assert.Equal(0.5, Assert.Single(second.InlineFittings).T, 6);
        Assert.All(network.Edges, x => Assert.All(x.InlineFittings, f => Assert.Equal(0, f.Order)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(0.00005)]
    [InlineData(0.99995)]
    public void SplitSegment_AtOrNearEndpoint_DoesNothing(double t)
    {
        var (network, edge) = Line();

        var split = network.SplitSegment(edge.Id, t);

        Assert.Null(split);
        Assert.Single(network.Edges);
        // 실패했으면 고아 절점도 남지 않아야 한다.
        Assert.Equal(2, network.Nodes.Count);
    }

    [Fact]
    public void SplitSegment_DoesNotDisturbOtherEdges()
    {
        // NormalizeIntersections를 부르지 않는다는 보증이다 — 떨어져 있는 선은 그대로여야 한다(docs/179 § H-2).
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(0, 0), new Point2D(1000, 0), 200);
        network.AddSegment(new Point2D(0, 5000), new Point2D(1000, 5000), 300);
        var target = network.Edges.Single(x => Math.Abs(network.Nodes.First(n => n.Id == x.StartNodeId).Position.Y) < 1);

        network.SplitSegment(target.Id, 0.5);

        Assert.Equal(3, network.Edges.Count);
        Assert.Single(network.Edges, x => x.DiameterMm == 300);
    }

    [Fact]
    public void SplitSegment_UnknownEdge_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new PipeNetwork().SplitSegment(Guid.NewGuid(), 0.5));

    // ── detach: 두 조각이 각자 끝점을 갖는 분할(docs/181 § B) ────────────────────

    /// <summary>분할점에 같은 좌표의 절점이 2개 생기고, 둘 다 degree 1이라 끝점으로 분류된다.</summary>
    [Fact]
    public void SplitSegment_Detached_MakesTwoCoincidentEndPoints()
    {
        var (network, edge) = Line();

        network.SplitSegment(edge.Id, 0.5, detach: true);

        var middle = network.Nodes.Where(x => x.Position.DistanceTo(new Point2D(500, 0)) < 1).ToList();
        Assert.Equal(2, middle.Count);
        Assert.All(middle, x => Assert.Equal(1, x.Degree));
        Assert.All(middle, x => Assert.Equal(NodeKind.EndPoint, x.NodeKind));
        Assert.Equal(4, network.Nodes.Count);   // 원래 끝점 2 + 갈라진 끝점 2
    }

    /// <summary>끝점 하나를 끌면 그 조각만 움직인다 — 이 기능의 핵심이다.
    /// 공유 절점이던 종전에는 두 조각이 함께 따라왔다.</summary>
    [Fact]
    public void SplitSegment_Detached_MovingOneEndPointLeavesTheOtherPiece()
    {
        var (network, edge) = Line();
        var split = network.SplitSegment(edge.Id, 0.5, detach: true);
        var second = network.Edges.Single(x => x.Id == split!.Value.Second);
        var movable = network.Nodes.Single(x =>
            x.Position.DistanceTo(new Point2D(500, 0)) < 1 && (x.Id == second.StartNodeId || x.Id == second.EndNodeId));

        network.MoveNode(movable.Id, new Point2D(500, 400));

        // 앞 조각의 끝점은 제자리에 남는다.
        Assert.Contains(network.Nodes, x => x.Position.DistanceTo(new Point2D(500, 0)) < 1);
        Assert.Contains(network.Nodes, x => x.Position.DistanceTo(new Point2D(500, 400)) < 1);
        Assert.Equal(2, network.Edges.Count);
    }

    /// <summary>떼어 놓은 끝점을 상대 끝점 자리로 되돌리면 <c>MergeCoincidentNodes</c>가 다시 하나로 합친다
    /// (기존 동작). 잘못 끈 것을 되돌리는 경로가 된다.</summary>
    [Fact]
    public void SplitSegment_Detached_DraggingBackOntoTheOtherEndMergesThem()
    {
        var (network, edge) = Line();
        var split = network.SplitSegment(edge.Id, 0.5, detach: true);
        var second = network.Edges.Single(x => x.Id == split!.Value.Second);
        var movable = network.Nodes.Single(x =>
            x.Position.DistanceTo(new Point2D(500, 0)) < 1 && (x.Id == second.StartNodeId || x.Id == second.EndNodeId));

        network.MoveNode(movable.Id, new Point2D(500, 400));
        network.MoveNode(movable.Id, new Point2D(500, 0));

        Assert.Single(network.Nodes, x => x.Position.DistanceTo(new Point2D(500, 0)) < 1);
        Assert.Equal(3, network.Nodes.Count);
    }

    /// <summary>회귀 — 기본값은 종전대로 절점 하나를 공유한다.
    /// 교차 정리(<c>SplitFirstIntersection</c>)가 이 경로를 쓰므로 진짜 교차점은 연결된 채로 있어야 한다.</summary>
    [Fact]
    public void SplitSegment_WithoutDetach_KeepsOneSharedNode()
    {
        var (network, edge) = Line();

        network.SplitSegment(edge.Id, 0.5);

        Assert.Single(network.Nodes, x => x.Position.DistanceTo(new Point2D(500, 0)) < 1);
        Assert.Equal(3, network.Nodes.Count);
    }
}
