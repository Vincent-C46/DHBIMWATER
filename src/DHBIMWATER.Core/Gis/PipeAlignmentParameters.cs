using DHBIMWATER.Core.Parameters;

namespace DHBIMWATER.Core.Gis;

/// <summary>
/// 관로 모델링(Adaptive 직관·곡관) 결과물에 기록하는 정보용 프로젝트 매개변수 정의.
/// GUID는 docs/32_관로매개변수_지시서.md §2 표가 원본이며 변경하면 안 된다.
/// <para>docs/178에서 아무도 읽지 않던 7개(`DH_접합방법`·`DH_시점표고`·`DH_종점표고`·`DH_경사`·`DH_절점측점`·
/// `DH_접합적용`·`DH_곡률반경`)를 뺐고 <see cref="UnitWeight"/>를 더했다. 이미 바인딩된 문서에서는
/// 뺀 매개변수가 빈 칸으로 남는다 — EnsureParameters는 추가·병합만 하기 때문이다.</para>
/// <para>docs/214에서 5개(`DH_외경`·`DH_관두께`·`DH_레코드번호`·`DH_곡관형식`·`DH_부설길이`)를 더 뺐다.
/// 앞 둘은 DHBoost 측점편집의 DirectShape 원통 재생성이 읽던 값인데 DirectShape를 측점편집 대상에서
/// 제외하기로 확정(2026-09-22)해 읽는 곳이 없어졌다. `DH_레코드번호`는 `Alignment` 태그와 중복,
/// `DH_곡관형식`은 읽는 곳이 없었고, `DH_부설길이`는 곡관에서 연장이 아닌 값(절점→관끝 거리)이라 뺐다.</para>
/// </summary>
public static class PipeAlignmentParameters
{
    public const string Addin = "DH_Addin";
    /// <summary>미리보기·최종 배치가 "관로"를 쓰는 분류 매개변수(docs/209 § E). 이 클래스의 Definitions에는 없고
    /// (RevitSharedParameterRepository의 공통 표가 바인딩한다) 미리보기 삭제 필터만 GUID를 쓴다 — Guids 사전은 Definitions와 1:1이라 따로 둔다.</summary>
    public const string Category = "DH_Category";
    /// <summary>RevitSharedParameterRepository의 GUID 표(변경 금지)와 같은 값.</summary>
    public static readonly Guid CategoryGuid = new("7e539f5b-80e4-4dd9-a7e1-ac070dbcaa24");
    public const string Part = "DH_Part";
    public const string NominalDiameter = "DH_호칭지름";
    public const string Material = "DH_관종";
    public const string Grade = "DH_관종등급";
    public const string ElevationDatum = "DH_기준높이";
    public const string Length = "DH_연장";
    public const string NodeId = "DH_절점번호";
    public const string ActualDeflection = "DH_실제굴곡각";
    public const string StandardAngle = "DH_곡관규격각";
    /// <summary>주관 외 나머지 구경 표기 — T형 분기경 `D150`, 십자 `D600xD300`, 이경관 작은 쪽 `D600`.
    /// <para>수량산출(DHBoost)의 <b>규격2</b> 칸이다. <see cref="NominalDiameter"/>는 주관 한 구경(`DN300`)이라
    /// 분기 쪽을 담을 자리가 없어 따로 둔다.</para>
    /// <para><b>사용자가 고쳐 쓰는 칸</b>이다 — 십자관 세 구경이 다 다르면 배치가 적은 값 위에 손으로 덮어쓴다.
    /// 그래서 배치·재기록 경로 모두 <b>비어 있을 때만</b> 쓴다(<see cref="Remark"/>와 같은 규칙).</para></summary>
    public const string BranchSpec = "DH_분기규격";

    /// <summary>현장 절단 개소(Integer). 수량산출 「강관 절단」·「주철관 절단」 행을 만든다.
    /// <para>배치가 <b>1</b>을 적어 두고 사용자가 고친다 &mdash; 부재 하나를 한 번 자르는 것이 기본이고,
    /// 자르지 않는 부재는 0으로 지운다. 접합 슬롯 합계가 0인 것과 빈칸을 구분한다.
    /// 빈칸(미태깅)과 0(세어보니 없다)을 구분한다.</para>
    /// <para>밸브·이음관에는 적지 않는다 &mdash; 자르는 대상이 아니다(<see cref="Piping.PipeMaterialSpecText.IsCutTarget"/>).</para></summary>
    public const string CutCount = "DH_절단수";

