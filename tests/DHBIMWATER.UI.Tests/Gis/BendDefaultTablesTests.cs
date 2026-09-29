using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using DHBIMWATER.Core.Gis;
using Xunit;

namespace DHBIMWATER.UI.Tests.Gis;

public class BendDefaultTablesTests
{
    /// <summary>사용자 제공 허용굴곡표(2026-08-18)의 구간 경계 DN을 직접 확인한다.</summary>
    [Theory]
    // KP 메커니컬 — 80~150 / 200~300 / 350~500 / 600~700 / 800~1200
    [InlineData("KP 메커니컬 조인트", 80, 5.0)]
    [InlineData("KP 메커니컬 조인트", 150, 5.0)]
    [InlineData("KP 메커니컬 조인트", 200, 4.0)]
    [InlineData("KP 메커니컬 조인트", 300, 4.0)]
    [InlineData("KP 메커니컬 조인트", 350, 3.0)]
    [InlineData("KP 메커니컬 조인트", 500, 3.0)]
    [InlineData("KP 메커니컬 조인트", 600, 2.0)]
    [InlineData("KP 메커니컬 조인트", 700, 2.0)]
    [InlineData("KP 메커니컬 조인트", 800, 1.5)]
    [InlineData("KP 메커니컬 조인트", 1200, 1.5)]
    // 타이튼 — 80~300 / 350~400 / 450~600 / 700~900 / 1000~1200
    [InlineData("타이튼 조인트", 80, 5.0)]
    [InlineData("타이튼 조인트", 300, 5.0)]
    [InlineData("타이튼 조인트", 350, 4.0)]
    [InlineData("타이튼 조인트", 400, 4.0)]
    [InlineData("타이튼 조인트", 450, 3.0)]
    [InlineData("타이튼 조인트", 600, 3.0)]
    [InlineData("타이튼 조인트", 700, 2.5)]
    [InlineData("타이튼 조인트", 900, 2.5)]
    [InlineData("타이튼 조인트", 1000, 2.0)]
    [InlineData("타이튼 조인트", 1200, 2.0)]
    public void JointDeflection_default_matches_handbook_ranges(string jointType, double dn, double expected)
        => Assert.Equal(expected, JointDeflectionTable.Default.AllowableFor(jointType, dn));

    [Fact]
    public void JointDeflection_default_covers_every_dn_for_both_joint_types()
    {
        foreach (var jointType in JointTypeCatalog.All)
            foreach (var dn in StraightPipeSpecTable.DuctileIronNominalDiameters)
                Assert.NotNull(JointDeflectionTable.Default.AllowableFor(jointType, dn));
    }

    /// <summary>배관길이 t가 접선길이보다 짧으면 호가 곡관 몸통 밖으로 나가 전 절점이 치수 불일치로 보고된다. 주철관·강관 전 행.</summary>
    [Fact]
    public void Handbook_fittings_are_size_consistent_for_every_dn_angle_form()
    {
        foreach (var entry in BendFittingCatalog.Default.Entries)
        {
            var tangent = BendResolver.TangentLength(entry.CenterlineRadiusMm, entry.AngleDeg);
            Assert.True(entry.LayingLengthMm >= tangent,
                $"{entry.Material} DN{entry.DiameterMm} {entry.AngleDeg}° {entry.Form}: 배관길이 {entry.LayingLengthMm} < 접선길이 {tangent}");
            Assert.True(entry.Material is PipeMaterial.DuctileIron or PipeMaterial.Steel);
        }
    }

    /// <summary>핸드북 소켓곡관 표(docs/주철관핸드북(2020년판)_이형관.pdf)의 대표 행을 직접 확인한다.</summary>
    [Theory]
    // 4. 90° 소켓곡관
    [InlineData(80, 90, 7.0, 75, 150)]
    [InlineData(1200, 90, 20.4, 1165, 1270)]
    // 5. 45° 소켓곡관
    [InlineData(300, 45, 9.6, 300, 175)]
    [InlineData(1200, 45, 20.4, 1195, 575)]
    // 6. 22½° 소켓곡관
    [InlineData(500, 22.5, 12.0, 495, 155)]
    // 7. 11¼° 소켓곡관
    [InlineData(100, 11.25, 7.2, 115, 55)]
    [InlineData(1200, 11.25, 20.4, 1195, 175)]
    public void Bend_default_matches_handbook_rows(double dn, double angle, double e, double radius, double laying)
    {
        var entry = BendFittingCatalog.Default.Find(dn, angle);
        Assert.NotNull(entry);
        Assert.Equal(e, entry!.WallThicknessMm);
        Assert.Equal(radius, entry.CenterlineRadiusMm);
        Assert.Equal(laying, entry.LayingLengthMm);
    }

