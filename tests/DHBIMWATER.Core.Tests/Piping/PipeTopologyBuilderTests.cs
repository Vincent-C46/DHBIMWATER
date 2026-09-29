using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

public sealed class PipeTopologyBuilderTests
{
    [Fact]
    public void AddSegment_DoesNotBendExistingEdge_WhenOnlyInfiniteExtensionIntersects()
    {
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(-1000, 0), new Point2D(1000, 0));

        network.AddSegment(new Point2D(0, 200), new Point2D(0, 400));

        Assert.Equal(2, network.Edges.Count);
        Assert.Equal(4, network.Nodes.Count);
        Assert.Contains(network.Edges, edge => IsEdge(network, edge, new(-1000, 0), new(1000, 0)));
    }

    [Fact]
    public void AddSegment_DoesNotConnectSeparatedCollinearEdges()
    {
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(0, 0), new Point2D(1000, 0));

        network.AddSegment(new Point2D(1200, 0), new Point2D(1400, 0));

        Assert.Equal(2, network.Edges.Count);
        Assert.Equal(4, network.Nodes.Count);
    }

    [Fact]
    public void AddSegment_SplitsBothEdgesAtActualIntersection()
    {
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(-1000, 0), new Point2D(1000, 0));

        network.AddSegment(new Point2D(0, -1000), new Point2D(0, 1000));

        Assert.Equal(4, network.Edges.Count);
        var intersection = Assert.Single(network.Nodes, x => x.Position.DistanceTo(new Point2D(0, 0)) <= 1e-6);
        Assert.Equal(4, intersection.Degree);
        Assert.Equal(NodeKind.Cross, intersection.NodeKind);
    }

    [Fact]
    public void AddSegment_DoesNotReuseNearbyUnrelatedEndpointForIntersection()
    {
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(50, 50), new Point2D(250, 50));
        network.AddSegment(new Point2D(-1000, 0), new Point2D(1000, 0));

        network.AddSegment(new Point2D(0, -1000), new Point2D(0, 1000));

        var intersection = Assert.Single(network.Nodes, x => x.Position.DistanceTo(new Point2D(0, 0)) <= 1e-6);
        Assert.Equal(new Point2D(0, 0), intersection.Position);
        Assert.Contains(network.Nodes, x => x.Position == new Point2D(50, 50));
    }

    [Fact]
    public void TeeResolver_ReturnsCollinearMainAndOrthogonalBranch()
    {
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(-1000, 0), new Point2D(1000, 0));
        network.AddSegment(new Point2D(0, 0), new Point2D(0, 1000));
        var definition = network.ToDefinition(100, PipeOutputMode.MepPipe, new Point2D(0, 0));
        var tee = Assert.Single(definition.Nodes, x => x.NodeKind == NodeKind.Tee);

        var order = Assert.IsType<PipeTeeLegOrder>(PipeTeeResolver.Resolve(tee, definition.Edges));

        var branch = Assert.Single(definition.Edges, x => x.Id == order.BranchEdgeId);
        Assert.Equal(0, branch.Start.X, 6);
        Assert.Equal(0, branch.End.X, 6);
    }

    [Fact]
    public void TeeResolver_RejectsNonOrthogonalThreeWayBranch()
    {
        // 45° 분기는 docs/187부터 Y형으로 분류된다. T형 판정은 여전히 이 절점을 거부해야 한다.
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(-1000, 0), new Point2D(1000, 0));
        network.AddSegment(new Point2D(0, 0), new Point2D(1000, 1000));
        var definition = network.ToDefinition(100, PipeOutputMode.MepPipe, new Point2D(0, 0));
        var wye = Assert.Single(definition.Nodes, x => x.NodeKind == NodeKind.Wye);

        Assert.Null(PipeTeeResolver.Resolve(wye, definition.Edges));
        Assert.NotNull(PipeWyeResolver.Resolve(wye, definition.Edges));
    }

    [Fact]
    public void TeeResolver_RejectsThreeWayBranchOutsideWyeRange()
    {
        // 20° 분기는 T형도 Y형도 아니다 — Tee로 남아 배치 경계에서 건너뛴다(docs/187 § 1-2).
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(-1000, 0), new Point2D(1000, 0));
        network.AddSegment(new Point2D(0, 0), new Point2D(1000, 364));
        var definition = network.ToDefinition(100, PipeOutputMode.MepPipe, new Point2D(0, 0));
        var tee = Assert.Single(definition.Nodes, x => x.NodeKind == NodeKind.Tee);

        Assert.Null(PipeTeeResolver.Resolve(tee, definition.Edges));
        Assert.Null(PipeWyeResolver.Resolve(tee, definition.Edges));
    }

    // ── 엣지별 관경 · 이동 (docs/152 § C) ────────────────────────────────────────

    [Fact]
    public void AddSegment_KeepsPerEdgeDiameter()
    {
        var network = new PipeNetwork();

        network.AddSegment(new Point2D(0, 0), new Point2D(1000, 0), 100);
        network.AddSegment(new Point2D(0, 500), new Point2D(1000, 500), 200);

        Assert.Equal(100, network.Edges.Single(x => EdgeMidY(network, x) == 0).DiameterMm);
        Assert.Equal(200, network.Edges.Single(x => EdgeMidY(network, x) == 500).DiameterMm);
    }

    [Fact]
    public void AddSegment_SplitPieces_InheritOriginalDiameter()
    {
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(-1000, 0), new Point2D(1000, 0), 200);

        network.AddSegment(new Point2D(0, -1000), new Point2D(0, 1000), 100);

        // 가로선을 쪼갠 두 조각은 원본 200을 유지하고, 새로 그은 세로선 두 조각만 100이다.
        Assert.Equal(4, network.Edges.Count);
        Assert.Equal(2, network.Edges.Count(x => x.DiameterMm == 200));
        Assert.Equal(2, network.Edges.Count(x => x.DiameterMm == 100));
    }

    [Fact]
    public void DeepClone_PreservesEdgeDiameter()
    {
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(0, 0), new Point2D(1000, 0), 350);

        var clone = network.DeepClone();

        Assert.Equal(350, Assert.Single(clone.Edges).DiameterMm);
    }

    [Fact]
    public void SetEdgeDiameter_ChangesOnlyThatEdge()
    {
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(0, 0), new Point2D(1000, 0), 100);
        network.AddSegment(new Point2D(0, 500), new Point2D(1000, 500), 100);
        var target = network.Edges[0];

        network.SetEdgeDiameter(target.Id, 250);

        Assert.Equal(250, network.Edges.Single(x => x.Id == target.Id).DiameterMm);
        Assert.Equal(100, network.Edges.Single(x => x.Id != target.Id).DiameterMm);
    }

    [Fact]
    public void MoveEdges_DetachShared_LeavesOtherEdgeInPlace()
    {
        // ㄴ자: (0,0)-(1000,0) 과 (1000,0)-(1000,1000)이 (1000,0)을 공유한다.
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(0, 0), new Point2D(1000, 0));
        network.AddSegment(new Point2D(1000, 0), new Point2D(1000, 1000));
        var horizontal = network.Edges.First(x => EdgeMidY(network, x) == 0);
        var verticalId = network.Edges.First(x => x.Id != horizontal.Id).Id;

        network.MoveEdges([horizontal.Id], 0, -500);

        // 세로선은 제자리, 가로선만 내려가고 공유 절점이 갈라져 절점이 하나 늘었다.
        var vertical = network.Edges.Single(x => x.Id == verticalId);
        Assert.Contains(network.Nodes, x => x.Position == new Point2D(1000, 0));
        Assert.Contains(network.Nodes, x => x.Position == new Point2D(1000, -500));
        Assert.Equal(0, Math.Min(NodeAt(network, vertical.StartNodeId).Y, NodeAt(network, vertical.EndNodeId).Y));
        Assert.Equal(4, network.Nodes.Count);
    }

    [Fact]
    public void MoveNode_MovesEveryIncidentEdge()
    {
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(0, 0), new Point2D(1000, 0));
        network.AddSegment(new Point2D(1000, 0), new Point2D(1000, 1000));
        var shared = network.Nodes.Single(x => x.Position == new Point2D(1000, 0));

        network.MoveNode(shared.Id, new Point2D(1500, 0));

        // 절점 그립은 분리하지 않는다 — 두 엣지가 모두 따라온다.
        Assert.Equal(3, network.Nodes.Count);
        Assert.Contains(network.Nodes, x => x.Position == new Point2D(1500, 0));
        Assert.DoesNotContain(network.Nodes, x => x.Position == new Point2D(1000, 0));
    }

    [Fact]
    public void MoveNode_MergesOntoCoincidentNode()
    {
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(0, 0), new Point2D(1000, 0));
        network.AddSegment(new Point2D(0, 1000), new Point2D(1000, 1000));
        var loose = network.Nodes.Single(x => x.Position == new Point2D(1000, 1000));

        network.MoveNode(loose.Id, new Point2D(1000, 0));

        // 끝점을 다른 선의 끝점에 갖다 붙이면 절점이 병합되고 차수가 2가 된다.
        Assert.Equal(3, network.Nodes.Count);
        Assert.Equal(2, network.Nodes.Single(x => x.Position == new Point2D(1000, 0)).Degree);
    }

    [Fact]
    public void MoveEdges_SplitsAtNewIntersection()
    {
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(-1000, 0), new Point2D(1000, 0));
        network.AddSegment(new Point2D(0, 500), new Point2D(0, 1500));
        var vertical = network.Edges.First(x => x.Id != network.Edges[0].Id);

        // 세로선을 내려 아래 끝점이 가로선 위에 닿게 한다.
        network.MoveEdges([vertical.Id], 0, -500);

        // 가로선만 둘로 쪼개지고 접점은 3방향 T가 된다.
        Assert.Equal(3, network.Edges.Count);
        Assert.Equal(NodeKind.Tee, network.Nodes.Single(x => x.Position == new Point2D(0, 0)).NodeKind);
    }

    [Fact]
    public void MoveInlineFitting_SlidesAlongOwnEdge()
    {
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(0, 0), new Point2D(2000, 0));
        var edge = Assert.Single(network.Edges);
        network.AddInlineFitting(edge.Id, "valve", "밸브 : DN100", 0.2);
        var fitting = Assert.Single(edge.InlineFittings);

        network.MoveInlineFitting(edge.Id, fitting.Id, 0.75);

        Assert.Equal(0.75, Assert.Single(network.Edges.Single(x => x.Id == edge.Id).InlineFittings).T, 6);
    }

    [Fact]
    public void MoveInlineFitting_KeepsMinimumSpacing()
    {
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(0, 0), new Point2D(2000, 0));
        var edge = Assert.Single(network.Edges);
        network.AddInlineFitting(edge.Id, "valve", "밸브 : DN100", 0.2);
        network.AddInlineFitting(edge.Id, "valve", "밸브 : DN100", 0.8);
        var moving = edge.InlineFittings.Single(x => Math.Abs(x.T - 0.2) < 1e-9);

        network.MoveInlineFitting(edge.Id, moving.Id, 0.8);

        var fittings = network.Edges.Single(x => x.Id == edge.Id).InlineFittings;
        Assert.Equal(2, fittings.Count);
        // 최소 간격은 배치할 때와 같은 규칙(길이 2000mm → 100/2000 = 0.05)이다.
        Assert.True(Math.Abs(fittings[0].T - fittings[1].T) >= 0.05 - 1e-9);
    }

    private static double EdgeMidY(PipeNetwork network, PipeEdge edge)
        => (NodeAt(network, edge.StartNodeId).Y + NodeAt(network, edge.EndNodeId).Y) / 2;

    private static Point2D NodeAt(PipeNetwork network, Guid nodeId)
        => network.Nodes.Single(x => x.Id == nodeId).Position;

    private static bool IsEdge(PipeNetwork network, PipeEdge edge, Point2D expectedStart, Point2D expectedEnd)
    {
        var start = network.Nodes.Single(x => x.Id == edge.StartNodeId).Position;
        var end = network.Nodes.Single(x => x.Id == edge.EndNodeId).Position;
        return start == expectedStart && end == expectedEnd || start == expectedEnd && end == expectedStart;
    }
}