    /// <summary><see cref="CutCount"/>의 배치 기본값.</summary>
    public const int DefaultCutCount = 1;
    public const string AllowableDeflection = "DH_허용굴곡각";
    public const string ResidualDeflection = "DH_잔여굴곡각";
    public const string IsAcceptable = "DH_허용여부";
    /// <summary>요소 한 개의 무게(kg)(docs/178 § A-2, 2026-09-16 사용자 확정).
    /// <para><b>곡관</b>: 곡관표의 한 개 무게(소켓 포함, KP/타이튼·A/B형별). <b>직관</b>: <see cref="UnitWeight"/> × <see cref="Length"/> + 소켓 무게
    /// (<see cref="StraightPipeWeight"/>). 이경관은 비워 둔다. 강관 직관은 kg/m × 연장(소켓 무게 0), 강관 곡관은 F 종류별 한 개 무게(docs/211).</para></summary>
    public const string Weight = "DH_무게";
    /// <summary>직관부 단위중량(kg/m, 핸드북 M무게). 직관·단관에만 있고 곡관은 비워 둔다 — 곡관은 한 개 무게로 센다.</summary>
    public const string UnitWeight = "DH_단위중량";
    /// <summary>접합 슬롯 수 합계. <c>DH_접합{n}_수</c>를 기록한 뒤 자동으로 쓴다.</summary>
    public const string JointTotalCount = "DH_접합수";
    /// <summary>부재 하나의 접합 구성 — `1S1F`·`2S1F`(<see cref="Piping.PipeJointComposition"/>, 소켓 수 + 플랜지 수).
    /// <para>접합 종류가 늘어도 <b>값의 어휘만</b> 늘도록 한 칸에 담는다. 종류마다 개소 매개변수를 두면
    /// 종류가 늘 때마다 공유 매개변수와 바인딩이 함께 늘고, `1S1F`처럼 한 부재에 두 종류가 섞이면
    /// 어느 칸에도 온전히 적히지 않는다.</para></summary>
    public const string JointComposition = "DH_접합구성";

    /// <summary>접합 슬롯 이름의 머리 — 한 벌은 <see cref="JointSlotKind"/>·<see cref="JointSlotSpec"/>·<see cref="JointSlotCount"/> 셋이다(docs/186 § 4-1).
    /// <para>접합 종류를 매개변수 <b>이름</b>이 아니라 <b>값</b>으로 옮기기 위한 칸이다. 이름으로 가르면 종류가 늘 때마다
    /// 공유 매개변수와 바인딩이 같이 늘고, 이경관처럼 한 부재가 <b>규격이 다른 접합 둘</b>을 가질 때 적을 자리가 없다
    /// (`D2400x1650` 하나가 2400 접합 1개소 + 1650 접합 1개소다 — 산출서 `수량산출` 90~93행).</para>
    /// <para>계획관로와 밸브실 배관 이음쇠가 같은 이름·GUID를 쓴다.</para></summary>
    public const string JointSlotPrefix = "DH_접합";
    /// <summary>슬롯 n의 `종류` 칸 — <see cref="JointKindFor"/>가 내는 문자열(`용접합`·`KP접합` …)을 담는다.
    /// 비어 있으면 그 슬롯은 <b>없는 것</b>이다(수량산출이 그렇게 읽는다).</summary>
    public static string JointSlotKind(int slot) => $"{JointSlotPrefix}{slot}_종류";
    /// <summary>슬롯 n의 `규격` 칸 — 산출서 규격 표기(`D2400`). <b>비우면 수량산출이 요소 관경(<see cref="NominalDiameter"/>)을 쓴다.</b>
    /// 직관·곡관은 요소 관경과 같으므로 비우고, 이경관만 슬롯마다 적는다(docs/186 § 4-3).</summary>
    public static string JointSlotSpec(int slot) => $"{JointSlotPrefix}{slot}_규격";
    /// <summary>슬롯 n의 `수` 칸(Integer). 0 이하면 그 슬롯은 없는 것이다.</summary>
    public static string JointSlotCount(int slot) => $"{JointSlotPrefix}{slot}_수";

