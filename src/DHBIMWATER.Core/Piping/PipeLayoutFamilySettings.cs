namespace DHBIMWATER.Core.Piping;

/// <summary>PipeLayout 패밀리·파라미터 매핑. 프로젝트(DataStorage)에 저장한다(docs/175 § E).
/// 이름은 전부 "패밀리명 : 유형명"이며, 빈 문자열은 "지정 없음"이다.
/// <para>배치 DTO(<see cref="PipeSegmentFamilySelection"/>)와 1:1이지만 따로 두는 이유:
/// 이쪽은 저장·편집 단위이고 저쪽은 한 번의 생성 요청에 실리는 스냅샷이다. 변환은 <see cref="ToSelection"/> 한곳에만 둔다.</para></summary>
public sealed record PipeLayoutFamilySettings(
    string SegmentFamilyTypeName = "",
    string LengthParameterName = "",
    string DiameterParameterName = "",
    double StraightLengthMm = PipeSegmentPlan.StraightLengthMm,
    string BendFamilyTypeName = "",
    string BendDiameterParameterName = "",
    string BendAngleParameterName = "",
    string TeeFamilyTypeName = "",
    string TeeDiameterParameterName = "",
    string TeeBranchDiameterParameterName = "",
    string CrossFamilyTypeName = "",
    string CrossDiameterParameterName = "",
    string CrossBranchDiameterParameterName = "",
    /// <summary>Y형관(경사 분기). 차수 3 절점 중 분기각 30~60°인 곳에 배치한다(docs/187 § 3-1).</summary>
    string WyeFamilyTypeName = "",
    string WyeDiameterParameterName = "",
    string WyeBranchDiameterParameterName = "",
    /// <summary>Y형 분기각(도)을 기록할 파라미터명. 비어 있으면 각도를 구동하지 않는다.</summary>
    string WyeBranchAngleParameterName = "",
    /// <summary>이경관. 매핑만 저장하고 배치하지 않는다(docs/175 D1).
    /// <para>2026-09-18 사용자 결정: 동심·편심을 <b>한 칸으로 통합</b>했다 — 편락관 전용 3칸은 삭제했다(docs/188).
    /// 옛 저장값에 편락관 항목이 남아 있어도 JSON 역직렬화가 모르는 키를 버리므로 그대로 읽힌다.</para></summary>
    string ReducerFamilyTypeName = "",
    string ReducerLargeDiameterParameterName = "",
    string ReducerSmallDiameterParameterName = "",
    /// <summary>밸브 패밀리명(유형명 제외)별 직경 파라미터명(docs/135 § B).</summary>
    IReadOnlyDictionary<string, string>? ValveDiameterParameterNamesByFamily = null,
    /// <summary>이음관 구경 파라미터. 전역 1개(docs/167 § C).</summary>
    string JointDiameterParameterName = "",
    /// <summary>외곽으로 가져온 벽체 ElementId. 좌표가 아니라 Id를 저장한다 — 창을 열 때 형상을 다시 뽑으므로
    /// 벽을 옮기거나 두께를 바꿔도 캔버스가 따라오고, 지운 벽은 빠진다.</summary>
    IReadOnlyList<long>? OutlineWallElementIds = null,
    /// <summary>함께 가져온 기둥 ElementId. 기둥 1본이 단면 여러 개로 쪼개지므로 저장 전에 Distinct 한다.</summary>
    IReadOnlyList<long>? OutlineColumnElementIds = null,
    /// <summary>캔버스 기준점(Revit 프로젝트 좌표 mm). 벽 일부 삭제로 centroid가 이동해도 이미 그린 배관이 어긋나지 않게 저장값을 쓴다.</summary>
    double? ReferenceX = null,
    double? ReferenceY = null,
    /// <summary>화살표 기준 방향. 포트 이격 기준 모서리를 정하므로 포트와 함께 저장한다.</summary>
    bool ArrowFromTop = false,
    /// <summary>IN/OUT 포트 구성. null이면 저장본 없음으로 기본 포트를 유지한다.</summary>
    IReadOnlyList<StoredPipePort>? Ports = null,
    /// <summary>밸브부관 B(배관 부속류, docs/225 § B). 캔버스에서 밸브부관 B로 바꾼 꺾인 절점에만 놓는다.
    /// <para>맨 뒤에 두는 이유: 저장은 JSON 이름 기준이라 순서와 무관하지만, 이 레코드를 위치 인자로 만드는 호출부가 있다.</para></summary>
    string ValveBendFamilyTypeName = "",
    string ValveBendDiameterParameterName = "",
    /// <summary>밸브부관 A(T자, 4포트, docs/227 § B). 캔버스에서 밸브부관 A로 바꾼 T 절점에만 놓는다.</summary>
    string ValveTeeFamilyTypeName = "",
    string ValveTeeMainDiameterParameterName = "",
    string ValveTeeBranchDiameterParameterName = "",
    string ValveTeeHorizontalBranchParameterName = "",
    string ValveTeeVerticalBranchParameterName = "",
    /// <summary>강관 밸브부관 등급(docs/256). 바이패스 지관 규격표 조회에 쓴다. 옛 저장본에는 키가 없어 기본값 F12로 읽힌다.</summary>
    SteelFittingClass SteelFittingClass = SteelFittingClass.F12,
    /// <summary>관 등급(docs/260 § C). null이면 옛 저장본 — 창이 재료별 기본값을 쓴다. 빈 문자열은 강관 "DN별 기본 등급"이다.</summary>
    string? PipeKind = null,
    /// <summary>관 중심 높이 지정 방식(docs/285). null이면 옛 저장본 — 기본값 Relative.</summary>
    PipeElevationMode? ElevationMode = null,
    /// <summary>상대값 기준 레벨 ElementId. 이름 변경에 견디도록 Id로 저장한다.</summary>
    long? ElevationLevelId = null,
    /// <summary>레벨로부터 Offset(mm).</summary>
    double? ElevationOffsetMm = null)
{
    public static PipeLayoutFamilySettings Empty { get; } = new();

    /// <summary>필수 항목(관 패밀리·길이 파라미터)이 비었으면 true. 창 요약에 빨간 표시를 낸다.</summary>
    public bool IsIncomplete => string.IsNullOrWhiteSpace(SegmentFamilyTypeName) || string.IsNullOrWhiteSpace(LengthParameterName);

    /// <summary>밸브 딕셔너리의 읽기 전용 뷰. null이면 빈 사전.</summary>
    public IReadOnlyDictionary<string, string> ValveDiameterParameters
        => ValveDiameterParameterNamesByFamily ?? new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>배치 DTO로 변환. 필드 대응은 1:1이며 여기 한곳에만 둔다.
    /// <para>밸브 딕셔너리는 <b>복사본</b>을 넘긴다 — ExternalEvent가 처리하는 사이 창에서 값이 바뀔 수 있다.</para></summary>
    public PipeSegmentFamilySelection ToSelection() => new(
        SegmentFamilyTypeName, LengthParameterName,
        BendFamilyTypeName, TeeFamilyTypeName,
        StraightLengthMm, DiameterParameterName,
        BendDiameterParameterName, TeeDiameterParameterName, "",
        BendAngleParameterName, TeeBranchDiameterParameterName,
        new Dictionary<string, string>(ValveDiameterParameters, StringComparer.Ordinal),
        JointDiameterParameterName,
        CrossFamilyTypeName, CrossDiameterParameterName, CrossBranchDiameterParameterName,
        ReducerFamilyTypeName, ReducerLargeDiameterParameterName, ReducerSmallDiameterParameterName,
        WyeFamilyTypeName, WyeDiameterParameterName, WyeBranchDiameterParameterName, WyeBranchAngleParameterName,
        ValveBendFamilyTypeName, ValveBendDiameterParameterName,
        ValveTeeFamilyTypeName, ValveTeeMainDiameterParameterName, ValveTeeBranchDiameterParameterName,
        ValveTeeHorizontalBranchParameterName, ValveTeeVerticalBranchParameterName);
}
