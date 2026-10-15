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
    public void Label_RoundsToCentimeters()
    {
        var room = new JunctionValveRoom([], 0, 0, 2450, 1234, 0, 0, 0, 0);
        Assert.Equal("2.45 x 1.23", room.Label);
    }
}
