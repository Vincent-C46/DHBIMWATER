namespace DHBIMWATER.Core.Piping;

/// <summary>무게 조회 결과. 직관·단관만 <see cref="UnitWeightKgPerM"/>가 있다. 표에 없으면 둘 다 null — 값을 지어내지 않는다.</summary>
public sealed record FittingWeight(double? WeightKg, double? UnitWeightKgPerM = null)
{
    // new(null)은 레코드 복사 생성자로 해석돼 NRE가 난다 — 형을 밝힌다.
    public static FittingWeight None { get; } = new((double?)null);
}

/// <summary>무게 규칙을 고르는 부재 종류. 밸브·이음관(<see cref="Other"/>)은 카탈로그가 없어 패밀리 매개변수를 복사한다(Infrastructure).</summary>
public enum FittingWeightPart { Other, Straight, Bend, Tee, Cross, Wye, ValveTee, ValveBend }

/// <summary>무게 조회 입력 — 밸브실 신규 배치(<c>MaterialPart</c>)·재기록(<c>Restored</c>)이 같은 모양으로 넘긴다.</summary>
/// <param name="SteelClass">강관 이형관 종류. 모르면 <see cref="SteelFittingClass.None"/> — 강관 부관은 그때 비운다(재기록 경로).</param>
public sealed record FittingWeightInput(FittingWeightPart Part, PipeMaterial? Material, BendConnection Connection,
    SteelFittingClass SteelClass, string? PipeKind, double DiameterMm, double? BranchDiameterMm = null,
    double? LengthMm = null, double? DeflectionDeg = null, double? WyeAngleDeg = null);

/// <summary>배관 부재의 카탈로그 무게(`DH_무게`) 규칙(docs/260 § B). Revit과 무관한 순수 함수다.
/// <para>밸브실(PipeLayout) 신규 배치와 재기록 경로가 같은 규칙을 쓰도록 여기 한곳에 둔다.</para>
/// <para>사용자 결정(2026-10-02): 소켓 접합은 항상 KP 열, 곡관 형식은 B형, 표준각과 ±0.5° 이내일 때만 곡관 무게를 쓴다.
/// 십자·강관 Y형·강관 F20 T형은 표가 없어 비운다. 강관 T형은 docs/261 표를 쓴다.</para></summary>
public static class FittingWeightResolver
{
    /// <summary>곡관·Y관 편각을 표준각으로 인정하는 허용치(°).</summary>
    public const double AngleToleranceDeg = 0.5;

    /// <summary>직관 무게용 등급 확정. 명시 등급이 정규화되면 그것을, 비었으면 강관만 DN별 기본 등급(<see cref="PipeKindCatalog.DefaultSteelGrade"/>)을 쓴다.
    /// 주철관은 등급을 추측하지 않는다(null).</summary>
    public static string? ResolveGrade(PipeMaterial material, string? pipeKind, double diameterMm)
    {
        var normalized = PipeKindCatalog.Normalize(pipeKind);
        if (normalized is not null) return PipeKindCatalog.MaterialOf(normalized) == material ? normalized : null;
        return material == PipeMaterial.Steel ? PipeKindCatalog.DefaultSteelGrade(diameterMm) : null;
    }

    /// <summary>직관·단관 — 단위중량 × 연장(+ 소켓 접합이면 KP 소켓 무게). 플랜지 접합은 소켓이 없으므로 더하지 않는다.</summary>
    public static FittingWeight Straight(StraightPipeSpecTable table, PipeMaterial material, string? pipeKind,
        BendConnection connection, double diameterMm, double lengthMm)
    {
        var grade = ResolveGrade(material, pipeKind, diameterMm);
        if (grade is null || lengthMm <= 0d) return FittingWeight.None;
        var spec = table.Find(grade, diameterMm);
        if (spec is null || spec.UnitWeightKgPerM <= 0d) return FittingWeight.None;
        var bodyKg = spec.UnitWeightKgPerM * lengthMm / 1000d;
        var socketKg = connection == BendConnection.Socket ? StraightPipeWeight.SocketWeightKg(spec, JointTypeCatalog.KpMechanical) : 0d;
        return new FittingWeight(bodyKg + socketKg, spec.UnitWeightKgPerM);
    }

