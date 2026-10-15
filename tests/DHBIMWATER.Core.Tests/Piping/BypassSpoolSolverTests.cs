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
