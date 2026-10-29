using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

public sealed class PipeMaterialKeywordsTests
{
    [Theory]
    [InlineData("덕타일 주철관", PipeMaterial.DuctileIron)]
    [InlineData("Ductile Iron", PipeMaterial.DuctileIron)]
    [InlineData("DIP K9", PipeMaterial.DuctileIron)]
    [InlineData("Steel, Carbon", PipeMaterial.Steel)]
    [InlineData("SP관 D300", PipeMaterial.Steel)]
    [InlineData("HDPE 이중벽관", PipeMaterial.Pe)]
    [InlineData("PE관", PipeMaterial.Pe)]
    [InlineData("PVC - DWV", PipeMaterial.Pvc)]
    public void Resolve_MapsKeyword(string text, PipeMaterial expected)
        => Assert.Equal(expected, PipeMaterialKeywords.Resolve(text));

    [Theory]
    [InlineData(null)]
    [InlineData("기본값")]
    [InlineData("SPEC 표준")]
    [InlineData("강관 + PVC")]
    public void Resolve_ReturnsNullWhenUnknownOrMixed(string? text)
        => Assert.Null(PipeMaterialKeywords.Resolve(text));

    [Fact]
    public void ResolveFirst_SkipsUnresolvedTexts()
        => Assert.Equal(PipeMaterial.Steel, PipeMaterialKeywords.ResolveFirst(["기본값", null, "Steel"]));
}
