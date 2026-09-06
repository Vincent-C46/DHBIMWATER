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
    public void External_and_tangent_distances_ignore_alignment_deflection()
    {
        var results = new[] { 40d, 43d, 45d, 47d, 50d }
            .Select(Compute)
            .ToList();

        Assert.All(results, result => Assert.Equal(results[0].ExternalMm, result.ExternalMm, 12));
        Assert.All(results, result => Assert.Equal(results[0].TangentMm, result.TangentMm, 12));
    }

    [Fact]
    public void Point_distances_follow_catalog_t_T_E_T_t_for_every_alignment_deflection()
    {
        var origin = new Point3D(0, 0, 0);
        var tangentM = BendResolver.TangentLength(RadiusMm, FittingAngleDeg) / 1000d;
        var externalM = BendArcGeometry.ExternalDistance(RadiusMm, FittingAngleDeg) / 1000d;

        foreach (var result in new[] { 40d, 43d, 45d, 47d, 50d }.Select(Compute))
        {
            Assert.Equal(LayingLengthMm / 1000d, result.Start.DistanceTo(origin), 12);
            Assert.Equal(tangentM, result.ArcStart.DistanceTo(origin), 12);
            Assert.Equal(externalM, result.ArcMid.DistanceTo(origin), 12);
            Assert.Equal(tangentM, result.ArcEnd.DistanceTo(origin), 12);
            Assert.Equal(LayingLengthMm / 1000d, result.End.DistanceTo(origin), 12);
        }
    }

    [Fact]
    public void External_matches_catalog_external_distance_identity()
    {
        var result = Compute(40d);

        Assert.Equal(BendArcGeometry.ExternalDistance(RadiusMm, FittingAngleDeg), result.ExternalMm, 12);
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
