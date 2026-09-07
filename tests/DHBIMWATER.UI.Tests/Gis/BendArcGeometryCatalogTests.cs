using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Gis;
using Xunit;

namespace DHBIMWATER.UI.Tests.Gis;

public class BendArcGeometryCatalogTests
{
    private const double FittingAngleDeg = 45d;
    private const double RadiusMm = 300d;
    private const double LayingLengthMm = 175d;

    [Fact]
    public void External_and_tangent_distances_follow_alignment_deflection()
    {
        var results = new[] { 40d, 43d, 45d, 47d, 50d }
            .Select(Compute)
            .ToList();

        Assert.NotEqual(results[0].ExternalMm, results[^1].ExternalMm);
        Assert.NotEqual(results[0].TangentMm, results[^1].TangentMm);
    }

    [Fact]
    public void Point_distances_follow_measured_deflection_t_T_E_T_t_for_every_alignment_deflection()
    {
        var origin = new Point3D(0, 0, 0);
        foreach (var alignmentDeflectionDeg in new[] { 40d, 43d, 45d, 47d, 50d })
        {
            var result = Compute(alignmentDeflectionDeg);
            var tangentM = BendResolver.TangentLength(RadiusMm, alignmentDeflectionDeg) / 1000d;
            var externalM = BendArcGeometry.ExternalDistance(RadiusMm, alignmentDeflectionDeg) / 1000d;
            Assert.Equal(LayingLengthMm / 1000d, result.Start.DistanceTo(origin), 12);
            Assert.Equal(tangentM, result.ArcStart.DistanceTo(origin), 12);
            Assert.Equal(externalM, result.ArcMid.DistanceTo(origin), 12);
            Assert.Equal(tangentM, result.ArcEnd.DistanceTo(origin), 12);
            Assert.Equal(LayingLengthMm / 1000d, result.End.DistanceTo(origin), 12);
        }
    }

    [Fact]
    public void External_matches_measured_deflection_external_distance_identity()
    {
        var result = Compute(40d);

        Assert.Equal(BendArcGeometry.ExternalDistance(RadiusMm, 40d), result.ExternalMm, 12);
    }

    private static BendArcPoints Compute(double alignmentDeflectionDeg)
    {
        var angleRad = alignmentDeflectionDeg * Math.PI / 180d;
        return BendArcGeometry.ComputeForAlignment(
            new Point3D(0, 0, 0),
            new Vector3D(-1, 0, 0),
            new Vector3D(Math.Cos(angleRad), Math.Sin(angleRad), 0),
            FittingAngleDeg, alignmentDeflectionDeg, RadiusMm, LayingLengthMm, LayingLengthMm);
    }
}
