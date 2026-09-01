using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Gis;
using Xunit;

namespace DHBIMWATER.Core.Tests.Gis;

public sealed class AlignmentStationProfileTests
{
    [Fact]
    public void 원본_측점이_없으면_누적거리를_측점으로_쓴다()
    {
        var profile = new AlignmentStationProfile(Alignment([P(0), P(50), P(100)]));

        Assert.False(profile.HasSourceStation);
        Assert.Equal(25d, profile.StationAt(25));
        Assert.Equal(100d, profile.DistanceAtVertex(2));
    }

    [Fact]
    public void 원본_측점은_가장_가까운_앞쪽_정점에서_계산한다()
    {
        var profile = new AlignmentStationProfile(Alignment([P(0), P(50), P(100)], [100d, 150d, 200d]));

        Assert.True(profile.HasSourceStation);
        Assert.Equal(125d, profile.StationAt(25));
        Assert.Equal(175d, profile.StationAt(75));
        Assert.Equal(0, profile.MismatchCount);
    }

    [Fact]
    public void 정점_측점은_정점에서_리셋하고_불일치를_센다()
    {
        var profile = new AlignmentStationProfile(Alignment([P(0), P(50)], [100d, 152d]));

        Assert.Equal(125d, profile.StationAt(25));
        Assert.Equal(152d, profile.StationAt(50));
        Assert.Equal(1, profile.MismatchCount);
    }

    [Fact]
    public void 첫_측점이_없으면_다음_유효_정점에서_외삽한다()
    {
        var profile = new AlignmentStationProfile(Alignment([P(0), P(50), P(100)], [null, 25d, 75d]));

        Assert.True(profile.HasSourceStation);
        Assert.Equal(-25d, profile.StationAt(0));
        Assert.Equal(0d, profile.StationAt(25));
    }

    private static Point3D P(double x) => new(x, 0, 0);

    private static PipeAlignment Alignment(IReadOnlyList<Point3D> vertices, IReadOnlyList<double?>? stations = null) => new(
        vertices, "상수", 100, "sample.xlsx", "1", new Dictionary<string, string>(), VertexStationM: stations);
}
