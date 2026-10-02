using DHBIMWATER.Core.Parameters;

namespace DHBIMWATER.Core.Gis;

/// <summary>연동 애드인 배치 태그 계약. 이름·타입은 연동 애드인의 <c>PlacementSourceTagger</c>와 같이 바꾼다.
/// <para>2026-10-02 계약 개정(docs/264): 측점은 m 숫자(Number)이고 이름에 단위를 붙였다(`StaStart(m)`·`StaEnd(m)`, 새 GUID).
/// 프로젝트 길이 단위(보통 mm)로 보이면 측점을 읽기 어려워서다. Z 오프셋은 길이 형식이며 옛 모델의 Number도 쓴다.</para></summary>
public static class PlacementStationParameters
{
    public const string SourceFile = "SourceFile";
    public const string Alignment = "Alignment";
    public const string Profile = "Profile";
    public const string StaStart = "StaStart(m)";
    public const string StaEnd = "StaEnd(m)";
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
        // 이름을 바꿔 새 GUID를 받았다(docs/264). 옛 `StaStart` cb041b23-… · `StaEnd` 39479abc-…
        [StaStart] = new("26a4c229-638c-4e3e-89de-21fb23211344"),
        [StaEnd] = new("02dc4315-38ef-4197-9ed5-ff61feebefef"),
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
        Def(ZOffset, ParameterSpecType.Length),
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