    /// <summary>
    /// 행은 DN×각도 하나뿐이다(2026-08-20, A형/B형 중복 행 폐지). 기본 형식은 B형이고,
    /// A형으로 바꿔도 e·R·t는 그대로이며 s·무게만 형식을 따라간다.
    /// </summary>
    [Fact]
    public void Bend_default_has_one_row_per_dn_and_angle_defaulting_to_b_form()
    {
        foreach (var dn in StraightPipeSpecTable.DuctileIronNominalDiameters)
            foreach (var angle in JointDeflectionRule.StandardAngles)
            {
                var entry = BendFittingCatalog.Default.Find(dn, angle);
                Assert.NotNull(entry);
                Assert.Equal(BendForm.BType, entry!.Form);
                Assert.Equal(200d, entry.ExtraLegLengthMm);

                Assert.True(BendFittingCatalog.TryGetHandbookWeight(dn, angle, BendForm.AType, out var kpA, out var tytonA));
                Assert.True(BendFittingCatalog.TryGetHandbookWeight(dn, angle, BendForm.BType, out var kpB, out var tytonB));
                Assert.Equal(kpB, entry.WeightKpMechanicalKg);
                Assert.Equal(tytonB, entry.WeightTytonKg);
                // A형은 s가 없어 더 가볍다(같은 DN·각도).
                Assert.True(kpA > kpB);
                Assert.True(tytonA > tytonB);
            }
        // 주철관 108행(소켓 72 + 플랜지 36) + 강관 960행(docs/211 § C-5).
        Assert.Equal(108, BendFittingCatalog.Default.Entries.Count(x => x.Material == PipeMaterial.DuctileIron));
        Assert.Equal(960, BendFittingCatalog.Default.Entries.Count(x => x.Material == PipeMaterial.Steel));
    }

    /// <summary>이형관 벽두께 e는 각도와 무관한 DN 단일값이며, 관종별 직관 두께와는 다른 계열이다.</summary>
    [Fact]
    public void Bend_wall_thickness_depends_on_dn_only()
    {
        foreach (var dn in StraightPipeSpecTable.DuctileIronNominalDiameters)
        {
            var thicknesses = JointDeflectionRule.StandardAngles
                .Select(angle => BendFittingCatalog.Default.Find(dn, angle)!.WallThicknessMm)
                .Distinct()
                .ToList();
            Assert.Single(thicknesses);
        }
        // DN300: 이형관 9.6 vs 상수 1종관 직관 8.8
        Assert.Equal(9.6, BendFittingCatalog.Default.Find(300, 45)!.WallThicknessMm);
        Assert.Equal(8.8, StraightPipeSpecTable.Default.Find(PipeKindCatalog.Water1, 300)!.ThicknessMm);
    }

    [Fact]
    public void Flanged_default_matches_handbook_and_has_only_45_and_90_degrees()
    {
        var socket90 = BendFittingCatalog.Default.Find(80, 90, connection: BendConnection.Socket);
        var flanged90 = BendFittingCatalog.Default.Find(80, 90, connection: BendConnection.Flanged);
        var flanged45 = BendFittingCatalog.Default.Find(80, 45, connection: BendConnection.Flanged);

        Assert.NotNull(socket90);
        Assert.Equal(75, socket90!.CenterlineRadiusMm);
        Assert.Equal(150, socket90.LayingLengthMm);
        Assert.Equal(7.0, socket90.WallThicknessMm);
        Assert.NotNull(flanged90);
        Assert.Equal(122, flanged90!.CenterlineRadiusMm);
        Assert.Equal(165, flanged90.LayingLengthMm);
        Assert.Equal(7.0, flanged90.WallThicknessMm);
        Assert.NotNull(flanged45);
        Assert.Equal(210, flanged45!.CenterlineRadiusMm);
        Assert.Equal(130, flanged45.LayingLengthMm);
        Assert.Equal(200d, flanged45.ExtraLegLengthMm);
        Assert.Equal(200d, flanged90.ExtraLegLengthMm);
        Assert.Null(BendFittingCatalog.Default.Find(80, 11.25, connection: BendConnection.Flanged));
        Assert.Equal(new[] { 45d, 90d }, BendFittingCatalog.AnglesFor(PipeMaterial.DuctileIron, BendConnection.Flanged));
        Assert.Equal(new[] { 5.625, 11.25, 22.5, 45d, 90d }, BendFittingCatalog.AnglesFor(PipeMaterial.Steel, BendConnection.Flanged));
        Assert.Equal(new[] { 5.625, 11.25, 22.5, 45d, 90d }, BendFittingCatalog.AnglesFor(PipeMaterial.Steel, BendConnection.Socket));
    }

