using DHBIMWATER.Core.Parameters;

namespace DHBIMWATER.Core.Gis;

/// <summary>DHBoost 배치 태그 계약. 이름·타입 변경 금지.</summary>
public static class PlacementStationParameters
{
    public const string SourceFile = "SourceFile";
    public const string Alignment = "Alignment";
    public const string Profile = "Profile";
    public const string StaStart = "StaStart";
    public const string StaEnd = "StaEnd";
    public const string ZOffset = "ZOffset";
    public const string Flip = "Flip";
    public const string VerticalBasis = "VerticalBasis";
    public const string ShapeKind = "ShapeKind";
    public const string AlignmentId = "AlignmentId";
    public const string SourceKind = "SourceKind";
    public const string SourceOptions = "SourceOptions";

    public static IReadOnlyDictionary<string, Guid> Guids { get; } = new Dictionary<string, Guid>(StringComparer.Ordinal)
    {
        [SourceFile] = new("53dfa8cd-0583-4a4b-90a9-31762c06eb2c"),
        [Alignment] = new("bdd5ffa3-97ed-4617-a8fe-7bc1bbd824a2"),
        [Profile] = new("993b83c5-17a0-4de0-af07-d262d6f8ccc1"),
        [StaStart] = new("cb041b23-f945-4e3d-a3fe-0f74ff49ae94"),
        [StaEnd] = new("39479abc-5fbc-4024-8b86-3e89204f9df7"),
        [ZOffset] = new("2f1fd1d2-eb40-49c5-8b4e-bc3bb4df7554"),
        [Flip] = new("7989ea02-926a-43c0-a688-2bdf65c74c47"),
        [VerticalBasis] = new("adcc3144-7be1-4ae2-9f8b-67074c3cee22"),
        [ShapeKind] = new("e4b3d3e1-b811-4b61-bf22-7b6c92d43cb6"),
        [AlignmentId] = new("061d3c75-1c08-4d53-bf9f-506522a1d077"),
        [SourceKind] = new("dcbf73e3-67f7-4e89-8d2d-89e52b8eeef4"),
        [SourceOptions] = new("a15f698d-fa7d-477f-a67b-a89dfae7d813"),
    };

    public static IReadOnlyList<SharedParameterDefinition> Definitions { get; } =
    [
        Def(SourceFile, ParameterSpecType.Text),
        Def(Alignment, ParameterSpecType.Text),
        Def(Profile, ParameterSpecType.Text),
        Def(StaStart, ParameterSpecType.Number),
        Def(StaEnd, ParameterSpecType.Number),
        Def(ZOffset, ParameterSpecType.Number),
        Def(Flip, ParameterSpecType.YesNo),
        Def(VerticalBasis, ParameterSpecType.Text),
        Def(ShapeKind, ParameterSpecType.Text),
        Def(AlignmentId, ParameterSpecType.Text),
        Def(SourceKind, ParameterSpecType.Text),
        Def(SourceOptions, ParameterSpecType.Text),
    ];

    private static SharedParameterDefinition Def(string name, ParameterSpecType spec) => new()
    {
        Name = name,
        Guid = Guids[name],
        SpecType = spec,
        GroupType = ParameterGroupType.Data,
        BindingType = ParameterBindingType.Instance,
        Categories = [ParameterCategory.GenericModel]
    };
}
