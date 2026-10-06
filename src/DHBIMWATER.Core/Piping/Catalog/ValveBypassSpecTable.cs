using System.Globalization;
using DHBIMWATER.Core.Gis;

namespace DHBIMWATER.Core.Piping;

/// <summary>주철관 부관 A(제수밸브 및 버터플라이밸브 부관) 한 행 — KCIP 덕타일 주철관 핸드북 p.117 「4.」. 무게는 PN10(kg).</summary>
public sealed record DuctileValveTeeSpec(
    double MainDn, double BranchDn, double E1, double E2, double L, double S, double H,
    double WeightA, double WeightBKp, double WeightCFlange);

/// <summary>주철관 부관 D형(밸브부관 B) 한 행 — 같은 핸드북 p.118 「5.」. 표준형(Std) / 버터플라이밸브 짧은형(Short).</summary>
public sealed record DuctileValveBendSpec(
    double MainDn, double BranchDn,
    double StdE, double StdR, double StdL, double StdH, double StdWeight,
    double ShortE, double ShortR, double ShortL, double ShortH, double ShortWeight);

/// <summary>강관 게이트 밸브 부관 A 한 행 — 강관 핸드북 부도 10(p.252~254). 보강판(t1·b) "–"는 null. 무게는 플랜지 제외 참고값(kg).</summary>
public sealed record SteelValveTeeSpec(
    SteelFittingClass Class, double MainDn, double BranchDn, double D2, double d2, double T, double t,
    double? ReinforceT1, double? ReinforceB, double B, double I, double J, double Weight);

/// <summary>강관 게이트 밸브 부관 B 한 행 — 강관 핸드북 부도 11(p.255~257). 무게는 플랜지 제외 참고값(kg).</summary>
public sealed record SteelValveBendSpec(
    SteelFittingClass Class, double MainDn, double BranchDn, double d2, double T1, double T2,
    double A, double F, double R, double Weight);

/// <summary>밸브부관(바이패스) 핸드북 규격표(docs/256).
/// <para>주관 호칭지름이 바이패스(지관) 호칭지름을 정한다. 출처: <c>docs/주철관핸드북_밸브부관.pdf</c>,
/// <c>docs/강관핸드북_밸브부관.pdf</c>. 강관 부관 B 첫 쪽(p.255)은 등급 표기가 없으나 F12로 본다(2026-10-01 사용자 확정).</para>
/// <para>dn 외 열(두께·길이·무게)은 아직 쓰지 않는다 — 등급별 두께 매개변수 기록은 후속 작업이다. 값은 PDF 인쇄 그대로이며
/// 오타처럼 보여도(주철관 D형 300 짧은형 h=195, F12 A 1600 이상 J 등) 고치지 않는다.</para></summary>
public static class ValveBypassSpecTable
{
    private const double MatchToleranceMm = 0.5;

    public static IReadOnlyList<DuctileValveTeeSpec> DuctileTees => PipeCatalog.Current.DuctileValveTees;

    public static IReadOnlyList<DuctileValveBendSpec> DuctileBends => PipeCatalog.Current.DuctileValveBends;

    private const SteelFittingClass F12 = SteelFittingClass.F12;
    private const SteelFittingClass F15 = SteelFittingClass.F15;
    private const SteelFittingClass F20 = SteelFittingClass.F20;

    public static IReadOnlyList<SteelValveTeeSpec> SteelTees => PipeCatalog.Current.SteelValveTees;

    public static IReadOnlyList<SteelValveBendSpec> SteelBends => PipeCatalog.Current.SteelValveBends;

    /// <summary>강관 등급 정규화 — <see cref="SteelFittingClass.None"/>은 기본값 F12로 본다.</summary>
    public static SteelFittingClass NormalizeSteelClass(SteelFittingClass steelClass)
        => steelClass == SteelFittingClass.None ? SteelFittingClass.F12 : steelClass;

    /// <summary>주관 호칭지름에 대한 바이패스(지관) 호칭지름(mm). 표에 없으면 null — 보간·근삿값 선택은 하지 않는다.
    /// 주철관은 <paramref name="steelClass"/>를 무시한다. dn 출처는 부관 A표다(B표와 같은 값임을 테스트가 보장한다).</summary>
    public static double? BranchDiameterMm(PipeMaterial material, SteelFittingClass steelClass, double mainDiameterMm)
    {
        switch (material)
        {
            case PipeMaterial.DuctileIron:
                return DuctileTees.FirstOrDefault(x => Matches(x.MainDn, mainDiameterMm))?.BranchDn;
            case PipeMaterial.Steel:
                var cls = NormalizeSteelClass(steelClass);
                return SteelTees.FirstOrDefault(x => x.Class == cls && Matches(x.MainDn, mainDiameterMm))?.BranchDn;
            default:
                return null;
        }
    }

    /// <summary>안내 문구용 규격 범위 — "덕타일주철관 DN300~1200", "강관 F20 400~1500". 규격이 없는 관종은 "{관종} 핸드북 규격 없음".</summary>
    public static string RangeText(PipeMaterial material, SteelFittingClass steelClass)
    {
        var label = PipeAlignmentParameters.Label(material);
        switch (material)
        {
            case PipeMaterial.DuctileIron:
                return $"{label} DN{Num(DuctileTees.Min(x => x.MainDn))}~{Num(DuctileTees.Max(x => x.MainDn))}";
            case PipeMaterial.Steel:
                var cls = NormalizeSteelClass(steelClass);
                var rows = SteelTees.Where(x => x.Class == cls).ToList();
                return $"{label} {cls} {Num(rows.Min(x => x.MainDn))}~{Num(rows.Max(x => x.MainDn))}";
            default:
                return $"{label} 핸드북 규격 없음";
        }
    }

    /// <summary>핸드북 밸브부관 규격이 있는 관종인가(덕타일주철관·강관).</summary>
    public static bool HasSpec(PipeMaterial material) => material is PipeMaterial.DuctileIron or PipeMaterial.Steel;

    private static bool Matches(double tableDn, double mainMm) => Math.Abs(tableDn - mainMm) <= MatchToleranceMm;

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
