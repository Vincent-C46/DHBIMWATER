using DHBIMWATER.Core.Gis;
using System.Text.Json.Serialization;

namespace DHBIMWATER.Core.Piping;

/// <summary>적용 규격이 정의하는 이형관 형식 구분. Joint Type과는 별개다.</summary>
public enum BendForm { AType, BType }

/// <summary>이형관 접합 방식. 핸드북에서 소켓곡관과 플랜지곡관은 R·t가 다른 별도 표다(e·s는 같다).
/// 강관에서 <see cref="Socket"/>은 <b>용접</b>을 뜻한다(접합 슬롯 종류 <c>PipeAlignmentParameters.JointKindFor</c>와 같은 규약).</summary>
public enum BendConnection { Socket, Flanged }

/// <summary>KS D 3578 강관 이형관의 종류(F12/F15/F20). 관두께 T와 무게만 다르고 외경·R·L은 같다.
/// 주철관 행은 <see cref="None"/>이다. 직관 등급(STWW)과 대응시키지 않는다 — 사용자가 고른다(docs/211 § 0 확정 2).</summary>
public enum SteelFittingClass { None, F12, F15, F20 }

/// <param name="DiameterMm">호칭지름 DN. 조회는 Exact Match다.</param>
/// <param name="Form">
/// A형(양쪽 소켓)/B형(한쪽 소켓). 핸드북상 e·R·t는 형식과 무관하게 같고 s(추가 다리 길이)·무게만 다르다
/// (2026-08-19 확인). 행 하나가 한 형식만 대표하므로 기본은 B형이고, A형이 필요한 자리만 사용자가 바꾼다.
/// </param>
/// <param name="WallThicknessMm">e — 곡관 자체의 규격상 벽두께(mm).</param>
/// <param name="WeightKpMechanicalKg">KP 메커니컬 조인트 기준 무게(kg). 현재 <see cref="Form"/> 값에 대응한다.</param>
/// <param name="WeightTytonKg">타이튼 조인트 기준 무게(kg). 현재 <see cref="Form"/> 값에 대응한다.</param>
/// <param name="Material">
/// 이 규격이 속한 관종. DN·각도만으로는 관종이 구분되지 않아 조회 키에 필요하다.
/// 기본값이 있으므로 관종 축 도입 이전에 저장된 JSON도 그대로 역직렬화된다.
/// </param>
/// <param name="Connection">소켓/플랜지. 기본값이 있어 이 축 도입 이전 JSON도 소켓으로 읽힌다.</param>
/// <param name="FittingClass">강관 이형관 종류(F12/F15/F20). 주철관 행은 None. 기본값이 있어 이 축 도입 이전 JSON도 그대로 읽힌다(docs/211 § C-2).</param>
/// <param name="WeightKg">접합 종류와 무관한 한 개 무게(kg) — 강관 곡관과 주철관 플랜지곡관(B형, docs/260)용.
/// 주철관 소켓 행은 0이며 KP/타이튼 무게 열을 쓴다.</param>
public sealed record BendFittingEntry(
    double DiameterMm,
    double AngleDeg,
    BendForm Form,
    double LayingLengthMm,
    double CenterlineRadiusMm,
    double WallThicknessMm = 0d,
    double WeightKpMechanicalKg = 0d,
    double WeightTytonKg = 0d,
    PipeMaterial Material = PipeMaterial.DuctileIron,
    BendConnection Connection = BendConnection.Socket,
    SteelFittingClass FittingClass = SteelFittingClass.None,
    double WeightKg = 0d)
{
    /// <summary>s — B형(한쪽 소켓)만 200mm, A형(양쪽 소켓)은 0. 접합 종류와 무관하다(2026-09-04 확정).
    /// 강관은 소켓이 없어 형식과 무관하게 0이다(docs/211 § C-2).</summary>
    [JsonIgnore]
    public double ExtraLegLengthMm => Material == PipeMaterial.Steel ? 0d : Form == BendForm.BType ? 200d : 0d;
}

public sealed class BendFittingCatalog
{
    private const double Epsilon = 1e-6;

    public BendFittingCatalog(IReadOnlyList<BendFittingEntry> entries) => Entries = entries;
    public IReadOnlyList<BendFittingEntry> Entries { get; }

    /// <summary>
    /// 저장된 곡관표가 현행 규격 구조와 맞지 않아 [기본값 복원] 안내가 필요한 상태인지.
    /// ① 08-18자 임시표(R = 계수 × DN) ② 08-19자 A형·B형 이중행 ③ 접합종류 축 이전(플랜지 행 없음).
    /// </summary>
    public bool NeedsRestore => Entries.SequenceEqual(LegacyPlaceholder.Entries)
        || Entries.GroupBy(x => (x.Material, x.Connection, x.FittingClass, x.DiameterMm, x.AngleDeg)).Any(x => x.Count() > 1)
        || !Entries.Any(x => x.Connection == BendConnection.Flanged);

    /// <summary>DN·각도·관종·접합종류(·강관 종류)로 조회한다. 형식(A/B)은 행 자체가 갖고 있으므로 조회 키가 아니다.</summary>
    public BendFittingEntry? Find(double diameterMm, double angleDeg, PipeMaterial material = PipeMaterial.DuctileIron,
        BendConnection connection = BendConnection.Socket, SteelFittingClass fittingClass = SteelFittingClass.None) => Entries.FirstOrDefault(x =>
        x.Material == material
        && x.Connection == connection
        && x.FittingClass == fittingClass
        && Math.Abs(x.DiameterMm - diameterMm) <= Epsilon
        && Math.Abs(x.AngleDeg - angleDeg) <= Epsilon);

