using DHBIMWATER.Core.Structures;
using Xunit;

namespace DHBIMWATER.Core.Tests.Structures;

/// <summary>편집 제원표 조회·저장 전 검사(docs/312).</summary>
public sealed class ValveRoomDimensionTableTests
{
    [Fact]
    public void 내장_표준표는_검사를_통과한다()
        => Assert.Null(ValveRoomStandardDimensionCatalog.Default.Validate());

    [Fact]
    public void 편집_표에서_고친_값을_찾는다()
    {
        var table = new ValveRoomDimensionTable([new ValveRoomStandardDimension("이토밸브실", null, 4, 1200, 3300, 2600, 4700, "2.5x4.5")]);

        var entry = table.Find("이토밸브실", 1200);

        Assert.NotNull(entry);
        Assert.Equal(3300d, entry.LengthMm);
    }

    [Fact]
    public void 치수가_0이면_검사에_걸린다()
    {
        var table = new ValveRoomDimensionTable([new ValveRoomStandardDimension("공기밸브실", null, 1, 100, 0, 1500, 1800, "")]);

        Assert.Contains("0보다 커야", table.Validate());
    }

    [Fact]
    public void 같은_유형_형식_관경이_두_행이면_검사에_걸린다()
    {
        var row = new ValveRoomStandardDimension("제수밸브실", "B형", 2, 100, 3400, 1500, 1800, "");
        var table = new ValveRoomDimensionTable([row, row with { LengthMm = 3500 }]);

        Assert.Contains("2개", table.Validate());
    }

    [Fact]
    public void 형식이_다르면_같은_관경도_허용한다()
    {
        var row = new ValveRoomStandardDimension("제수밸브실", "A형", 1, 100, 2400, 1500, 1800, "");
        var table = new ValveRoomDimensionTable([row, row with { Variant = "B형", LengthMm = 3400 }]);

        Assert.Null(table.Validate());
    }
}
