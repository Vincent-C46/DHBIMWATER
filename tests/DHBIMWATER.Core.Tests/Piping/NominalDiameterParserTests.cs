using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

public sealed class NominalDiameterParserTests
{
    [Theory]
    [InlineData("버터플라이밸브 : DN300", 300)]
    [InlineData("게이트밸브 : 300A", 300)]
    [InlineData("밸브 : Ø150", 150)]
    [InlineData("밸브 : DN300 - 2단", 300)]
    [InlineData("게이트밸브 300A : 표준", 300)]
    [InlineData("게이트밸브 300A", 300)]
    [InlineData("밸브 : DN300", 300)]
    [InlineData("밸브 500A : 300형", 300)]
    public void Parse_ReturnsNominalDiameterFromTypeName(string familyTypeName, double expected)
        => Assert.Equal(expected, NominalDiameterParser.Parse(familyTypeName));

    [Theory]
    [InlineData("밸브 : 표준형")]
    public void Parse_ReturnsNull_WhenNoValidNominalDiameterExists(string familyTypeName)
        => Assert.Null(NominalDiameterParser.Parse(familyTypeName));

    [Fact]
    public void Parse_ReturnsDiameterWithinConfiguredRange()
        => Assert.Equal(2026, NominalDiameterParser.Parse("밸브 : DN2026 기본"));
}