    /// <summary>강관 행이 하나도 없으면 기본 강관 행을 뒤에 붙인 사본을, 있으면 자신을 돌려준다(docs/211 § E-1).</summary>
    public BendFittingCatalog WithSteelDefaults()
        => Entries.Any(x => x.Material == PipeMaterial.Steel) ? this : new BendFittingCatalog(Entries.Concat(SteelDefaultEntries).ToList());

    // ══════════════════════════════════════════════════════════════════════════
    // 주철관 핸드북(2020년판) 소켓곡관 규격 — 출처: docs/주철관핸드북(2020년판)_이형관.pdf
    //   Ⅴ장 4. 90° / 5. 45° / 6. 22½° / 7. 11¼° 소켓곡관 표 (DN80~1200)
    //
    // 열 대응
    //   e → WallThicknessMm, R → CenterlineRadiusMm, t → LayingLengthMm(절점→관 끝), s → ExtraLegLengthMm(계산값)
    //
    // 판단 근거 (2026-08-19 / 2026-08-20 사용자 확인)
    //   · A형(양쪽 소켓)과 B형(한쪽 소켓)은 치수 e·R·t가 동일하고 s·무게만 다르다 → 행은 DN×각도 72개로 두고
    //     형식별 무게만 따로 둔다. s=200은 도면상 B형의 소켓 없는 쪽에만 표기된다 → A형 0, B형 200(계산값).
    //   · 표에 DE(외경) 열이 없다 → 곡관 외경은 직관 제원표(StraightPipeSpecTable)의 DE를 쓴다. DE는 관종 무관 DN 단일값이다.
    //   · 이형관 벽두께 e는 관종과 무관한 단일값이며 4개 각도가 모두 같다. 직관 두께(관종별)와는 다른 계열이다.
    //   · 무게는 조인트 종류(KP메커니컬/타이튼)·형식(A/B)별로 다르다. 핸드북에는 KP-L(장소켓) 무게도 있으나
    //     시스템이 다루는 조인트가 KP메커니컬·타이튼 2종뿐이라(JointTypeCatalog) 이번에는 반영하지 않는다.
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>이형관 벽두께 e — DN만의 함수다(각도·관종 무관).</summary>
    private static readonly (double Dn, double WallThicknessMm)[] WallThickness =
    {
        (80, 7.0), (100, 7.2), (125, 7.5), (150, 7.8), (200, 8.4), (250, 9.0),
        (300, 9.6), (350, 10.2), (400, 10.8), (450, 11.4), (500, 12.0), (600, 13.2),
        (700, 14.4), (800, 15.6), (900, 16.8), (1000, 18.0), (1100, 19.2), (1200, 20.4)
    };

    /// <summary>11¼° 소켓곡관 — (DN, R, t).</summary>
    private static readonly (double Dn, double RadiusMm, double LayingMm)[] Bend1125 =
    {
        (80, 75, 55), (100, 115, 55), (125, 125, 60), (150, 150, 60), (200, 185, 65), (250, 230, 75),
        (300, 310, 80), (350, 345, 85), (400, 380, 90), (450, 415, 95), (500, 495, 100), (600, 570, 110),
        (700, 685, 120), (800, 760, 135), (900, 875, 145), (1000, 995, 155), (1100, 1075, 165), (1200, 1195, 175)
    };

    /// <summary>22½° 소켓곡관 — (DN, R, t).</summary>
    private static readonly (double Dn, double RadiusMm, double LayingMm)[] Bend225 =
    {
        (80, 85, 65), (100, 105, 65), (125, 130, 75), (150, 155, 80), (200, 195, 90), (250, 240, 100),
        (300, 300, 110), (350, 345, 120), (400, 390, 135), (450, 435, 145), (500, 495, 155), (600, 590, 175),
        (700, 695, 200), (800, 800, 220), (900, 890, 245), (1000, 995, 265), (1100, 1050, 295), (1200, 1150, 310)
    };

    /// <summary>45° 소켓곡관 — (DN, R, t).</summary>
    private static readonly (double Dn, double RadiusMm, double LayingMm)[] Bend45 =
    {
        (80, 88, 80), (100, 100, 90), (125, 120, 100), (150, 145, 110), (200, 200, 135), (250, 245, 155),
        (300, 300, 175), (350, 350, 200), (400, 400, 220), (450, 450, 245), (500, 495, 265), (600, 595, 310),
        (700, 695, 355), (800, 795, 395), (900, 895, 440), (1000, 995, 485), (1100, 1095, 525), (1200, 1195, 575)
    };

    /// <summary>90° 소켓곡관 — (DN, R, t).</summary>
    private static readonly (double Dn, double RadiusMm, double LayingMm)[] Bend90 =
    {
        (80, 75, 150), (100, 95, 170), (125, 120, 195), (150, 145, 220), (200, 195, 270), (250, 240, 320),
        (300, 290, 370), (350, 340, 420), (400, 390, 470), (450, 435, 520), (500, 485, 570), (600, 580, 670),
        (700, 680, 770), (800, 775, 870), (900, 870, 970), (1000, 970, 1070), (1100, 1070, 1170), (1200, 1165, 1270)
    };

    /// <summary>표준각도 순서는 <see cref="JointDeflectionRule.StandardAngles"/>와 같게 둔다(규격표 행 순서).</summary>
    private static readonly (double AngleDeg, (double Dn, double RadiusMm, double LayingMm)[] Rows)[] AngleTables =
    {
        (11.25, Bend1125), (22.5, Bend225), (45d, Bend45), (90d, Bend90)
    };

    /// <summary>90° 플랜지곡관 — (DN, R, t). 출처 핸드북 p.105 (13. 90° FLANGED BEND).</summary>
    private static readonly (double Dn, double RadiusMm, double LayingMm)[] Flanged90 =
    {
        (80, 122, 165), (100, 135, 180), (125, 152.5, 200), (150, 170, 220), (200, 205, 260), (250, 290, 350),
        (300, 335, 400), (350, 380, 450), (400, 425, 500), (450, 470, 550), (500, 515, 600), (600, 605, 700),
        (700, 695, 800), (800, 785, 900), (900, 875, 1000), (1000, 965, 1100), (1100, 1055, 1200), (1200, 1145, 1300)
    };