    /// <summary>공유 매개변수로 <b>정의해 두는</b> 슬롯 벌 수. 스캔 상한이 아니다 —
    /// 수량산출(DHBoost)은 `DH_접합{n}_*` 이름 규칙으로 훑으므로 나중에 벌을 더 정의해도 그쪽을 다시 빌드하지 않는다.</summary>
    public const int JointSlotDefinitionCount = 3;

    /// <summary>부설 개소 수. 산출 관행상 요소당 1이며, 접합수와 무관한 별도 계수다(docs/166 § 3-3).</summary>
    public const string LayingCount = "DH_부설수";
    /// <summary>야간 작업 여부. 체크가 야간, 해제가 주간이다(docs/141 § C).
    /// 실행(파일) 단위로 기록하므로 한 선형 안에서 주/야간이 섞이면 배치 후 개별 수정해야 한다.</summary>
    public const string NightWork = "DH_야간";

    public const string AddinValue = "DHBIMWATER";
    public const string StraightPartValue = "직관";
    public const string BendPartValue = "곡관";
    public const string ReducerPartValue = "이경관";

    /// <summary>이름 → GUID. 하드코딩이며 변경 금지 (docs/32 §2).</summary>
    public static IReadOnlyDictionary<string, Guid> Guids { get; } = new Dictionary<string, Guid>(StringComparer.Ordinal)
    {
        [Addin] = new("f0ff9795-a26f-4a2f-869c-532d2c418fac"),
        [Part] = new("47c74ae0-3fc8-4a06-9c4e-80713b564b0c"),
        [NominalDiameter] = new("bdfa5b79-1ef4-416e-a7f7-ee98d93f92d4"),
        [Material] = new("7c34e9d9-bace-40bb-9924-3660e8db398f"),
        [Grade] = new("480d1033-9ed2-47c8-af8e-ab8ca7aed4f0"),
        [ElevationDatum] = new("522778f9-6c5c-45e9-8f74-4aed525205bb"),
        [Length] = new("0f8756f5-b4d2-48b6-9b9c-f112af0fbb08"),
        [NodeId] = new("bc52b57a-bb41-4f92-a9df-122842826d58"),
        [ActualDeflection] = new("93048122-1ee0-47df-add3-2851984ca7f3"),
        [StandardAngle] = new("3c2b2a81-35ed-48f9-bcaf-0307e36a163a"),
        [AllowableDeflection] = new("081b5774-0112-4aae-ba65-2d5bd26a9ccc"),
        [ResidualDeflection] = new("8f192977-1c4d-44df-b5d0-7aef46e1b120"),
        [IsAcceptable] = new("2d00a14e-573a-44ee-9c2d-4c9e60dc23dd"),
        [Weight] = new("a6e1f5b0-9c1e-4d7a-8e2f-1b3c4d5e6f70"),
        [UnitWeight] = new("89b45662-b0e6-4047-9fe7-1d40a8be0359"),
        [NightWork] = new("f4159d85-ccdf-424a-9ff7-feac5eb91294"),
        [JointTotalCount] = new("c7d15820-8520-4c14-9b59-cd3152687aa6"),
        [LayingCount] = new("baf3ea94-8d06-4767-8632-486fe6294ca7"),
        [JointComposition] = new("179db41a-504c-48d4-8a63-5acac9b09521"),
        [BranchSpec] = new("5d7a1c34-6e92-4b08-9f41-2c8d0ab73e15"),
        [CutCount] = new("a3f26b81-47d0-4e5c-b912-6f80d4c1a97e"),
        // 접합 슬롯 3벌(docs/186 § 4-1 표가 원본). 배포된 뒤에는 바꾸지 않는다.
        [JointSlotKind(1)] = new("b6083907-2201-4bf8-861a-20f484fa2be0"),
        [JointSlotSpec(1)] = new("85a80c2f-505b-432b-aaeb-90ea5f13bc2e"),
        [JointSlotCount(1)] = new("bc329e65-3451-414a-a4a7-9b14885aac42"),
        [JointSlotKind(2)] = new("8ac38caf-7232-439c-a9bf-bde215367b51"),
        [JointSlotSpec(2)] = new("c25f3616-6a76-497c-992d-04388200ebb7"),
        [JointSlotCount(2)] = new("b7ba9b2b-5616-4647-af63-54950e729f1b"),
        [JointSlotKind(3)] = new("c18cb936-d12d-47d3-894e-894afc8e98e9"),
        [JointSlotSpec(3)] = new("8dae6754-d8c4-44bf-b11d-4bb542909b9c"),
        [JointSlotCount(3)] = new("3f3c9dc6-b6d6-463d-bbd6-c2c7b138e785"),
    };