public sealed class PipeTopologyBuilderSnapPriorityTests
{
    [Fact]
    public void FindSnapPoint_PrefersEndpointOverCloserIntersection()
    {
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(-1000, 0), new Point2D(1000, 0));
        network.AddSegment(new Point2D(0, 0), new Point2D(0, 1000));
        network.AddSegment(new Point2D(90, 10), new Point2D(90, 1000));

        var result = network.FindSnapPoint(new Point2D(10, 0), PipeSnapMode.Endpoint | PipeSnapMode.Intersection);

        Assert.NotNull(result);
        Assert.Equal(PipeSnapKind.Endpoint, result.Value.Kind);
        Assert.Equal(new Point2D(90, 10), result.Value.Point);
    }

    [Fact]
    public void FindSnapPoint_PrefersEndpointOverCloserNearestPoint()
    {
        var network = SingleHorizontalEdge();
        var cursor = new Point2D(Math.Sqrt(2700), 30);

        var result = network.FindSnapPoint(cursor, PipeSnapMode.Endpoint | PipeSnapMode.Nearest);

        Assert.NotNull(result);
        Assert.Equal(PipeSnapKind.Endpoint, result.Value.Kind);
        Assert.Equal(new Point2D(0, 0), result.Value.Point);
        Assert.Equal(60, result.Value.Distance, 6);
    }

    [Fact]
    public void FindSnapPoint_FallsBackToNearestWhenEndpointIsOutsideTolerance()
    {
        var network = SingleHorizontalEdge();

        var result = network.FindSnapPoint(new Point2D(150, 30), PipeSnapMode.Endpoint | PipeSnapMode.Nearest);

        Assert.NotNull(result);
        Assert.Equal(PipeSnapKind.Nearest, result.Value.Kind);
        Assert.Equal(new Point2D(150, 0), result.Value.Point);
    }

    [Fact]
    public void FindSnapPoint_ClassifiesDegreeThreeNodeAsIntersectionInEndpointMode()
    {
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(-1000, 0), new Point2D(1000, 0));
        network.AddSegment(new Point2D(0, 0), new Point2D(0, 1000));

        var result = network.FindSnapPoint(new Point2D(20, 20), PipeSnapMode.Endpoint);

        Assert.NotNull(result);
        Assert.Equal(PipeSnapKind.Intersection, result.Value.Kind);
        Assert.Equal(new Point2D(0, 0), result.Value.Point);
    }

    [Fact]
    public void FindSnapPoint_PrefersMidpointOverNearestPoint()
    {
        var network = SingleHorizontalEdge();

        var result = network.FindSnapPoint(new Point2D(500, 30), PipeSnapMode.Midpoint | PipeSnapMode.Nearest);

        Assert.NotNull(result);
        Assert.Equal(PipeSnapKind.Midpoint, result.Value.Kind);
        Assert.Equal(new Point2D(500, 0), result.Value.Point);
    }

    [Fact]
    public void FindSnapPoint_PrefersQuadrantOverNearestPoint()
    {
        var network = SingleHorizontalEdge();

        var result = network.FindSnapPoint(new Point2D(250, 30), PipeSnapMode.Quadrant | PipeSnapMode.Nearest);

        Assert.NotNull(result);
        Assert.Equal(PipeSnapKind.Quadrant, result.Value.Kind);
        Assert.Equal(new Point2D(250, 0), result.Value.Point);
    }

    [Fact]
    public void FindSnapPoint_ReturnsNullWhenEveryCandidateIsOutsideTolerance()
    {
        var network = SingleHorizontalEdge();

        var result = network.FindSnapPoint(new Point2D(500, 200),
            PipeSnapMode.Endpoint | PipeSnapMode.Intersection | PipeSnapMode.Midpoint | PipeSnapMode.Quadrant | PipeSnapMode.Nearest);

        Assert.Null(result);
    }

    private static PipeNetwork SingleHorizontalEdge()
    {
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(0, 0), new Point2D(1000, 0));
        return network;
    }
}
