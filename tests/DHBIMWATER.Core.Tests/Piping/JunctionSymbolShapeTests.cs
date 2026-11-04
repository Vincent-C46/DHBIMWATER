using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

public sealed class JunctionSymbolShapeTests
{
    [Fact]
    public void FromPolylines_CalculatesBoundsAcrossAllLines()
    {
        var shape = JunctionSymbolShape.FromPolylines([
            (IReadOnlyList<ShapePoint>)[new(-10, 5), new(20, 15)],
            (IReadOnlyList<ShapePoint>)[new(0, -25), new(4, 2)]
        ]);

        Assert.Equal(-10, shape.MinX);
        Assert.Equal(-25, shape.MinY);
        Assert.Equal(20, shape.MaxX);
        Assert.Equal(15, shape.MaxY);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(30)]
    public void RestoreOffset_RemovesRotatedLocalOffset(double degrees)
    {
        var radians = degrees * Math.PI / 180;
        var basisX = new SymbolVector(Math.Cos(radians), Math.Sin(radians), 0);
        var basisY = new SymbolVector(-Math.Sin(radians), Math.Cos(radians), 0);
        var basisZ = new SymbolVector(0, 0, 1);
        var baseline = new SymbolVector(300, -70, 12);
        var offset = new SymbolVector(100, 50, 5);
        var shifted = new SymbolVector(
            baseline.X + basisX.X * offset.X + basisY.X * offset.Y,
            baseline.Y + basisX.Y * offset.X + basisY.Y * offset.Y,
            baseline.Z + offset.Z);

        var restored = JunctionSymbolShapeMath.RestoreOffset(shifted, offset, basisX, basisY, basisZ);

        Assert.Equal(baseline.X, restored.X, 8);
        Assert.Equal(baseline.Y, restored.Y, 8);
        Assert.Equal(baseline.Z, restored.Z, 8);
    }

    [Fact]
    public void ToView_RotatedPlan_RotatesShapeLikePosition()
    {
        // 뷰 90° 회전: 오른쪽 = 세계 +Y, 위쪽 = 세계 −X. 세계 +X 방향 선 → 뷰에서 아래쪽
        var shape = JunctionSymbolShape.FromPolylines([(IReadOnlyList<ShapePoint>)[new(0, 0), new(100, 0)]]);
        var right = new SymbolVector(0, 1, 0);
        var up = new SymbolVector(-1, 0, 0);

        var view = JunctionSymbolShapeMath.ToView(shape, right, up);

        // 위치 변환(점·right, 점·up)과 같은 결과
        Assert.Equal(0, view.Polylines[0][1].X, 8);
        Assert.Equal(-100, view.Polylines[0][1].Y, 8);
        Assert.Equal(-100, view.MinY, 8);
    }

    [Fact]
    public void ToView_NoPlanAxes_KeepsShape()
    {
        var shape = JunctionSymbolShape.FromPolylines([(IReadOnlyList<ShapePoint>)[new(0, 0), new(100, 0)]]);
        Assert.Same(shape, JunctionSymbolShapeMath.ToView(shape, default, default));
        Assert.Same(shape, JunctionSymbolShapeMath.ToView(shape, new SymbolVector(1, 0, 0), new SymbolVector(0, 1, 0)));
    }
}
