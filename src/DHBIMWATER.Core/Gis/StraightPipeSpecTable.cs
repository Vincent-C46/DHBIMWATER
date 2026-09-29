namespace DHBIMWATER.Core.Gis;

/// <summary>직관 전용 규격. 곡관 벽두께와 허용굴곡에는 관여하지 않는다.</summary>
/// <param name="DiameterMm">호칭지름 DN. 조회는 Exact Match다.</param>
/// <param name="UnitWeightKgPerM">직관부 M무게(kg/m, 핸드북 "M무게(약)"). 관종(등급)마다 다르다. 0이면 미기재(docs/178 § A-2).</param>
/// <param name="SocketWeightKpKg">KP 메커니컬 조인트 소켓 무게(kg). 관종과 무관한 DN 단일값이다.</param>
/// <param name="SocketWeightTytonKg">타이튼 조인트 소켓 무게(kg). 관종과 무관한 DN 단일값이다.</param>
public sealed record StraightPipeSpec(string PipeKind, double DiameterMm, double OuterDiameterMm, double ThicknessMm,
    double UnitWeightKgPerM = 0d, double SocketWeightKpKg = 0d, double SocketWeightTytonKg = 0d);

/// <summary>직관 한 본의 무게(docs/178 § A-2, 2026-09-16 사용자 확정) — <b>단위중량 × 연장 + 소켓(접합) 무게</b>.
/// <para>핸드북의 "KP 메커니컬 조인트관 총무게"가 이 식과 같다(DN300 상수 1종 6M: 61.8×6 + 16.7 = 387.5 ≈ 388).
/// 라이닝 무게는 더하지 않는다 — 산출서 관 중량은 관체 기준이다.</para>
/// <para>곡관은 이 식을 쓰지 않는다. 곡관표(<see cref="BendFittingEntry"/>)의 무게가 소켓을 포함한 한 개 무게다.</para></summary>
public static class StraightPipeWeight
{
    /// <summary>접합 종류에 맞는 소켓 무게. 타이튼이면 타이튼 소켓, 그 외(KP·미상)는 KP 소켓이다.</summary>
    public static double SocketWeightKg(StraightPipeSpec spec, string? jointType)
        => string.Equals(jointType, JointTypeCatalog.Tyton, StringComparison.OrdinalIgnoreCase) ? spec.SocketWeightTytonKg : spec.SocketWeightKpKg;

    /// <summary>단위중량이 없으면 null — 값을 지어내지 않고 매개변수를 비워 둔다.</summary>
    public static double? TotalKg(StraightPipeSpec? spec, double lengthM, string? jointType)
        => spec is null || spec.UnitWeightKgPerM <= 0d || lengthM <= 0d
            ? null
            : spec.UnitWeightKgPerM * lengthM + SocketWeightKg(spec, jointType);
}

public sealed class StraightPipeSpecTable
{
    private const double Epsilon = 1e-6;

    public StraightPipeSpecTable(IReadOnlyList<StraightPipeSpec> entries) => Entries = entries;

    public IReadOnlyList<StraightPipeSpec> Entries { get; }

    /// <summary>정확히 일치하는 (관종, DN)이 없으면 null을 반환한다.</summary>
    public StraightPipeSpec? Find(string pipeKind, double diameterMm) => Entries.FirstOrDefault(x =>
        string.Equals(x.PipeKind, pipeKind, StringComparison.OrdinalIgnoreCase)
        && Math.Abs(x.DiameterMm - diameterMm) <= Epsilon);

