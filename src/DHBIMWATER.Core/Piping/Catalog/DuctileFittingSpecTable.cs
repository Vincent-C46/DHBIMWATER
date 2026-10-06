namespace DHBIMWATER.Core.Piping;

/// <summary>주철 소켓 T자관 한 행 — 핸드북 9항(SOCKET TEE) p.99~100. 무게는 PN10(kg).</summary>
public sealed record DuctileSocketTeeSpec(double MainDn, double BranchDn, double L, double H,
    double KpA, double KpB, double TytonA, double TytonB);

/// <summary>주철 플랜지 T자관 한 행 — 핸드북 16항(FLANGED TEE) p.108~110. 무게는 PN10(kg), A형/B형.</summary>
public sealed record DuctileFlangedTeeSpec(double MainDn, double BranchDn, double L, double H, double WeightA, double WeightB);

/// <summary>주철 소켓 편락관 한 행 — 핸드북 10항(SOCKET CONCENTRIC TAPER) p.101~102.
/// L은 테이퍼 길이이며 B·C형은 여기에 직관부 200이 붙는다. 타이튼은 B형 무게만 있다.</summary>
public sealed record DuctileSocketTaperSpec(double LargeDn, double SmallDn, double L,
    double KpA, double KpB, double KpC, double TytonB);

/// <summary>주철 플랜지 편락관 한 행 — 핸드북 11항(FLANGED CONCENTRIC TAPER) p.103. 형식 구분 없는 단일 L·무게(PN10).</summary>
public sealed record DuctileFlangedTaperSpec(double LargeDn, double SmallDn, double L, double Weight);

/// <summary>주철 Y관 한 행 — 핸드북 특수이형관 3항(Y BRANCH) p.116. 무게는 KP메커니컬 A/B형, 플랜지 C형(PN10).</summary>
public sealed record DuctileWyeSpec(double MainDn, double BranchDn, double L, double KpA, double KpB, double FlangeC);

/// <summary>주철관 이형관 핸드북 고정표(docs/260 § A). 출처 <c>docs/주철관핸드북(2020년판)_이형관.pdf</c>.
/// <para>PDF 텍스트 레이어를 좌표로 추출한 뒤 페이지 이미지로 열 의미를 대조했다(2026-10-02). 값은 인쇄 그대로이며
/// 오타처럼 보여도 고치지 않는다(소켓 T 300×300 타이튼 A 98.5 등). 규격표 창에서 편집하지 않는 코드 내장값이다(사용자 결정).</para>
/// <para>조회는 DN ±0.5mm 일치이고, 없으면 null — 보간·근삿값은 하지 않는다.</para></summary>
public static class DuctileFittingSpecTable
{
    private const double MatchToleranceMm = 0.5;

    /// <summary>Y관 분기각. 핸드북 그림이 45° Y관이며 표에는 각도 열이 없다.</summary>
    public const double WyeAngleDeg = 45d;

    /// <summary>편락관 B·C형에 붙는 직관부 길이(mm). 핸드북 그림의 200 표기.</summary>
    public const double TaperStraightLengthMm = 200d;

    public static IReadOnlyList<DuctileSocketTeeSpec> SocketTees => PipeCatalog.Current.DuctileSocketTees;

    public static IReadOnlyList<DuctileFlangedTeeSpec> FlangedTees => PipeCatalog.Current.DuctileFlangedTees;

    public static IReadOnlyList<DuctileSocketTaperSpec> SocketTapers => PipeCatalog.Current.DuctileSocketTapers;

    public static IReadOnlyList<DuctileFlangedTaperSpec> FlangedTapers => PipeCatalog.Current.DuctileFlangedTapers;

    public static IReadOnlyList<DuctileWyeSpec> Wyes => PipeCatalog.Current.DuctileWyes;

    public static DuctileSocketTeeSpec? FindSocketTee(double mainMm, double branchMm)
        => SocketTees.FirstOrDefault(x => Matches(x.MainDn, mainMm) && Matches(x.BranchDn, branchMm));

    public static DuctileFlangedTeeSpec? FindFlangedTee(double mainMm, double branchMm)
        => FlangedTees.FirstOrDefault(x => Matches(x.MainDn, mainMm) && Matches(x.BranchDn, branchMm));

    public static DuctileSocketTaperSpec? FindSocketTaper(double largeMm, double smallMm)
        => SocketTapers.FirstOrDefault(x => Matches(x.LargeDn, largeMm) && Matches(x.SmallDn, smallMm));

    public static DuctileFlangedTaperSpec? FindFlangedTaper(double largeMm, double smallMm)
        => FlangedTapers.FirstOrDefault(x => Matches(x.LargeDn, largeMm) && Matches(x.SmallDn, smallMm));

    public static DuctileWyeSpec? FindWye(double mainMm, double branchMm)
        => Wyes.FirstOrDefault(x => Matches(x.MainDn, mainMm) && Matches(x.BranchDn, branchMm));

    private static bool Matches(double tableDn, double valueMm) => Math.Abs(tableDn - valueMm) <= MatchToleranceMm;
}
