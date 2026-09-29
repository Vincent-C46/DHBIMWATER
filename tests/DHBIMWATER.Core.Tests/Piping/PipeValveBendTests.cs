using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>꺾인 절점 곡관 ↔ 밸브부관 전환(docs/225 § A · § E).</summary>
public sealed class PipeValveBendTests
{
    private const string ValveTypeKey = "배관 밸브류";

    [Fact]
    public void SetNodeFittingKind_OnlyAppliesToElbow()
    {
        var network = Corner();
        var endPoint = network.Nodes.First(x => x.NodeKind == NodeKind.EndPoint);

        Assert.False(network.SetNodeFittingKind(endPoint.Id, PipeNodeFittingKind.ValveBend));
        Assert.True(network.SetNodeFittingKind(CornerNode(network).Id, PipeNodeFittingKind.ValveBend));
        Assert.Equal(PipeNodeFittingKind.ValveBend, CornerNode(network).FittingKind);
    }

    [Fact]
    public void ValveSide_IsLegCarryingTheOnlyValve()
    {
        var network = Corner();
        var vertical = EdgeTo(network, P(1000, 1000));
        network.AddInlineFitting(vertical.Id, ValveTypeKey, "게이트밸브 : DN100", 0.5);
        network.SetNodeFittingKind(CornerNode(network).Id, PipeNodeFittingKind.ValveBend);

        var definition = network.ToDefinition(100, PipeOutputMode.PipeAccessorySegment, P(0, 0));
        var node = definition.Nodes.Single(x => x.IsValveBend);

        Assert.Equal(EdgeTo(network, P(1000, 1000)).Id, node.ValveSideEdgeId);
        AssertDirection(P(0, 1), network.ResolveValveSideDirection(node.Id));
    }

    [Fact]
    public void ValveSide_IgnoresJoints_AndIsNullWhenAmbiguous()
    {
        var network = Corner();
        // 이음관은 밸브가 아니므로 방향 판정에 쓰지 않는다 → 밸브 없음 = 모호.
        network.AddInlineFitting(EdgeTo(network, P(1000, 1000)).Id, InlineJoint.TypeKey, "이음관 : DN100", 0.5);
        network.SetNodeFittingKind(CornerNode(network).Id, PipeNodeFittingKind.ValveBend);

        var node = network.ToDefinition(100, PipeOutputMode.PipeAccessorySegment, P(0, 0)).Nodes.Single(x => x.IsValveBend);

        Assert.Null(node.ValveSideEdgeId);
    }

    [Fact]
    public void Flip_SwapsSide_AndOverridesAutoValve()
    {
        var network = Corner();
        network.AddInlineFitting(EdgeTo(network, P(1000, 1000)).Id, ValveTypeKey, "게이트밸브 : DN100", 0.5);
        var corner = CornerNode(network);
        network.SetNodeFittingKind(corner.Id, PipeNodeFittingKind.ValveBend);

        Assert.True(network.FlipValveBendSide(corner.Id));
        AssertDirection(P(-1, 0), network.ResolveValveSideDirection(corner.Id));
        Assert.True(network.FlipValveBendSide(corner.Id));
        AssertDirection(P(0, 1), network.ResolveValveSideDirection(corner.Id));
    }

    [Fact]
    public void Flip_WhenAmbiguous_PicksALeg()
    {
        var network = Corner();
        var corner = CornerNode(network);
        network.SetNodeFittingKind(corner.Id, PipeNodeFittingKind.ValveBend);
        Assert.Null(network.ResolveValveSideDirection(corner.Id));

        Assert.True(network.FlipValveBendSide(corner.Id));

        Assert.NotNull(network.ResolveValveSideDirection(corner.Id));
    }

    [Fact]
    public void Flip_OnPlainBend_DoesNothing()
    {
        var network = Corner();
        Assert.False(network.FlipValveBendSide(CornerNode(network).Id));
    }

    [Fact]
    public void ValveSide_SurvivesInlineFittingRemoval_WhichRecreatesTheEdge()
    {
        // 부속 삭제는 엣지를 새 Guid로 다시 만든다(PipeTopologyBuilder.RemoveInlineFitting) — 방향 지정은 남아야 한다.
        var network = Corner();
        var vertical = EdgeTo(network, P(1000, 1000));
        network.AddInlineFitting(vertical.Id, ValveTypeKey, "게이트밸브 : DN100", 0.5);
        var corner = CornerNode(network);
        network.SetNodeFittingKind(corner.Id, PipeNodeFittingKind.ValveBend);
        network.FlipValveBendSide(corner.Id);   // 수평 레그를 밸브 쪽으로 지정

        var fitting = EdgeTo(network, P(1000, 1000)).InlineFittings.Single();
        network.RemoveInlineFitting(EdgeTo(network, P(1000, 1000)).Id, fitting.Id);

        AssertDirection(P(-1, 0), network.ResolveValveSideDirection(corner.Id));
    }

