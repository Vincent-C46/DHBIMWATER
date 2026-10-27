using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>강관 플랜지 볼트 표 — 핸드북 p.264 부도 13A 표본 행 대조.</summary>
public sealed class SteelFlangeBoltSpecTableTests
{
    [Theory]
    // (등급, DN, d, L1, S, B, 세트수)
    [InlineData(SteelFittingClass.F12, 80, 16, 75, 38, 24, 4)]
    [InlineData(SteelFittingClass.F12, 3000, 52, 240, 121, 75, 64)]
    [InlineData(SteelFittingClass.F15, 1000, 33, 140, 84, 55, 28)]
    [InlineData(SteelFittingClass.F15, 2600, 52, 220, 129, 80, 60)]
    [InlineData(SteelFittingClass.F20, 300, 24, 95, 54, 36, 16)]     // B 원문 369 보정
    [InlineData(SteelFittingClass.F20, 1500, 56, 240, 137, 85, 36)]
    [InlineData(SteelFittingClass.None, 500, 24, 100, 54, 36, 20)]   // None = F12
    public void Find_ReturnsHandbookRow(SteelFittingClass cls, double dn, double d, double l1, double s, double b, int sets)
    {
        var spec = SteelFlangeBoltSpecTable.Find(cls, dn);

        Assert.NotNull(spec);
        Assert.Equal(d, spec!.BoltDiameterMm);
        Assert.Equal(l1, spec.LengthL1Mm);
        Assert.Equal(s, spec.ThreadLengthMm);
        Assert.Equal(b, spec.NutWidthMm);
        Assert.Equal(sets, spec.SetCount);
    }

    [Theory]
    [InlineData(SteelFittingClass.F12, 80, 75d)]
    [InlineData(SteelFittingClass.F12, 1500, 180d)]
    [InlineData(SteelFittingClass.F12, 1600, null)]
    [InlineData(SteelFittingClass.F15, 80, null)]
    [InlineData(SteelFittingClass.F20, 80, null)]
    public void LengthL2_OnlyF12UpToDn1500(SteelFittingClass cls, double dn, double? l2)
        => Assert.Equal(l2, SteelFlangeBoltSpecTable.Find(cls, dn)!.LengthL2Mm);

    [Theory]
    [InlineData(SteelFittingClass.F12, 1900)]
    [InlineData(SteelFittingClass.F12, 2900)]
    [InlineData(SteelFittingClass.F15, 2700)]
    [InlineData(SteelFittingClass.F20, 1600)]
    [InlineData(SteelFittingClass.F12, 90)]
    public void Find_ReturnsNull_OutsideTable(SteelFittingClass cls, double dn)
        => Assert.Null(SteelFlangeBoltSpecTable.Find(cls, dn));

    [Fact]
    public void Find_ToleratesHalfMillimetre()
        => Assert.Equal(24d, SteelFlangeBoltSpecTable.Find(SteelFittingClass.F12, 500.3)!.BoltDiameterMm);

    [Fact]
    public void RowCounts_MatchHandbook()
    {
        Assert.Equal(33, SteelFlangeBoltSpecTable.All.Count(x => x.Class == SteelFittingClass.F12));
        Assert.Equal(30, SteelFlangeBoltSpecTable.All.Count(x => x.Class == SteelFittingClass.F15));
        Assert.Equal(20, SteelFlangeBoltSpecTable.All.Count(x => x.Class == SteelFittingClass.F20));
    }

    [Fact]
    public void EveryRow_NutWiderThanBolt()
        => Assert.All(SteelFlangeBoltSpecTable.All, x =>
            Assert.True(x.NutWidthMm > x.BoltDiameterMm, $"{x.Class}/DN{x.Dn} B>d"));
}
