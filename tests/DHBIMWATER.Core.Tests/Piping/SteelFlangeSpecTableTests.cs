using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>강관 RF형 플랜지 표 — 핸드북 p.191~194 표본 행 대조.</summary>
public sealed class SteelFlangeSpecTableTests
{
    [Theory]
    // (등급, DN, D5, D4, D3, K)
    [InlineData(SteelFittingClass.F12, 80, 211, 160, 133, 18)]
    [InlineData(SteelFittingClass.F12, 1100, 1366, 1270, 1200, 41)]
    [InlineData(SteelFittingClass.F12, 3000, 3405, 3290, 3210, 76)]
    [InlineData(SteelFittingClass.F15, 400, 582, 515, 477, 28)]
    [InlineData(SteelFittingClass.F15, 2600, 2960, 2850, 2780, 72)]
    [InlineData(SteelFittingClass.F20, 80, 200, 160, 135, 22)]
    [InlineData(SteelFittingClass.F20, 1350, 1700, 1590, 1510, 76)]
    [InlineData(SteelFittingClass.F20, 1500, 1865, 1750, 1670, 80)]
    [InlineData(SteelFittingClass.None, 600, 810, 725, 682, 30)]   // None = F12
    public void Find_ReturnsHandbookRow(SteelFittingClass cls, double dn, double d5, double d4, double d3, double k)
    {
        var spec = SteelFlangeSpecTable.Find(cls, dn);

        Assert.NotNull(spec);
        Assert.Equal(d5, spec!.FlangeDiameterMm);
        Assert.Equal(d4, spec.BoltCircleDiameterMm);
        Assert.Equal(d3, spec.GasketDiameterMm);
        Assert.Equal(k, spec.FlangeThicknessMm);
        Assert.Equal(3d, spec.GasketThicknessMm);
    }

    [Theory]
    [InlineData(SteelFittingClass.F15, 2700)]
    [InlineData(SteelFittingClass.F20, 1600)]
    [InlineData(SteelFittingClass.F12, 65)]
    public void Find_ReturnsNull_OutsideTable(SteelFittingClass cls, double dn)
        => Assert.Null(SteelFlangeSpecTable.Find(cls, dn));

    [Fact]
    public void RowCounts_MatchHandbook()
    {
        Assert.Equal(35, SteelFlangeSpecTable.All.Count(x => x.Class == SteelFittingClass.F12));
        Assert.Equal(31, SteelFlangeSpecTable.All.Count(x => x.Class == SteelFittingClass.F15));
        Assert.Equal(20, SteelFlangeSpecTable.All.Count(x => x.Class == SteelFittingClass.F20));
    }

    [Fact]
    public void EveryRow_IsGeometricallyOrdered()
        => Assert.All(SteelFlangeSpecTable.All, x =>
        {
            Assert.True(x.FlangeDiameterMm > x.BoltCircleDiameterMm, $"{x.Class}/DN{x.Dn} D5>D4");
            Assert.True(x.BoltCircleDiameterMm > x.GasketDiameterMm, $"{x.Class}/DN{x.Dn} D4>D3");
            Assert.True(x.GasketDiameterMm > x.Dn, $"{x.Class}/DN{x.Dn} D3>DN");
        });
}
