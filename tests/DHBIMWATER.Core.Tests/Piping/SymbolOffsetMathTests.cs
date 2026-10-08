using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

public sealed class SymbolOffsetMathTests
{
    [Theory]
    [InlineData(0, 0, 400)]
    [InlineData(90, 400, 0)]
    [InlineData(30, 200, 346.41016151377545)]
    public void ToLocal_HandlesRotationAndPreservesLength(double degrees, double expectedX, double expectedY)
    {
        var radians = degrees * Math.PI / 180;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        var local = SymbolOffsetMath.ToLocal(new SymbolVector(0, 400, 0),
            new SymbolVector(cosine, sine, 0), new SymbolVector(-sine, cosine, 0), new SymbolVector(0, 0, 1));

        Assert.Equal(expectedX, local.X, 8);
        Assert.Equal(expectedY, local.Y, 8);
        Assert.Equal(0, local.Z, 8);
        Assert.Equal(400, Math.Sqrt(local.X * local.X + local.Y * local.Y + local.Z * local.Z), 8);
    }
}
