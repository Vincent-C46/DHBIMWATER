using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>공기밸브실 본관 관통 Void 지름 = 외경 + 여유(0), PipeLayout 역산(2026-10-29).</summary>
public sealed class MainPipeVoidSizingTests
{
    [Fact]
    public void VoidDiameter_IsOuterDiameterOfMaterial()
    {
        // 규격표: DN300 주철관 326, 강관 318.5(docs/211 § B-3).
        Assert.Equal(326, MainPipeVoidSizing.VoidDiameterMm(300, PipeMaterial.DuctileIron), 6);
        Assert.Equal(318.5, MainPipeVoidSizing.VoidDiameterMm(300, PipeMaterial.Steel), 6);
    }

    [Fact]
    public void VoidDiameter_UnknownNominal_FallsBackToNominal()
    {
        Assert.Equal(12345, MainPipeVoidSizing.VoidDiameterMm(12345, PipeMaterial.DuctileIron), 6);
    }

    [Theory]
    [InlineData(326)]     // 주철관 외경
    [InlineData(318.5)]   // 강관 외경
    [InlineData(300)]     // 종전 모델(Void = DN)
    public void NominalFromVoidDiameter_RecoversDn(double voidDiameter)
    {
        Assert.Equal(300, MainPipeVoidSizing.NominalFromVoidDiameter(voidDiameter));
    }

    [Fact]
    public void NominalFromVoidDiameter_NoMatch_ReturnsNull()
    {
        Assert.Null(MainPipeVoidSizing.NominalFromVoidDiameter(12345));
    }
}
