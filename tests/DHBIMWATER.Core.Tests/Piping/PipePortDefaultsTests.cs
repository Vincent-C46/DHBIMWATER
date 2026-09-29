using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>포트 자동 이격 규칙 — 벽 길이 N+1 등분(docs/174 § A-1, § B).</summary>
public sealed class PipePortDefaultsTests
{
    [Theory]
    [InlineData(3000, 1, 1, 1500)]
    [InlineData(3000, 1, 2, 1000)]
    [InlineData(3000, 2, 2, 2000)]
    [InlineData(3000, 1, 3, 750)]
    [InlineData(3000, 2, 3, 1500)]
    [InlineData(3000, 3, 3, 2250)]
    [InlineData(3000, 1, 4, 600)]
    [InlineData(3000, 2, 4, 1200)]
    [InlineData(3000, 3, 4, 1800)]
    [InlineData(3000, 4, 4, 2400)]
    public void EvenOffsetMm_DividesSideIntoCountPlusOne(double length, int rank, int count, double expected)
    {
        Assert.Equal(expected, PipePortDefaults.EvenOffsetMm(length, rank, count), 6);
    }

    [Fact]
    public void EvenOffsetMm_RoundsToOneDecimal()
    {
        Assert.Equal(333.3, PipePortDefaults.EvenOffsetMm(1000, 1, 2), 6);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(3, 2)]
    public void EvenOffsetMm_RejectsInvalidRankOrCount(int rank, int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PipePortDefaults.EvenOffsetMm(3000, rank, count));
    }
}