    /// <summary>45° 플랜지곡관 — (DN, R, t). 출처 핸드북 p.106 (14. 45° FLANGED BEND).</summary>
    // TODO: 핸드북 원문 확인 필요 — DN250·300·350 R·t 비단조(인쇄 오류 의심). 원문 그대로 전사했다.
    private static readonly (double Dn, double RadiusMm, double LayingMm)[] Flanged45 =
    {
        (80, 210, 130), (100, 230, 140), (125, 250, 150), (150, 265, 160), (200, 300, 180), (250, 700, 350),
        (300, 809, 400), (350, 550, 300), (400, 600, 325), (450, 650, 350), (500, 700, 375), (600, 800, 425),
        (700, 900, 480), (800, 1000, 530), (900, 1100, 580), (1000, 1200, 630), (1100, 1300, 695), (1200, 1400, 750)
    };

    private static readonly (double AngleDeg, (double Dn, double RadiusMm, double LayingMm)[] Rows)[] FlangedAngleTables =
    {
        (45d, Flanged45), (90d, Flanged90)
    };

    /// <summary>관종·접합종류별 수록 각도. 주철관 플랜지곡관은 핸드북에 90°·45°만 있다(11¼·22½ 플랜지 표는 존재하지 않음).
    /// 강관은 접합과 무관하게 <see cref="SteelBendRule.StandardAngles"/>(5⅝° 포함)다 — 표에 없는 (DN, 각도)는 조회에서 빠진다.</summary>
    public static IReadOnlyList<double> AnglesFor(PipeMaterial material, BendConnection connection) =>
        material == PipeMaterial.Steel ? SteelBendRule.StandardAngles
        : connection == BendConnection.Flanged ? new[] { 45d, 90d } : JointDeflectionRule.StandardAngles;

    /// <summary>11¼° 소켓곡관 무게 — (DN, KP메커니컬 A형, KP메커니컬 B형, 타이튼 A형, 타이튼 B형) kg.</summary>
    private static readonly (double Dn, double KpA, double KpB, double TytonA, double TytonB)[] Weight1125 =
    {
        (80, 9.6, 8.4, 8.3, 7.4), (100, 11.6, 10.3, 10.5, 9.3), (125, 14.5, 13.1, 13.8, 12.2), (150, 18.2, 16.4, 17.6, 15.4),
        (200, 25.0, 23.0, 25.0, 22.0), (250, 34.5, 32.0, 35.5, 31.0), (300, 44.0, 41.0, 47.0, 40.5), (350, 57.5, 52.5, 60.5, 52.0),
        (400, 70.5, 64.0, 71.5, 62.0), (450, 86.0, 78.0, 93.5, 78.5), (500, 104, 93.5, 108, 92.0), (600, 149, 131, 143, 123),
        (700, 202, 175, 184, 160), (800, 248, 219, 234, 205), (900, 303, 271, 298, 259), (1000, 372, 332, 382, 326),
        (1100, 436, 393, 460, 393), (1200, 532, 475, 562, 476)
    };

    /// <summary>22½° 소켓곡관 무게 — (DN, KP메커니컬 A형, KP메커니컬 B형, 타이튼 A형, 타이튼 B형) kg.</summary>
    private static readonly (double Dn, double KpA, double KpB, double TytonA, double TytonB)[] Weight225 =
    {
        (80, 9.8, 8.6, 8.5, 7.6), (100, 11.9, 10.6, 10.8, 9.6), (125, 15.2, 13.8, 14.5, 12.9), (150, 19.3, 17.5, 18.7, 16.5),
        (200, 27.0, 25.0, 27.0, 24.0), (250, 37.5, 34.5, 38.0, 33.5), (300, 48.0, 45.0, 51.0, 44.5), (350, 63.0, 58.0, 66.0, 57.5),
        (400, 79.0, 73.0, 80.0, 71.0), (450, 97.5, 89.5, 105, 90.0), (500, 119, 108, 123, 107), (600, 172, 154, 166, 146),
        (700, 239, 211, 220, 196), (800, 295, 267, 282, 253), (900, 371, 338, 366, 327), (1000, 461, 420, 471, 415),
        (1100, 558, 515, 582, 515), (1200, 679, 623, 709, 624)
    };

    /// <summary>45° 소켓곡관 무게 — (DN, KP메커니컬 A형, KP메커니컬 B형, 타이튼 A형, 타이튼 B형) kg.</summary>
    private static readonly (double Dn, double KpA, double KpB, double TytonA, double TytonB)[] Weight45 =
    {
        (80, 10.2, 9.0, 8.9, 8.0), (100, 12.7, 11.4, 11.6, 10.4), (125, 16.2, 14.8, 15.5, 13.9), (150, 21.0, 19.0, 20.0, 18.0),
        (200, 30.0, 28.0, 30.5, 27.5), (250, 42.5, 40.0, 43.5, 39.0), (300, 56.0, 53.0, 59.0, 52.5), (350, 75.0, 70.0, 78.0, 69.5),
        (400, 94.5, 88.5, 95.5, 86.5), (450, 119, 111, 127, 112), (500, 147, 136, 151, 135), (600, 217, 199, 211, 191),
        (700, 304, 277, 285, 262), (800, 387, 358, 373, 344), (900, 494, 461, 489, 450), (1000, 626, 585, 636, 580),
        (1100, 758, 715, 782, 715), (1200, 950, 893, 980, 894)
    };

