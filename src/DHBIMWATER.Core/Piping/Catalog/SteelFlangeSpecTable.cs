namespace DHBIMWATER.Core.Piping;

/// <summary>강관 RF형 관 플랜지 한 행 — KS D 3578 부도 13(강관 핸드북 p.192~194). 단위 mm.
/// <para>D5 = 플랜지 바깥지름, D4 = 볼트 중심원 지름, D3 = RF 대평면 자리 지름(개스킷 지름으로 씀),
/// K = 플랜지 두께, 개스킷 두께 = RF형 개스킷 t(부도 13B p.191, 전 DN 3).</para></summary>
public sealed record SteelFlangeSpec(SteelFittingClass Class, double Dn,
    double FlangeDiameterMm, double BoltCircleDiameterMm, double GasketDiameterMm,
    double FlangeThicknessMm, double GasketThicknessMm);

/// <summary>강관 RF형 플랜지 고정표. 출처 <c>docs/강관핸드북_플랜지규격.pdf</c>(스캔, 텍스트 레이어 없음) — 확대 이미지 눈 전사.
/// 정리본 <c>docs/강관_플랜지_RF_규격_카탈로그.md</c>.
/// <para>p.192 제목이 `F 20`으로 인쇄됐으나 F12 표다(도면 라벨 `F 12`, 사용자 확인). F20 표 `1 330` 행은 관 외경 1371.6으로 보아 1350.</para>
/// <para>F12·F15는 D5·D4·D3·M이 같은 DN에서 같고 K만 다르다. 범위: F12 DN80~3000, F15 DN80~2600, F20 DN80~1500 — 밖이면 null.</para>
/// <para>행 데이터를 연동 애드인 카탈로그로 옮기지 않는다 — 수량 규칙이 아니라 배치 형상값(사용자 결정 2026-10-26).</para></summary>
public static class SteelFlangeSpecTable
{
    private const double MatchToleranceMm = 0.5;
    /// <summary>RF형 개스킷 두께 t — 부도 13B(p.191) DN80~3000 전부 3.</summary>
    public const double RfGasketThicknessMm = 3d;

    // (DN, D5, D4, D3, K F12, K F15, M). F15 K 0 = F15 표에 없는 DN. M(면 틈)은 참고 보관, 개스킷 두께는 RF t 사용.
    private static readonly (double Dn, double D5, double D4, double D3, double KF12, double KF15, double M)[] F12F15Rows =
    {
        (80, 211, 160, 133, 18, 18, 2),
        (100, 238, 180, 153, 18, 18, 2),
        (125, 263, 210, 183, 20, 20, 2),
        (150, 290, 240, 209, 22, 22, 2),
        (200, 342, 295, 264, 22, 22, 2),
        (250, 410, 350, 319, 24, 24, 3),
        (300, 464, 400, 367, 24, 24, 3),
        (350, 530, 460, 427, 26, 26, 3),
        (400, 582, 515, 477, 26, 28, 3),
        (450, 652, 565, 518, 28, 30, 3),
        (500, 706, 620, 582, 28, 30, 3),
        (600, 810, 725, 682, 30, 34, 3),
        (700, 928, 840, 797, 32, 34, 3),
        (800, 1034, 950, 904, 34, 36, 3),
        (900, 1156, 1050, 1004, 36, 38, 3),
        (1000, 1262, 1160, 1111, 38, 42, 3),
        (1100, 1366, 1270, 1200, 41, 43, 3),
        (1200, 1470, 1387, 1304, 43, 45, 3),
        (1350, 1642, 1552, 1462, 45, 51, 3),
        (1500, 1800, 1710, 1620, 48, 53, 3),
        (1600, 1915, 1820, 1760, 53, 58, 3),
        (1650, 1950, 1860, 1770, 53, 58, 3),
        (1800, 2115, 2020, 1960, 55, 59, 3),
        (1900, 2220, 2126, 2066, 58, 59, 3),
        (2000, 2325, 2230, 2170, 58, 62, 4),
        (2100, 2440, 2340, 2240, 59, 64, 4),
        (2200, 2550, 2440, 2370, 61, 68, 4),
        (2300, 2655, 2540, 2440, 62, 69, 4),
        (2400, 2760, 2650, 2570, 64, 70, 4),
        (2500, 2860, 2750, 2670, 68, 72, 5),
        (2600, 2960, 2850, 2780, 68, 72, 5),
        (2700, 3080, 2960, 2850, 71, 0, 5),
        (2800, 3180, 3070, 3000, 72, 0, 5),
        (2900, 3292, 3180, 3104, 74, 0, 5),
        (3000, 3405, 3290, 3210, 76, 0, 5),
    };

    // (DN, D5, D4, D3, K, M). M은 참고 보관.
    private static readonly (double Dn, double D5, double D4, double D3, double K, double M)[] F20Rows =
    {
        (80, 200, 160, 135, 22, 2),
        (100, 225, 185, 160, 22, 2),
        (125, 270, 225, 195, 23, 2),
        (150, 305, 260, 230, 27, 2),
        (200, 350, 305, 275, 27, 2),
        (250, 430, 380, 345, 28, 2),
        (300, 480, 430, 395, 30, 3),
        (350, 540, 480, 440, 34, 3),
        (400, 605, 540, 495, 38, 3),
        (450, 675, 605, 560, 40, 3),
        (500, 730, 660, 615, 42, 3),
        (600, 845, 770, 720, 46, 3),
        (700, 960, 875, 820, 50, 5),
        (800, 1085, 990, 930, 54, 5),
        (900, 1185, 1090, 1030, 58, 5),
        (1000, 1320, 1210, 1140, 64, 5),
        (1100, 1420, 1310, 1240, 67, 5),
        (1200, 1530, 1420, 1350, 70, 5),
        (1350, 1700, 1590, 1510, 76, 5),
        (1500, 1865, 1750, 1670, 80, 5),
    };

    public static IReadOnlyList<SteelFlangeSpec> All { get; } = F12F15Rows
        .Select(x => new SteelFlangeSpec(SteelFittingClass.F12, x.Dn, x.D5, x.D4, x.D3, x.KF12, RfGasketThicknessMm))
        .Concat(F12F15Rows.Where(x => x.KF15 > 0)
            .Select(x => new SteelFlangeSpec(SteelFittingClass.F15, x.Dn, x.D5, x.D4, x.D3, x.KF15, RfGasketThicknessMm)))
        .Concat(F20Rows.Select(x => new SteelFlangeSpec(SteelFittingClass.F20, x.Dn, x.D5, x.D4, x.D3, x.K, RfGasketThicknessMm)))
        .ToList();

    /// <summary>등급·DN으로 조회. 표에 없으면 null. <see cref="SteelFittingClass.None"/>은 F12로 봄.</summary>
    public static SteelFlangeSpec? Find(SteelFittingClass steelClass, double dnMm)
    {
        var cls = ValveBypassSpecTable.NormalizeSteelClass(steelClass);
        return All.FirstOrDefault(x => x.Class == cls && Math.Abs(x.Dn - dnMm) <= MatchToleranceMm);
    }
}
