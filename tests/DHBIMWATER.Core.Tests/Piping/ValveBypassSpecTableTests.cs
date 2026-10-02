using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>밸브부관 핸드북 규격표 — 주관 → 바이패스(지관) 관경(docs/256).</summary>
public sealed class ValveBypassSpecTableTests
{
    [Theory]
    [InlineData(300, 100)]
    [InlineData(600, 100)]
    [InlineData(700, 150)]
    [InlineData(1100, 200)]
    [InlineData(1200, 250)]
    public void Ductile_MainToBranch(double main, double branch)
        => Assert.Equal(branch, ValveBypassSpecTable.BranchDiameterMm(PipeMaterial.DuctileIron, SteelFittingClass.None, main));

    [Theory]
    [InlineData(250)]
    [InlineData(1300)]
    [InlineData(1350)]
    public void Ductile_OutOfRange_IsNull(double main)
        => Assert.Null(ValveBypassSpecTable.BranchDiameterMm(PipeMaterial.DuctileIron, SteelFittingClass.F12, main));

    [Theory]
    [InlineData(SteelFittingClass.F12, 400, 100)]
    [InlineData(SteelFittingClass.F12, 1350, 250)]
    [InlineData(SteelFittingClass.F12, 3000, 500)]
    [InlineData(SteelFittingClass.F15, 2600, 500)]
    [InlineData(SteelFittingClass.F20, 1500, 300)]
    [InlineData(SteelFittingClass.None, 400, 100)]
    public void Steel_MainToBranch(SteelFittingClass cls, double main, double branch)
        => Assert.Equal(branch, ValveBypassSpecTable.BranchDiameterMm(PipeMaterial.Steel, cls, main));

    [Theory]
    [InlineData(SteelFittingClass.F12, 300)]
    [InlineData(SteelFittingClass.F12, 2900)]
    [InlineData(SteelFittingClass.F12, 3100)]
    [InlineData(SteelFittingClass.F15, 2700)]
    [InlineData(SteelFittingClass.F20, 1600)]
    public void Steel_OutOfRange_IsNull(SteelFittingClass cls, double main)
        => Assert.Null(ValveBypassSpecTable.BranchDiameterMm(PipeMaterial.Steel, cls, main));

    [Fact]
    public void Steel_NoneClass_SameAsF12()
    {
        foreach (var row in ValveBypassSpecTable.SteelTees.Where(x => x.Class == SteelFittingClass.F12))
            Assert.Equal(row.BranchDn, ValveBypassSpecTable.BranchDiameterMm(PipeMaterial.Steel, SteelFittingClass.None, row.MainDn));
    }

    [Theory]
    [InlineData(PipeMaterial.Pe)]
    [InlineData(PipeMaterial.Pvc)]
    public void PeAndPvc_HaveNoSpec(PipeMaterial material)
    {
        Assert.False(ValveBypassSpecTable.HasSpec(material));
        Assert.Null(ValveBypassSpecTable.BranchDiameterMm(material, SteelFittingClass.F12, 400));
    }

    [Fact]
    public void Ductile_TeeAndBendTables_AgreeOnBranch()
    {
        Assert.Equal(ValveBypassSpecTable.DuctileTees.Count, ValveBypassSpecTable.DuctileBends.Count);
        Assert.Equal(
            ValveBypassSpecTable.DuctileTees.Select(x => (x.MainDn, x.BranchDn)),
            ValveBypassSpecTable.DuctileBends.Select(x => (x.MainDn, x.BranchDn)));
    }

    [Theory]
    [InlineData(SteelFittingClass.F12, 25)]
    [InlineData(SteelFittingClass.F15, 22)]
    [InlineData(SteelFittingClass.F20, 12)]
    public void Steel_TeeAndBendTables_AgreeOnBranch_PerClass(SteelFittingClass cls, int rows)
    {
        var tees = ValveBypassSpecTable.SteelTees.Where(x => x.Class == cls).Select(x => (x.MainDn, x.BranchDn)).ToList();
        var bends = ValveBypassSpecTable.SteelBends.Where(x => x.Class == cls).Select(x => (x.MainDn, x.BranchDn)).ToList();
        Assert.Equal(rows, tees.Count);
        Assert.Equal(tees, bends);
    }

    [Fact]
    public void BranchDn_IsSameAcrossMaterialsAndClasses_WhereRangesOverlap()
    {
        foreach (var row in ValveBypassSpecTable.SteelTees)
        {
            var f12 = ValveBypassSpecTable.BranchDiameterMm(PipeMaterial.Steel, SteelFittingClass.F12, row.MainDn);
            Assert.Equal(f12, row.BranchDn);
            var ductile = ValveBypassSpecTable.BranchDiameterMm(PipeMaterial.DuctileIron, SteelFittingClass.None, row.MainDn);
            if (ductile is not null) Assert.Equal(ductile, row.BranchDn);
        }
    }

    [Fact]
    public void RangeText_DescribesTableRange()
    {
        Assert.Equal("덕타일주철관 DN300~1200", ValveBypassSpecTable.RangeText(PipeMaterial.DuctileIron, SteelFittingClass.None));
        Assert.Equal("강관 F20 400~1500", ValveBypassSpecTable.RangeText(PipeMaterial.Steel, SteelFittingClass.F20));
        Assert.Equal("강관 F12 400~3000", ValveBypassSpecTable.RangeText(PipeMaterial.Steel, SteelFittingClass.None));
        Assert.Contains("핸드북 규격 없음", ValveBypassSpecTable.RangeText(PipeMaterial.Pe, SteelFittingClass.F12));
    }

    [Fact]
    public void NetworkDefinition_CarriesSteelFittingClass()
    {
        var network = new PipeNetwork();
        Assert.Equal(SteelFittingClass.F12, network.ToDefinition(300, PipeOutputMode.PipeAccessorySegment, new(0, 0)).SteelFittingClass);
        Assert.Equal(SteelFittingClass.F20,
            network.ToDefinition(300, PipeOutputMode.PipeAccessorySegment, new(0, 0), steelFittingClass: SteelFittingClass.F20).SteelFittingClass);
    }
}
