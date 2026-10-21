using DHBIMWATER.Core.Gis;
using DHBIMWATER.Shared.Errors;
using Xunit;

namespace DHBIMWATER.Core.Tests.Gis;

public sealed class AlignmentStationLocatorTests
{
    [Fact]
    public void 행_사이는_측점_비율로_보간한다()
    {
        var point = AlignmentStationLocator.Locate([Row(0, 0, 0, 10), Row(100, 100, 0, 11)], 50);

        Assert.Equal(50d, point.X, 9);
        Assert.Equal(0d, point.Y, 9);
        Assert.Equal(10.5d, point.Z, 9);
        Assert.Equal(1d, point.DirX, 9);
        Assert.Equal(0d, point.DirY, 9);
    }

    [Fact]
    public void 정점과_같은_측점은_다음_구간_방향을_쓴다()
    {
        var point = AlignmentStationLocator.Locate([Row(0, 0, 0), Row(100, 100, 0), Row(200, 100, 100)], 100);

        Assert.Equal(1, point.SegmentIndex);
        Assert.Equal(0d, point.DirX, 9);
        Assert.Equal(1d, point.DirY, 9);
    }

    [Fact]
    public void 마지막_측점은_마지막_구간_방향을_쓴다()
    {
        var point = AlignmentStationLocator.Locate([Row(0, 0, 0), Row(100, 100, 0), Row(200, 100, 100)], 200);

        Assert.Equal(1, point.SegmentIndex);
        Assert.Equal(100d, point.Y, 9);
        Assert.Equal(1d, point.DirY, 9);
    }

    [Fact]
    public void 평면_길이_0_구간은_인접_구간_방향을_쓴다()
    {
        // 같은 XY에서 측점만 늘어난 행(수직 이동 등).
        var point = AlignmentStationLocator.Locate([Row(0, 0, 0), Row(10, 0, 0), Row(110, 0, 100)], 5);

        Assert.Equal(0d, point.DirX, 9);
        Assert.Equal(1d, point.DirY, 9);
    }

    [Fact]
    public void 범위_밖_측점은_VR502()
    {
        var ex = Assert.Throws<DhbimwaterException>(() => AlignmentStationLocator.Locate([Row(0, 0, 0), Row(100, 100, 0)], 100.5));
        Assert.Equal(ErrorCodes.VR_PlacementStationOutOfRange, ex.Code);
    }

    [Fact]
    public void 측점_역행은_VR503()
    {
        var ex = Assert.Throws<DhbimwaterException>(() => AlignmentStationLocator.Locate([Row(0, 0, 0), Row(100, 100, 0), Row(50, 200, 0)], 10));
        Assert.Equal(ErrorCodes.VR_PlacementProfileInvalid, ex.Code);
    }

    [Fact]
    public void 행이_1개면_VR503()
    {
        var ex = Assert.Throws<DhbimwaterException>(() => AlignmentStationLocator.Locate([Row(0, 0, 0)], 0));
        Assert.Equal(ErrorCodes.VR_PlacementProfileInvalid, ex.Code);
    }

    private static AlignmentProfileRow Row(double station, double x, double y, double z = 0)
        => new(station, x, y, z);
}