    /// <summary>
    /// 같은 관종 안에서 DN만으로 외경 DE를 찾는다. 핸드북상 DE는 <b>같은 관종 안에서</b> 등급과 무관한 DN 단일값이라,
    /// 규격표에 (등급, DN) 행이 없어도 같은 관종·DN의 다른 등급 행에서 그대로 쓸 수 있다.
    /// 관종을 넘어서는 빌리지 않는다 — 강관 DN300은 318.5, 주철관은 326으로 다르다(docs/211 § B-3).
    /// 표에 먼저 나오는 등급 행을 쓴다.
    /// </summary>
    /// <returns>(외경, 값을 가져온 등급). 그 관종·DN이 표에 아예 없으면 null.</returns>
    public (double OuterDiameterMm, string PipeKind)? FindOuterDiameterByDiameter(double diameterMm, PipeMaterial material)
        => Entries.FirstOrDefault(x => Math.Abs(x.DiameterMm - diameterMm) <= Epsilon && MaterialOf(x) == material) is { } entry
            ? (entry.OuterDiameterMm, entry.PipeKind)
            : null;

    /// <summary>같은 관종·DN 행의 OD 불일치를 반환한다. 관종이 다르면 외경이 달라도 정상이다.</summary>
    public IReadOnlyList<(PipeMaterial Material, double DiameterMm)> FindOuterDiameterConflicts() => Entries
        .Where((entry, index) => Entries.Skip(index + 1).Any(other =>
            Math.Abs(entry.DiameterMm - other.DiameterMm) <= Epsilon
            && MaterialOf(entry) == MaterialOf(other)
            && Math.Abs(entry.OuterDiameterMm - other.OuterDiameterMm) > Epsilon))
        .Select(x => (Material: MaterialOf(x), x.DiameterMm))
        .Distinct()
        .OrderBy(x => x.Material).ThenBy(x => x.DiameterMm)
        .ToList();

    /// <summary>알 수 없는 등급은 주철관으로 본다(관종 축 도입 전과 같은 동작).</summary>
    private static PipeMaterial MaterialOf(StraightPipeSpec spec) => PipeKindCatalog.MaterialOf(spec.PipeKind) ?? PipeMaterial.DuctileIron;

    // ── 주철관 핸드북(2020년판) 출처: docs/주철관핸드북(2020년판)_DN.pdf ──────────────
    // 직관부의 실바깥지름(DE)·관두께(e)·M무게(kg/m)·소켓 무게(KP/타이튼)를 옮겼다(무게는 docs/178 § A-2에서 추가).
    // 라이닝 무게·4M/5M/6M 총무게 열은 옮기지 않았다 — 총무게는 M무게×길이+소켓 무게로 같은 값이 나온다.

    /// <summary>DN → DE. 7개 관종 전부 동일한 실바깥지름을 쓴다.</summary>
    private static readonly (double Dn, double De)[] Diameters =
    {
        (80, 98), (100, 118), (125, 144), (150, 170), (200, 222), (250, 274),
        (300, 326), (350, 378), (400, 429), (450, 480), (500, 532), (600, 635),
        (700, 738), (800, 842), (900, 945), (1000, 1048), (1100, 1144), (1200, 1255)
    };

    // ── 강관 핸드북 출처: KS D 3565 부표 1(docs/강관핸드북_직관두께.pdf, 원본 docs/강관핸드북-1.pdf p.149) ──────
    // docs/211_data_강관직관_KSD3565.csv를 그대로 옮겼다(스크립트 생성). 무게 kg/m는 W = 0.02466·t·(D−t)로 전 행 검산했다.
    // 강관은 소켓이 없어 소켓 무게 열이 없다 → DH_무게 = kg/m × 연장. 정적 초기화 순서 때문에 이 배열들은
    // SteelNominalDiameters 앞에 있어야 한다.

    /// <summary>강관 DN → 바깥지름(KS D 3565 부표 1). 등급(STWW)과 무관한 DN 단일값이다.</summary>
    private static readonly (double Dn, double De)[] SteelDiameters =
    {
        (80, 89.1), (100, 114.3), (125, 139.8), (150, 165.2), (200, 216.3), (250, 267.4),
        (300, 318.5), (350, 355.6), (400, 406.4), (450, 457.2), (500, 508), (600, 609.6),
        (700, 711.2), (800, 812.8), (900, 914.4), (1000, 1016), (1100, 1117.6), (1200, 1219.2),
        (1350, 1371.6), (1500, 1524), (1600, 1625.6), (1650, 1676.4), (1800, 1828.8), (1900, 1930.4),
        (2000, 2032), (2100, 2133.6), (2200, 2235.2), (2300, 2336.8), (2400, 2438.4), (2500, 2540),
        (2600, 2641.6), (2700, 2743.2), (2800, 2844.8), (2900, 2946.4), (3000, 3048)
    };