    // ── 강관(KS D 3565 / 3578, docs/211) ─────────────────────────────────────────

    /// <summary>강관 직관 기본 행 — 등급별 수록 DN 구간(docs/211_data_강관직관_KSD3565.csv).</summary>
    [Fact]
    public void Steel_straight_default_has_65_rows_in_handbook_dn_ranges()
    {
        var steel = StraightPipeSpecTable.Default.Entries.Where(x => PipeKindCatalog.MaterialOf(x.PipeKind) == PipeMaterial.Steel).ToList();
        Assert.Equal(65, steel.Count);
        Assert.Equal(7, steel.Count(x => x.PipeKind == PipeKindCatalog.Steel290));
        Assert.Equal(7, steel.Count(x => x.PipeKind == PipeKindCatalog.Steel370));
        Assert.Equal(28, steel.Count(x => x.PipeKind == PipeKindCatalog.Steel400A));
        Assert.Equal(23, steel.Count(x => x.PipeKind == PipeKindCatalog.Steel400B));
        Assert.Equal(35, StraightPipeSpecTable.SteelNominalDiameters.Count);
        Assert.Equal(35, StraightPipeSpecTable.NominalDiameters.Count);
        Assert.Equal(18, StraightPipeSpecTable.DuctileIronNominalDiameters.Count);
        // 강관은 소켓이 없다.
        Assert.All(steel, x => Assert.Equal(0d, x.SocketWeightKpKg));
    }

    [Theory]
    [InlineData("STWW 290", 80, 89.1, 4.2, 8.79)]
    [InlineData("STWW 370", 300, 318.5, 6.4, 49.3)]
    [InlineData("STWW 400 A", 350, 355.6, 6.0, 51.7)]
    [InlineData("STWW 400 B", 700, 711.2, 6.0, 104)]
    [InlineData("STWW 400 A", 3000, 3048.0, 29.0, 2159)]
    [InlineData("STWW 400 B", 3000, 3048.0, 22.0, 1642)]
    public void Steel_straight_default_matches_handbook_rows(string kind, double dn, double od, double t, double kgPerM)
    {
        var spec = StraightPipeSpecTable.Default.Find(kind, dn);
        Assert.NotNull(spec);
        Assert.Equal(od, spec!.OuterDiameterMm);
        Assert.Equal(t, spec.ThicknessMm);
        Assert.Equal(kgPerM, spec.UnitWeightKgPerM);
    }

    /// <summary>핸드북 무게식 W = 0.02466·t·(D−t) (kg/m, 유효숫자 3자리)으로 강관 직관 전 행을 검산한다 — 옮겨 적기 오류 방지.</summary>
    [Fact]
    public void Steel_straight_unit_weight_matches_handbook_formula()
    {
        foreach (var x in StraightPipeSpecTable.Default.Entries.Where(x => PipeKindCatalog.MaterialOf(x.PipeKind) == PipeMaterial.Steel))
        {
            var calc = 0.02466 * x.ThicknessMm * (x.OuterDiameterMm - x.ThicknessMm);
            Assert.True(Math.Abs(calc - x.UnitWeightKgPerM) / x.UnitWeightKgPerM <= 0.006, $"{x.PipeKind} DN{x.DiameterMm}: {x.UnitWeightKgPerM} vs {calc:0.##}");
        }
    }

