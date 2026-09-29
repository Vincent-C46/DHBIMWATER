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
    public void 정점_측점_사이는_비율로_보간하고_불일치를_센다()
    {
        // docs/215 § A — 엑셀 두 점 사이는 평면거리 비율 보간이다(종전: 직전 앵커 + 거리 = 125).
        var profile = new AlignmentStationProfile(Alignment([P(0), P(50)], [100d, 152d]));

        Assert.Equal(126d, profile.StationAt(25));
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

    [Fact]
    public void 경사_구간은_평면거리로_측점을_매긴다()
    {
        // 3D 50m / 평면 30m. 3D 중간(25m)은 평면 15m 지점이다(docs/215 § A).
        var profile = new AlignmentStationProfile(Alignment([new Point3D(0, 0, 0), new Point3D(30, 0, 40)], [0d, 30d]));

        Assert.Equal(15d, profile.StationAt(25), 9);
        Assert.Equal(30d, profile.StationAtVertex(1), 9);
        Assert.Equal(0, profile.MismatchCount);
    }

    [Fact]
    public void 원본_측점이_없는_경사_구간은_평면_누적거리다()
    {
        var profile = new AlignmentStationProfile(Alignment([new Point3D(0, 0, 0), new Point3D(30, 0, 40)]));

        Assert.False(profile.HasSourceStation);
        Assert.Equal(30d, profile.StationAt(50), 9);
    }

    [Fact]
    public void 역방향_선형은_측점이_감소한다()
    {
        var profile = new AlignmentStationProfile(Alignment([P(0), P(50), P(100)], [200d, 150d, 100d]));

        Assert.Equal(175d, profile.StationAt(25), 9);
        Assert.Equal(210d, profile.StationAt(-10), 9);
        Assert.Equal(0, profile.MismatchCount);
    }

    [Fact]
    public void 같은_측점_앵커_사이는_그_측점이다()
    {
        // 이경관 단차 정점(up/down)은 같은 Station을 받는다(AlignmentDatumOffsetter).
        var profile = new AlignmentStationProfile(Alignment([P(0), P(0.2), P(50.2)], [100d, 100d, 150d]));

        Assert.Equal(100d, profile.StationAt(0.1), 9);
    }

    private static Point3D P(double x) => new(x, 0, 0);

    private static PipeAlignment Alignment(IReadOnlyList<Point3D> vertices, IReadOnlyList<double?>? stations = null) => new(
        vertices, "상수", 100, "sample.xlsx", "1", new Dictionary<string, string>(), VertexStationM: stations);
}