    /// <summary>STWW 290 — DN80~300. (DN, 두께 mm, kg/m).</summary>
    private static readonly (double Dn, double ThicknessMm, double UnitWeightKgPerM)[] Steel290Specs =
    {
        (80, 4.2, 8.79), (100, 4.5, 12.2), (125, 4.5, 15), (150, 5, 19.8), (200, 5.8, 30.1),
        (250, 6.6, 42.4), (300, 6.9, 53)
    };

    /// <summary>STWW 370 — DN80~300. (DN, 두께 mm, kg/m).</summary>
    private static readonly (double Dn, double ThicknessMm, double UnitWeightKgPerM)[] Steel370Specs =
    {
        (80, 4.5, 9.39), (100, 4.9, 13.2), (125, 5.1, 16.9), (150, 5.5, 21.7), (200, 6.4, 33.1),
        (250, 6.4, 41.2), (300, 6.4, 49.3)
    };

    /// <summary>STWW 400 호칭두께 A — DN350~3000. (DN, 두께 mm, kg/m).</summary>
    private static readonly (double Dn, double ThicknessMm, double UnitWeightKgPerM)[] Steel400ASpecs =
    {
        (350, 6, 51.7), (400, 6, 59.2), (450, 6, 66.8), (500, 6, 74.3), (600, 6, 89.3),
        (700, 7, 122), (800, 8, 159), (900, 8, 179), (1000, 9, 223), (1100, 10, 273),
        (1200, 11, 328), (1350, 12, 402), (1500, 14, 521), (1600, 15, 596), (1650, 15, 615),
        (1800, 16, 715), (1900, 17, 802), (2000, 18, 894), (2100, 19, 991), (2200, 20, 1093),
        (2300, 21, 1199), (2400, 22, 1311), (2500, 23, 1428), (2600, 24, 1549), (2700, 25, 1676),
        (2800, 26, 1807), (2900, 27, 1944), (3000, 29, 2159)
    };

    /// <summary>STWW 400 호칭두께 B — DN700~3000 (DN350~600은 A만 수록). (DN, 두께 mm, kg/m).</summary>
    private static readonly (double Dn, double ThicknessMm, double UnitWeightKgPerM)[] Steel400BSpecs =
    {
        (700, 6, 104), (800, 7, 139), (900, 7, 157), (1000, 8, 199), (1100, 8, 219),
        (1200, 9, 269), (1350, 10, 336), (1500, 11, 410), (1600, 12, 477), (1650, 12, 493),
        (1800, 13, 582), (1900, 14, 662), (2000, 15, 746), (2100, 16, 836), (2200, 16, 876),
        (2300, 17, 973), (2400, 18, 1074), (2500, 18, 1119), (2600, 19, 1229), (2700, 20, 1343),
        (2800, 21, 1462), (2900, 21, 1515), (3000, 22, 1642)
    };

    /// <summary>주철관 핸드북에 수록된 호칭지름 목록(DN80~1200, 18개). 허용굴곡표·주철관 곡관 규격표가 같은 DN 축을 쓴다.</summary>
    public static IReadOnlyList<double> DuctileIronNominalDiameters { get; } = Diameters.Select(x => x.Dn).ToList();

    /// <summary>강관 핸드북(KS D 3565)에 수록된 호칭지름 목록(DN80~3000, 35개).</summary>
    public static IReadOnlyList<double> SteelNominalDiameters { get; } = SteelDiameters.Select(x => x.Dn).ToList();

    /// <summary>모든 관종의 호칭지름 합집합(오름차순). 직경 콤보·규격 매핑처럼 관종을 아직 모르는 자리가 쓴다(docs/211 § B-2).</summary>
    public static IReadOnlyList<double> NominalDiameters { get; } =
        DuctileIronNominalDiameters.Concat(SteelNominalDiameters).Distinct().OrderBy(x => x).ToList();

