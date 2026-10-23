using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

public sealed class ReducerEndLocatorTests
{
    private static ReducerArc Arc(double x, double y, double z, double radius) => new(new Point3D(x, y, z), radius);

    [Fact]
    public void Locate_ConcentricCylinderConeCylinder_PicksOuterEnds()
    {
        // 대경 원통(0~200) + 원뿔(200~800) + 소경 원통(800~1000). 안·밖 원, 반원 두 조각이 섞여 있다.
        var arcs = new[]
        {
            Arc(0, 0, 0, 300), Arc(0, 0, 0, 300), Arc(0, 0, 0, 280),
            Arc(200, 0, 0, 300), Arc(200, 0, 0, 280),
            Arc(800, 0, 0, 150), Arc(800, 0, 0, 135),
            Arc(1000, 0, 0, 150), Arc(1000.4, 0, 0, 135),
        };

        var ends = ReducerEndLocator.Locate(arcs);

        Assert.NotNull(ends);
        Assert.Equal(new Point3D(0, 0, 0), ends!.Large);
        Assert.Equal(new Point3D(1000, 0, 0), ends.Small);
    }

    [Fact]
    public void Locate_EccentricReducer_UsesEndCenters()
    {
        // 편심 — 소경 쪽 중심이 Z로 내려가 있다. 큰 원이 뒤에 와도 대경 쪽이 Large다.
        var arcs = new[] { Arc(1000, 0, -150, 150), Arc(0, 0, 0, 300), Arc(500, 0, -75, 225) };

        var ends = ReducerEndLocator.Locate(arcs);

        Assert.NotNull(ends);
        Assert.Equal(new Point3D(0, 0, 0), ends!.Large);
        Assert.Equal(new Point3D(1000, 0, -150), ends.Small);
    }

    [Fact]
    public void Locate_SingleCenterOrEqualRadii_ReturnsNull()
    {
        Assert.Null(ReducerEndLocator.Locate([Arc(0, 0, 0, 300), Arc(0.5, 0, 0, 280)]));
        Assert.Null(ReducerEndLocator.Locate([Arc(0, 0, 0, 300), Arc(1000, 0, 0, 300)]));
    }
}
