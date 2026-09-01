using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Gis;
using Xunit;

namespace DHBIMWATER.UI.Tests.Gis;

public class StraightJointCutPlaneTests
{
    [Fact]
    public void Bisect_returns_half_angle_normal_for_two_directions()
    {
        var normal = StraightJointCutPlane.Bisect(Vector3D.UnitX, Vector3D.UnitY);
        var expected = Math.Sqrt(0.5d);

        Assert.Equal(expected, normal.X, 9);
        Assert.Equal(expected, normal.Y, 9);
        Assert.Equal(0d, normal.Z, 9);
        Assert.Equal(1d, normal.Length, 9);
    }

    [Fact]
    public void Bisect_keeps_same_direction()
    {
        var normal = StraightJointCutPlane.Bisect(new Vector3D(4, 0, 0), new Vector3D(2, 0, 0));

        Assert.Equal(Vector3D.UnitX, normal);
    }

    [Fact]
    public void Bisect_splits_three_dimensional_angle_equally()
    {
        var first = new Vector3D(1, 0, 1).Normalize();
        var second = new Vector3D(0, 1, -1).Normalize();

        var normal = StraightJointCutPlane.Bisect(first, second);
        var firstDot = normal.X * first.X + normal.Y * first.Y + normal.Z * first.Z;
        var secondDot = normal.X * second.X + normal.Y * second.Y + normal.Z * second.Z;

        Assert.Equal(firstDot, secondDot, 9);
        Assert.Equal(1d, normal.Length, 9);
    }

    [Theory]
    [InlineData(150.0001d, true)]
    [InlineData(150d, false)]
    [InlineData(90d, false)]
    public void ExceedsMiterLimit_uses_strict_deflection_boundary(double deflectionDeg, bool expected)
    {
        var radians = deflectionDeg * Math.PI / 180d;
        var second = new Vector3D(Math.Cos(radians), Math.Sin(radians), 0d);

        var result = StraightJointCutPlane.ExceedsMiterLimit(
            Vector3D.UnitX, second, StraightJointCutPlane.MaxMiterDeflectionDeg);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void CutSlope_caps_near_reversal_at_150_degree_miter_slope()
    {
        const double deflectionDeg = 179.9999d;
        var halfAngleRadians = deflectionDeg / 2d * Math.PI / 180d;
        var bisector = new Vector3D(Math.Cos(halfAngleRadians), Math.Sin(halfAngleRadians), 0d);
        var maxSlope = Math.Tan(StraightJointCutPlane.MaxMiterDeflectionDeg / 2d * Math.PI / 180d);

        var slope = StraightJointCutPlane.CutSlope(Vector3D.UnitX, bisector, maxSlope);

        Assert.Equal(maxSlope, slope, 9);
    }
}