    /// <summary>표준각 채택 — 허용치 안의 각이 없으면 null.</summary>
    public static double? StandardAngle(PipeMaterial material, BendConnection connection, double deflectionDeg)
        => BendFittingCatalog.AnglesFor(material, connection)
            .Where(x => Math.Abs(x - deflectionDeg) <= AngleToleranceDeg)
            .Select(x => (double?)x)
            .FirstOrDefault();

    /// <summary>곡관 — 주철 소켓은 핸드북 KP B형, 주철 플랜지는 핸드북 플랜지곡관 B형, 강관은 곡관표의 F 종류별 한 개 무게.</summary>
    public static double? Bend(BendFittingCatalog catalog, PipeMaterial material, BendConnection connection,
        SteelFittingClass steelClass, double diameterMm, double deflectionDeg)
    {
        if (StandardAngle(material, connection, deflectionDeg) is not { } angle) return null;
        switch (material)
        {
            case PipeMaterial.DuctileIron:
                if (connection == BendConnection.Flanged)
                    return BendFittingCatalog.TryGetFlangedHandbookWeight(diameterMm, angle, BendForm.BType, out var flangedKg) ? Positive(flangedKg) : null;
                return BendFittingCatalog.TryGetHandbookWeight(diameterMm, angle, BendForm.BType, out var kpKg, out _) ? Positive(kpKg) : null;
            case PipeMaterial.Steel:
                return Positive(catalog.Find(diameterMm, angle, PipeMaterial.Steel, connection,
                    ValveBypassSpecTable.NormalizeSteelClass(steelClass))?.WeightKg ?? 0d);
            default:
                return null;
        }
    }

    /// <summary>T형 — 주철 소켓은 소켓 T KP B형, 주철 플랜지는 플랜지 T B형, 강관은 F 종류별 T자관(docs/261, F20은 표가 없어 null).</summary>
    public static double? Tee(PipeMaterial material, BendConnection connection, double mainMm, double branchMm,
        SteelFittingClass steelClass = SteelFittingClass.F12)
    {
        if (material == PipeMaterial.Steel)
            return Positive(SteelFittingSpecTable.FindTee(steelClass, mainMm, branchMm)?.Weight ?? 0d);
        if (material != PipeMaterial.DuctileIron) return null;
        return connection == BendConnection.Flanged
            ? Positive(DuctileFittingSpecTable.FindFlangedTee(mainMm, branchMm)?.WeightB ?? 0d)
            : Positive(DuctileFittingSpecTable.FindSocketTee(mainMm, branchMm)?.KpB ?? 0d);
    }

    /// <summary>Y형 — 주철만, 분기각이 핸드북 Y관(45°)과 ±0.5° 이내일 때. 소켓은 KP B형, 플랜지는 C형.</summary>
    public static double? Wye(PipeMaterial material, BendConnection connection, double mainMm, double branchMm, double branchAngleDeg)
    {
        if (material != PipeMaterial.DuctileIron
            || Math.Abs(branchAngleDeg - DuctileFittingSpecTable.WyeAngleDeg) > AngleToleranceDeg) return null;
        var row = DuctileFittingSpecTable.FindWye(mainMm, branchMm);
        if (row is null) return null;
        return Positive(connection == BendConnection.Flanged ? row.FlangeC : row.KpB);
    }

    /// <summary>밸브부관 A — 주철은 접합별 열(소켓→B형 KP, 플랜지→C형), 강관은 F 종류 행의 무게.</summary>
    public static double? ValveTee(PipeMaterial material, BendConnection connection, SteelFittingClass steelClass, double mainMm, double branchMm)
    {
        switch (material)
        {
            case PipeMaterial.DuctileIron:
                var ductile = ValveBypassSpecTable.DuctileTees.FirstOrDefault(x => Matches(x.MainDn, mainMm) && Matches(x.BranchDn, branchMm));
                if (ductile is null) return null;
                // 사용자 결정: KP→B형, 플랜지→C형, 그 외→A형. BendConnection은 두 값뿐이라 A형 열은 지금은 쓰이지 않는다.
                return Positive(connection == BendConnection.Flanged ? ductile.WeightCFlange : ductile.WeightBKp);
            case PipeMaterial.Steel:
                var cls = ValveBypassSpecTable.NormalizeSteelClass(steelClass);
                return Positive(ValveBypassSpecTable.SteelTees.FirstOrDefault(x => x.Class == cls && Matches(x.MainDn, mainMm) && Matches(x.BranchDn, branchMm))?.Weight ?? 0d);
            default:
                return null;
        }
    }