    /// <summary>90° 소켓곡관 무게 — (DN, KP메커니컬 A형, KP메커니컬 B형, 타이튼 A형, 타이튼 B형) kg.</summary>
    private static readonly (double Dn, double KpA, double KpB, double TytonA, double TytonB)[] Weight90 =
    {
        (80, 11.8, 10.6, 9.8, 8.9), (100, 14.9, 13.6, 12.9, 11.7), (125, 19.5, 18.1, 17.6, 16.0), (150, 25.5, 23.5, 23.5, 21.0),
        (200, 38.0, 36.0, 36.0, 33.0), (250, 55.0, 52.5, 53.5, 49.0), (300, 75.0, 71.5, 74.5, 68.0), (350, 101, 96.0, 100, 91.0),
        (400, 130, 123, 126, 116), (450, 165, 156, 166, 151), (500, 206, 195, 202, 186), (600, 307, 289, 292, 272),
        (700, 435, 408, 405, 381), (800, 573, 544, 545, 516), (900, 744, 712, 722, 683), (1000, 953, 912, 942, 887),
        (1100, 1178, 1135, 1178, 1111), (1200, 1475, 1418, 1477, 1391)
    };

    private static readonly (double AngleDeg, (double Dn, double KpA, double KpB, double TytonA, double TytonB)[] Rows)[] WeightTables =
    {
        (11.25, Weight1125), (22.5, Weight225), (45d, Weight45), (90d, Weight90)
    };

    /// <summary>핸드북 소켓곡관 무게표에서 (DN, 각도, 형식)에 해당하는 조인트별 무게를 찾는다. 규격표 창에서 행의 형식을 바꿀 때도 이 값으로 다시 채운다.</summary>
    public static bool TryGetHandbookWeight(double diameterMm, double angleDeg, BendForm form, out double kpMechanicalKg, out double tytonKg)
    {
        var table = WeightTables.FirstOrDefault(x => Math.Abs(x.AngleDeg - angleDeg) <= Epsilon).Rows;
        var row = table?.FirstOrDefault(x => Math.Abs(x.Dn - diameterMm) <= Epsilon);
        if (table is null || row is null || row.Value == default)
        {
            kpMechanicalKg = 0d; tytonKg = 0d; return false;
        }
        kpMechanicalKg = form == BendForm.AType ? row.Value.KpA : row.Value.KpB;
        tytonKg = form == BendForm.AType ? row.Value.TytonA : row.Value.TytonB;
        return true;
    }

    /// <summary>90° 플랜지곡관 무게 — (DN, A형, B형) kg, PN10. 출처 핸드북 p.105 (13. 90° FLANGED BEND, docs/260 § A).</summary>
    private static readonly (double Dn, double A, double B)[] FlangedWeight90 =
    {
        (80, 9.9, 9.8), (100, 11.9, 12.2), (125, 15.6, 16.2), (150, 20.0, 21.0), (200, 31.0, 32.5), (250, 50.0, 51.0),
        (300, 70.0, 71.0), (350, 90.0, 93.0), (400, 116, 120), (450, 143, 149), (500, 181, 187), (600, 272, 278),
        (700, 386, 393), (800, 533, 537), (900, 698, 704), (1000, 907, 912), (1100, 1069, 1059), (1200, 1463, 1440)
    };

    /// <summary>45° 플랜지곡관 무게 — (DN, A형, B형) kg, PN10. 출처 핸드북 p.106 (14. 45° FLANGED BEND).</summary>
    private static readonly (double Dn, double A, double B)[] FlangedWeight45 =
    {
        (80, 9.5, 9.4), (100, 11.3, 11.6), (125, 14.6, 15.2), (150, 18.5, 19.3), (200, 27.5, 29.0), (250, 55.0, 56.0),
        (300, 78.0, 79.0), (350, 76.0, 79.0), (400, 96.0, 100), (450, 115, 121), (500, 145, 151), (600, 212, 218),
        (700, 296, 303), (800, 403, 407), (900, 519, 525), (1000, 668, 673), (1100, 859, 850), (1200, 1050, 1041)
    };

    /// <summary>주철관 플랜지곡관 핸드북 무게(접합 종류 무관 한 개 무게). 소켓곡관의 <see cref="TryGetHandbookWeight"/>와 같은 역할이다 —
    /// 저장된 곡관표의 플랜지 행은 무게가 0이라(docs/260 이전 저장본) 배치 시 형식(A/B)에 맞춰 여기서 다시 찾는다.</summary>
    public static bool TryGetFlangedHandbookWeight(double diameterMm, double angleDeg, BendForm form, out double weightKg)
    {
        var table = Math.Abs(angleDeg - 90d) <= Epsilon ? FlangedWeight90 : Math.Abs(angleDeg - 45d) <= Epsilon ? FlangedWeight45 : null;
        var row = table?.FirstOrDefault(x => Math.Abs(x.Dn - diameterMm) <= Epsilon);
        if (table is null || row is null || row.Value == default) { weightKg = 0d; return false; }
        weightKg = form == BendForm.AType ? row.Value.A : row.Value.B;
        return true;
    }

