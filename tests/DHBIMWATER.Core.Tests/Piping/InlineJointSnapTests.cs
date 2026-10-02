using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>이음관 밀착 규칙(docs/175 § D-2, docs/250). 축 0 / 엣지 길이 = 절점 부속 실측 커넥터.</summary>
public sealed class InlineJointSnapTests
{
    private const double Width = 200;
    private const double Edge = 3000;
    private const double Tol = 50;

    private static OccupiedSpan? Snap(double center, bool start = true, bool end = true, params OccupiedSpan[] valves)
        => InlineJointSnap.Resolve(center, Width, valves, Edge, start, end, Tol);

    [Theory]
    [InlineData(40)]     // T 몸통과 60mm 겹침
    [InlineData(-300)]   // 깊게 겹침 — 하한 없음
    [InlineData(130)]    // 틈 30mm
    public void StartNode_OverlapOrWithinTolerance_SnapsToConnector(double center)
        => Assert.Equal(new OccupiedSpan(0, Width), Snap(center));

    [Fact]
    public void StartNode_GapBeyondTolerance_KeepsClickedPosition()
        => Assert.Null(Snap(160));   // 틈 60mm

    [Fact]
    public void StartNode_WithoutAnchor_DoesNotSnap()
        => Assert.Null(Snap(40, start: false, end: false));

    [Fact]
    public void EndNode_SnapsToConnector()
        => Assert.Equal(new OccupiedSpan(Edge - Width, Edge), Snap(2880));

    [Fact]
    public void ValveOnly_BehavesLikeFormerSnapToValve()
        => Assert.Equal(new OccupiedSpan(800, 1000), Snap(880, false, false, new OccupiedSpan(1000, 1300)));

    [Fact]
    public void NodeAndValve_PicksSmallerGap()
        // 이음관 [20,220]: 시작 절점 틈 20, 밸브 [230,530] 틈 10 → 밸브 앞에 붙는다.
        => Assert.Equal(new OccupiedSpan(30, 230), Snap(120, valves: [new OccupiedSpan(230, 530)]));

    [Fact]
    public void NodeAndValve_Tie_PrefersNode()
        // 이음관 [10,210]: 시작 절점 틈 10 = 밸브 [220,520] 틈 10.
        => Assert.Equal(new OccupiedSpan(0, Width), Snap(110, valves: [new OccupiedSpan(220, 520)]));
}
