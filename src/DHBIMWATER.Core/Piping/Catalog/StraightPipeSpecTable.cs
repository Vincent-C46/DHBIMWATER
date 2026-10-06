namespace DHBIMWATER.Core.Piping;

/// <summary>직관 전용 규격. 곡관 벽두께와 허용굴곡에는 관여하지 않는다.</summary>
/// <param name="DiameterMm">호칭지름 DN. 조회는 Exact Match다.</param>
/// <param name="UnitWeightKgPerM">직관부 M무게(kg/m, 핸드북 "M무게(약)"). 관종(등급)마다 다르다. 0이면 미기재(docs/178 § A-2).</param>
/// <param name="SocketWeightKpKg">KP 메커니컬 조인트 소켓 무게(kg). 관종과 무관한 DN 단일값이다.</param>
/// <param name="SocketWeightTytonKg">타이튼 조인트 소켓 무게(kg). 관종과 무관한 DN 단일값이다.</param>
public sealed record StraightPipeSpec(string PipeKind, double DiameterMm, double OuterDiameterMm, double ThicknessMm,
    double UnitWeightKgPerM = 0d, double SocketWeightKpKg = 0d, double SocketWeightTytonKg = 0d);

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

    public static IReadOnlyList<double> DuctileIronNominalDiameters => PipeCatalog.Current.DuctileIronNominalDiameters;
    public static IReadOnlyList<double> SteelNominalDiameters => PipeCatalog.Current.SteelNominalDiameters;
    public static IReadOnlyList<double> NominalDiameters => PipeCatalog.Current.NominalDiameters;
    public static IReadOnlyList<StraightPipeSpec> SteelDefaultEntries => PipeCatalog.Current.SteelStraightDefaults;
    public static StraightPipeSpecTable Default => PipeCatalog.Current.StraightPipes;

    /// <summary>강관 행이 하나도 없으면 기본 강관 행을 뒤에 붙인 사본을, 있으면 자신을 돌려준다(docs/211 § E-1).</summary>
    public StraightPipeSpecTable WithSteelDefaults()
        => Entries.Any(x => MaterialOf(x) == PipeMaterial.Steel) ? this : new StraightPipeSpecTable(Entries.Concat(SteelDefaultEntries).ToList());
}
