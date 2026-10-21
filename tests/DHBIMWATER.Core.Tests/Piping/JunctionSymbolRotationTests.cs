using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

public sealed class JunctionSymbolRotationTests
{
    private static readonly SymbolVector Right = new(1, 0, 0);
    private static readonly SymbolVector Up = new(0, 1, 0);

    // 심볼 위쪽 +Y 기준 → 펼침 방향으로 돌 각(docs/306)
    [Theory]
    [InlineData(JunctionExpandAxis.Y, 400, 0)]
    [InlineData(JunctionExpandAxis.Y, -400, 180)]
    [InlineData(JunctionExpandAxis.X, 400, -90)]
    [InlineData(JunctionExpandAxis.X, -400, 90)]
    public void Angle_MapsDirections(JunctionExpandAxis axis, double step, double degrees)
    {
        var angle = JunctionSymbolRotation.AngleRadians(Up, Right, Up, new(1, axis, step));
        Assert.NotNull(angle);
        Assert.Equal(degrees, Math.Abs(degrees) == 180 ? Math.Abs(angle!.Value) * 180 / Math.PI : angle!.Value * 180 / Math.PI, 6);
    }

    [Fact]
    public void Angle_AlreadyAligned_ReturnsZero()
        => Assert.Equal(0, JunctionSymbolRotation.AngleRadians(new(0, -1, 0), Right, Up, new(1, JunctionExpandAxis.Y, -400)));

    [Fact]
    public void Angle_StepZero_NoRotation()
        => Assert.Equal(0, JunctionSymbolRotation.AngleRadians(new(1, 0, 0), Right, Up, new(1, JunctionExpandAxis.Y, 0)));

    [Fact]
    public void Angle_VerticalBasisY_Skips()
        => Assert.Null(JunctionSymbolRotation.AngleRadians(new(0, 0, 1), Right, Up, new(1, JunctionExpandAxis.Y, 400)));

    [Fact]
    public void Rotate_HalfTurn_FlipsShape()
    {
        var shape = JunctionSymbolShape.FromPolylines([[new ShapePoint(-10, 0), new ShapePoint(0, 30), new ShapePoint(10, 0)]]);
        var rotated = JunctionSymbolRotation.Rotate(shape, Math.PI);
        Assert.Equal(-30, rotated.MinY, 6);
        Assert.Equal(0, rotated.MaxY, 6);
    }
}
