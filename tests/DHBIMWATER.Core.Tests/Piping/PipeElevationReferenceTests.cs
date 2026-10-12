using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

public sealed class PipeElevationReferenceTests
{
    private static readonly LevelInfo[] Levels = [new(1, "Lower", 0), new(2, "Upper", 3000), new(3, "Basement", -2500)];

    [Fact]
    public void PickBaseLevelId_SingleLevel_ReturnsThatId()
    {
        Assert.Equal(1, PipeElevationReference.PickBaseLevelId([1, 1], Levels, out var mixed));
        Assert.False(mixed);
    }

    [Fact]
    public void PickBaseLevelId_MostCommonLevelWins()
        => Assert.Equal(2, PipeElevationReference.PickBaseLevelId([2, 1, 2], Levels, out _));

    [Fact]
    public void PickBaseLevelId_TieChoosesLowestElevation()
        => Assert.Equal(3, PipeElevationReference.PickBaseLevelId([1, 3], Levels, out var mixed));

    [Fact]
    public void PickBaseLevelId_TieReportsMixed()
    {
        PipeElevationReference.PickBaseLevelId([1, 3], Levels, out var mixed);
        Assert.True(mixed);
    }

    [Fact]
    public void PickBaseLevelId_IgnoresZeroAndUnknownIds()
        => Assert.Equal(1, PipeElevationReference.PickBaseLevelId([0, 99, 1], Levels, out var mixed));

    [Fact]
    public void PickBaseLevelId_NoKnownIds_ReturnsNull()
        => Assert.Null(PipeElevationReference.PickBaseLevelId([0, 99], Levels, out var mixed));

    [Fact]
    public void OutlineTranslate_PreservesWallBaseLevelId()
    {
        var wall = new OutlineWall(1, new(0, 0), new(1, 0), new(0, -1), new(1, -1), 1, BaseLevelId: 42);
        var translated = new ValveRoomOutline([wall]).Translate(new(10, 20));

        Assert.Equal(42, Assert.Single(translated.Walls).BaseLevelId);
    }
}
