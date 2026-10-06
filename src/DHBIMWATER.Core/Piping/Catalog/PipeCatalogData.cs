namespace DHBIMWATER.Core.Piping;

public sealed record PipeCatalogData
{
    public required StraightPipeSpecTable StraightPipes { get; init; }
    public required BendFittingCatalog Fittings { get; init; }
    public required JointDeflectionTable JointDeflections { get; init; }
    public required IReadOnlyList<StraightPipeSpec> SteelStraightDefaults { get; init; }
    public required IReadOnlyList<BendFittingEntry> SteelBendDefaults { get; init; }
    public required IReadOnlyList<SteelTeeSpec> SteelTees { get; init; }
    public required IReadOnlyList<SteelReducerSpec> SteelReducers { get; init; }
    public required IReadOnlyList<DuctileSocketTeeSpec> DuctileSocketTees { get; init; }
    public required IReadOnlyList<DuctileFlangedTeeSpec> DuctileFlangedTees { get; init; }
    public required IReadOnlyList<DuctileSocketTaperSpec> DuctileSocketTapers { get; init; }
    public required IReadOnlyList<DuctileFlangedTaperSpec> DuctileFlangedTapers { get; init; }
    public required IReadOnlyList<DuctileWyeSpec> DuctileWyes { get; init; }
    public required IReadOnlyList<DuctileValveTeeSpec> DuctileValveTees { get; init; }
    public required IReadOnlyList<DuctileValveBendSpec> DuctileValveBends { get; init; }
    public required IReadOnlyList<SteelValveTeeSpec> SteelValveTees { get; init; }
    public required IReadOnlyList<SteelValveBendSpec> SteelValveBends { get; init; }
    public required IReadOnlyList<string> DuctileIronGrades { get; init; }
    public required IReadOnlyList<string> SteelGrades { get; init; }
    public required IReadOnlyList<string> AllGrades { get; init; }
    public required IReadOnlyList<double> DuctileIronNominalDiameters { get; init; }
    public required IReadOnlyList<double> SteelNominalDiameters { get; init; }
    public required IReadOnlyList<double> NominalDiameters { get; init; }
    public required IReadOnlyList<string> JointTypes { get; init; }
    public required IReadOnlyDictionary<(PipeMaterial Material, BendConnection Connection), IReadOnlyList<double>> StandardAngles { get; init; }
    public required IReadOnlyList<BendFittingEntry> BendHandbookEntries { get; init; }
    public required IReadOnlyList<SocketBendHandbookRow> SocketBendHandbook { get; init; }
    public required IReadOnlyList<FlangedBendHandbookRow> FlangedBendHandbook { get; init; }
}

public sealed record SocketBendHandbookRow(double DiameterMm, double AngleDeg, BendForm Form,
    double KpWeightKg, double TytonWeightKg);

public sealed record FlangedBendHandbookRow(double DiameterMm, double AngleDeg, BendForm Form, double WeightKg);

public static class PipeCatalog
{
    private static PipeCatalogData? _current;

    public static PipeCatalogData Current => _current ?? throw new InvalidOperationException("규격표가 로드되지 않았습니다.");
    public static bool IsInstalled => _current is not null;

    public static IReadOnlyList<string> Install(PipeCatalogData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _current = data;
        var warnings = new List<string>();
        string[] gradeNames =
        [
            PipeKindCatalog.Water1, PipeKindCatalog.Water2, PipeKindCatalog.Water3,
            PipeKindCatalog.Water4, PipeKindCatalog.Sewer1, PipeKindCatalog.Sewer2,
            PipeKindCatalog.Sewer3, PipeKindCatalog.Steel290, PipeKindCatalog.Steel370,
            PipeKindCatalog.Steel400A, PipeKindCatalog.Steel400B
        ];
        foreach (var grade in gradeNames)
            if (!data.AllGrades.Contains(grade, StringComparer.Ordinal)) warnings.Add($"규격표 등급 누락: {grade}");
        string[] jointNames = [JointTypeCatalog.KpMechanical, JointTypeCatalog.Tyton];
        foreach (var joint in jointNames)
            if (!data.JointTypes.Contains(joint, StringComparer.Ordinal)) warnings.Add($"규격표 조인트 누락: {joint}");
        return warnings;
    }
}
