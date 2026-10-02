using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>Y형(경사 분기) 판정과 규격·부재명 표기(docs/187 § 1·2).</summary>
public sealed class PipeWyeResolverTests
{
    [Fact]
    public void Resolve_FortyFiveDegreeBranch_ReturnsAcuteMainFirst()
    {
        // 주관은 ±X 일직선, 분기는 +45°다. 예각 쪽 주관은 +X 레그다.
        var (node, edges) = Wye(P(1, 0), P(-1, 0), Dir(45));

        var order = PipeWyeResolver.Resolve(node, edges);

        Assert.NotNull(order);
        Assert.Equal(45, order!.BranchAngleDeg, 3);
        Assert.Equal(edges[0].Id, order.MainEdge1Id);
        Assert.Equal(edges[1].Id, order.MainEdge2Id);
        Assert.Equal(edges[2].Id, order.BranchEdgeId);
    }

    [Fact]
    public void Resolve_OrthogonalBranch_ReturnsNull()
    {
        // 직교 분기는 T형이다 — Y로 판정하면 안 된다.
        var (node, edges) = Wye(P(1, 0), P(-1, 0), P(0, 1));
        Assert.Null(PipeWyeResolver.Resolve(node, edges));
    }

    [Theory]
    [InlineData(20)]
    [InlineData(70)]
    public void Resolve_BranchAngleOutOfRange_ReturnsNull(double branchAngleDeg)
    {
        var (node, edges) = Wye(P(1, 0), P(-1, 0), Dir(branchAngleDeg));
        Assert.Null(PipeWyeResolver.Resolve(node, edges));
    }

    [Fact]
    public void Resolve_MainsNotCollinear_ReturnsNull()
    {
        var (node, edges) = Wye(P(1, 0), Dir(170), Dir(45));
        Assert.Null(PipeWyeResolver.Resolve(node, edges));
    }

    [Fact]
    public void ClassifyNodes_FortyFiveDegreeBranch_IsWye()
    {
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(-1000, 0), new Point2D(1000, 0));
        network.AddSegment(new Point2D(0, 0), new Point2D(1000, 1000));

        var node = network.Nodes.Single(x => x.Degree == 3);
        Assert.Equal(NodeKind.Wye, node.NodeKind);
    }

    [Fact]
    public void ClassifyNodes_OrthogonalBranch_StaysTee()
    {
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(-1000, 0), new Point2D(1000, 0));
        network.AddSegment(new Point2D(0, 0), new Point2D(0, 1000));

        var node = network.Nodes.Single(x => x.Degree == 3);
        Assert.Equal(NodeKind.Tee, node.NodeKind);
    }

    [Fact]
    public void ClassifyNodes_BranchAngleOutOfRange_StaysTee()
    {
        // 20°는 Y 범위 밖이다 — 종전대로 Tee로 남아 배치 경계에서 건너뛴다.
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(-1000, 0), new Point2D(1000, 0));
        network.AddSegment(new Point2D(0, 0), new Point2D(1000, 364));

        var node = network.Nodes.Single(x => x.Degree == 3);
        Assert.Equal(NodeKind.Tee, node.NodeKind);
    }

    [Fact]
    public void BranchAngleDegFromDots_MatchesLegAngle()
    {
        // 분기 바깥방향과 주관 두 바깥방향의 내적. 45°/135° 중 작은 쪽이 분기각이다.
        var branch = Dir(45);
        var mainA = P(1, 0);
        var mainB = P(-1, 0);

        var angle = PipeWyeResolver.BranchAngleDegFromDots(Dot(branch, mainA), Dot(branch, mainB));

        Assert.Equal(45, angle, 3);
        Assert.True(PipeWyeResolver.IsWyeBranchAngle(angle));
    }

    [Fact]
    public void SpecText_WyeAddsBranchAngleAndHasThreeEnds()
    {
        Assert.Equal("D300x300 45°", PipeMaterialSpecText.Wye(300, 300, 45));
        Assert.Equal(3, PipeMaterialSpecText.EndCountFor(PipeMaterialSpecText.WyeName));
        Assert.Equal("3F Y형", PipeMaterialSpecText.WithJointMark(PipeMaterialSpecText.WyeName, DHBIMWATER.Core.Piping.BendConnection.Flanged));
    }

    private static Point2D P(double x, double y) => new(x, y);

    private static Point2D Dir(double degrees)
        => new(Math.Cos(degrees * Math.PI / 180), Math.Sin(degrees * Math.PI / 180));

    private static double Dot(Point2D a, Point2D b) => a.X * b.X + a.Y * b.Y;

    private static PipeEdgeDefinition Edge(Point2D start, Point2D end)
        => new(Guid.NewGuid(), start, end, 0, 100);

    private static (PipeNodeDefinition Node, IReadOnlyList<PipeEdgeDefinition> Edges) Wye(params Point2D[] directions)
    {
        var node = new PipeNodeDefinition(Guid.NewGuid(), P(0, 0), NodeKind.Wye);
        var edges = directions.Select(d => Edge(P(0, 0), P(d.X * 1000, d.Y * 1000))).ToList();
        return (node, edges);
    }
}
