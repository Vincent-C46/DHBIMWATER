using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>외곽 벽체가 실어 온 밸브실 유형(`DH_Zone`) 집계 규칙(docs/161 § A-2).</summary>
public sealed class ValveRoomOutlineZoneTests
{
    [Fact]
    public void Zone_AllWallsSame_ReturnsThatZone()
    {
        var outline = Outline("이토밸브실", "이토밸브실", "이토밸브실", "이토밸브실");

        Assert.Equal("이토밸브실", outline.Zone);
    }

    [Fact]
    public void Zone_BlanksMixedWithOneValue_IsNotTreatedAsConflict()
    {
        // 일부 벽만 밸브실 빌더 산출물인 경우가 정상이다. 빈 값은 혼재가 아니다.
        var outline = Outline("이토밸브실", "", "이토밸브실", "");

        Assert.Equal("이토밸브실", outline.Zone);
    }

    [Fact]
    public void Zone_DifferentValues_ReturnsEmpty()
    {
        var outline = Outline("이토밸브실", "제수밸브실", "", "");

        Assert.Equal(string.Empty, outline.Zone);
    }

    [Fact]
    public void Zone_NoValues_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, Outline("", "", "", "").Zone);
    }

    [Fact]
    public void Translate_KeepsZone()
    {
        var outline = Outline("공기밸브실", "공기밸브실", "공기밸브실", "공기밸브실");

        var moved = outline.Translate(new Point2D(-1000, 500));

        Assert.Equal("공기밸브실", moved.Zone);
    }

    /// <summary>내측면이 원점에서 시작하는 4000×3000 직사각형 외곽. 벽마다 Zone을 다르게 줄 수 있다.</summary>
    private static ValveRoomOutline Outline(params string[] zones)
    {
        const double width = 4000, height = 3000, thickness = 200;
        var bl = new Point2D(0, 0);
        var br = new Point2D(width, 0);
        var tr = new Point2D(width, height);
        var tl = new Point2D(0, height);
        OutlineWall Wall(long id, Point2D a, Point2D b, Point2D oa, Point2D ob, string zone)
            => new(id, a, b, oa, ob, thickness, zone);

        return new ValveRoomOutline(
        [
            Wall(1, bl, br, new Point2D(0, -thickness), new Point2D(width, -thickness), zones[0]),
            Wall(2, br, tr, new Point2D(width + thickness, 0), new Point2D(width + thickness, height), zones[1]),
            Wall(3, tr, tl, new Point2D(width, height + thickness), new Point2D(0, height + thickness), zones[2]),
            Wall(4, tl, bl, new Point2D(-thickness, height), new Point2D(-thickness, 0), zones[3]),
        ]);
    }
}
