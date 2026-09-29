using DHBIMWATER.Core.Gis;
using Xunit;

namespace DHBIMWATER.Core.Tests.Gis;

/// <summary>docs/211 § A — 등급 축에 강관(STWW) 4종을 더했다. 등급 → 관종, 별칭 → 기본 등급.</summary>
public class PipeKindCatalogTests
{
    [Theory]
    [InlineData("상수 1종관", PipeMaterial.DuctileIron)]
    [InlineData("하수 3종관", PipeMaterial.DuctileIron)]
    [InlineData("STWW 290", PipeMaterial.Steel)]
    [InlineData("STWW 370", PipeMaterial.Steel)]
    [InlineData("STWW 400 A", PipeMaterial.Steel)]
    [InlineData("stww400b", PipeMaterial.Steel)]
    public void MaterialOf_maps_grade_to_material(string kind, PipeMaterial expected)
        => Assert.Equal(expected, PipeKindCatalog.MaterialOf(kind));

    [Theory]
    [InlineData("강관")]
    [InlineData("SP")]
    [InlineData("STWW")]
    [InlineData("")]
    [InlineData(null)]
    public void MaterialOf_returns_null_for_non_grades(string? kind)
        => Assert.Null(PipeKindCatalog.MaterialOf(kind));

    /// <summary>docs/212 § A — 필드·수동·직경 셀 관종 어느 후보든 강관 별칭이면 DN 기본 등급. 정규화되는 등급이 우선.</summary>
    [Theory]
    [InlineData(null, null, "강관", 300, "STWW 290", true, true)]
    [InlineData("STWW 400 A", null, "강관", 500, "STWW 400 A", true, false)]
    [InlineData("미상", null, "강관", 500, "STWW 400 A", true, true)]
    [InlineData(null, null, "SP", 0, "STWW 290", true, true)]
    [InlineData("PE", null, null, 300, "PE", false, false)]
    [InlineData("상수 2종관", "강관", null, 300, "상수 2종관", true, false)]
    public void ResolveGrade_UsesAnyCandidateSteelAlias(string? field, string? manual, string? parsed, double dn,
        string expectedKind, bool expectedResolved, bool expectedSteelDefault)
    {
        var grade = PipeKindCatalog.ResolveGrade(field, manual, parsed, dn);
        Assert.Equal(expectedKind, grade.Kind);
        Assert.Equal(expectedResolved, grade.IsResolved);
        Assert.Equal(expectedSteelDefault, grade.IsSteelDefault);
    }

    [Theory]
    [InlineData("stww 400 a", "STWW 400 A")]
    [InlineData("STWW400B", "STWW 400 B")]
    [InlineData(" STWW 290 ", "STWW 290")]
    [InlineData("상수1종관", "상수 1종관")]
    public void Normalize_is_whitespace_and_case_insensitive(string raw, string expected)
        => Assert.Equal(expected, PipeKindCatalog.Normalize(raw));

    [Fact]
    public void All_lists_ductile_iron_then_steel()
    {
        Assert.Equal(11, PipeKindCatalog.All.Count);
        Assert.Equal(PipeKindCatalog.Water1, PipeKindCatalog.All[0]);
        Assert.Equal(PipeKindCatalog.Steel290, PipeKindCatalog.All[7]);
        Assert.Equal(PipeKindCatalog.SteelAll, PipeKindCatalog.AllFor(PipeMaterial.Steel));
        Assert.Equal(PipeKindCatalog.DuctileIronAll, PipeKindCatalog.AllFor(PipeMaterial.DuctileIron));
        Assert.Empty(PipeKindCatalog.AllFor(PipeMaterial.Pe));
    }

    /// <summary>docs/211 § 0 확정 7 — DN≤300은 STWW 290, 그 밖은 STWW 400 A.</summary>
    [Theory]
    [InlineData(80, "STWW 290")]
    [InlineData(300, "STWW 290")]
    [InlineData(350, "STWW 400 A")]
    [InlineData(3000, "STWW 400 A")]
    [InlineData(0, "STWW 290")]
    public void DefaultSteelGrade_splits_at_dn300(double dn, string expected)
        => Assert.Equal(expected, PipeKindCatalog.DefaultSteelGrade(dn));

    [Theory]
    [InlineData("SP", true)]
    [InlineData("강관", true)]
    [InlineData("STWW 290", false)]
    [InlineData("덕타일주철관", false)]
    [InlineData("DCIP", false)]
    [InlineData(null, false)]
    public void IsSteelAlias_recognizes_material_code_and_label_only(string? value, bool expected)
        => Assert.Equal(expected, PipeKindCatalog.IsSteelAlias(value));
}
