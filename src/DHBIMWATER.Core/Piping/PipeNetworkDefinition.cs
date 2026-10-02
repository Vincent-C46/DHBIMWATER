using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Core.Piping;

public enum PipeOutputMode
{
    MepPipe,
    GenericModel,
    /// <summary>배관 밸브류 카테고리의 직관·단관 패밀리로 관을 만든다(docs/39). MEP Pipe를 만들지 않는다.</summary>
    PipeAccessorySegment
}
/// <param name="FittingKind">절점의 부속 종류(docs/225 § A-5, docs/227 § A). 지정과 절점 모양이 맞지 않으면 Bend(기본)다.</param>
/// <param name="ValveSideEdgeId">밸브부관 B일 때 밸브 쪽 레그의 엣지 Id. 정할 수 없으면 null — 검증에서 막는다.</param>
/// <param name="MainDiameterMm">밸브부관 B일 때 이어진 밸브부관 A의 주관 구경(docs/227 § A). 규격 `D주관xD분기`의 주관이자, B의 `부관_구경`(본관 구경 키)에 구동하는 값이다(docs/244 § C, docs/229 § A 번복).
/// A의 분기에 닿지 않으면 null — 검증에서 막는다.</param>
public sealed record PipeNodeDefinition(Guid Id, Point2D Position, NodeKind NodeKind,
    PipeNodeFittingKind FittingKind = PipeNodeFittingKind.Bend, Guid? ValveSideEdgeId = null, double? MainDiameterMm = null)
{
    public bool IsValveBend => NodeKind == NodeKind.Elbow && FittingKind == PipeNodeFittingKind.ValveBend;
    public bool IsValveTee => NodeKind == NodeKind.Tee && FittingKind == PipeNodeFittingKind.ValveTee;
}

