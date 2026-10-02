using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>인라인 부속 위치 오류 → 그 부속만 건너뛰기(docs/251 § B).</summary>
public sealed class InlineSpanFilterTests
{
    private const double Edge = 3000;

    private static InlineSpanCandidate C(Guid id, double start, double end, int priority = InlineSpanFilter.ValvePriority)
        => new(id, new OccupiedSpan(start, end), priority);

    [Fact]
    public void NoConflict_KeepsEverything()
    {
        var result = InlineSpanFilter.Filter(Edge, [C(Guid.NewGuid(), 1000, 1300), C(Guid.NewGuid(), 2000, 2300)]);

        Assert.Equal(2, result.Kept.Count);
        Assert.Empty(result.Dropped);
    }

    [Fact]
    public void OutsideEdge_DropsOnlyThatFitting()
    {
        // 타이밸브실 선1 재현: 축 2412mm, 버터플라이 밸브 [2604,2896] → 484mm 밖.
        var outside = Guid.NewGuid();
        var inside = Guid.NewGuid();
        var result = InlineSpanFilter.Filter(2412, [C(outside, 2604, 2896), C(inside, 500, 800)]);

        Assert.Equal([outside], result.Dropped.Keys);
        Assert.Contains("484mm", result.Dropped[outside]);
        Assert.Equal([new OccupiedSpan(500, 800)], result.Kept);
    }

    [Fact]
    public void Overlap_DropsLowerPriority()
    {
        var valve = Guid.NewGuid();
        var joint = Guid.NewGuid();
        var result = InlineSpanFilter.Filter(Edge,
            [C(joint, 900, 1100, InlineSpanFilter.JointPriority), C(valve, 1000, 1300)]);

        Assert.Equal([joint], result.Dropped.Keys);
    }

    [Fact]
    public void Overlap_SamePriority_DropsLaterOnAxis()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var result = InlineSpanFilter.Filter(Edge, [C(second, 1200, 1500), C(first, 1000, 1300)]);

        Assert.Equal([second], result.Dropped.Keys);
    }

    [Fact]
    public void ShortGapBetweenFittings_DropsLowerPriority()
    {
        var valve = Guid.NewGuid();
        var joint = Guid.NewGuid();
        var result = InlineSpanFilter.Filter(Edge,
            [C(valve, 1000, 1300), C(joint, 1305, 1505, InlineSpanFilter.JointPriority)]);

        Assert.Equal([joint], result.Dropped.Keys);
        Assert.Contains("5mm", result.Dropped[joint]);
    }

    [Fact]
    public void ShortGapAtEdgeEnd_DropsAdjacentFitting()
    {
        var valve = Guid.NewGuid();
        var result = InlineSpanFilter.Filter(Edge, [C(valve, 2700, 2995)]);

        Assert.Equal([valve], result.Dropped.Keys);
    }

    [Fact]
    public void FlushSpans_AreNotShortGaps()
    {
        // 절점 밀착(0)·밸브–이음관 밀착은 틈 0이라 정상이다.
        var result = InlineSpanFilter.Filter(Edge,
            [C(Guid.NewGuid(), 0, 300), C(Guid.NewGuid(), 300, 500, InlineSpanFilter.JointPriority), C(Guid.NewGuid(), 2800, 3000)]);

        Assert.Empty(result.Dropped);
    }

    [Fact]
    public void BypassSet_IsDroppedAsWhole_AndIgnoresOwnInnerGap()
    {
        // 세트 A 두 구간 사이 3mm 틈은 세트 자신의 것이라 보지 않는다. 밸브와 겹치면 밸브(낮은 우선순위)가 빠진다.
        var set = Guid.NewGuid();
        var valve = Guid.NewGuid();
        var result = InlineSpanFilter.Filter(Edge,
        [
            C(set, 1000, 1400, InlineSpanFilter.BypassSetPriority),
            C(set, 1403, 1800, InlineSpanFilter.BypassSetPriority),
            C(valve, 1700, 1900),
        ]);

        Assert.Equal([valve], result.Dropped.Keys);
        Assert.Equal(2, result.Kept.Count);
    }

    [Fact]
    public void DroppingCascades_UntilStable()
    {
        // 밸브가 빠지면 이음관이 엣지 밖은 아니지만 남은 틈이 짧아질 수 있다 — 다시 본다.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var result = InlineSpanFilter.Filter(Edge, [C(a, -10, 290), C(b, 295, 495, InlineSpanFilter.JointPriority)]);

        // a는 밖으로 나가 빠지고, b는 시작 끝(0)과 295mm 떨어져 정상.
        Assert.Equal([a], result.Dropped.Keys);
        Assert.Equal([new OccupiedSpan(295, 495)], result.Kept);
    }
}
