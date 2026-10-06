namespace DHBIMWATER.Core.Piping;

/// <summary>강관 T자관 한 행 — KS D 3578 부도 6(강관 핸드북 p.162~169). 무게는 참고값(kg).
/// <para>T·t는 주관·지관 두께, H는 주관 반길이, I는 주관 중심 → 지관 끝 길이다. 보강판(t1·B)은 무게에 이미 들어 있어 따로 두지 않는다.</para></summary>
public sealed record SteelTeeSpec(SteelFittingClass Class, double MainDn, double BranchDn,
    double MainThicknessMm, double BranchThicknessMm, double H, double I, double Weight);

/// <summary>강관 편락관 한 행 — KS D 3578 부도 7(강관 핸드북 p.170~173). 무게는 F12/F15/F20별 참고값(kg).
/// <para>형상은 대경 원통 C + 테이퍼 W + 소경 원통 E이고 L = C + W + E다. E는 저장하지 않고 <see cref="SmallStraightMm"/>로 계산한다
/// (2800×2700 행의 E가 3000으로 인쇄돼 있다 — L·C·W로 보면 300이다).</para></summary>
public sealed record SteelReducerSpec(double LargeDn, double SmallDn, double C, double W, double L,
    double WeightF12, double WeightF15, double WeightF20)
{
    /// <summary>소경 쪽 원통 E = L − C − W.</summary>
    public double SmallStraightMm => L - C - W;

    public double WeightFor(SteelFittingClass steelClass) => steelClass switch
    {
        SteelFittingClass.F15 => WeightF15,
        SteelFittingClass.F20 => WeightF20,
        _ => WeightF12
    };
}

/// <summary>강관 이형관 핸드북 고정표(docs/261). 출처 <c>docs/강관핸드북_곡관및이형관.pdf</c>(150dpi 스캔, 텍스트 레이어 없음).
/// <para>2026-10-02 페이지를 확대해 눈으로 전사했다(Windows OCR은 소수점·숫자 오인식이 많아 쓰지 않음). 검증은 이중 전사 대신
/// <b>치수로 계산한 무게와 대조</b>했다 — T자관 396행 중 391행 ±5%, 편락관 F12·F20 전 행 ±0.3%(아래 예외 제외). 벗어난 행은
/// 이미지로 재확인했고 인쇄값 그대로다. 오타처럼 보여도 고치지 않는다(<see cref="ValveBypassSpecTable"/>와 같은 원칙).</para>
/// <para>인쇄 그대로인 이상값: T자관 F12 250×125=27.2·250×150=28.2(F15는 37.2·38.2), F12/F15 300×250=45.2/45.0, F15 2000×1200=2980,
/// F12 3000×2100=6140 / 편락관 F12 1350×1200=306·1650×1600=628, F20 200×125=16.2.
/// 두께 열 인쇄 오류(F12 소경 T에 외경 값, DN1100 이상 F15 T가 F12와 같음)는 무게와 무관해 저장하지 않는다.</para>
/// <para>T자관 2900 묶음의 「2800 × 1800」은 외경 2946.4로 보아 2900×1800의 표기 오류라 2900으로 넣었다.
/// F20 T자관은 핸드북에 표가 없다(「KS B 1541, 1543에 따르거나 당사자 간 협의」) — 조회는 null이다(사용자 결정: 비움).</para></summary>
public static class SteelFittingSpecTable
{
    private const double MatchToleranceMm = 0.5;
    private const SteelFittingClass F12 = SteelFittingClass.F12;
    private const SteelFittingClass F15 = SteelFittingClass.F15;

    public static IReadOnlyList<SteelTeeSpec> Tees => PipeCatalog.Current.SteelTees;

    public static IReadOnlyList<SteelReducerSpec> Reducers => PipeCatalog.Current.SteelReducers;

    /// <summary>T자관 조회. F20·표에 없는 조합은 null. <see cref="SteelFittingClass.None"/>은 F12로 본다.</summary>
    public static SteelTeeSpec? FindTee(SteelFittingClass steelClass, double mainMm, double branchMm)
    {
        var cls = ValveBypassSpecTable.NormalizeSteelClass(steelClass);
        return Tees.FirstOrDefault(x => x.Class == cls && Matches(x.MainDn, mainMm) && Matches(x.BranchDn, branchMm));
    }

    /// <summary>편락관 조회(등급 무관 치수, 무게는 <see cref="SteelReducerSpec.WeightFor"/>). 표에 없으면 null.</summary>
    public static SteelReducerSpec? FindReducer(double largeMm, double smallMm)
        => Reducers.FirstOrDefault(x => Matches(x.LargeDn, largeMm) && Matches(x.SmallDn, smallMm));

    private static bool Matches(double tableDn, double valueMm) => Math.Abs(tableDn - valueMm) <= MatchToleranceMm;
}
