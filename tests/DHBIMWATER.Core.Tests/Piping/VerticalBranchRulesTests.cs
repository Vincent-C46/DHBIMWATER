using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>수직분기 입력 규칙(docs/301 § C).</summary>
public sealed class VerticalBranchRulesTests
{
    private const string Valve = "DH_제수밸브 : DN200";

    [Fact]
    public void NoParts_IsError()
        => Assert.Single(VerticalBranchRules.Validate([]));

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(9.9)]
    [InlineData(double.NaN)]
    public void ShortOrInvalidPipe_IsError(double lengthMm)
    {
        var errors = VerticalBranchRules.Validate([new VerticalBranchPart("", lengthMm)]);
        Assert.Single(errors);
        Assert.Contains("#1 단관", errors[0]);
    }

    [Fact]
    public void MinPipeAndValve_AreValid()
        => Assert.Empty(VerticalBranchRules.Validate([new("", PipeSegmentPlan.MinSegmentMm), new(Valve), new("", 800)]));

    [Fact]
    public void ValveLength_IsIgnored()
        => Assert.Empty(VerticalBranchRules.Validate([new VerticalBranchPart(Valve, 0)]));

    [Fact]
    public void PipeLengthSum_SkipsValves()
        => Assert.Equal(1300, VerticalBranchRules.PipeLengthSum([new("", 500), new(Valve, 999), new("", 800)]));
}