/// <summary>관·절점부속 배치(<see cref="PipeOutputMode.PipeAccessorySegment"/>)에 필요한 패밀리·파라미터 지정 묶음.
/// 이름은 모두 "패밀리명 : 타입명" 형식이다.</summary>
public sealed record PipeSegmentFamilySelection(
    /// <summary>직관·단관을 모두 만드는 단일 관 패밀리. 길이는 <see cref="LengthParameterName"/>으로 구동한다.</summary>
    string SegmentFamilyTypeName,
    /// <summary>관 인스턴스에 실제 길이(mm)를 기록할 파라미터명.</summary>
    string LengthParameterName,
    /// <summary>각도 구분 없이 모든 곡관 절점에 배치하는 단일 곡관 패밀리.</summary>
    string BendFamilyTypeName,
    string TeeFamilyTypeName,
    /// <summary>직관 1본의 정척 길이(mm). 패밀리 수식의 직관/단관 분기 기준과 같아야 한다.</summary>
    double StraightLengthMm = PipeSegmentPlan.StraightLengthMm,
    /// <summary>관 인스턴스에 직경(mm)을 기록할 파라미터명. 비어 있으면 직경을 구동하지 않는다.</summary>
    string DiameterParameterName = "",
    /// <summary>곡관 인스턴스에 직경(mm)을 기록할 파라미터명. 비어 있거나 유형 파라미터면 기록을 건너뛴다.</summary>
    string BendDiameterParameterName = "",
    /// <summary>T형 인스턴스에 직경(mm)을 기록할 파라미터명. 비어 있거나 유형 파라미터면 기록을 건너뛴다.</summary>
    string TeeDiameterParameterName = "",
    /// <summary>배관 밸브류 인스턴스에 직경(mm)을 기록할 파라미터명. 비어 있거나 유형 파라미터면 기록을 건너뛴다.</summary>
    string ValveDiameterParameterName = "",
    /// <summary>곡관 인스턴스에 꺾임각(도)을 기록할 파라미터명.</summary>
    string BendAngleParameterName = "",
    /// <summary>T형 인스턴스에 분기 직경(mm)을 기록할 파라미터명. 비어 있으면 분기 구경을 구동하지 않는다.
    /// 주관·분기 구경이 함께 맞아야 형상 수식이 성립하는 패밀리를 위한 항목이다(docs/130).</summary>
    string TeeBranchDiameterParameterName = "",
    /// <summary>밸브 패밀리명별 직경 파라미터명. 패밀리마다 파라미터 이름이 달라도 섞어 배치할 수 있게 한다(docs/135 § B).
    /// 키는 유형명을 뺀 패밀리명이다. 항목이 없거나 값이 비면 그 패밀리는 직경을 구동하지 않는다.
    /// null이면 <see cref="ValveDiameterParameterName"/> 하나를 모든 밸브에 쓰던 기존 동작으로 되돌아간다.</summary>
    IReadOnlyDictionary<string, string>? ValveDiameterParameterNamesByFamily = null,
    /// <summary>이음관 인스턴스에 직경(mm)을 기록할 파라미터명(`이음관_구경`). 비어 있으면 구동하지 않는다.
    /// <para>밸브와 달리 패밀리별 딕셔너리로 가지 않고 전역 1개다 — 이음관 패밀리는 실무상 파라미터명이
    /// 하나로 통일돼 있고, 관·곡관·이형관이 이미 전역 1개 방식이다(docs/167 § C).</para></summary>
    string JointDiameterParameterName = "",
    /// <summary>십자 절점에 배치하는 십자관 패밀리(docs/175 § B). 비어 있으면 십자 절점이 있을 때 검증에서 막는다.</summary>
    string CrossFamilyTypeName = "",
    /// <summary>십자관 주관 직경 파라미터명. T형과 같은 규칙으로 절점의 큰 쪽 직경을 쓴다.</summary>
    string CrossDiameterParameterName = "",
    /// <summary>십자관 분기 직경 파라미터명. 주·분기 구경이 함께 맞아야 형상 수식이 성립하는 패밀리를 위한 항목(docs/130).</summary>
    string CrossBranchDiameterParameterName = "",
    /// <summary>이경관 패밀리. <b>저장·전달만 하고 배치하지 않는다</b>(docs/175 D1) —
    /// 바이패스관처럼 이경관 없이 관만 붙는 경우가 있어 자동 배치를 하지 않기로 했다. 배치 규칙은 후속 지시서.
    /// <para>동심·편심 구분은 2026-09-18부터 없다 — 한 칸으로 통합했다(docs/188).</para></summary>
    string ReducerFamilyTypeName = "",
    string ReducerLargeDiameterParameterName = "",
    string ReducerSmallDiameterParameterName = "",
    /// <summary>Y형 절점(분기각 30~60°)에 배치하는 Y형관 패밀리(docs/187 § 3-2).
    /// 비어 있으면 Y형 절점이 있을 때 검증에서 막는다.
    /// <para>맨 뒤에 두는 이유: 이 레코드를 위치 인자로 만드는 호출부·테스트가 있어 중간에 끼우면 인자가 밀린다.</para></summary>
    string WyeFamilyTypeName = "",
    /// <summary>Y형관 주관 직경 파라미터명. T형과 같은 규칙으로 절점의 큰 쪽 직경을 쓴다.</summary>
    string WyeDiameterParameterName = "",
    /// <summary>Y형관 분기 직경 파라미터명. 주·분기 구경을 함께 맞춰야 형상 수식이 성립한다(docs/130).</summary>
    string WyeBranchDiameterParameterName = "",
    /// <summary>Y형관 분기각(도) 파라미터명. 곡관의 각도 파라미터와 같은 방식으로 캔버스 실제 각도를 기록한다.
    /// 비어 있으면 구동하지 않는다.</summary>
    string WyeBranchAngleParameterName = "",
    /// <summary>밸브부관 B 패밀리(배관 부속류, docs/225 § B). 꺾인 절점을 밸브부관 B로 바꾼 곳에만 쓴다.
    /// 비어 있으면 그런 절점이 있을 때 검증에서 막는다. 맨 뒤 자리인 이유는 Y형 칸과 같다.</summary>
    string ValveBendFamilyTypeName = "",
    /// <summary>밸브부관 B 직경(자기 관경) 파라미터명. 비어 있거나 유형 파라미터면 기록을 건너뛴다.</summary>
    string ValveBendDiameterParameterName = "",
    /// <summary>밸브부관 A 패밀리(배관 부속류, 4포트, docs/227 § B). T 절점을 밸브부관 A로 바꾼 곳에만 쓴다.</summary>
    string ValveTeeFamilyTypeName = "",
    /// <summary>밸브부관 A 주관 직경 파라미터명(주관 레그 관경).</summary>
    string ValveTeeMainDiameterParameterName = "",
    /// <summary>밸브부관 A 분기 직경 파라미터명(분기 레그 관경).</summary>
    string ValveTeeBranchDiameterParameterName = "",
    /// <summary>밸브부관 A 수평분기 가시성 Yes/No 파라미터명. 캔버스 분기는 평면이므로 항상 예로 쓴다.</summary>
    string ValveTeeHorizontalBranchParameterName = "",
    /// <summary>밸브부관 A 수직분기 가시성 Yes/No 파라미터명. 이번 범위는 수평분기만이라 항상 아니오로 쓴다(docs/227 § 0-7).</summary>
    string ValveTeeVerticalBranchParameterName = "");