    /// <summary>밸브부관 B — 주철은 표준형 무게(사용자 결정), 강관은 F 종류 행의 무게.</summary>
    public static double? ValveBend(PipeMaterial material, SteelFittingClass steelClass, double mainMm, double branchMm)
    {
        switch (material)
        {
            case PipeMaterial.DuctileIron:
                return Positive(ValveBypassSpecTable.DuctileBends.FirstOrDefault(x => Matches(x.MainDn, mainMm) && Matches(x.BranchDn, branchMm))?.StdWeight ?? 0d);
            case PipeMaterial.Steel:
                var cls = ValveBypassSpecTable.NormalizeSteelClass(steelClass);
                return Positive(ValveBypassSpecTable.SteelBends.FirstOrDefault(x => x.Class == cls && Matches(x.MainDn, mainMm) && Matches(x.BranchDn, branchMm))?.Weight ?? 0d);
            default:
                return null;
        }
    }

    /// <summary>접합 표기를 뗀 품목명(`직관`·`단관`·`곡관`·`T형`·`십자`·`Y형`) → 부재 종류. 그 밖(밸브류 패밀리명)은 <see cref="FittingWeightPart.Other"/>.</summary>
    public static FittingWeightPart PartOf(string? strippedName) => strippedName switch
    {
        PipeMaterialSpecText.StraightName or PipeMaterialSpecText.ShortName => FittingWeightPart.Straight,
        PipeMaterialSpecText.BendName => FittingWeightPart.Bend,
        PipeMaterialSpecText.TeeName => FittingWeightPart.Tee,
        PipeMaterialSpecText.CrossName => FittingWeightPart.Cross,
        PipeMaterialSpecText.WyeName => FittingWeightPart.Wye,
        _ => FittingWeightPart.Other
    };

    /// <summary>부재 종류별 규칙을 한곳에서 고른다. 관종을 모르거나 표에 없으면 <see cref="FittingWeight.None"/>.</summary>
    public static FittingWeight Resolve(FittingWeightInput input, BendSettings settings)
    {
        if (input.Material is not { } material) return FittingWeight.None;
        var branch = input.BranchDiameterMm ?? input.DiameterMm;
        switch (input.Part)
        {
            case FittingWeightPart.Straight:
                return input.LengthMm is { } lengthMm
                    ? Straight(settings.StraightPipes, material, input.PipeKind, input.Connection, input.DiameterMm, lengthMm)
                    : FittingWeight.None;
            case FittingWeightPart.Bend:
                return input.DeflectionDeg is { } deflection
                    ? new FittingWeight(Bend(settings.Fittings, material, input.Connection, input.SteelClass, input.DiameterMm, deflection))
                    : FittingWeight.None;
            case FittingWeightPart.Tee:
                // 강관 종류를 모르는 경로(재기록, SteelClass=None)는 비운다 — 밸브부관과 같은 규칙.
                return material == PipeMaterial.Steel && input.SteelClass == SteelFittingClass.None
                    ? FittingWeight.None
                    : new FittingWeight(Tee(material, input.Connection, input.DiameterMm, branch, input.SteelClass));
            case FittingWeightPart.Wye:
                return input.WyeAngleDeg is { } angle
                    ? new FittingWeight(Wye(material, input.Connection, input.DiameterMm, branch, angle))
                    : FittingWeight.None;
            case FittingWeightPart.ValveTee:
                return material == PipeMaterial.Steel && input.SteelClass == SteelFittingClass.None
                    ? FittingWeight.None
                    : new FittingWeight(ValveTee(material, input.Connection, input.SteelClass, input.DiameterMm, branch));
            case FittingWeightPart.ValveBend:
                return material == PipeMaterial.Steel && input.SteelClass == SteelFittingClass.None
                    ? FittingWeight.None
                    : new FittingWeight(ValveBend(material, input.SteelClass, input.DiameterMm, branch));
            default:
                // 십자는 핸드북 표가 없다(사용자 결정: 비움). 밸브류는 호출부가 패밀리 매개변수에서 읽는다.
                return FittingWeight.None;
        }
    }

    private static double? Positive(double value) => value > 0d ? value : null;

    private static bool Matches(double tableDn, double valueMm) => Math.Abs(tableDn - valueMm) <= 0.5;
}