    [Fact]
    public void ReturningToBend_ClearsDirection()
    {
        var network = Corner();
        var corner = CornerNode(network);
        network.SetNodeFittingKind(corner.Id, PipeNodeFittingKind.ValveBend);
        network.FlipValveBendSide(corner.Id);

        network.SetNodeFittingKind(corner.Id, PipeNodeFittingKind.Bend);

        Assert.Null(corner.ValveSideDirection);
        Assert.Equal(PipeNodeFittingKind.Bend, network.ToDefinition(100, PipeOutputMode.PipeAccessorySegment, P(0, 0)).Nodes.Single(x => x.Id == corner.Id).FittingKind);
    }

    [Fact]
    public void Classification_ResetsValveBend_WhenNodeStopsBeingElbow()
    {
        var network = Corner();
        var corner = CornerNode(network);
        network.SetNodeFittingKind(corner.Id, PipeNodeFittingKind.ValveBend);
        network.FlipValveBendSide(corner.Id);

        // 세 번째 선을 붙여 T형이 되면 밸브부관 지정은 뜻이 없다.
        network.AddSegment(P(1000, 0), P(2000, 0));

        var node = network.Nodes.Single(x => x.Id == corner.Id);
        Assert.NotEqual(NodeKind.Elbow, node.NodeKind);
        Assert.Equal(PipeNodeFittingKind.Bend, node.FittingKind);
        Assert.Null(node.ValveSideDirection);
    }

    [Fact]
    public void DeepClone_KeepsFittingKindAndDirection()
    {
        var network = Corner();
        var corner = CornerNode(network);
        network.SetNodeFittingKind(corner.Id, PipeNodeFittingKind.ValveBend);
        network.FlipValveBendSide(corner.Id);

        var clone = network.DeepClone().Nodes.Single(x => x.Id == corner.Id);

        Assert.Equal(PipeNodeFittingKind.ValveBend, clone.FittingKind);
        Assert.Equal(corner.ValveSideDirection, clone.ValveSideDirection);
    }

    [Theory]
    [InlineData(1000, 1000, true)]    // 90°
    [InlineData(2000, 1000, false)]   // 45°
    public void HasSupportedAngle_OnlyNinetyDegrees(double endX, double endY, bool expected)
    {
        var network = new PipeNetwork();
        network.AddSegment(P(0, 0), P(1000, 0));
        network.AddSegment(P(1000, 0), P(endX, endY));
        var definition = network.ToDefinition(100, PipeOutputMode.PipeAccessorySegment, P(0, 0));
        var node = definition.Nodes.Single(x => x.NodeKind == NodeKind.Elbow);

        Assert.Equal(expected, PipeValveBend.HasSupportedAngle(node, definition.Edges));
    }

    [Theory]
    [InlineData("DH_밸브부관 : 100A", true)]
    [InlineData("밸브부관", true)]
    [InlineData("DH_게이트 밸브 : 300A", false)]
    [InlineData(null, false)]
    public void IsValveBendFamily_UsesKeyword(string? name, bool expected)
        => Assert.Equal(expected, PipeValveBend.IsValveBendFamily(name));

    [Fact]
    public void Reconstructor_ValveBendBecomesNode_WithValveSideDirection()
    {
        // 커넥터[0](최소 Id)이 +Y 쪽 = 밸브 쪽. 역가져오기 저장소가 Id 순으로 정렬해 넘긴다(docs/225 § E).
        var corner = P(2000, 0);
        var valveBend = new PickedPipePart(2, PipePartRole.ValveBend, "DH_밸브부관 : 100A", corner, 0,
        [
            new PickedConnector(P(2000, 200), 0, P(0, 1), 100, []),
            new PickedConnector(P(1800, 0), 0, P(-1, 0), 100, [])
        ]);

        var import = PipeNetworkReconstructor.Build(
        [
            Segment(1, P(0, 0), P(1800, 0)),
            valveBend,
            Segment(3, P(2000, 200), P(2000, 1500)),
        ]);

        Assert.Equal(2, import.Edges.Count);
        Assert.Empty(import.Fittings);   // 인라인 밸브로 올라가면 안 된다
        var nodeFitting = Assert.Single(import.NodeFittings!);
        Assert.Equal(PipeNodeFittingKind.ValveBend, nodeFitting.FittingKind);
        Assert.Equal(2, nodeFitting.ElementId);
        AssertDirection(P(0, 1), nodeFitting.ValveSideDirection);
        Assert.Equal("DH_밸브부관 : 100A", import.ValveBendFamilyTypeName);
        Assert.Null(import.BendFamilyTypeName);   // 곡관 최빈값을 오염시키지 않는다
    }

