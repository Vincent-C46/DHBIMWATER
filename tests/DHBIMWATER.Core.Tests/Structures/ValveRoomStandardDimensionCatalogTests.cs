using DHBIMWATER.Core.Structures;
using Xunit;

namespace DHBIMWATER.Core.Tests.Structures;

public sealed class ValveRoomStandardDimensionCatalogTests
{
    [Fact]
    public void 제수밸브실_DN80_A형은_2400_1500_1800()
    {
        var entry = ValveRoomStandardDimensionCatalog.Find("제수밸브실", 80, "A형");

        Assert.NotNull(entry);
        Assert.Equal("A형", entry.Variant);
        Assert.Equal(1, entry.Group);
        Assert.Equal(2400d, entry.LengthMm);
        Assert.Equal(1500d, entry.WidthMm);
        Assert.Equal(1800d, entry.HeightMm);
        Assert.Equal("1.5x1.8", entry.Section);
    }

    [Fact]
    public void 제수밸브실_B형_DN80은_길이_3400()
    {
        Assert.Equal(3400d, ValveRoomStandardDimensionCatalog.Find("제수밸브실", 80, "B형")!.LengthMm);
    }

    [Fact]
    public void A형에_없는_관경은_B형으로_넘어간다()
    {
        var entry = ValveRoomStandardDimensionCatalog.Find("제수밸브실", 600, "A형");

        Assert.Equal("B형", entry!.Variant);
        Assert.Equal(2200d, entry.HeightMm);
    }

    [Fact]
    public void 표_밖_관경은_null()
    {
        Assert.Null(ValveRoomStandardDimensionCatalog.Find("타이밸브실", 1350));
        Assert.Null(ValveRoomStandardDimensionCatalog.Find("이토밸브실", 1350));
    }

    [Theory]
    [InlineData("이토밸브실", 300, null, 1500, 1500, 1900)]
    [InlineData("타이밸브실", 700, null, 2000, 2500, 3200)]
    [InlineData("타이밸브실", 1200, null, 2500, 3000, 4500)]
    [InlineData("감압밸브실", 600, null, 8000, 3000, 1800)]
    [InlineData("공기밸브실", 3000, null, 2000, 2200, 1900)]
    [InlineData("유량계실", 300, "전자식", 5000, 2500, 1900)]
    [InlineData("유량계실", 3000, "초음파", 4000, 4200, 4400)]
    [InlineData("유량계실및실험실겸용", 2800, "초음파", 7500, 4000, 4200)]
    public void 표_4_2_부터_4_6_값(string kind, double dn, string? variant, double length, double width, double height)
    {
        var entry = ValveRoomStandardDimensionCatalog.Find(kind, dn, variant);

        Assert.NotNull(entry);
        Assert.Equal(length, entry.LengthMm);
        Assert.Equal(width, entry.WidthMm);
        Assert.Equal(height, entry.HeightMm);
    }

    [Fact]
    public void 표_6개_행_수와_형식_목록()
    {
        // PDF 표 행 수 그대로.
        Assert.Equal(43, ValveRoomStandardDimensionCatalog.Entries.Count(x => x.RoomKind == "제수밸브실"));
        Assert.Equal(18, ValveRoomStandardDimensionCatalog.Entries.Count(x => x.RoomKind == "이토밸브실"));
        Assert.Equal(10, ValveRoomStandardDimensionCatalog.Entries.Count(x => x.RoomKind == "감압밸브실"));
        Assert.Equal(35, ValveRoomStandardDimensionCatalog.Entries.Count(x => x.RoomKind == "공기밸브실"));
        Assert.Equal(44, ValveRoomStandardDimensionCatalog.Entries.Count(x => x.RoomKind == "유량계실"));
        Assert.Equal(44, ValveRoomStandardDimensionCatalog.Entries.Count(x => x.RoomKind == "유량계실및실험실겸용"));
        Assert.Equal(["A형", "B형"], ValveRoomStandardDimensionCatalog.Variants("제수밸브실"));
        Assert.Equal(["전자식", "초음파"], ValveRoomStandardDimensionCatalog.Variants("유량계실"));
        Assert.Empty(ValveRoomStandardDimensionCatalog.Variants("이토밸브실"));
    }

    [Fact]
    public void 같은_유형_형식_안에서_관경_중복이_없다()
    {
        var duplicates = ValveRoomStandardDimensionCatalog.Entries
            .GroupBy(x => (x.RoomKind, x.Variant, x.DiameterMm))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }
}
