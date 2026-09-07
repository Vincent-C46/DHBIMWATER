using DHBIMWATER.Core.Parameters;

namespace DHBIMWATER.Core.Piping;

/// <summary>
/// 밸브실 배관 배치(<see cref="PipeOutputMode.PipeAccessorySegment"/>) 결과물에 기록하는
/// 격점상세도 배관재료표용 정보 매개변수 정의(docs/131 § A).
/// <para>여기 있는 값은 전부 "정보"다. 패밀리 형상을 구동하는 치수 파라미터
/// (<see cref="PipeSegmentFamilySelection"/>의 이름들)와 절대 섞지 않는다.</para>
/// GUID는 docs/131 § A-2 표가 원본이며 변경하면 안 된다.
/// </summary>
public static class ValvePipingParameters
{
    public const string Addin = "DH_Addin";
    public const string Part = "DH_Part";
    /// <summary>격점 식별자. `V-3`·`3-1` 같은 형태가 오므로 Text다.</summary>
    public const string JunctionId = "DH_격점번호";
    /// <summary>격점 안에서 `명칭 + 규격` 조합별 일련번호. 일람표 정렬이 1·2·10 순이어야 하므로 Integer다.</summary>
    public const string ItemNumber = "DH_부재번호";
    public const string ItemName = "DH_부재명칭";
    public const string ItemSpec = "DH_부재규격";
    /// <summary>정의만 만들고 값은 쓰지 않는다. 사용자가 Revit 속성창에서 직접 채우는 열이다.</summary>
    public const string Remark = "DH_비고";

    public const string AddinValue = "DHBIMWATER";

    /// <summary>이름 → GUID. 하드코딩이며 변경 금지 (docs/131 § A-2).
    /// <see cref="Addin"/>·<see cref="Part"/>는 <see cref="Gis.PipeAlignmentParameters"/>와 같은 GUID를 재사용한다.</summary>
    public static IReadOnlyDictionary<string, Guid> Guids { get; } = new Dictionary<string, Guid>(StringComparer.Ordinal)
    {
        [Addin] = new("f0ff9795-a26f-4a2f-869c-532d2c418fac"),
        [Part] = new("47c74ae0-3fc8-4a06-9c4e-80713b564b0c"),
        [JunctionId] = new("93fb35b4-9d6c-4412-8540-1d7826ade173"),
        [ItemNumber] = new("f2409d67-9ccb-4c54-9682-eb45242e5db1"),
        [ItemName] = new("fbc1b727-e0cf-4e03-a42e-30955054bffb"),
        [ItemSpec] = new("e3ee74a7-14c5-41cc-adbb-54001564ec55"),
        [Remark] = new("b1b2cb4c-93bd-4001-89ef-ff1f86c88cdb"),
    };

    public static IReadOnlyList<SharedParameterDefinition> Definitions { get; } = new[]
    {
        Text(Addin, ParameterGroupType.IdentityData, userModifiable: false),
        Text(Part, ParameterGroupType.IdentityData),
        Text(JunctionId, ParameterGroupType.IdentityData),
        Def(ItemNumber, ParameterSpecType.Integer, ParameterGroupType.IdentityData),
        Text(ItemName),
        Text(ItemSpec),
        Text(Remark),
    };

    /// <summary>규격 문자열의 직경 표기(docs/131 § B-3). mm이며 소수는 두 자리까지만 남긴다.</summary>
    public static string DiameterText(double diameterMm) => $"D{diameterMm:0.##}";

    private static SharedParameterDefinition Text(string name, ParameterGroupType group = ParameterGroupType.Data, bool userModifiable = true)
        => Def(name, ParameterSpecType.Text, group, userModifiable);

    private static SharedParameterDefinition Def(string name, ParameterSpecType spec,
        ParameterGroupType group = ParameterGroupType.Data, bool userModifiable = true)
        => new()
        {
            Name = name,
            Guid = Guids[name],
            SpecType = spec,
            GroupType = group,
            BindingType = ParameterBindingType.Instance,
            Categories = new[] { ParameterCategory.PipeAccessory },
            UserModifiable = userModifiable
        };
}