    public static IReadOnlyList<SharedParameterDefinition> Definitions { get; } = new[]
    {
        Text(Addin, ParameterGroupType.IdentityData, false), Text(Part, ParameterGroupType.IdentityData),
        Text(NominalDiameter), Text(Material), Text(Grade), Text(ElevationDatum),
        LengthDef(Length), Def(NodeId, ParameterSpecType.Integer),
        Def(ActualDeflection, ParameterSpecType.Angle), Def(StandardAngle, ParameterSpecType.Angle), Def(AllowableDeflection, ParameterSpecType.Angle), Def(ResidualDeflection, ParameterSpecType.Angle),
        Def(IsAcceptable, ParameterSpecType.YesNo),
        NumberDef(Weight), NumberDef(UnitWeight), Def(NightWork, ParameterSpecType.YesNo),
        Def(JointTotalCount, ParameterSpecType.Integer),
        Def(LayingCount, ParameterSpecType.Integer),
        Text(JointComposition),
        Text(BranchSpec),
        Def(CutCount, ParameterSpecType.Integer),
        // 접합 슬롯 3벌 — 종류·규격은 문자열, 수만 Integer다(docs/186 § 4-1).
        Text(JointSlotKind(1)), Text(JointSlotSpec(1)), Def(JointSlotCount(1), ParameterSpecType.Integer),
        Text(JointSlotKind(2)), Text(JointSlotSpec(2)), Def(JointSlotCount(2), ParameterSpecType.Integer),
        Text(JointSlotKind(3)), Text(JointSlotSpec(3)), Def(JointSlotCount(3), ParameterSpecType.Integer),
    };

    /// <summary><b>관종 + 곡관 접합</b> → 접합 슬롯 `종류` 칸에 적을 문자열(docs/186 § 4-2).
    /// <para>수량산출(DHBoost)의 <c>PipeWorkCatalog</c> 키와 <b>문자열이 같아야</b> 행이 선다.</para>
    /// <para>대응이 없으면(PE·PVC 융착·접착) null이다. 호출부는 값을 지어내지 말고 <b>슬롯을 비우고</b> 경고를 남긴다.</para></summary>
    public static string? JointKindFor(PipeMaterial? material, BendConnection connection, bool useRestraintGland = false)
        => connection == BendConnection.Flanged ? "플랜지접합" : material switch
        {
            PipeMaterial.Steel => "용접합",
            PipeMaterial.DuctileIron when useRestraintGland => "이탈방지압륜접합",
            PipeMaterial.DuctileIron => "KP접합",
            _ => null
        };

    public static string DiameterText(double diameterMm) => $"DN{diameterMm:0.##}";
    /// <summary><see cref="BranchSpec"/>에 적을 표기 — `D150`, 구경이 여럿이면 `D600xD300`.
    /// <para><c>DN</c>이 아니라 <c>D</c>인 이유: 이 값은 호칭지름이 아니라 <b>산출서 규격 칸</b>이고,
    /// 같은 칸의 다른 값들(`D1650`·`6.00m`·`45˚`)이 전부 산출서 표기다. 구분자는 사용자가 손으로 잇기 쉽게 반각 `x`다.</para>
    /// <para>구경이 없으면 빈 문자열이다 &mdash; 직관·곡관처럼 분기가 없는 부재는 이 칸을 비운다.</para></summary>
    public static string BranchSpecText(params double[] branchDiametersMm)
        => branchDiametersMm is null || branchDiametersMm.Length == 0
            ? string.Empty
            : string.Join("x", branchDiametersMm.Select(diameter => $"D{diameter:0.##}"));
    /// <summary>두 구경을 갖는 부재(이경관 대×소, T형·십자 주관×분기)의 `DH_호칭지름` 표기 — `DN300×200`.
    /// 계획관로 이경관과 밸브실 T형·십자가 같은 표기를 써야 수량산출이 한 어휘로 읽는다(docs/178 § A-3).</summary>
    public static string DiameterText(double firstMm, double secondMm) => $"DN{firstMm:0.##}×{secondMm:0.##}";
    public static string Label(PipeMaterial material) => material switch { PipeMaterial.DuctileIron => "덕타일주철관", PipeMaterial.Steel => "강관", PipeMaterial.Pe => "PE관", PipeMaterial.Pvc => "PVC관", _ => material.ToString() };