    private static IEnumerable<BendFittingEntry> BuildHandbook()
    {
        foreach (var dn in StraightPipeSpecTable.DuctileIronNominalDiameters)
            foreach (var table in AngleTables)
            {
                var row = table.Rows.First(x => Math.Abs(x.Dn - dn) <= Epsilon);
                TryGetHandbookWeight(dn, table.AngleDeg, BendForm.BType, out var kpKg, out var tytonKg);
                yield return new BendFittingEntry(
                    dn,
                    table.AngleDeg,
                    BendForm.BType,
                    row.LayingMm,
                    row.RadiusMm,
                    WallThickness.First(x => Math.Abs(x.Dn - dn) <= Epsilon).WallThicknessMm,
                    kpKg,
                    tytonKg,
                    Connection: BendConnection.Socket);
            }

        foreach (var dn in StraightPipeSpecTable.DuctileIronNominalDiameters)
            foreach (var table in FlangedAngleTables)
            {
                var row = table.Rows.First(x => Math.Abs(x.Dn - dn) <= Epsilon);
                TryGetFlangedHandbookWeight(dn, table.AngleDeg, BendForm.BType, out var flangedKg);
                yield return new BendFittingEntry(
                    dn,
                    table.AngleDeg,
                    BendForm.BType,
                    row.LayingMm,
                    row.RadiusMm,
                    WallThickness.First(x => Math.Abs(x.Dn - dn) <= Epsilon).WallThicknessMm,
                    Connection: BendConnection.Flanged,
                    WeightKg: flangedKg);
            }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 강관 곡관 규격 — 출처: KS D 3578 부도 1~5 (docs/강관핸드북_곡관및이형관.pdf, 원본 docs/강관핸드북-1.pdf p.157~161)
    //   docs/211_data_강관곡관_KSD3578.csv를 그대로 옮겼다(스크립트 생성).
    //
    // 열 대응 (docs/211 § C-5, 2026-09-22 가변점 검토)
    //   R → CenterlineRadiusMm, L(두 중심선 교점=절점 → 관 끝) → LayingLengthMm, T → WallThicknessMm(종류별), 무게 → WeightKg(종류별)
    //   l₃는 L − R·tan(θ/2)로 유도되고 l₁·l₂는 미터 절단 제작 치수라 단일 호 5점 패밀리에는 쓰지 않는다.
    //   전 행에서 L ≥ R·tan(θ/2)이고, 무게 = 관실길이 × 0.02466·T·(D−T)로 검산했다(불일치 0건).
    //
    // 판단 근거 (2026-09-22 사용자 확정)
    //   · 종류 F12/F15/F20은 관두께·무게만 다르고 외경·R·L은 같다. 직관 등급(STWW)과 대응시키지 않고 사용자가 고른다.
    //   · 5⅝°는 핸드북에 DN1000 이상만 있다. 그 미만 DN에는 만들지 않는다.
    //   · 플랜지 곡관 표가 발췌본에 없어 용접곡관 치수·무게(플랜지 제외)를 플랜지 접합에도 그대로 쓴다(docs/211 § 0-1 A1).
    //   · 강관 곡관에는 A/B형·소켓이 없다 — Form은 BType으로 채우되 ExtraLegLengthMm은 0이다.
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>강관 곡관 관두께 T — 종류(F12/F15/F20)별 DN 단일값(각도 무관). 출처 KS D 3578 부표 2·부도 1~5.</summary>
    private static readonly (double Dn, double F12, double F15, double F20)[] SteelWallThickness =
    {
        (80, 4.2, 4.2, 4.5), (100, 4.5, 4.5, 4.9), (125, 4.5, 4.5, 5.1), (150, 5, 5, 5.5), (200, 5.8, 5.8, 6.4),
        (250, 6.6, 6.6, 6.4), (300, 6.9, 6.9, 6.4), (350, 6, 6, 6), (400, 6, 6, 6), (450, 6, 6, 6),
        (500, 6, 6, 6), (600, 6, 6, 6), (700, 6, 6, 7), (800, 7, 7, 8), (900, 7, 8, 8),
        (1000, 8, 9, 9), (1100, 8, 10, 10), (1200, 9, 11, 11), (1350, 10, 12, 12), (1500, 11, 14, 14),
        (1600, 12, 15, 15), (1650, 12, 15, 15), (1800, 13, 16, 16), (1900, 14, 17, 17), (2000, 15, 18, 18),
        (2100, 16, 19, 19), (2200, 16, 20, 20), (2300, 17, 21, 21), (2400, 18, 22, 22), (2500, 18, 23, 23),
        (2600, 19, 24, 24), (2700, 20, 25, 25), (2800, 21, 26, 26), (2900, 21, 27, 27), (3000, 22, 29, 29)
    };

    /// <summary>부도 1 90° 곡관 — (DN, R, L, 무게 F12, F15, F20 kg).</summary>
    private static readonly (double Dn, double RadiusMm, double LayingMm, double WeightF12Kg, double WeightF15Kg, double WeightF20Kg)[] SteelBend90 =
    {
        (80, 230, 400, 6.24, 6.24, 6.66), (100, 230, 400, 8.66, 8.66, 9.37), (125, 230, 400, 10.6, 10.6, 12),
        (150, 250, 450, 15.9, 15.9, 17.4), (200, 310, 500, 26.4, 26.4, 29.1), (250, 360, 550, 40.7, 40.7, 39.5),
        (300, 410, 600, 55.1, 55.1, 51.2), (350, 460, 600, 52.7, 52.7, 52.7), (400, 510, 650, 65.1, 65.1, 65.1),
        (450, 530, 700, 79.6, 79.6, 79.6), (500, 560, 700, 87.7, 87.7, 87.7), (600, 660, 850, 129, 129, 129),
        (700, 790, 950, 165, 165, 194), (800, 790, 950, 221, 221, 253), (900, 860, 1050, 277, 316, 316),
        (1000, 910, 1100, 367, 411, 411), (1100, 910, 1100, 404, 503, 503), (1200, 970, 1150, 516, 630, 630),
        (1350, 1020, 1200, 672, 804, 804), (1500, 1070, 1250, 853, 1080, 1080), (1600, 1200, 1350, 1060, 1330, 1330),
        (1650, 1250, 1400, 1140, 1420, 1420), (1800, 1300, 1450, 1390, 1710, 1710), (1900, 1350, 1500, 1640, 1980, 1980),
        (2000, 1400, 1550, 1900, 2280, 2280), (2100, 1450, 1600, 2200, 2610, 2610), (2200, 1500, 1650, 2380, 2960, 2960),
        (2300, 1550, 1700, 2720, 3350, 3350), (2400, 1600, 1750, 3080, 3770, 3770), (2500, 1650, 1800, 3300, 4220, 4220),
        (2600, 1700, 1850, 3730, 4700, 4700), (2700, 1750, 1900, 4180, 5220, 5220), (2800, 1800, 1950, 4670, 5770, 5770),
        (2900, 1850, 2000, 4960, 6360, 6360), (3000, 1900, 2050, 5510, 7240, 7240)
    };

    /// <summary>부도 2 45° 곡관 — (DN, R, L, 무게 F12, F15, F20 kg).</summary>
    private static readonly (double Dn, double RadiusMm, double LayingMm, double WeightF12Kg, double WeightF15Kg, double WeightF20Kg)[] SteelBend45 =
    {
        (80, 370, 350, 6.05, 6.05, 6.46), (100, 370, 350, 8.39, 8.39, 9.08), (125, 370, 350, 10.3, 10.3, 11.6),
        (150, 430, 450, 17.5, 17.5, 19.2), (200, 490, 450, 26.6, 26.6, 29.3), (250, 550, 450, 37.4, 37.4, 36.3),
        (300, 610, 450, 46.6, 46.6, 43.4), (350, 680, 500, 50.6, 50.6, 50.6), (400, 740, 500, 57.8, 57.8, 57.8),
        (450, 800, 500, 65, 65, 65), (500, 860, 500, 72.2, 72.2, 72.2), (600, 980, 750, 131, 131, 131),
        (700, 1170, 750, 152, 152, 178), (800, 1170, 1000, 273, 273, 312), (900, 1290, 1000, 307, 350, 350),
        (1000, 1350, 1000, 389, 436, 436), (1100, 1350, 1000, 428, 534, 534), (1200, 1410, 1000, 526, 641, 641),
        (1350, 1470, 1000, 656, 785, 785), (1500, 1530, 1000, 799, 1020, 1020), (1600, 1680, 1000, 928, 1160, 1160),
        (1650, 1680, 1000, 959, 1200, 1200), (1800, 1680, 1000, 1130, 1390, 1390), (1900, 1800, 1000, 1290, 1560, 1560),
        (2000, 1800, 1000, 1450, 1740, 1740), (2100, 1920, 1050, 1700, 2020, 2020), (2200, 1920, 1050, 1780, 2230, 2230),
        (2300, 2040, 1100, 2080, 2560, 2560), (2400, 2040, 1100, 2290, 2800, 2800), (2500, 2160, 1150, 2500, 3180, 3180),
        (2600, 2160, 1150, 2740, 3450, 3450), (2700, 2160, 1150, 2990, 3740, 3740), (2800, 2280, 1200, 3400, 4200, 4200),
        (2900, 2280, 1200, 3520, 4520, 4520), (3000, 2400, 1250, 3980, 5230, 5230)
    };

    /// <summary>부도 3 22½° 곡관 — (DN, R, L, 무게 F12, F15, F20 kg).</summary>
    private static readonly (double Dn, double RadiusMm, double LayingMm, double WeightF12Kg, double WeightF15Kg, double WeightF20Kg)[] SteelBend225 =
    {
        (80, 380, 200, 3.52, 3.52, 3.76), (100, 380, 200, 4.88, 4.88, 5.28), (125, 380, 200, 6, 6, 6.76),
        (150, 380, 200, 7.92, 7.92, 8.68), (200, 510, 250, 15.1, 15.1, 16.5), (250, 510, 250, 21.2, 21.2, 20.6),
        (300, 640, 250, 26.5, 26.5, 24.6), (350, 640, 500, 51.7, 51.7, 51.7), (400, 770, 500, 59.2, 59.2, 59.2),
        (450, 770, 500, 66.8, 66.8, 66.8), (500, 890, 500, 74.3, 74.3, 74.3), (600, 1020, 750, 134, 134, 134),
        (700, 1150, 750, 156, 156, 183), (800, 1150, 1000, 278, 278, 318), (900, 1280, 1000, 314, 358, 358),
        (1000, 1410, 1000, 398, 446, 446), (1100, 1410, 1000, 438, 546, 546), (1200, 1410, 1000, 538, 656, 656),
        (1350, 1530, 1000, 672, 804, 804), (1500, 1530, 1000, 820, 1040, 1040), (1600, 1750, 1000, 954, 1190, 1190),
        (1650, 1750, 1000, 986, 1230, 1230), (1800, 1750, 1000, 1160, 1430, 1430), (1900, 1750, 1000, 1320, 1600, 1600),
        (2000, 1750, 1000, 1490, 1790, 1790), (2100, 1950, 1000, 1670, 1980, 1980), (2200, 1950, 1000, 1750, 2190, 2190),
        (2300, 1950, 1000, 1950, 2400, 2400), (2400, 1950, 1000, 2150, 2620, 2620), (2500, 1950, 1000, 2240, 2860, 2860),
        (2600, 2150, 1000, 2460, 3100, 3100), (2700, 2150, 1000, 2690, 3350, 3350), (2800, 2150, 1000, 2920, 3610, 3610),
        (2900, 2150, 1000, 3030, 3890, 3890), (3000, 2150, 1000, 3280, 4320, 4320)
    };

    /// <summary>부도 4 11¼° 곡관 — (DN, R, L, 무게 F12, F15, F20 kg).</summary>
    private static readonly (double Dn, double RadiusMm, double LayingMm, double WeightF12Kg, double WeightF15Kg, double WeightF20Kg)[] SteelBend1125 =
    {
        (80, 770, 200, 3.52, 3.52, 3.76), (100, 770, 200, 4.88, 4.88, 5.28), (125, 770, 200, 6, 6, 6.76),
        (150, 770, 200, 7.92, 7.92, 8.68), (200, 1030, 250, 15.1, 15.1, 16.5), (250, 1030, 250, 21.2, 21.2, 20.6),
        (300, 1290, 250, 26.5, 26.5, 24.6), (350, 1290, 500, 51.7, 51.7, 51.7), (400, 1550, 500, 59.2, 59.2, 59.2),
        (450, 1550, 500, 66.8, 66.8, 66.8), (500, 1810, 500, 74.3, 74.3, 74.3), (600, 2060, 750, 134, 134, 134),
        (700, 2320, 750, 156, 156, 183), (800, 2320, 1000, 278, 278, 318), (900, 2580, 1000, 314, 358, 358),
        (1000, 2840, 1000, 398, 446, 446), (1100, 2840, 1000, 438, 546, 546), (1200, 2840, 1000, 538, 656, 656),
        (1350, 3100, 1000, 672, 804, 804), (1500, 3100, 1000, 820, 1040, 1040), (1600, 3530, 1000, 954, 1190, 1190),
        (1650, 3530, 1000, 986, 1230, 1230), (1800, 3530, 1000, 1160, 1430, 1430), (1900, 3530, 1000, 1320, 1600, 1600),
        (2000, 3530, 1000, 1490, 1790, 1790), (2100, 3950, 1000, 1670, 1980, 1980), (2200, 3950, 1000, 1750, 2190, 2190),
        (2300, 3950, 1000, 1950, 2400, 2400), (2400, 3950, 1000, 2150, 2620, 2620), (2500, 3950, 1000, 2240, 2860, 2860),
        (2600, 4400, 1000, 2460, 3100, 3100), (2700, 4400, 1000, 2690, 3350, 3350), (2800, 4400, 1000, 2920, 3610, 3610),
        (2900, 4400, 1000, 3030, 3890, 3890), (3000, 4400, 1000, 3280, 4320, 4320)
    };

    /// <summary>부도 5 5⅝° (DN1000 이상만 수록) 곡관 — (DN, R, L, 무게 F12, F15, F20 kg).
    /// DN2400 F15/F20 2 610kg은 다른 각도 표(2 620)와 다르지만 원문 그대로다(docs/211 § 0-1 A5).</summary>
    private static readonly (double Dn, double RadiusMm, double LayingMm, double WeightF12Kg, double WeightF15Kg, double WeightF20Kg)[] SteelBend5625 =
    {
        (1000, 5690, 1000, 398, 446, 446), (1100, 5690, 1000, 438, 546, 546), (1200, 5690, 1000, 538, 656, 656),
        (1350, 6210, 1000, 672, 804, 804), (1500, 6210, 1000, 820, 1040, 1040), (1600, 7080, 1000, 954, 1190, 1190),
        (1650, 7080, 1000, 986, 1230, 1230), (1800, 7080, 1000, 1160, 1430, 1430), (1900, 7080, 1000, 1320, 1600, 1600),
        (2000, 7080, 1000, 1490, 1790, 1790), (2100, 7920, 1000, 1670, 1980, 1980), (2200, 7920, 1000, 1750, 2190, 2190),
        (2300, 7920, 1000, 1950, 2400, 2400), (2400, 7920, 1000, 2150, 2610, 2610), (2500, 7920, 1000, 2240, 2860, 2860),
        (2600, 8820, 1000, 2460, 3100, 3100), (2700, 8820, 1000, 2690, 3350, 3350), (2800, 8820, 1000, 2920, 3610, 3610),
        (2900, 8820, 1000, 3030, 3890, 3890), (3000, 8820, 1000, 3280, 4320, 4320)
    };

    private static readonly (double AngleDeg, (double Dn, double RadiusMm, double LayingMm, double WeightF12Kg, double WeightF15Kg, double WeightF20Kg)[] Rows)[] SteelAngleTables =
    {
        (5.625, SteelBend5625), (11.25, SteelBend1125), (22.5, SteelBend225), (45d, SteelBend45), (90d, SteelBend90)
    };

    private static readonly SteelFittingClass[] SteelClasses = { SteelFittingClass.F12, SteelFittingClass.F15, SteelFittingClass.F20 };

    /// <summary>강관 곡관 종류별 관두께(F12/F15/F20). 표에 없는 DN이면 null.</summary>
    public static double? SteelWallThicknessOf(double diameterMm, SteelFittingClass fittingClass)
    {
        var row = SteelWallThickness.FirstOrDefault(x => Math.Abs(x.Dn - diameterMm) <= Epsilon);
        if (row == default) return null;
        return fittingClass switch
        {
            SteelFittingClass.F12 => row.F12,
            SteelFittingClass.F15 => row.F15,
            SteelFittingClass.F20 => row.F20,
            _ => null
        };
    }

    /// <summary>강관 기본 곡관 행 — 각도 5 × DN × 종류 3 × 접합 2 = 960행(docs/211 § C-5).</summary>
    private static IEnumerable<BendFittingEntry> BuildSteelHandbook()
    {
        foreach (var table in SteelAngleTables)
            foreach (var row in table.Rows)
                foreach (var fittingClass in SteelClasses)
                {
                    var thickness = SteelWallThicknessOf(row.Dn, fittingClass) ?? 0d;
                    var weight = fittingClass switch
                    {
                        SteelFittingClass.F12 => row.WeightF12Kg,
                        SteelFittingClass.F15 => row.WeightF15Kg,
                        _ => row.WeightF20Kg
                    };
                    foreach (var connection in new[] { BendConnection.Socket, BendConnection.Flanged })
                        yield return new BendFittingEntry(
                            row.Dn, table.AngleDeg, BendForm.BType, row.LayingMm, row.RadiusMm, thickness,
                            Material: PipeMaterial.Steel, Connection: connection, FittingClass: fittingClass, WeightKg: weight);
                }
    }

    /// <summary>강관 기본 곡관 행. 기존 프로젝트 저장본에 강관 행이 없을 때 병합용으로도 쓴다(<see cref="WithSteelDefaults"/>).</summary>
    public static IReadOnlyList<BendFittingEntry> SteelDefaultEntries { get; } = BuildSteelHandbook().ToList();

    public static BendFittingCatalog Default { get; } = new(BuildHandbook().Concat(SteelDefaultEntries).ToList());

    // ── 구 임시값 감지 전용 (2026-08-18~19 사이에 저장된 프로젝트가 대상) ────────────
    // 아래 블록은 배치에 쓰이지 않는다. 저장된 규격이 그 시기의 임시값 그대로인지 판별해
    // [기본값 복원] 안내를 띄우는 데에만 쓴다. 안내가 충분히 돌고 나면 통째로 삭제할 수 있다.

    /// <summary>구 임시표의 형식별 곡률반경 계수(R = 계수 × DN).</summary>
    private static readonly (BendForm Form, double RadiusFactor)[] PlaceholderForms =
    {
        (BendForm.AType, 2.5), (BendForm.BType, 1.5)
    };

    private const double PlaceholderLayingClearanceMm = 50d;

    private static IEnumerable<BendFittingEntry> BuildPlaceholders() =>
        from dn in StraightPipeSpecTable.DuctileIronNominalDiameters
        from form in PlaceholderForms
        from angle in JointDeflectionRule.StandardAngles
        let radius = Math.Round(form.RadiusFactor * dn, 1)
        select new BendFittingEntry(
            dn,
            angle,
            form.Form,
            Math.Ceiling(BendResolver.TangentLength(radius, angle)) + PlaceholderLayingClearanceMm,
            radius,
            StraightPipeSpecTable.Default.Find(PipeKindCatalog.Water1, dn)?.ThicknessMm ?? 0d);

    private static BendFittingCatalog LegacyPlaceholder { get; } = new(BuildPlaceholders().ToList());
}

/// <summary>관로 규격과 판정 설정의 저장 단위.</summary>
public sealed record BendSettings(
    StraightPipeSpecTable StraightPipes,
    JointDeflectionTable JointDeflections,
    BendFittingCatalog Fittings,
    string ActiveJointType,
    /// <summary>배치에 쓸 곡관 접합 종류. 모델링 창에서 고르며 기본은 소켓이다.</summary>
    BendConnection ActiveBendConnection = BendConnection.Socket,
    /// <summary>배치에 쓸 곡관 형식. A형(양쪽 소켓)은 s=0·양쪽 Joint, B형(한쪽 소켓)은 s=200·한쪽 Joint를 결정하며 접합 종류와 무관하다(2026-09-04 확정).</summary>
    BendForm ActiveBendForm = BendForm.BType,
    /// <summary>주철관 소켓 접합에 <b>이탈방지압륜</b>을 쓰는지(docs/166 § 11-4).
    /// <para>산출서에서 `이탈방지압륜 접합 및 부설`은 `KP접합 및 부설`과 별개 공종이라 개소를 다른 열에 적어야 한다.
    /// 켜면 주철관 소켓 부재의 접합 슬롯 `종류`가 <c>이탈방지압륜접합</c>, 끄면 <c>KP접합</c>이 된다.</para>
    /// <para>형상·허용굴곡에는 <b>영향이 없다</b> — 압륜은 같은 KP 메커니컬 조인트에 덧대는 부속이다.
    /// 강관·플랜지 접합에도 영향이 없다.</para></summary>
    bool UseRestraintGland = false,
    /// <summary>강관 곡관·이형관에 쓸 종류(F12/F15/F20). 직관 등급(STWW)과 무관하게 모델링 창에서 고른다(docs/211 § 0 확정 2).
    /// 주철관 절점에는 영향이 없다.</summary>
    SteelFittingClass ActiveSteelFittingClass = SteelFittingClass.F12,
    /// <summary>강관 곡관의 접합 적용(편측 1개소 · 양측 2개소). 슬롯 수와 접합구성(1S/2S)만 정한다 — 강관 허용굴곡은 늘 한쪽 Joint로 판정한다.
    /// <para>강관에는 A/B형이 없어 <see cref="ApplicationMode"/>(곡관 형식 유도)와 분리한다 — 공유하면 주철관 형상(s=0/200)과 간섭한다(docs/243 § B).</para></summary>
    JointApplicationMode SteelApplicationMode = JointApplicationMode.SingleJoint)
{
    /// <summary>허용굴곡 적용 방식은 곡관 형식이 결정한다. A형은 양쪽 Joint(θ₁+θ₂), B형은 한쪽 Joint.</summary>
    public JointApplicationMode ApplicationMode =>
        ActiveBendForm == BendForm.AType ? JointApplicationMode.BothJoints : JointApplicationMode.SingleJoint;

    public static BendSettings Default { get; } = new(
        StraightPipeSpecTable.Default,
        JointDeflectionTable.Default,
        BendFittingCatalog.Default,
        JointTypeCatalog.KpMechanical);

    /// <summary>직관표·곡관표에 강관 행이 전무하면 기본 강관 행을 붙인 사본을 돌려준다(docs/211 § E-1).
    /// 기존 프로젝트 저장본에는 강관 행이 없는데 [기본값 복원]을 강요하면 사용자가 손본 주철관 값이 날아가므로 병합한다.
    /// 강관 행이 하나라도 남아 있으면 사용자 편집으로 보고 건드리지 않는다.</summary>
    public BendSettings WithSteelDefaults()
    {
        var straight = StraightPipes.WithSteelDefaults();
        var fittings = Fittings.WithSteelDefaults();
        return ReferenceEquals(straight, StraightPipes) && ReferenceEquals(fittings, Fittings)
            ? this
            : this with { StraightPipes = straight, Fittings = fittings };
    }
}
