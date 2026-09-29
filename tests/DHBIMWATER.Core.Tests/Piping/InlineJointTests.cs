using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>이음관(docs/167 → docs/175 § C). 이음관은 밸브의 속성이 아니라 <b>독립 인라인 부속</b>이다.
/// 정방향은 배치 정의를 TypeKey만 다르게 방출하는지, 역방향은 독립 부속으로 되돌리는지 본다.</summary>
public sealed class InlineJointTests
{
    private const string ValveFamily = "제수밸브 : DN100";
    private const string JointFamily = "DH_이음관 : DN100";

    // ── 정방향 ─────────────────────────────────────────────

    [Fact]
    public void ToDefinition_EmitsValveAsSingleDefinition()
    {
        var network = NetworkWithValve(out var edgeId, out var valveId);

        var fitting = Assert.Single(network.ToDefinition(100, PipeOutputMode.PipeAccessorySegment, new(0, 0)).InlineFittings);
        Assert.False(fitting.IsJoint);
        Assert.Equal(valveId, fitting.Id);
        Assert.Equal(edgeId, fitting.EdgeId);
    }

    [Fact]
    public void ToDefinition_EmitsJointAsIndependentDefinition_AtItsOwnPoint()
    {
        var network = NetworkWithValve(out var edgeId, out _);
        network.AddInlineFitting(edgeId, InlineJoint.TypeKey, JointFamily, 0.25);

        var fittings = network.ToDefinition(100, PipeOutputMode.PipeAccessorySegment, new(0, 0)).InlineFittings;

        Assert.Equal(2, fittings.Count);
        var joint = Assert.Single(fittings, x => x.IsJoint);
        Assert.Equal(InlineJoint.TypeKey, joint.TypeKey);
        Assert.Equal(JointFamily, joint.FamilyTypeName);
        Assert.Equal(edgeId, joint.EdgeId);
        // 이음관은 자기 캔버스 점을 싣는다(docs/175 § C-2). 밸브(0.5)와 다른 자리여야 한다.
        Assert.Equal(1250, joint.Position.X, 3);
    }

    [Fact]
    public void InlineFitting_IsJoint_FollowsTypeKeyOnly()
    {
        Assert.True(new InlineFitting(InlineJoint.TypeKey, "아무 패밀리", 0.5, 0).IsJoint);
        Assert.False(new InlineFitting("배관 밸브류", JointFamily, 0.5, 0).IsJoint);
    }

    // ── 역방향(docs/175 § C-4) ────────────────────────────────

    [Fact]
    public void Build_JointBecomesIndependentFitting_NextToValve()
    {
        // 관 ─ 이음관 ─ 밸브 ─ 이음관 ─ 관
        var import = PipeNetworkReconstructor.Build(
        [
            Segment(1, P(0, 0), P(1000, 0)),
            Joint(2, P(1000, 0), P(1200, 0)),
            Valve(3, P(1200, 0), P(1500, 0)),
            Joint(4, P(1500, 0), P(1700, 0)),
            Segment(5, P(1700, 0), P(3000, 0)),
        ]);

        // 체인이 끊기지 않아 엣지는 하나로 합쳐지고, 부속은 밸브 1 + 이음관 2 = 3개다.
        Assert.Single(import.Edges);
        Assert.Equal(3, import.Fittings.Count);
        var valve = Assert.Single(import.Fittings, x => !x.IsJoint);
        Assert.Equal("밸브 : DN100", valve.FamilyTypeName);
        var joints = import.Fittings.Where(x => x.IsJoint).OrderBy(x => x.T).ToList();
        Assert.Equal(2, joints.Count);
        Assert.All(joints, x => Assert.Equal(JointFamily, x.FamilyTypeName));
        // 자리는 각자 커넥터 중점이다 — 1100/3000, 1600/3000.
        Assert.Equal(1100.0 / 3000, joints[0].T, 3);
        Assert.Equal(1600.0 / 3000, joints[1].T, 3);
        Assert.True(joints[0].T < valve.T && valve.T < joints[1].T);
    }

    [Fact]
    public void Build_JointDoesNotPolluteSegmentFamilyVote()
    {
        // 이음관이 직관으로 분류되면 최빈값 투표를 이겨 관 패밀리가 '이음관'이 된다(docs/167 § F-1).
        var import = PipeNetworkReconstructor.Build(
        [
            Segment(1, P(0, 0), P(1000, 0)),
            Joint(2, P(1000, 0), P(1200, 0)),
            Valve(3, P(1200, 0), P(1500, 0)),
            Joint(4, P(1500, 0), P(1700, 0)),
            Segment(5, P(1700, 0), P(3000, 0)),
        ]);

        Assert.Equal("직관 : DN100", import.SegmentFamilyTypeName);
    }

    [Fact]
    public void Build_JointWithoutAdjacentValve_IsKeptAsFitting()
    {
        // docs/167에서는 인접 밸브가 없으면 버렸다. 독립 부속이 된 뒤로는 그대로 올린다(docs/175 § C-4).
        var import = PipeNetworkReconstructor.Build(
        [
            Segment(1, P(0, 0), P(1000, 0)),
            Joint(2, P(1000, 0), P(1200, 0)),
            Segment(3, P(1200, 0), P(3000, 0)),
        ]);

        var joint = Assert.Single(import.Fittings);
        Assert.True(joint.IsJoint);
        Assert.Equal(JointFamily, joint.FamilyTypeName);
        Assert.Single(import.Edges);
        Assert.DoesNotContain(import.Warnings, x => x.Contains("인접한 밸브가 없어", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("DH_이음관 : DN100", true)]
    [InlineData("신축이음관 : 300", true)]
    [InlineData("직관 : DN100", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsJointFamily_MatchesOnNameKeyword(string? familyTypeName, bool expected)
        => Assert.Equal(expected, InlineJoint.IsJointFamily(familyTypeName));

    // ── 헬퍼 ──────────────────────────────────────────────

    private static PipeNetwork NetworkWithValve(out Guid edgeId, out Guid valveId)
    {
        var network = new PipeNetwork();
        network.AddSegment(P(0, 0), P(5000, 0), 100);
        edgeId = network.Edges.Single().Id;
        network.AddInlineFitting(edgeId, "배관 밸브류", ValveFamily, 0.5);
        valveId = network.Edges.Single().InlineFittings.Single().Id;
        return network;
    }

    private static Point2D P(double x, double y) => new(x, y);

    private static PickedPipePart Segment(long id, Point2D start, Point2D end)
        => Inline(id, PipePartRole.Segment, "직관 : DN100", start, end);

    private static PickedPipePart Valve(long id, Point2D start, Point2D end)
        => Inline(id, PipePartRole.Accessory, "밸브 : DN100", start, end);

    private static PickedPipePart Joint(long id, Point2D start, Point2D end)
        => Inline(id, PipePartRole.Joint, JointFamily, start, end);

    private static PickedPipePart Inline(long id, PipePartRole role, string familyTypeName, Point2D start, Point2D end)
        => new(id, role, familyTypeName, new Point2D((start.X + end.X) / 2, (start.Y + end.Y) / 2), 0,
        [
            new PickedConnector(start, 0, Unit(end, start), 100, []),
            new PickedConnector(end, 0, Unit(start, end), 100, [])
        ]);

    private static Point2D Unit(Point2D from, Point2D to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        return length <= 1e-9 ? new Point2D(0, 0) : new Point2D(dx / length, dy / length);
    }
}
