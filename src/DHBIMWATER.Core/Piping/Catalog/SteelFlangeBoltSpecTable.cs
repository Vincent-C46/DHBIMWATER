namespace DHBIMWATER.Core.Piping;

/// <summary>강관 플랜지 접합용 6각 볼트·너트 한 행 — 부도 13A(강관 핸드북 p.264). 단위 mm.
/// <para>d = 볼트 호칭(M16 → 16), L1 = RF-RF/RF-GF 접속 길이, L2 = 플랜지-게이트밸브 접속 길이(F12 DN1500 이하만, 밖은 null),
/// S = 나사부 길이, B = 너트 맞변거리, SetCount = 1세트수(볼트 개수).</para></summary>
public sealed record SteelFlangeBoltSpec(SteelFittingClass Class, double Dn,
    double BoltDiameterMm, double LengthL1Mm, double? LengthL2Mm,
    double ThreadLengthMm, double NutWidthMm, int SetCount);

/// <summary>강관 플랜지 볼트 고정표. 출처 <c>docs/강관핸드북_플랜지규격.pdf</c> 1쪽(스캔) — 확대 이미지 눈 전사(docs/317).
/// <para>F20 DN300 B 원문 `369` = 오인쇄로 보고 36(같은 M24인 DN250과 같음). F12 DN80 세트수 4, F20 DN1350 세트수 32는 원문 그대로.</para>
/// <para>범위: F12 DN80~3000, F15 DN80~2600, F20 DN80~1500. DN1900·DN2900 행 없음(플랜지 표에는 있음) — 밖이면 null.</para>
/// <para>행 데이터를 연동 애드인 카탈로그로 옮기지 않는다 — 배치 형상값(<see cref="SteelFlangeSpecTable"/>과 같은 결정).</para></summary>
public static class SteelFlangeBoltSpecTable
{
    private const double MatchToleranceMm = 0.5;

    // (DN, d, L1, L2, S, B, 세트수). L2 0 = 표에 `-`.
    private static readonly (double Dn, double D, double L1, double L2, double S, double B, int Sets)[] F12Rows =
    {
        (80, 16, 75, 75, 38, 24, 4),
        (100, 16, 75, 75, 38, 24, 8),
        (125, 16, 75, 75, 38, 24, 8),
        (150, 20, 75, 75, 38, 24, 8),
        (200, 20, 80, 80, 38, 24, 8),
        (250, 20, 85, 85, 46, 30, 12),
        (300, 20, 85, 90, 46, 30, 12),
        (350, 20, 95, 95, 50, 32, 16),
        (400, 24, 95, 95, 50, 32, 16),
        (450, 24, 100, 100, 54, 36, 20),
        (500, 24, 100, 110, 54, 36, 20),
        (600, 27, 100, 120, 54, 36, 20),
        (700, 27, 110, 130, 66, 46, 24),
        (800, 30, 120, 130, 66, 46, 24),
        (900, 30, 120, 140, 66, 46, 28),
        (1000, 33, 130, 150, 72, 46, 28),
        (1100, 33, 130, 150, 72, 46, 32),
        (1200, 33, 140, 160, 72, 46, 32),
        (1350, 36, 150, 170, 84, 55, 36),
        (1500, 36, 150, 180, 84, 55, 36),
        (1600, 36, 160, 0, 84, 55, 40),
        (1650, 36, 160, 0, 84, 55, 40),
        (1800, 45, 170, 0, 84, 55, 44),
        (2000, 45, 180, 0, 96, 65, 48),
        (2100, 45, 190, 0, 96, 65, 48),
        (2200, 52, 190, 0, 96, 65, 52),
        (2300, 52, 190, 0, 96, 65, 52),
        (2400, 52, 200, 0, 96, 65, 56),
        (2500, 52, 220, 0, 121, 75, 56),
        (2600, 52, 220, 0, 121, 75, 60),
        (2700, 52, 220, 0, 121, 75, 60),
        (2800, 52, 220, 0, 121, 75, 64),
        (3000, 52, 240, 0, 121, 75, 64),
    };

