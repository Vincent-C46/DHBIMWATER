using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

public sealed class JunctionValveRoomTests
{
    private static JunctionWallFootprint Wall(long id, double minX, double minY, double maxX, double maxY)
        => new(id, [new(minX, minY), new(maxX, minY), new(maxX, maxY), new(minX, maxY)]);

    // 안쪽 2400 × 1800, 벽 두께 300 사각 밸브실
    private static readonly JunctionWallFootprint[] Room =
    [
        Wall(1, -300, -300, 0, 2100),      // 왼쪽
        Wall(2, 2400, -300, 2700, 2100),   // 오른쪽
        Wall(3, -300, -300, 2700, 0),      // 아래
        Wall(4, -300, 1800, 2700, 2100)    // 위
    ];

    [Fact]
    public void Resolve_FindsFourWalls_InnerAndOuterBounds()
    {
        var room = JunctionValveRoom.Resolve(Room, new ShapePoint(1200, 900));

        Assert.NotNull(room);
        Assert.Equal(4, room.Walls.Count);
        Assert.Equal((2400d, 1800d), (room.InnerWidth, room.InnerHeight));
        Assert.Equal((-300d, -300d, 2700d, 2100d), (room.OuterMinX, room.OuterMinY, room.OuterMaxX, room.OuterMaxY));
        Assert.Equal("2.4 x 1.8", room.Label);
    }

    [Fact]
    public void Resolve_PicksNearestWall_IgnoresFartherOnes()
    {
        var walls = Room.Append(Wall(5, 5000, -300, 5300, 2100)).ToList();
        var room = JunctionValveRoom.Resolve(walls, new ShapePoint(1200, 900));
        Assert.Equal(2400, room!.InnerMaxX);
    }

    [Fact]
    public void Resolve_MissingSide_ReturnsNull()
        => Assert.Null(JunctionValveRoom.Resolve(Room.Take(3).ToList(), new ShapePoint(1200, 900)));

    [Fact]
    public void ResolveMajority_IgnoresMembersOutsideRoom()
    {
        var room = JunctionValveRoom.ResolveMajority(Room, [new(500, 500), new(1200, 900), new(2000, 1200), new(5000, 900)], null);
        Assert.NotNull(room);
        Assert.Equal((2400d, 1800d), (room.InnerWidth, room.InnerHeight));
    }

    [Fact]
    public void ResolveMajority_AllInside_SameAsCenter()
    {
        var points = new[] { new ShapePoint(500, 500), new ShapePoint(2000, 1200) };
        var majority = JunctionValveRoom.ResolveMajority(Room, points, null);
        var center = JunctionValveRoom.Resolve(Room, new ShapePoint(1250, 850));
        Assert.Equal(center!.Walls.Select(x => x.Id).Order(), majority!.Walls.Select(x => x.Id).Order());
    }

    [Fact]
    public void ResolveMajority_Tie_PrefersRoomWithRoot()
    {
        var secondRoom = new[]
        {
            Wall(11, 4700, -300, 5000, 2100), Wall(12, 7400, -300, 7700, 2100),
            Wall(13, 4700, -300, 7700, 0), Wall(14, 4700, 1800, 7700, 2100)
        };
        var points = new[] { new ShapePoint(1200, 900), new ShapePoint(6200, 900) };
        var room = JunctionValveRoom.ResolveMajority(Room.Concat(secondRoom).ToList(), points, points[1]);
        Assert.Equal(2400, room!.InnerWidth);
        Assert.Equal(4700, room.OuterMinX);
    }

    [Fact]
    public void ResolveMajority_NoPointInside_ReturnsNull()
        => Assert.Null(JunctionValveRoom.ResolveMajority(Room, [new(5000, 900)], null));

    [Fact]
    public void ExplainMajorityFailure_PrependsNoMemberInsideMessage()
    {
        var message = JunctionValveRoom.ExplainMajorityFailure(Room, new ShapePoint(5000, 900));
        Assert.StartsWith("밸브실 벽 없음: 어느 부재도 4면 벽 안에 없습니다. ", message);
        Assert.Contains("벽이 부재 중심", message);
        Assert.Equal("밸브실 벽 없음: 부재 주변 10m 안에 직선 벽이 없습니다.",
            JunctionValveRoom.ExplainMajorityFailure([], new ShapePoint(0, 0)));
    }

    // 못 찾은 면을 이유로 알려줌, 찾으면 null (docs/299)
    [Fact]
    public void Explain_NamesMissingSides()
    {
        Assert.Null(JunctionValveRoom.Explain(Room, new ShapePoint(1200, 900)));
        Assert.Contains("벽 없음", JunctionValveRoom.Explain([], new ShapePoint(0, 0)));
        // 중심이 실 밖 왼쪽 → 좌측 벽 없음
        Assert.Contains("좌측", JunctionValveRoom.Explain(Room, new ShapePoint(-5000, 900)));
    }

    // 세로 선 = 평면에서 점 → 기호선에서 제외 (docs/299)
    [Fact]
    public void IsVisibleInPlan_DropsZeroLengthLines()
    {
        Assert.False(JunctionSymbolShapeMath.IsVisibleInPlan([new ShapePoint(5, 5), new ShapePoint(5, 5)]));
        Assert.True(JunctionSymbolShapeMath.IsVisibleInPlan([new ShapePoint(0, 0), new ShapePoint(10, 0)]));
    }

    [Fact]
    public void Label_RoundsToCentimeters()
    {
        var room = new JunctionValveRoom([], 0, 0, 2450, 1234, 0, 0, 0, 0);
        Assert.Equal("2.45 x 1.23", room.Label);
    }
}