    /// <summary>산출서 표기 코드. 수량산출 설정의 관종 키와 같은 어휘다(docs/139 § F-1, docs/141 § B-1).
    /// <para><c>DH_관종</c>에 기록하는 값이며, 수량산출이 이 어휘로 관종을 필터한다.</para></summary>
    public static string Code(PipeMaterial material) => material switch { PipeMaterial.DuctileIron => "DCIP", PipeMaterial.Steel => "SP", PipeMaterial.Pe => "PE", PipeMaterial.Pvc => "PVC", _ => string.Empty };

    /// <summary><see cref="Code(PipeMaterial)"/>의 역함수. 코드와 <b>정확히 일치</b>할 때만 값을 돌려준다.</summary>
    public static PipeMaterial? MaterialFromCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        foreach (PipeMaterial material in Enum.GetValues(typeof(PipeMaterial)))
            if (string.Equals(Code(material), code, StringComparison.Ordinal)) return material;
        return null;
    }

    /// <summary>모델에 찍힌 <c>DH_관종</c> 값에서 관종을 되찾는다.
    /// <para>코드를 먼저 보고, 못 찾으면 라벨로 본다 — 코드 기록으로 바꾸기 전에 배치된 모델은
    /// 아직 한글 라벨을 갖고 있기 때문이다. 둘 다 아니면 null이며, 호출부는 값을 지어내지 말고 건너뛴다.</para></summary>
    public static PipeMaterial? MaterialFromModelValue(string? value)
        => MaterialFromCode(value) ?? MaterialFromLabel(value);

    /// <summary><see cref="Label(PipeMaterial)"/>의 역함수. 모델에 찍힌 <c>DH_관종</c> 라벨에서 관종을 되찾는다.
    /// <para>라벨과 <b>정확히 일치</b>할 때만 값을 돌려준다 — 못 찾으면 null이며, 호출부는 값을 지어내지 말고 건너뛴다.</para></summary>
    public static PipeMaterial? MaterialFromLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        foreach (PipeMaterial material in Enum.GetValues(typeof(PipeMaterial)))
            if (string.Equals(Label(material), label, StringComparison.Ordinal)) return material;
        return null;
    }
    public static string Label(ZDatum datum) => datum switch { ZDatum.Centerline => "중심선", ZDatum.Invert => "관저고 (내부 바닥)", ZDatum.Crown => "크라운 (내부 천장)", ZDatum.OutsideTop => "외부 맨 위", ZDatum.OutsideBottom => "외부 맨 아래", _ => datum.ToString() };
    public static string Label(JointApplicationMode mode) => mode switch { JointApplicationMode.SingleJoint => "편측 1개소", JointApplicationMode.BothJoints => "양측 2개소", _ => mode.ToString() };

    private static SharedParameterDefinition Text(string name, ParameterGroupType group = ParameterGroupType.Data, bool userModifiable = true) => Def(name, ParameterSpecType.Text, group, userModifiable);
    private static SharedParameterDefinition LengthDef(string name) => Def(name, ParameterSpecType.Length);
    private static SharedParameterDefinition NumberDef(string name) => Def(name, ParameterSpecType.Number);
    private static SharedParameterDefinition Def(string name, ParameterSpecType spec, ParameterGroupType group = ParameterGroupType.Data, bool userModifiable = true) => new() { Name = name, Guid = Guids[name], SpecType = spec, GroupType = group, BindingType = ParameterBindingType.Instance, Categories = new[] { ParameterCategory.GenericModel }, UserModifiable = userModifiable };
}
