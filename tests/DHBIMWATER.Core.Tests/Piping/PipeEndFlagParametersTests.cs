using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>docs/315 § 4-1 표 — 단부 가시성 Yes/No 규칙.</summary>
public sealed class PipeEndFlagParametersTests
{
    [Theory]
    // 직관
    [InlineData(false, PipeMaterial.DuctileIron, BendConnection.Socket, true, false, false, false)]
    [InlineData(false, PipeMaterial.DuctileIron, BendConnection.Flanged, false, false, true, true)]
    [InlineData(false, PipeMaterial.Steel, BendConnection.Socket, false, false, false, false)]
    [InlineData(false, PipeMaterial.Steel, BendConnection.Flanged, false, false, true, true)]
    // 강관 곡관
    [InlineData(true, PipeMaterial.Steel, BendConnection.Socket, false, false, false, false)]
    [InlineData(true, PipeMaterial.Steel, BendConnection.Flanged, false, false, true, true)]
    public void Resolve_WritesAllFour(bool isBend, PipeMaterial material, BendConnection connection,
        bool leftSocket, bool rightSocket, bool leftFlange, bool rightFlange)
    {
        var flags = PipeEndFlagParameters.Resolve(isBend, material, connection);

        Assert.Equal(leftSocket, flags.LeftSocket);
        Assert.Equal(rightSocket, flags.RightSocket);
        Assert.Equal(leftFlange, flags.LeftFlange);
        Assert.Equal(rightFlange, flags.RightFlange);
    }

    [Theory]
    [InlineData(BendConnection.Socket, false)]
    [InlineData(BendConnection.Flanged, true)]
    public void Resolve_DuctileBend_LeavesSocketToType(BendConnection connection, bool flange)
    {
        var flags = PipeEndFlagParameters.Resolve(isBend: true, PipeMaterial.DuctileIron, connection);

        Assert.Null(flags.LeftSocket);
        Assert.Null(flags.RightSocket);
        Assert.Equal(flange, flags.LeftFlange);
        Assert.Equal(flange, flags.RightFlange);
    }

    [Fact]
    public void Resolve_UnknownMaterial_UsesDuctileRule()
        => Assert.Equal(PipeEndFlagParameters.Resolve(false, PipeMaterial.DuctileIron, BendConnection.Socket),
            PipeEndFlagParameters.Resolve(false, null, BendConnection.Socket));
}