    /// <summary>강관 곡관 대표 행(docs/211_data_강관곡관_KSD3578.csv). 종류(F)마다 T·무게만 다르고 R·L은 같다.</summary>
    [Theory]
    [InlineData(80, 90, SteelFittingClass.F12, 230, 400, 4.2, 6.24)]
    [InlineData(80, 90, SteelFittingClass.F20, 230, 400, 4.5, 6.66)]
    [InlineData(300, 45, SteelFittingClass.F15, 610, 450, 6.9, 46.6)]
    [InlineData(700, 22.5, SteelFittingClass.F20, 1150, 750, 7.0, 183)]
    [InlineData(1000, 5.625, SteelFittingClass.F12, 5690, 1000, 8.0, 398)]
    [InlineData(2400, 5.625, SteelFittingClass.F15, 7920, 1000, 22.0, 2610)]   // 원문 그대로(다른 각도 표는 2620) — docs/211 § 0-1 A5
    [InlineData(3000, 5.625, SteelFittingClass.F15, 8820, 1000, 29.0, 4320)]
    [InlineData(3000, 90, SteelFittingClass.F12, 1900, 2050, 22.0, 5510)]
    public void Steel_bend_default_matches_handbook_rows(double dn, double angle, SteelFittingClass fittingClass, double r, double l, double t, double weight)
    {
        foreach (var connection in new[] { BendConnection.Socket, BendConnection.Flanged })
        {
            var entry = BendFittingCatalog.Default.Find(dn, angle, PipeMaterial.Steel, connection, fittingClass);
            Assert.NotNull(entry);
            Assert.Equal(r, entry!.CenterlineRadiusMm);
            Assert.Equal(l, entry.LayingLengthMm);
            Assert.Equal(t, entry.WallThicknessMm);
            Assert.Equal(weight, entry.WeightKg);
            Assert.Equal(0d, entry.ExtraLegLengthMm);   // 강관은 소켓이 없다 — B형 200mm가 붙지 않는다.
        }
    }

    /// <summary>5⅝°는 핸드북에 DN1000 이상만 있다(docs/211 § 0 확정 5). 종류 없는(None) 조회는 강관 행을 찾지 못한다.</summary>
    [Fact]
    public void Steel_5625_bend_exists_only_from_dn1000()
    {
        Assert.Null(BendFittingCatalog.Default.Find(900, 5.625, PipeMaterial.Steel, BendConnection.Socket, SteelFittingClass.F12));
        Assert.NotNull(BendFittingCatalog.Default.Find(1000, 5.625, PipeMaterial.Steel, BendConnection.Socket, SteelFittingClass.F12));
        Assert.Null(BendFittingCatalog.Default.Find(1000, 5.625, PipeMaterial.Steel, BendConnection.Socket));
        Assert.Equal(20 * 3 * 2, BendFittingCatalog.Default.Entries.Count(x => x.Material == PipeMaterial.Steel && Math.Abs(x.AngleDeg - 5.625) < 1e-9));
    }

    /// <summary>
    /// 곡관 관두께 F12는 직관 STWW 290(≤300)/400 B(≥350), F20은 STWW 370(≤300)/400 A(≥350)와 전 DN 일치한다(판독 검증 결과).
    /// 등급과 대응시키지는 않지만(확정 2) 이 관계가 깨지면 옮겨 적기 오류다.
    /// </summary>
    [Fact]
    public void Steel_bend_thickness_agrees_with_straight_grade_thickness()
    {
        foreach (var dn in StraightPipeSpecTable.SteelNominalDiameters)
        {
            var f12 = BendFittingCatalog.SteelWallThicknessOf(dn, SteelFittingClass.F12);
            var f20 = BendFittingCatalog.SteelWallThicknessOf(dn, SteelFittingClass.F20);
            var straight12 = StraightPipeSpecTable.Default.Find(dn <= 300 ? PipeKindCatalog.Steel290 : PipeKindCatalog.Steel400B, dn)?.ThicknessMm
                ?? StraightPipeSpecTable.Default.Find(PipeKindCatalog.Steel400A, dn)?.ThicknessMm;   // DN350~600은 B가 없어 A와 같다.
            var straight20 = StraightPipeSpecTable.Default.Find(dn <= 300 ? PipeKindCatalog.Steel370 : PipeKindCatalog.Steel400A, dn)?.ThicknessMm;
            Assert.Equal(straight12, f12);
            Assert.Equal(straight20, f20);
        }
    }

