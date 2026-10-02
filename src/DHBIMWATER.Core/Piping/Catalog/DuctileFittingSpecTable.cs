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

    public static IReadOnlyList<DuctileSocketTeeSpec> SocketTees { get; } =
    [
        new(80, 80, 220, 110, 16.2, 16.2, 14.1, 14.4),
        new(100, 80, 220, 120, 18.8, 19.1, 16.9, 17.3),
        new(100, 100, 240, 120, 20.0, 20.5, 18.2, 18.6),
        new(125, 125, 275, 135, 26.0, 26.5, 24.5, 25.0),
        new(150, 80, 220, 145, 26.5, 27.5, 25.0, 25.5),
        new(150, 100, 245, 145, 28.5, 29.0, 27.0, 27.0),
        new(150, 150, 305, 150, 33.0, 34.0, 32.0, 32.0),
        new(200, 80, 225, 170, 35.0, 36.5, 34.0, 34.5),
        new(200, 100, 250, 170, 37.0, 38.5, 36.0, 36.5),
        new(200, 150, 305, 175, 42.5, 44.0, 41.5, 42.0),
        new(200, 200, 365, 180, 47.5, 49.0, 47.5, 48.0),
        new(250, 100, 255, 205, 48.5, 50.0, 48.0, 48.0),
        new(250, 200, 370, 210, 60.5, 62.5, 61.0, 61.0),
        new(250, 250, 425, 210, 67.0, 69.0, 67.5, 68.0),
        new(300, 150, 312, 227, 66.0, 69.0, 68.0, 67.5),
        new(300, 200, 370, 232, 73.0, 76.0, 75.5, 75.0),
        new(300, 250, 428, 238, 81.5, 84.5, 83.5, 83.5),
        new(300, 300, 486, 243, 89.0, 91.5, 98.5, 95.0),
        new(350, 100, 257, 246, 73.5, 76.0, 75.5, 74.0),
        new(350, 200, 373, 257, 90.5, 93.5, 92.5, 91.0),
        new(350, 300, 489, 268, 108, 110, 111, 110),
        new(350, 350, 547, 274, 118, 120, 121, 120),
        new(400, 100, 260, 271, 88.5, 91.5, 87.5, 87.5),
        new(400, 200, 376, 282, 107, 110, 107, 107),
        new(400, 300, 492, 293, 127, 130, 128, 128),
        new(400, 400, 608, 304, 149, 152, 149, 149),
        new(450, 150, 321, 302, 116, 119, 122, 118),
        new(450, 200, 379, 307, 127, 129, 133, 129),
        new(450, 300, 495, 318, 149, 152, 156, 152),
        new(450, 450, 669, 335, 186, 188, 195, 191),
        new(500, 200, 382, 332, 149, 151, 151, 148),
        new(500, 300, 498, 343, 174, 176, 177, 174),
        new(500, 400, 614, 354, 201, 203, 212, 208),
        new(500, 500, 730, 365, 229, 231, 233, 229),
        new(600, 200, 390, 400, 204, 202, 195, 192),
        new(600, 300, 500, 405, 233, 231, 226, 223),
        new(600, 400, 620, 410, 266, 264, 267, 263),
        new(600, 500, 730, 415, 298, 296, 291, 287),
        new(600, 600, 850, 425, 335, 333, 324, 320),
    ];

    public static IReadOnlyList<DuctileFlangedTeeSpec> FlangedTees { get; } =
    [
        new(80, 80, 330, 165, 16.0, 17.0),
        new(100, 80, 360, 175, 18.6, 20.5),
        new(100, 100, 360, 180, 19.4, 21.5),
        new(125, 80, 400, 190, 23.5, 26.0),
        new(125, 100, 400, 195, 24.0, 26.5),
        new(125, 125, 400, 200, 25.5, 28.0),
        new(150, 80, 440, 205, 29.0, 32.4),
        new(150, 100, 440, 210, 29.5, 33.0),
        new(150, 150, 440, 220, 32.5, 36.0),
        new(200, 80, 520, 235, 42.5, 47.5),
        new(200, 100, 520, 240, 43.0, 48.0),
        new(200, 150, 520, 250, 46.0, 51.0),
        new(200, 200, 520, 260, 49.5, 54.5),
        new(250, 100, 700, 275, 68.0, 74.0),
        new(250, 200, 700, 325, 76.0, 82.0),
        new(250, 250, 700, 350, 82.0, 88.0),
        new(300, 100, 800, 300, 94.0, 101),
        new(300, 200, 800, 350, 102, 109),
        new(300, 300, 800, 400, 116, 123),
        new(350, 100, 850, 325, 116, 127),
        new(350, 200, 850, 325, 121, 132),
        new(350, 350, 850, 425, 142, 153),
        new(400, 100, 900, 350, 143, 156),
        new(400, 200, 900, 350, 148, 161),
        new(400, 400, 900, 450, 174, 187),
        new(450, 200, 950, 375, 176, 193),
        new(450, 300, 950, 475, 196, 213),
        new(450, 450, 950, 475, 207, 224),
        new(500, 100, 1000, 400, 210, 229),
        new(500, 200, 1000, 400, 215, 234),
        new(500, 400, 1000, 500, 242, 261),
        new(500, 500, 1000, 500, 252, 271),
        new(600, 200, 1100, 450, 305, 328),
        new(600, 400, 1100, 550, 329, 352),
        new(600, 600, 1100, 550, 355, 378),
        new(700, 200, 650, 525, 268, 296),
        new(700, 400, 870, 555, 343, 371),
        new(700, 700, 1200, 600, 477, 505),
        new(800, 200, 690, 685, 352, 382),
        new(800, 400, 910, 615, 441, 471),
        new(800, 600, 1350, 645, 613, 643),
        new(800, 800, 1350, 675, 657, 687),
        new(900, 200, 730, 645, 436, 474),
        new(900, 400, 950, 675, 541, 579),
        new(900, 600, 1500, 705, 787, 825),
        new(900, 900, 1500, 750, 853, 891),
        new(1000, 200, 770, 705, 546, 587),
        new(1000, 400, 990, 735, 668, 709),
        new(1000, 600, 1650, 765, 1007, 1048),
        new(1000, 1000, 1650, 825, 1105, 1146),
        new(1100, 400, 980, 795, 778, 812),
        new(1100, 600, 1250, 800, 968, 1002),
        new(1100, 1000, 1675, 885, 1249, 1283),
        new(1100, 1100, 1780, 890, 1534, 1568),
        new(1200, 600, 1240, 885, 1101, 1142),
        new(1200, 800, 1470, 915, 1291, 1332),
        new(1200, 1000, 1700, 945, 1494, 1535),
        new(1200, 1200, 1950, 985, 1671, 1711),
    ];

    public static IReadOnlyList<DuctileSocketTaperSpec> SocketTapers { get; } =
    [
        new(100, 80, 90, 10.2, 9.0, 8.9, 9.8),
        new(125, 80, 140, 12.5, 11.3, 11.1, 12.4),
        new(125, 100, 100, 12.7, 11.4, 11.3, 12.8),
        new(150, 80, 190, 15.4, 14.2, 13.6, 15.5),
        new(150, 100, 150, 15.6, 14.3, 13.8, 15.9),
        new(150, 125, 100, 15.8, 14.4, 14.0, 16.4),
        new(200, 100, 250, 22.0, 20.5, 19.8, 23.0),
        new(200, 125, 200, 22.0, 20.5, 20.0, 23.5),
        new(200, 150, 150, 22.5, 20.5, 20.5, 24.0),
        new(250, 125, 300, 30.5, 29.0, 27.5, 32.5),
        new(250, 150, 250, 31.0, 29.0, 28.0, 33.0),
        new(250, 200, 150, 30.0, 28.0, 27.5, 33.0),
        new(300, 150, 350, 40.5, 38.5, 37.0, 44.0),
        new(300, 200, 250, 40.0, 38.0, 36.5, 44.0),
        new(300, 250, 150, 39.0, 36.5, 36.0, 44.0),
        new(350, 200, 360, 53.0, 51.0, 48.0, 58.0),
        new(350, 250, 260, 52.5, 50.0, 47.5, 58.0),
        new(350, 300, 160, 50.5, 47.0, 45.5, 57.0),
        new(400, 250, 360, 67.0, 64.0, 60.5, 71.5),
        new(400, 300, 260, 64.5, 61.5, 58.5, 70.5),
        new(400, 350, 160, 62.5, 57.5, 56.0, 69.0),
        new(450, 300, 360, 81.5, 78.5, 73.5, 91.0),
        new(450, 350, 260, 79.5, 74.5, 71.5, 89.5),
        new(450, 400, 160, 75.5, 69.5, 67.5, 85.0),
        new(500, 350, 360, 99.5, 94.5, 88.5, 108),
        new(500, 400, 260, 95.5, 89.5, 84.5, 104),
        new(600, 400, 460, 145, 138, 126, 149),
        new(600, 500, 260, 134, 124, 116, 141),
        new(700, 500, 480, 200, 189, 172, 201),
        new(700, 600, 280, 186, 168, 158, 184),
        new(800, 600, 480, 251, 233, 223, 253),
        new(800, 700, 280, 231, 204, 203, 228),
        new(900, 700, 480, 312, 285, 280, 315),
        new(900, 800, 280, 275, 247, 243, 282),
        new(1000, 800, 480, 374, 346, 334, 390),
        new(1000, 900, 280, 330, 297, 289, 351),
        new(1100, 800, 680, 482, 454, 438, 506),
        new(1100, 1000, 280, 386, 346, 343, 425),
        new(1200, 800, 880, 619, 590, 562, 648),
        new(1200, 1000, 280, 523, 482, 466, 567),
    ];

    public static IReadOnlyList<DuctileFlangedTaperSpec> FlangedTapers { get; } =
    [
        new(100, 80, 200, 9.4),
        new(125, 100, 200, 11.3),
        new(150, 125, 200, 14.0),
        new(200, 150, 300, 22.0),
        new(250, 200, 300, 30.0),
        new(300, 250, 300, 40.5),
        new(350, 300, 300, 49.5),
        new(400, 350, 300, 58.0),
        new(450, 400, 300, 67.0),
        new(500, 400, 600, 110),
        new(600, 500, 600, 149),
        new(700, 600, 600, 195),
        new(800, 700, 600, 250),
        new(900, 800, 600, 308),
        new(1000, 900, 600, 373),
        new(1100, 1000, 600, 480),
        new(1200, 1000, 790, 586),
    ];

    public static IReadOnlyList<DuctileWyeSpec> Wyes { get; } =
    [
        new(80, 80, 320, 23.5, 22.5, 20.0),
        new(100, 80, 370, 28.5, 28.0, 24.5),
        new(150, 150, 435, 51.0, 50.0, 43.5),
        new(200, 200, 510, 76.5, 76.0, 67.0),
        new(250, 250, 590, 110, 109, 99.0),
        new(300, 300, 622, 139, 139, 128),
        new(350, 350, 686, 178, 178, 159),
        new(400, 400, 762, 227, 227, 203),
        new(450, 450, 813, 282, 280, 246),
        new(500, 500, 889, 357, 352, 310),
        new(600, 600, 1039, 532, 522, 463),
        new(700, 700, 1190, 763, 744, 653),
        new(800, 800, 1340, 1021, 997, 900),
        new(900, 900, 1490, 1334, 1300, 1177),
        new(1000, 1000, 1640, 1734, 1681, 1526),
        new(1100, 1100, 1850, 2094, 2068, 1991),
        new(1200, 1200, 2000, 2613, 2577, 2471),
    ];

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
