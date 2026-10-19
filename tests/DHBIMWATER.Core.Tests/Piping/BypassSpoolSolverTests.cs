using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>단관 보정(docs/297 § D). Lb = 200, W1 + W2 = 600. 사용자 단관 300이면 오프셋 650.</summary>
public sealed class BypassSpoolSolverTests
{
    private const double Lb = 200;
    private const double Valves = 600;
    private const double UserOffset = 650;

    private static BypassSpoolSolution Solve(params double[] halves)
        => BypassSpoolSolver.Solve(new BypassSpoolInput(UserOffset, halves, Lb, Valves));

    [Fact]
    public void UserSpoolEnough_NotAdjusted()
    {
        var result = Solve(500, 500);
        Assert.False(result.Adjusted);
        Assert.Equal(UserOffset, result.OffsetMm);
        Assert.Equal(300, result.SpoolMm);
    }

    [Fact]
    public void SymmetricShortage_FitsExactly()
    {
        var result = Solve(900, 900);
        Assert.True(result.Adjusted);
        Assert.Equal(900, result.OffsetMm);
        Assert.Equal(2 * 900 - 2 * Lb - Valves, result.SpoolMm);
    }

    [Fact]
    public void AsymmetricTinyDifference_GrowsLongSideToMinSegment()
    {
        // 긴 쪽 기준이면 짧은 쪽 5mm, 짧은 쪽+10이면 긴 쪽 5mm — 둘 다 무효.
        var result = Solve(900, 895);
        Assert.Equal(910, result.OffsetMm);
    }

    [Fact]
    public void AsymmetricLargeDifference_FitsLongSide()
    {
        var result = Solve(900, 860);
        Assert.Equal(900, result.OffsetMm);
    }

    [Fact]
    public void UserSpoolEnoughButTinySlack_GrowsToMinSegment()
    {
        var result = Solve(646, 646);
        Assert.True(result.Adjusted);
        Assert.Equal(656, result.OffsetMm);
    }

    [Fact]
    public void TinySpool_GrowsToValidSpool()
    {
        // 사용자 직결(오프셋 500). 필요 503 → 단관 6mm는 무효.
        var result = BypassSpoolSolver.Solve(new BypassSpoolInput(500, [503, 503], Lb, Valves));
        Assert.True(result.SpoolMm >= PipeSegmentPlan.MinSegmentMm);
        Assert.True(result.OffsetMm - 503 >= PipeSegmentPlan.MinSegmentMm || result.OffsetMm - 503 <= 1);
    }

    [Theory]
    [InlineData(500, 500)]
    [InlineData(900, 900)]
    [InlineData(900, 895)]
    [InlineData(900, 860)]
    [InlineData(646, 646)]
    [InlineData(651, 400)]
    [InlineData(1234.5, 1229.7)]
    public void Invariants(double first, double second)
    {
        var input = new BypassSpoolInput(UserOffset, [first, second], Lb, Valves);
        var result = BypassSpoolSolver.Solve(input);
        Assert.True(result.SpoolMm >= 300 - 1);
        Assert.True(IsPiece(result.SpoolMm));
        Assert.True(IsPiece(result.OffsetMm - first));
        Assert.True(IsPiece(result.OffsetMm - second));
        Assert.Equal(BypassSpoolSolver.SpoolAt(input, result.OffsetMm), result.SpoolMm, 6);

        static bool IsPiece(double x) => x >= -1 && (x <= 1 || x >= PipeSegmentPlan.MinSegmentMm - 1e-6);
    }

    [Fact]
    public void NoHalves_NotAdjusted()
        => Assert.False(Solve().Adjusted);
}

/// <summary>커넥터 사슬(docs/297 § G). 결과 A 구간 안쪽 끝이 밸브 끝과 정확히 닿는지 확인.</summary>
public sealed class BypassTeeChainTests
{
    private const double Half = 250;

    // A 구간 = [중심 − 반폭 + 치우침, 중심 + 반폭 + 치우침].
    private static (double Start, double End) Span(double center, double offset, int sign, double skew)
        => (center + sign * offset - Half + skew, center + sign * offset + Half + skew);

    [Theory]
    [InlineData(-1, 0, 0)]     // T가 소속 엣지 끝, 대칭 A
    [InlineData(1, 0, 0)]      // T가 소속 엣지 시작
    [InlineData(-1, 0.3, 0)]   // T 레그 0.3mm 비대칭
    [InlineData(-1, 0, 7.5)]   // A 비대칭
    [InlineData(1, 0.3, -7.5)]
    public void InnerConnectorsTouchValveEnds(int ownerSign, double legSkew, double teeSkew)
    {
        // T 중심 5000, 레그 300(소속 쪽만 legSkew 더). 밸브 폭 소속 400 / 반대 380.
        // 소속 T 커넥터는 T 중심에서 소속 A 쪽(ownerSign)으로 레그만큼.
        var ownerTee = 5000 + ownerSign * (300 + legSkew);
        var otherTee = 5000 - ownerSign * 300;
        var (center, offset) = BypassTeeChain.Solve(ownerTee, otherTee, ownerSign, 400, 380, Half, teeSkew);

        var owner = Span(center, offset, ownerSign, teeSkew);
        var other = Span(center, offset, -ownerSign, teeSkew);
        // 소속 쪽 밸브 바깥 끝 = T 커넥터에서 A 방향으로 400.
        var ownerValveEnd = ownerTee + ownerSign * 400;
        var otherValveEnd = otherTee - ownerSign * 380;
        Assert.Equal(ownerValveEnd, ownerSign < 0 ? owner.End : owner.Start, 6);
        Assert.Equal(otherValveEnd, ownerSign < 0 ? other.Start : other.End, 6);
        // 오프셋 = T 중심 → A 중심 근사(레그 300 + 밸브 + 반폭).
        Assert.InRange(offset, 300 + 380 + Half - 1, 300 + 400 + Half + 1);
    }
}