    /// <summary>강관 행이 없는 저장본은 [기본값 복원] 없이 기본 강관 행이 병합된다(docs/211 § E-1). 강관 행이 하나라도 있으면 그대로다.</summary>
    [Fact]
    public void WithSteelDefaults_merges_only_when_no_steel_rows_exist()
    {
        var ductileOnly = new BendSettings(
            new StraightPipeSpecTable(StraightPipeSpecTable.Default.Entries.Where(x => PipeKindCatalog.MaterialOf(x.PipeKind) == PipeMaterial.DuctileIron).ToList()),
            JointDeflectionTable.Default,
            new BendFittingCatalog(BendFittingCatalog.Default.Entries.Where(x => x.Material == PipeMaterial.DuctileIron).ToList()),
            JointTypeCatalog.KpMechanical);
        var merged = ductileOnly.WithSteelDefaults();
        Assert.Equal(StraightPipeSpecTable.Default.Entries.Count, merged.StraightPipes.Entries.Count);
        Assert.Equal(BendFittingCatalog.Default.Entries.Count, merged.Fittings.Entries.Count);
        Assert.False(ductileOnly.Fittings.NeedsRestore);

        var oneSteelRow = ductileOnly with { Fittings = new BendFittingCatalog(ductileOnly.Fittings.Entries.Append(BendFittingCatalog.SteelDefaultEntries[0]).ToList()) };
        Assert.Same(oneSteelRow.Fittings, oneSteelRow.WithSteelDefaults().Fittings);
        Assert.Same(BendSettings.Default, BendSettings.Default.WithSteelDefaults());
    }

    /// <summary>중복행 또는 플랜지곡관 누락이 있는 구 규격은 [기본값 복원] 안내 대상이다.</summary>
    [Fact]
    public void NeedsRestore_detects_legacy_duplicate_and_missing_flanged_tables()
    {
        Assert.False(BendFittingCatalog.Default.NeedsRestore);

        var legacy = new BendFittingCatalog((
            from dn in StraightPipeSpecTable.DuctileIronNominalDiameters
            from form in new[] { (Form: BendForm.AType, Factor: 2.5), (Form: BendForm.BType, Factor: 1.5) }
            from angle in JointDeflectionRule.StandardAngles
            let radius = Math.Round(form.Factor * dn, 1)
            select new BendFittingEntry(dn, angle, form.Form,
                Math.Ceiling(BendResolver.TangentLength(radius, angle)) + 50d, radius,
                StraightPipeSpecTable.Default.Find(PipeKindCatalog.Water1, dn)?.ThicknessMm ?? 0d)).ToList());
        Assert.True(legacy.NeedsRestore);

        var socketOnly = new BendFittingCatalog(BendFittingCatalog.Default.Entries
            .Where(x => x.Connection == BendConnection.Socket).ToList());
        Assert.True(socketOnly.NeedsRestore);

        var duplicate = new BendFittingCatalog(BendFittingCatalog.Default.Entries
            .Append(BendFittingCatalog.Default.Entries[0]).ToList());
        Assert.True(duplicate.NeedsRestore);
    }

    [Fact]
    public void Old_json_without_connection_defaults_to_socket()
    {
        const string json = """
            {"DiameterMm":80,"AngleDeg":90,"Form":"BType","LayingLengthMm":150,"CenterlineRadiusMm":75,"WallThicknessMm":7}
            """;
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };

        var entry = JsonSerializer.Deserialize<BendFittingEntry>(json, options);

        Assert.NotNull(entry);
        Assert.Equal(BendConnection.Socket, entry!.Connection);
    }

    /// <summary>외경 폴백은 같은 관종 안에서만 — 강관 DN300은 318.5, 주철관은 326(docs/211 § B-3).</summary>
    [Fact]
    public void Straight_outer_diameter_can_be_resolved_by_dn_within_material()
    {
        var found = StraightPipeSpecTable.Default.FindOuterDiameterByDiameter(300, PipeMaterial.DuctileIron);
        Assert.NotNull(found);
        Assert.Equal(326d, found!.Value.OuterDiameterMm);
        Assert.Equal(PipeKindCatalog.Water1, found.Value.PipeKind);
        Assert.Null(StraightPipeSpecTable.Default.FindOuterDiameterByDiameter(75, PipeMaterial.DuctileIron));

        var steel = StraightPipeSpecTable.Default.FindOuterDiameterByDiameter(300, PipeMaterial.Steel);
        Assert.Equal(318.5, steel!.Value.OuterDiameterMm);
        Assert.Equal(PipeKindCatalog.Steel290, steel.Value.PipeKind);
        // DN1350은 강관에만 있다.
        Assert.Null(StraightPipeSpecTable.Default.FindOuterDiameterByDiameter(1350, PipeMaterial.DuctileIron));
        Assert.Equal(1371.6, StraightPipeSpecTable.Default.FindOuterDiameterByDiameter(1350, PipeMaterial.Steel)!.Value.OuterDiameterMm);
        // 관종이 다른 같은 DN의 OD 차이는 불일치가 아니다.
        Assert.Empty(StraightPipeSpecTable.Default.FindOuterDiameterConflicts());
    }
}
