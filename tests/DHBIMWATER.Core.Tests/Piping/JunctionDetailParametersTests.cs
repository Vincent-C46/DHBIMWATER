using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

public sealed class JunctionDetailParametersTests
{
    [Theory]
    [InlineData("심볼오프셋X")]
    [InlineData("심볼 오프셋X")]
    [InlineData("심볼오프셋 X")]
    [InlineData(" 심볼오프셋X ")]
    [InlineData("심볼\t오프셋X")]
    public void NameMatches_IgnoresWhitespace(string actual)
        => Assert.True(JunctionDetailParameters.NameMatches(actual, JunctionDetailParameters.SymbolOffsetX));

    [Theory]
    [InlineData("심볼오프셋x")]
    [InlineData("심볼오프셋Y")]
    [InlineData(null)]
    public void NameMatches_RejectsOtherNames(string? actual)
        => Assert.False(JunctionDetailParameters.NameMatches(actual, JunctionDetailParameters.SymbolOffsetX));
}
