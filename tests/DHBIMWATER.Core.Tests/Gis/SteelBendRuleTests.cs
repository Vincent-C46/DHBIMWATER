using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Gis;
using Xunit;

namespace DHBIMWATER.Core.Tests.Gis;

/// <summary>docs/211 § D — 강관 판정: 편각 1° 이하 무곡관, 각도 허용차(90°·45° ±2°, 그 이하 ±1°), 5⅝°는 DN1000 이상만.</summary>
public class SteelBendRuleTests
{
    private static NodeClassification Node(double deflectionDeg, double dn, string kind = PipeKindCatalog.Steel290)
        => new(1, new Point3D(0, 0, 0), NodeKind.Bend, 2, deflectionDeg, dn, dn, kind);

    private static BendSettings Settings(BendConnection connection = BendConnection.Socket, SteelFittingClass fittingClass = SteelFittingClass.F12)
        => BendSettings.Default with { ActiveBendConnection = connection, ActiveSteelFittingClass = fittingClass };

    [Fact]
    public void Deflection_at_or_below_one_degree_needs_no_bend()
    {
        var result = BendResolver.Resolve(Node(0.8, 300), Settings());
        Assert.Equal(BendResolutionKind.None, result.Kind);
        Assert.True(result.IsAcceptable);
        Assert.False(result.HasFittingSize);
        Assert.Equal(SteelBendRule.NoBendThresholdDeg, result.EffectiveAllowableDeg);
    }

    [Fact]
    public void Small_deflection_below_dn1000_picks_11_25_and_is_unresolved()
    {
        // DN300에는 5⅝° 행이 없어 최소각이 11¼°다. 잔여 |1.2−11.25| > 1° → 허용 초과.
        var result = BendResolver.Resolve(Node(1.2, 300), Settings());
        Assert.Equal(11.25, result.StandardAngleDeg);
        Assert.Equal(BendResolutionKind.Unresolved, result.Kind);
        Assert.False(result.IsAcceptable);
        Assert.True(result.HasFittingSize);
        Assert.Equal(1290, result.CenterlineRadiusMm);
        Assert.Equal(250, result.LayingLengthMm);
    }

    [Fact]
    public void Dn1000_uses_5_625_within_one_degree()
    {
        var result = BendResolver.Resolve(Node(6.0, 1000, PipeKindCatalog.Steel400A), Settings());
        Assert.Equal(5.625, result.StandardAngleDeg);
        Assert.Equal(BendResolutionKind.Standard, result.Kind);
        Assert.True(result.IsAcceptable);
        Assert.Equal(1.0, result.AllowableDeg);
        Assert.Equal(5690, result.CenterlineRadiusMm);
        Assert.Equal(1000, result.LayingLengthMm);
        Assert.Equal(8.0, result.WallThicknessMm);
        Assert.Equal(398, result.WeightKg);
    }

    [Theory]
    [InlineData(91.5, true)]
    [InlineData(92.5, false)]
    [InlineData(88.0, true)]
    [InlineData(87.9, false)]
    public void Ninety_degree_bend_tolerates_two_degrees(double deflection, bool acceptable)
    {
        var result = BendResolver.Resolve(Node(deflection, 300), Settings());
        Assert.Equal(90, result.StandardAngleDeg);
        Assert.Equal(acceptable, result.IsAcceptable);
        Assert.Equal(acceptable ? BendResolutionKind.Standard : BendResolutionKind.Unresolved, result.Kind);
        Assert.Equal(2.0, result.AllowableDeg);
    }

    [Fact]
    public void Forced_angle_is_honored_and_judged_by_tolerance()
    {
        var ok = BendResolver.Resolve(Node(44.0, 300), Settings(), forcedAngleDeg: 45);
        Assert.Equal(45, ok.StandardAngleDeg);
        Assert.True(ok.IsAcceptable);

        // DN800에는 5⅝° 행이 없다 — 지정각을 그대로 적되 치수 없이 미해결.
        var missing = BendResolver.Resolve(Node(5.5, 800, PipeKindCatalog.Steel400A), Settings(), forcedAngleDeg: 5.625);
        Assert.Equal(5.625, missing.StandardAngleDeg);
        Assert.False(missing.HasFittingSize);
        Assert.Equal(BendResolutionKind.Unresolved, missing.Kind);
    }

    [Fact]
    public void Flanged_connection_uses_same_dimensions_and_fitting_class_changes_thickness_and_weight()
    {
        var weld = BendResolver.Resolve(Node(90, 80), Settings(BendConnection.Socket, SteelFittingClass.F12));
        var flanged = BendResolver.Resolve(Node(90, 80), Settings(BendConnection.Flanged, SteelFittingClass.F20));
        Assert.Equal(weld.CenterlineRadiusMm, flanged.CenterlineRadiusMm);
        Assert.Equal(weld.LayingLengthMm, flanged.LayingLengthMm);
        Assert.Equal(4.2, weld.WallThicknessMm);
        Assert.Equal(6.24, weld.WeightKg);
        Assert.Equal(4.5, flanged.WallThicknessMm);
        Assert.Equal(6.66, flanged.WeightKg);
        Assert.Equal("용접", weld.JointType);
        Assert.Equal("플랜지", flanged.JointType);
        Assert.Equal(0d, weld.ExtraLegLengthMm);
        Assert.Equal(0d, weld.LongLegLengthMm - weld.LayingLengthMm);
    }

    [Fact]
    public void Unknown_dn_yields_unresolved_without_size()
    {
        var result = BendResolver.Resolve(Node(45, 75), Settings());
        Assert.Equal(BendResolutionKind.Unresolved, result.Kind);
        Assert.False(result.HasFittingSize);
        Assert.Equal(45, result.StandardAngleDeg);
    }

    /// <summary>주철관 절점은 규칙 분기 통일 뒤에도 기존대로 판정된다(회귀).</summary>
    [Fact]
    public void Ductile_iron_nodes_still_use_joint_deflection_rule()
    {
        var result = BendResolver.Resolve(Node(3.0, 300, PipeKindCatalog.Water1), BendSettings.Default);
        Assert.Equal(BendResolutionKind.None, result.Kind);   // KP 메커니컬 DN300 허용굴곡 4°
        Assert.Equal(JointTypeCatalog.KpMechanical, result.JointType);
    }

    [Fact]
    public void Steel_rule_is_registered_and_exposes_standard_angles()
    {
        var rule = BendingRuleCatalog.For(PipeMaterial.Steel);
        Assert.IsType<SteelBendRule>(rule);
        Assert.Equal(new[] { 5.625, 11.25, 22.5, 45d, 90d }, rule.StandardAngles);
        Assert.Equal(new[] { 11.25, 22.5, 45d, 90d }, BendingRuleCatalog.For(PipeMaterial.DuctileIron).StandardAngles);
    }
}
