using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Gis;
using Xunit;

namespace DHBIMWATER.Core.Tests.Gis;

public sealed class BendArcGeometryTests
{
    [Fact]
    public void ComputeForAlignment_UsesMeasuredDeflectionForTangentContinuousArc()
    {
        var result = Compute(45d, 30d, 495d, 400d);
        var center = Circumcenter(result.ArcStart, result.ArcMid, result.ArcEnd);
        var leg = new Vector3D(result.ArcStart.X - result.Start.X, result.ArcStart.Y - result.Start.Y, 0d);
        var radius = new Vector3D(result.ArcStart.X - center.X, result.ArcStart.Y - center.Y, 0d);

        Assert.True(Math.Abs(Dot(leg, radius)) <= 1e-6, $"접선 내적: {Dot(leg, radius)}");
    }

    [Fact]
    public void ComputeForAlignment_SmallMeasuredDeflectionHasPositiveSag()
    {
        var result = Compute(11.25d, 4.8d, 495d, 100d);

        Assert.True(result.ExternalMm > 0d);
        Assert.True(result.ArcMid.DistanceTo(new Point3D(0, 0, 0)) > 0d);
    }

    [Fact]
    public void ComputeForAlignment_MatchingAnglesPreservesCatalogValues()
    {
        var result = Compute(45d, 45d, 300d, 175d);

        Assert.Equal(BendResolver.TangentLength(300d, 45d), result.TangentMm, 12);
        Assert.Equal(BendArcGeometry.ExternalDistance(300d, 45d), result.ExternalMm, 12);
    }

    [Fact]
    public void EvaluatePlacementFeasibility_RejectsInnerPipePenetration()
    {
        var result = BendArcGeometry.EvaluatePlacementFeasibility(206, "0+371.80", 54.8d, 11.25d, 266d, 532d, 300d, 300d);

        Assert.False(result.IsPlaceable);
        Assert.Contains("외경/2", result.FailureReason);
    }

    [Fact]
    public void EvaluatePlacementFeasibility_RejectsLegShorterThanMeasuredTangent()
    {
        var result = BendArcGeometry.EvaluatePlacementFeasibility(1, "0+001.00", 45d, 11.25d, 495d, 500d, 100d, 500d);

        Assert.False(result.IsPlaceable);
        Assert.Contains("상류 다리", result.FailureReason);
    }

    private static BendArcPoints Compute(double fittingAngleDeg, double alignmentDeflectionDeg, double radiusMm, double layingLengthMm)
    {
        var angleRad = alignmentDeflectionDeg * Math.PI / 180d;
        return BendArcGeometry.ComputeForAlignment(
            new Point3D(0, 0, 0), new Vector3D(-1, 0, 0), new Vector3D(Math.Cos(angleRad), Math.Sin(angleRad), 0),
            fittingAngleDeg, alignmentDeflectionDeg, radiusMm, layingLengthMm, layingLengthMm);
    }

    private static Point3D Circumcenter(Point3D first, Point3D second, Point3D third)
    {
        var denominator = 2d * (first.X * (second.Y - third.Y) + second.X * (third.Y - first.Y) + third.X * (first.Y - second.Y));
        Assert.True(Math.Abs(denominator) > 1e-12);
        var firstSquared = first.X * first.X + first.Y * first.Y;
        var secondSquared = second.X * second.X + second.Y * second.Y;
        var thirdSquared = third.X * third.X + third.Y * third.Y;
        return new Point3D(
            (firstSquared * (second.Y - third.Y) + secondSquared * (third.Y - first.Y) + thirdSquared * (first.Y - second.Y)) / denominator,
            (firstSquared * (third.X - second.X) + secondSquared * (first.X - third.X) + thirdSquared * (second.X - first.X)) / denominator,
            0d);
    }

    private static double Dot(Vector3D first, Vector3D second) => first.X * second.X + first.Y * second.Y + first.Z * second.Z;
}