    // ── 밸브부관 A(docs/227) ─────────────────────────────────────────────────────

    [Fact]
    public void ValveTee_OnlyOnTee_ValveBend_OnlyOnElbow()
    {
        var network = Bypass();
        var tee = network.Nodes.Single(x => x.NodeKind == NodeKind.Tee);
        var corner = CornerNode(network);

        Assert.False(network.SetNodeFittingKind(corner.Id, PipeNodeFittingKind.ValveTee));
        Assert.False(network.SetNodeFittingKind(tee.Id, PipeNodeFittingKind.ValveBend));
        Assert.True(network.SetNodeFittingKind(tee.Id, PipeNodeFittingKind.ValveTee));
        Assert.True(network.SetNodeFittingKind(tee.Id, PipeNodeFittingKind.Bend));
    }

    [Fact]
    public void ValveBend_SideAndMainDiameter_ComeFromLinkedValveTee()
    {
        var network = Bypass();
        network.SetNodeFittingKind(network.Nodes.Single(x => x.NodeKind == NodeKind.Tee).Id, PipeNodeFittingKind.ValveTee);
        network.SetNodeFittingKind(CornerNode(network).Id, PipeNodeFittingKind.ValveBend);

        var definition = network.ToDefinition(100, PipeOutputMode.PipeAccessorySegment, P(0, 0));
        var bend = definition.Nodes.Single(x => x.IsValveBend);

        // 밸브가 없어도 A 반대쪽 레그((1000,1000)→(2000,1000))가 밸브 쪽이다.
        Assert.Equal(EdgeTo(network, P(2000, 1000)).Id, bend.ValveSideEdgeId);
        Assert.Equal(1650, bend.MainDiameterMm);
    }

    [Fact]
    public void ValveBend_MainDiameter_PassesThroughInlineNodes()
    {
        // 분기를 두 번에 나눠 그려 (1000,500)에 일직선 통과점이 생긴다.
        var network = new PipeNetwork();
        network.AddSegment(P(0, 0), P(2000, 0), 1650);
        network.AddSegment(P(1000, 0), P(1000, 500), 300);
        network.AddSegment(P(1000, 500), P(1000, 1000), 300);
        network.AddSegment(P(1000, 1000), P(2000, 1000), 300);
        network.SetNodeFittingKind(network.Nodes.Single(x => x.NodeKind == NodeKind.Tee).Id, PipeNodeFittingKind.ValveTee);
        network.SetNodeFittingKind(CornerNode(network).Id, PipeNodeFittingKind.ValveBend);

        var bend = network.ToDefinition(100, PipeOutputMode.PipeAccessorySegment, P(0, 0)).Nodes.Single(x => x.IsValveBend);

        Assert.Equal(1650, bend.MainDiameterMm);
    }

    [Fact]
    public void ValveBend_MainDiameter_IsNull_WhenArrivingOnValveTeeMainLeg()
    {
        // A의 주관이 세로(0,-1000)~(0,1000)이고 B는 그 주관 끝 (0,1000)에서 꺾인다 → 분기가 아니라 주관으로 도착.
        var network = new PipeNetwork();
        network.AddSegment(P(0, -1000), P(0, 1000), 300);
        network.AddSegment(P(0, 0), P(-1000, 0), 300);
        network.AddSegment(P(0, 1000), P(1000, 1000), 300);
        network.SetNodeFittingKind(network.Nodes.Single(x => x.NodeKind == NodeKind.Tee).Id, PipeNodeFittingKind.ValveTee);
        var corner = CornerNode(network);
        network.SetNodeFittingKind(corner.Id, PipeNodeFittingKind.ValveBend);
        network.SetValveBendSide(corner.Id, P(1, 0));

        var bend = network.ToDefinition(100, PipeOutputMode.PipeAccessorySegment, P(0, 0)).Nodes.Single(x => x.IsValveBend);

        Assert.NotNull(bend.ValveSideEdgeId);
        Assert.Null(bend.MainDiameterMm);
    }

