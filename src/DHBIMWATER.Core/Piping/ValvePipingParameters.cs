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
    /// <summary>격점 식별자. `V-3`·`3-1` 같은 형태가 오므로 Text다.</summary>
    public const string JunctionId = "DH_격점번호";
    /// <summary>격점 안에서 `명칭 + 규격` 조합별 일련번호. 일람표 정렬이 1·2·10 순이어야 하므로 Integer다.</summary>
    public const string ItemNumber = "DH_부재번호";
    public const string ItemName = "DH_부재명칭";
    public const string ItemSpec = "DH_부재규격";
    /// <summary>직관·단관은 관 재료 코드를 자동으로 채우고, 나머지 부재는 비워 둔다(docs/141 § B).
    /// 이미 값이 있으면 덮어쓰지 않는다 — 사용자가 손으로 적은 문구가 우선이다.</summary>
    public const string Remark = "DH_비고";
    /// <summary>관종 코드(`DCIP`·`SP` 등 — <see cref="Gis.PipeAlignmentParameters.Code"/>). 수량산출이 이 어휘로 필터한다.
    /// GUID는 <see cref="Gis.PipeAlignmentParameters.Material"/>과 같다(docs/139 § B-2).</summary>
    public const string Material = "DH_관종";
    /// <summary>야간 작업 여부. 체크가 야간, 해제가 주간이다(docs/141 § C).
    /// GUID는 <see cref="Gis.PipeAlignmentParameters.NightWork"/>와 같다.</summary>
    public const string NightWork = "DH_야간";
    /// <summary>밸브실 유형. 밸브실 빌더가 구조부재에 찍는 것과 <b>같은 매개변수</b>다(docs/161).
    /// 그래서 GUID도 <c>RevitSharedParameterRepository</c>의 하드코딩 표에 있는 값을 그대로 쓴다 —
    /// 새 GUID를 만들면 이름이 같은 공유 매개변수가 둘 생겨 docs/155(중복 정리)를 되돌린다.</summary>
    public const string Zone = "DH_Zone";
    public const string JointTotalCount = Gis.PipeAlignmentParameters.JointTotalCount;
    public const string LayingCount = Gis.PipeAlignmentParameters.LayingCount;
    public const string ValveJointLayingCount = "DH_접합및부설수";
    public const string Weight = Gis.PipeAlignmentParameters.Weight;
    public const string UnitWeight = Gis.PipeAlignmentParameters.UnitWeight;
    /// <summary>관 등급(`상수 1종관`·`STWW 400 A`). 계획관로와 같은 이름·GUID다(docs/260 § C-5). 밸브류에는 적지 않는다.</summary>
    public const string Grade = Gis.PipeAlignmentParameters.Grade;

    public static string JointSlotKind(int slot) => Gis.PipeAlignmentParameters.JointSlotKind(slot);
    public static string JointSlotSpec(int slot) => Gis.PipeAlignmentParameters.JointSlotSpec(slot);
    public static string JointSlotCount(int slot) => Gis.PipeAlignmentParameters.JointSlotCount(slot);

    /// <summary>접합 구성 문자열(<see cref="PipeJointComposition"/>). 이름·GUID 모두 계획관로와 같다 —
    /// 수량산출이 두 배치 경로를 한 어휘로 읽어야 한다.</summary>
    public const string JointComposition = Gis.PipeAlignmentParameters.JointComposition;

    // DH_곡관형식은 docs/214에서 폐지했다(계획관로·밸브실 양쪽, 읽는 곳이 없었다).

    /// <summary>부재 종류 — 접합 접두를 뗀 순수 값(`직관`·`단관`·`곡관`·`T형`·`십자`, 밸브·이음관은 패밀리명)(docs/178 § C).
    /// <para><see cref="ItemName"/>(`2S1F 단관`)은 재료표 표기라 접두가 붙는다. 수량산출은 부재종류를 `DH_Part` → `DH_부재명칭`
    /// 순서로 읽으므로, 계획관로의 `DH_Part`(`직관`·`곡관`)와 같은 형태의 값을 여기 따로 둔다.
    /// 이름·GUID는 계획관로·구조부재의 `DH_Part`와 같다(RevitSharedParameterRepository 표 47c74ae0-…).</para></summary>
    public const string Part = Gis.PipeAlignmentParameters.Part;
    /// <summary>호칭지름 텍스트 — `DN300`, T형·십자는 `DN300×300`(주관×분기). 계획관로와 같은 표기다(docs/178 § C).
    /// <see cref="ItemSpec"/>(`D300x6.00m`)은 재료표 규격이라 그대로 둔다.</summary>
    public const string NominalDiameter = Gis.PipeAlignmentParameters.NominalDiameter;
    /// <summary>직관·단관의 실제 연장(Length). 패밀리 길이 파라미터(`직관_길이` 등, 이름이 패밀리마다 다름)와 별개로
    /// 공유 매개변수에 한 번 더 적어, 수량산출이 패밀리 이름과 무관하게 읽게 한다(docs/178 § C).</summary>
    public const string Length = Gis.PipeAlignmentParameters.Length;
    /// <summary>곡관 편각(Angle). <see cref="ItemSpec"/>의 `D300x45°`와 같은 값을 숫자로 둔다 — 계획관로 곡관의 `DH_곡관규격각`과 같은 칸이다.</summary>
    public const string BendAngle = Gis.PipeAlignmentParameters.StandardAngle;
    /// <inheritdoc cref="Gis.PipeAlignmentParameters.BranchSpec"/>
    public const string BranchSpec = Gis.PipeAlignmentParameters.BranchSpec;

    /// <inheritdoc cref="Gis.PipeAlignmentParameters.CutCount"/>
    public const string CutCount = Gis.PipeAlignmentParameters.CutCount;

    public const string AddinValue = "DHBIMWATER";

    /// <summary>이름 → GUID. 하드코딩이며 변경 금지 (docs/131 § A-2).
    /// <see cref="Addin"/>·<see cref="Material"/>·<see cref="NightWork"/>는
    /// <see cref="Gis.PipeAlignmentParameters"/>와 같은 GUID를 재사용한다.</summary>
    public static IReadOnlyDictionary<string, Guid> Guids { get; } = new Dictionary<string, Guid>(StringComparer.Ordinal)
    {
        [Addin] = new("f0ff9795-a26f-4a2f-869c-532d2c418fac"),
        [JunctionId] = new("93fb35b4-9d6c-4412-8540-1d7826ade173"),
        [ItemNumber] = new("f2409d67-9ccb-4c54-9682-eb45242e5db1"),
        [ItemName] = new("fbc1b727-e0cf-4e03-a42e-30955054bffb"),
        [ItemSpec] = new("e3ee74a7-14c5-41cc-adbb-54001564ec55"),
        [Remark] = new("b1b2cb4c-93bd-4001-89ef-ff1f86c88cdb"),
        [Material] = new("7c34e9d9-bace-40bb-9924-3660e8db398f"),
        [NightWork] = new("f4159d85-ccdf-424a-9ff7-feac5eb91294"),
        // RevitSharedParameterRepository의 GUID 표(변경 금지)에서 가져온 값이다. 새로 만들지 말 것(docs/161 § C).
        [Zone] = new("b60a47f3-3ede-45db-8491-b8f70de329a4"),
        [JointTotalCount] = Gis.PipeAlignmentParameters.Guids[JointTotalCount],
        [LayingCount] = Gis.PipeAlignmentParameters.Guids[LayingCount],
        [ValveJointLayingCount] = new("f23792f1-948b-4a3a-8f29-7b4370b8e2da"),
        [Weight] = Gis.PipeAlignmentParameters.Guids[Weight],
        [UnitWeight] = Gis.PipeAlignmentParameters.Guids[UnitWeight],
        [Grade] = Gis.PipeAlignmentParameters.Guids[Grade],
        [JointComposition] = Gis.PipeAlignmentParameters.Guids[JointComposition],
        [Part] = Gis.PipeAlignmentParameters.Guids[Part],
        [NominalDiameter] = Gis.PipeAlignmentParameters.Guids[NominalDiameter],
        [Length] = Gis.PipeAlignmentParameters.Guids[Length],
        [BendAngle] = Gis.PipeAlignmentParameters.Guids[BendAngle],
        [BranchSpec] = Gis.PipeAlignmentParameters.Guids[BranchSpec],
        [CutCount] = Gis.PipeAlignmentParameters.Guids[CutCount],
        [JointSlotKind(1)] = Gis.PipeAlignmentParameters.Guids[JointSlotKind(1)],
        [JointSlotSpec(1)] = Gis.PipeAlignmentParameters.Guids[JointSlotSpec(1)],
        [JointSlotCount(1)] = Gis.PipeAlignmentParameters.Guids[JointSlotCount(1)],
        [JointSlotKind(2)] = Gis.PipeAlignmentParameters.Guids[JointSlotKind(2)],
        [JointSlotSpec(2)] = Gis.PipeAlignmentParameters.Guids[JointSlotSpec(2)],
        [JointSlotCount(2)] = Gis.PipeAlignmentParameters.Guids[JointSlotCount(2)],
        [JointSlotKind(3)] = Gis.PipeAlignmentParameters.Guids[JointSlotKind(3)],
        [JointSlotSpec(3)] = Gis.PipeAlignmentParameters.Guids[JointSlotSpec(3)],
        [JointSlotCount(3)] = Gis.PipeAlignmentParameters.Guids[JointSlotCount(3)],
    };

    public static IReadOnlyList<SharedParameterDefinition> Definitions { get; } = new[]
    {
        Text(Addin, ParameterGroupType.IdentityData, userModifiable: false),
        Text(JunctionId, ParameterGroupType.IdentityData),
        Def(ItemNumber, ParameterSpecType.Integer, ParameterGroupType.IdentityData),
        Text(ItemName),
        Text(ItemSpec),
        Text(Remark),
        Text(Material),
        Def(NightWork, ParameterSpecType.YesNo),
        // 구조부재에 이미 묶여 있는 DH_Zone에 배관 두 카테고리를 더한다. EnsureParameters가 기존 바인딩에
        // 카테고리를 병합(ReInsert)하므로 새 매개변수는 생기지 않는다(docs/141, docs/161 § C).
        Text(Zone),
        Def(JointSlotKind(1), ParameterSpecType.Text, categories: [ParameterCategory.PipeFitting]),
        Def(JointSlotSpec(1), ParameterSpecType.Text, categories: [ParameterCategory.PipeFitting]),
        Def(JointSlotCount(1), ParameterSpecType.Integer, categories: [ParameterCategory.PipeFitting]),
        Def(JointSlotKind(2), ParameterSpecType.Text, categories: [ParameterCategory.PipeFitting]),
        Def(JointSlotSpec(2), ParameterSpecType.Text, categories: [ParameterCategory.PipeFitting]),
        Def(JointSlotCount(2), ParameterSpecType.Integer, categories: [ParameterCategory.PipeFitting]),
        Def(JointSlotKind(3), ParameterSpecType.Text, categories: [ParameterCategory.PipeFitting]),
        Def(JointSlotSpec(3), ParameterSpecType.Text, categories: [ParameterCategory.PipeFitting]),
        Def(JointSlotCount(3), ParameterSpecType.Integer, categories: [ParameterCategory.PipeFitting]),
        Def(JointTotalCount, ParameterSpecType.Integer, categories: [ParameterCategory.PipeFitting]),
        Def(LayingCount, ParameterSpecType.Integer, categories: [ParameterCategory.PipeFitting]),
        Def(ValveJointLayingCount, ParameterSpecType.Integer, categories: [ParameterCategory.PipeAccessory]),
        Def(Weight, ParameterSpecType.Mass),                  // docs/264
        Def(UnitWeight, ParameterSpecType.MassPerUnitLength),
        Text(Grade),
        Text(JointComposition),
        // docs/178 § C — 계획관로와 값 형태를 맞추는 네 칸. 이름·GUID가 같으므로 일반 모델 바인딩에 두 카테고리가 병합된다.
        Text(Part, ParameterGroupType.IdentityData),
        Text(NominalDiameter),
        Def(Length, ParameterSpecType.Length),
        Def(BendAngle, ParameterSpecType.Angle),
        // 수량산출 규격2. T형·십자는 분기경이 여기 들어간다 — DH_호칭지름은 주관 한 구경뿐이다.
        Text(BranchSpec),
        Def(CutCount, ParameterSpecType.Integer),
    };

    /// <summary>규격 문자열의 직경 표기(docs/131 § B-3). mm이며 소수는 두 자리까지만 남긴다.</summary>
    public static string DiameterText(double diameterMm) => $"D{diameterMm:0.##}";

    private static SharedParameterDefinition Text(string name, ParameterGroupType group = ParameterGroupType.Data, bool userModifiable = true)
        => Def(name, ParameterSpecType.Text, group, userModifiable);

    private static SharedParameterDefinition Def(string name, ParameterSpecType spec,
        ParameterGroupType group = ParameterGroupType.Data, bool userModifiable = true,
        IReadOnlyList<ParameterCategory>? categories = null)
        => new()
        {
            Name = name,
            Guid = Guids[name],
            SpecType = spec,
            GroupType = group,
            BindingType = ParameterBindingType.Instance,
            Categories = categories ?? new[] { ParameterCategory.PipeAccessory, ParameterCategory.PipeFitting },
            UserModifiable = userModifiable
        };
}
