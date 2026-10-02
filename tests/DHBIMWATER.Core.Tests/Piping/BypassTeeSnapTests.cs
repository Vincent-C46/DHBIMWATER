using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>본관 밸브를 세트 A 안쪽 커넥터에 밀착(docs/257 § A). A1 = 1000~1400, A2 = 2000~2400(안쪽 틈 600).</summary>
public sealed class BypassTeeSnapTests
{
    private const double Width = 200;
    private static readonly BypassTeePair Set = new(new OccupiedSpan(1000, 1400), new OccupiedSpan(2000, 2400));

    [Theory]
    [InlineData(1500)]
    [InlineData(1700)]   // 정중앙 — 시작 쪽 A
    [InlineData(1250)]   // A1 몸통 안쪽 절반
    public void NearFirstTee_SnapsToFirstInnerConnector(double center)
        => Assert.Equal(new OccupiedSpan(1400, 1600), BypassTeeSnap.InsideSet(center, Width, [Set]));

    [Theory]
    [InlineData(1900)]
    [InlineData(2150)]   // A2 몸통 안쪽 절반
    public void NearSecondTee_SnapsToSecondInnerConnector(double center)
        => Assert.Equal(new OccupiedSpan(1800, 2000), BypassTeeSnap.InsideSet(center, Width, [Set]));

    [Theory]
    [InlineData(900)]
    [InlineData(1200)]   // A1 중심
    [InlineData(2300)]
    [InlineData(3000)]
    public void OutsideSet_ReturnsNull(double center)
        => Assert.Null(BypassTeeSnap.InsideSet(center, Width, [Set]));

    [Fact]
    public void SeveralSets_UsesTheSetContainingCenter()
    {
        var other = new BypassTeePair(new OccupiedSpan(5000, 5400), new OccupiedSpan(6000, 6400));

        Assert.Equal(new OccupiedSpan(5800, 6000), BypassTeeSnap.InsideSet(5900, Width, [Set, other]));
    }
}