    [Fact]
    public void Classification_ResetsValveTee_WhenBranchIsRemoved()
    {
        var network = Bypass();
        var tee = network.Nodes.Single(x => x.NodeKind == NodeKind.Tee);
        network.SetNodeFittingKind(tee.Id, PipeNodeFittingKind.ValveTee);

        var corner = CornerNode(network);
        var branch = network.Edges.Single(x => (x.StartNodeId == tee.Id && x.EndNodeId == corner.Id)
                                            || (x.StartNodeId == corner.Id && x.EndNodeId == tee.Id));
        network.RemoveSegment(branch.Id);

        Assert.All(network.Nodes, x => Assert.Equal(PipeNodeFittingKind.Bend, x.FittingKind));
    }

    [Fact]
    public void ValveBranchSpec_IsMainByBranch()
        => Assert.Equal("D1650xD300", PipeMaterialSpecText.ValveBranch(1650, 300));

    [Fact]
    public void Reconstructor_ValveTeeBecomesTeeNode()
    {
        var corner = P(2000, 0);
        var valveTee = new PickedPipePart(2, PipePartRole.ValveTee, "DH_밸브부관A : 1650A", corner, 0,
        [
            new PickedConnector(P(1800, 0), 0, P(-1, 0), 1650, []),
            new PickedConnector(P(2200, 0), 0, P(1, 0), 1650, []),
            new PickedConnector(P(2000, 200), 0, P(0, 1), 300, [])
        ]);

        var import = PipeNetworkReconstructor.Build(
        [
            Segment(1, P(0, 0), P(1800, 0)),
            valveTee,
            Segment(3, P(2200, 0), P(4000, 0)),
            Segment(4, P(2000, 200), P(2000, 1500)),
        ]);

        Assert.Equal(3, import.Edges.Count);
        Assert.Empty(import.Fittings);
        var nodeFitting = Assert.Single(import.NodeFittings!);
        Assert.Equal(PipeNodeFittingKind.ValveTee, nodeFitting.FittingKind);
        Assert.Equal("DH_밸브부관A : 1650A", import.ValveTeeFamilyTypeName);
        Assert.Null(import.TeeFamilyTypeName);
    }

    /// <summary>본관 (0,0)→(2000,0) D1650, 분기 (1000,0)→(1000,1000) D300, 꺾여서 (1000,1000)→(2000,1000) D300.</summary>
    private static PipeNetwork Bypass()
    {
        var network = new PipeNetwork();
        network.AddSegment(P(0, 0), P(2000, 0), 1650);
        network.AddSegment(P(1000, 0), P(1000, 1000), 300);
        network.AddSegment(P(1000, 1000), P(2000, 1000), 300);
        return network;
    }

    private static PipeNetwork Corner()
    {
        // (0,0) → (1000,0) → (1000,1000). 꺾인 절점은 (1000,0)이다.
        var network = new PipeNetwork();
        network.AddSegment(P(0, 0), P(1000, 0));
        network.AddSegment(P(1000, 0), P(1000, 1000));
        return network;
    }

    private static PipeNode CornerNode(PipeNetwork network) => network.Nodes.Single(x => x.NodeKind == NodeKind.Elbow);

    private static PipeEdge EdgeTo(PipeNetwork network, Point2D end)
    {
        var endNode = network.Nodes.Single(x => x.Position.DistanceTo(end) < 0.01);
        return network.Edges.Single(x => x.StartNodeId == endNode.Id || x.EndNodeId == endNode.Id);
    }

    private static PickedPipePart Segment(long id, Point2D start, Point2D end)
    {
        var length = start.DistanceTo(end);
        var ux = (end.X - start.X) / length;
        var uy = (end.Y - start.Y) / length;
        return new PickedPipePart(id, PipePartRole.Segment, "직관 : DN100", new((start.X + end.X) / 2, (start.Y + end.Y) / 2), 0,
        [
            new PickedConnector(start, 0, P(-ux, -uy), 100, []),
            new PickedConnector(end, 0, P(ux, uy), 100, [])
        ]);
    }

    private static void AssertDirection(Point2D expected, Point2D? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.X, actual!.X, 6);
        Assert.Equal(expected.Y, actual.Y, 6);
    }

    private static Point2D P(double x, double y) => new(x, y);
}