    /// <summary><see cref="Diameters"/>의 DN600 위치. 상수 4종관은 DN600부터 수록된다.</summary>
    private const int Dn600Index = 11;

    // 핸드북상 상수 2종관과 하수 1종관, 상수 3종관과 하수 2종관은 제원이 완전히 동일하다.
    private static readonly double[] WaterClass1 =
    {
        7.4, 7.5, 7.6, 7.7, 7.8, 8.3, 8.8, 9.4, 9.9,
        10.5, 11.0, 12.1, 13.2, 14.3, 15.4, 16.5, 17.6, 18.7
    };

    private static readonly double[] WaterClass2 =
    {
        6.7, 6.8, 6.9, 7.0, 7.1, 7.5, 8.0, 8.5, 9.0,
        9.5, 10.0, 11.0, 12.0, 13.0, 14.0, 15.0, 16.0, 17.0
    };

    private static readonly double[] WaterClass3 =
    {
        6.0, 6.1, 6.2, 6.3, 6.4, 6.8, 7.2, 7.7, 8.1,
        8.6, 9.0, 9.9, 10.8, 11.7, 12.6, 13.5, 14.4, 15.3
    };

    /// <summary>DN600~1200만 존재한다. 소구경 상수 4종관은 핸드북에 없다.</summary>
    private static readonly double[] WaterClass4 = { 8.8, 9.6, 10.4, 11.2, 12.0, 12.8, 13.6 };

    private static readonly double[] SewerClass3 =
    {
        4.7, 4.8, 4.8, 4.9, 5.0, 5.3, 5.6, 6.0, 6.3,
        6.7, 7.0, 7.7, 8.4, 9.1, 9.8, 10.5,
        // 핸드북 원문 그대로. DN1100·1200은 앞 구간 증가폭에서 벗어나 상수 4종관과 같은 값이다.
        12.8, 13.6
        // 참고: DN100과 DN125가 둘 다 4.8로 같은 것도 원문 그대로다.
    };

    // ── 직관부 M무게(kg/m). 핸드북 "M무게(약)" 열 그대로(docs/178 § A-2). 두께와 같은 DN 축이다. ──
    private static readonly double[] WaterClass1WeightKgPerM =
    {
        14.8, 18.4, 23.0, 27.7, 36.9, 48.8, 61.8, 76.7, 91.9,
        108.8, 126.9, 166.9, 211.9, 262.1, 317.1, 377.0, 438.9, 511.8
    };

    private static readonly double[] WaterClass2WeightKgPerM =
    {
        13.5, 16.4, 21.0, 25.3, 33.8, 44.3, 56.3, 69.6, 83.7,
        98.5, 115.6, 152.0, 193.0, 238.7, 288.7, 343.2, 399.5, 465.9
    };

    private static readonly double[] WaterClass3WeightKgPerM =
    {
        12.2, 15.1, 18.9, 22.8, 30.6, 40.2, 50.8, 63.2, 75.5,
        89.8, 104.3, 137.1, 173.9, 215.2, 260.2, 309.3, 360.1, 420.1
    };

    /// <summary>DN600~1200. 두께 배열과 같은 구간이다.</summary>
    private static readonly double[] WaterClass4WeightKgPerM = { 122.0, 154.9, 191.6, 231.6, 275.3, 320.7, 373.9 };

    private static readonly double[] SewerClass3WeightKgPerM =
    {
        9.7, 11.9, 14.9, 17.9, 23.9, 31.2, 39.7, 49.0, 59.0,
        69.7, 81.4, 106.9, 135.7, 167.8, 202.9, 241.2,
        // 두께와 마찬가지로 DN1100·1200은 상수 4종관과 같은 값이다(원문 그대로).
        320.5, 373.7
    };