    // (DN, d, L1, S, B, 세트수). L2 없음.
    private static readonly (double Dn, double D, double L1, double S, double B, int Sets)[] F15Rows =
    {
        (80, 16, 65, 38, 24, 4),
        (100, 16, 65, 38, 24, 8),
        (125, 16, 70, 46, 30, 8),
        (150, 20, 75, 46, 30, 8),
        (200, 20, 75, 46, 30, 8),
        (250, 20, 80, 50, 32, 12),
        (300, 20, 80, 50, 32, 12),
        (350, 20, 85, 50, 32, 16),
        (400, 24, 100, 54, 36, 16),
        (450, 24, 100, 54, 36, 20),
        (500, 24, 100, 54, 36, 20),
        (600, 27, 110, 66, 46, 20),
        (700, 27, 110, 66, 46, 24),
        (800, 30, 120, 66, 46, 24),
        (900, 30, 120, 66, 46, 28),
        (1000, 33, 140, 84, 55, 28),
        (1100, 33, 140, 84, 55, 32),
        (1200, 33, 150, 84, 55, 32),
        (1350, 36, 170, 96, 65, 36),
        (1500, 36, 170, 96, 65, 36),
        (1600, 36, 180, 102, 70, 40),
        (1650, 36, 180, 102, 70, 40),
        (1800, 45, 190, 102, 70, 44),
        (2000, 45, 190, 102, 70, 48),
        (2100, 45, 200, 102, 70, 48),
        (2200, 52, 220, 129, 80, 52),
        (2300, 52, 220, 129, 80, 52),
        (2400, 52, 220, 129, 80, 56),
        (2500, 52, 220, 129, 80, 56),
        (2600, 52, 220, 129, 80, 60),
    };

    // (DN, d, L1, S, B, 세트수). L2 없음.
    private static readonly (double Dn, double D, double L1, double S, double B, int Sets)[] F20Rows =
    {
        (80, 20, 75, 46, 30, 8),
        (100, 20, 75, 46, 30, 8),
        (125, 22, 80, 50, 32, 8),
        (150, 22, 85, 50, 32, 12),
        (200, 22, 85, 50, 32, 12),
        (250, 24, 95, 54, 36, 12),
        (300, 24, 95, 54, 36, 16),   // B 원문 369 — 오인쇄 보정
        (350, 30, 110, 66, 46, 16),
        (400, 30, 130, 72, 46, 16),
        (450, 30, 130, 72, 46, 20),
        (500, 30, 130, 72, 46, 20),
        (600, 36, 150, 84, 55, 24),
        (700, 39, 160, 90, 60, 24),
        (800, 45, 170, 102, 70, 24),
        (900, 45, 180, 102, 70, 28),
        (1000, 52, 200, 116, 80, 28),
        (1100, 52, 210, 116, 80, 32),
        (1200, 52, 210, 116, 80, 32),
        (1350, 56, 230, 137, 85, 32),
        (1500, 56, 240, 137, 85, 36),
    };

    public static IReadOnlyList<SteelFlangeBoltSpec> All { get; } = F12Rows
        .Select(x => new SteelFlangeBoltSpec(SteelFittingClass.F12, x.Dn, x.D, x.L1, x.L2 > 0 ? x.L2 : null, x.S, x.B, x.Sets))
        .Concat(F15Rows.Select(x => new SteelFlangeBoltSpec(SteelFittingClass.F15, x.Dn, x.D, x.L1, null, x.S, x.B, x.Sets)))
        .Concat(F20Rows.Select(x => new SteelFlangeBoltSpec(SteelFittingClass.F20, x.Dn, x.D, x.L1, null, x.S, x.B, x.Sets)))
        .ToList();

    /// <summary>등급·DN으로 조회. 표에 없으면 null. <see cref="SteelFittingClass.None"/>은 F12로 봄.</summary>
    public static SteelFlangeBoltSpec? Find(SteelFittingClass steelClass, double dnMm)
    {
        var cls = ValveBypassSpecTable.NormalizeSteelClass(steelClass);
        return All.FirstOrDefault(x => x.Class == cls && Math.Abs(x.Dn - dnMm) <= MatchToleranceMm);
    }
}