// 밸브부관 B 주관구경은 구동하지 않는다(2026-09-28 사용자 결정, docs/229 § A) — 연결된 A 주관 직경은 규격 매개변수에만 적는다.

public sealed record PipeNetworkDefinition(
    IReadOnlyList<PipeNodeDefinition> Nodes,
    IReadOnlyList<PipeEdgeDefinition> Edges,
    IReadOnlyList<FittingPlacementDefinition> InlineFittings,
    double Elevation,
    /// <summary>이 배치의 <b>기본</b> 관경(mm). 실제 관·부속은 엣지별 <see cref="PipeEdgeDefinition.DiameterMm"/>를
    /// 쓰며, 이 값은 폴백·검증용으로만 남는다(docs/152 § A-5).</summary>
    double DiameterMm,
    PipeOutputMode OutputMode,
    Point2D ReferencePoint,
    string PipingSystemTypeName = "",
    string PipeTypeName = "",
    string LevelName = "",
    PipeSegmentFamilySelection? SegmentFamilies = null,
    /// <summary>격점상세도 배관재료표에 쓰는 격점 식별자(docs/131). 비어 있으면 기록만 건너뛴다.</summary>
    string JunctionId = "",
    /// <summary>관 재료. `DH_관종`과 `DH_비고`(직관·단관)의 원본 값이다(docs/141 § B).</summary>
    PipeMaterial Material = PipeMaterial.DuctileIron,
    /// <summary>야간 작업 여부. 체크가 야간, 해제가 주간이다(docs/141 § C).</summary>
    bool IsNightWork = false,
    /// <summary>재생성 전에 삭제할 기존 요소 Id(docs/158 § F). 캔버스로 되가져온 원본을 사용자가
    /// 창에서 "원본 삭제 후 생성"으로 체크했을 때만 값이 찬다. 비어 있으면 아무것도 지우지 않는다 — 기본값이다.</summary>
    IReadOnlyList<long>? SourceElementIdsToDelete = null,
    /// <summary>밸브실 유형(`DH_Zone`). 외곽 벽체에서 읽거나 사용자가 창에서 고른 값이며,
    /// 비어 있으면 기록하지 않는다(docs/161 § D).</summary>
    string Zone = "",
    /// <summary>이 배치의 <b>기본</b> 접합 방식(docs/170 § E). 부재가 자기 패밀리 파라미터로 접합을 밝히면
    /// 그쪽이 이긴다 — 이 값은 밝히지 않은 부재에만 쓰인다(<c>PipeJointResolution.Resolve</c>).
    /// <para>기본값이 플랜지인 이유: 원장에서 밸브실은 실당 용접 1개소를 빼면 전량 플랜지다(docs/168 § 5-3).</para></summary>
    BendConnection JointConnection = BendConnection.Flanged,
    /// <summary>강관 밸브부관 등급(docs/256). 바이패스 지관 규격표 조회에 쓴다. 덕타일주철관 등에서는 쓰지 않는다.</summary>
    SteelFittingClass SteelFittingClass = SteelFittingClass.F12,
    /// <summary>관 등급(`상수 1종관`·`STWW 400 A` 등, docs/260 § C). 직관·단관 단위중량 조회 키이며 `DH_관종등급`에 그대로 적는다.
    /// 비어 있으면 강관은 DN별 기본 등급으로 무게를 구하고, 주철관은 무게를 비운다.</summary>
    string PipeKind = "",
    /// <summary>직관·곡관 규격표(규격표 창 편집값). UseCase가 프로젝트 저장값을 실어 보낸다. null이면 코드 내장 기본값(docs/260 § D).</summary>
    BendSettings? CatalogSettings = null);
// 이탈방지압륜 플래그는 창에서 지정하지 않는다(docs/175 § A). 신규 배치는 압륜 없음으로 기록하고,
// 사용자가 인스턴스 접합 슬롯 종류(이탈방지압륜접합)로 제어한다. 재기록 경로는 그 값을 보존한다(PipeJointCountWriter.ReadRestraintGland).