    // ── 소켓 무게(kg). 관종(등급)과 무관한 DN 단일값이라 표마다 같은 열이 반복된다. ──
    private static readonly double[] SocketWeightKpKg =
    {
        4.0, 4.8, 5.9, 7.4, 9.9, 13.4, 16.7, 21.6, 26.2,
        31.8, 38.4, 54.5, 73.5, 85.4, 101.5, 122.6, 139.4, 168.4
    };

    private static readonly double[] SocketWeightTytonKg =
    {
        5.2, 6.7, 8.6, 10.7, 14.9, 21.0, 26.5, 32.6, 37.2,
        // DN700의 타이튼 소켓(70.0)이 DN600(71.2)보다 가벼운 것은 원문 그대로다.
        49.3, 55.1, 71.2, 70.0, 85.7, 107.6, 137.8, 163.2, 197.3
    };

    private static IEnumerable<StraightPipeSpec> Build(string pipeKind, IReadOnlyList<double> thicknessMm, IReadOnlyList<double> unitWeightKgPerM, int startIndex = 0) =>
        thicknessMm.Select((thickness, offset) =>
        {
            var index = startIndex + offset;
            var (dn, de) = Diameters[index];
            return new StraightPipeSpec(pipeKind, dn, de, thickness, unitWeightKgPerM[offset], SocketWeightKpKg[index], SocketWeightTytonKg[index]);
        });

    /// <summary>강관 등급 한 벌. 소켓 무게는 0 — 강관은 용접·플랜지라 소켓이 없다.</summary>
    private static IEnumerable<StraightPipeSpec> BuildSteel(string pipeKind, IReadOnlyList<(double Dn, double ThicknessMm, double UnitWeightKgPerM)> rows) =>
        rows.Select(row => new StraightPipeSpec(pipeKind, row.Dn, SteelDiameters.First(x => Math.Abs(x.Dn - row.Dn) <= Epsilon).De,
            row.ThicknessMm, row.UnitWeightKgPerM));

    /// <summary>강관 기본 행(65개). 기존 프로젝트 저장본에 강관 행이 없을 때 병합용으로도 쓴다(<see cref="BendSettings.WithSteelDefaults"/>).</summary>
    public static IReadOnlyList<StraightPipeSpec> SteelDefaultEntries { get; } = new[]
        {
            BuildSteel(PipeKindCatalog.Steel290, Steel290Specs),
            BuildSteel(PipeKindCatalog.Steel370, Steel370Specs),
            BuildSteel(PipeKindCatalog.Steel400A, Steel400ASpecs),
            BuildSteel(PipeKindCatalog.Steel400B, Steel400BSpecs)
        }
        .SelectMany(x => x)
        .ToList();

    public static StraightPipeSpecTable Default { get; } = new(new[]
        {
            Build(PipeKindCatalog.Water1, WaterClass1, WaterClass1WeightKgPerM),
            Build(PipeKindCatalog.Water2, WaterClass2, WaterClass2WeightKgPerM),
            Build(PipeKindCatalog.Water3, WaterClass3, WaterClass3WeightKgPerM),
            Build(PipeKindCatalog.Water4, WaterClass4, WaterClass4WeightKgPerM, Dn600Index),
            // 핸드북상 상수 2종관=하수 1종관, 상수 3종관=하수 2종관은 두께·무게가 완전히 같다.
            Build(PipeKindCatalog.Sewer1, WaterClass2, WaterClass2WeightKgPerM),
            Build(PipeKindCatalog.Sewer2, WaterClass3, WaterClass3WeightKgPerM),
            Build(PipeKindCatalog.Sewer3, SewerClass3, SewerClass3WeightKgPerM),
            // 강관 4등급(docs/211 § B-1).
            SteelDefaultEntries
        }
        .SelectMany(x => x)
        .ToList());

    /// <summary>강관 행이 하나도 없으면 기본 강관 행을 뒤에 붙인 사본을, 있으면 자신을 돌려준다(docs/211 § E-1).</summary>
    public StraightPipeSpecTable WithSteelDefaults()
        => Entries.Any(x => MaterialOf(x) == PipeMaterial.Steel) ? this : new StraightPipeSpecTable(Entries.Concat(SteelDefaultEntries).ToList());
}
