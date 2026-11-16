using DHBIMWATER.Application.DTOs.Revit;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.UI.Base;
using DHBIMWATER.UI.Commands;
using DHBIMWATER.UI.Utilities;
using DHBIMWATER.Shared.Errors;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
// Core.Gis를 통째로 열면 NodeKind가 Core.Piping과 충돌한다. 필요한 형식만 별칭으로 끌어온다.
using PipeAlignmentParameters = DHBIMWATER.Core.Gis.PipeAlignmentParameters;

namespace DHBIMWATER.UI.ViewModels.Modeling;

public enum PipeLayoutMode { Selection, Drawing }

/// <summary>선택 모드에서 무엇을 끌고 있는지(docs/152 § E-1).</summary>
internal enum PipeDragKind { Edge, Node, Fitting }

/// <summary>진행 중인 드래그 한 건. 오프셋만 들고 있고 그래프는 커밋 시점에만 바뀐다(docs/152 § E-1).</summary>
internal sealed record PipeDragState(
    PipeDragKind Kind,
    Point StartScreen,
    DHBIMWATER.Core.Geometry.Point2D StartModel,
    IReadOnlyList<Guid> EdgeIds,
    Guid? NodeId,
    DHBIMWATER.Core.Geometry.Point2D? NodeOrigin,
    Guid? FittingId,
    Guid? FittingEdgeId)
{
    /// <summary>모델 좌표(mm) 이동량.</summary>
    public double Dx { get; set; }
    public double Dy { get; set; }
    /// <summary>부속 드래그의 미리보기 파라미터.</summary>
    public double T { get; set; }
    /// <summary>잡은 자리와 부속 중심의 T 차이(부속 T − 잡은 자리 정사영 T). 바이패스 세트는 중심에서 먼 표식도 잡히므로,
    /// 이것을 더하지 않으면 드래그 시작 순간 중심이 커서 밑으로 튄다(docs/273).</summary>
    public double GrabOffsetT { get; set; }
    /// <summary>클릭이 아니라 이동으로 확정됐는가.</summary>
    public bool Exceeded { get; set; }
}

public sealed class PipeLayoutViewModel : ViewModelBase
{
    private static readonly string[] PipeFamilyDefaultKeywords = ["직관", "단관"];
    /// <summary>곡관 기본값 우선순위. 소켓곡관이 앞이다(docs/74 § 6, docs/135 § 1).
    /// 한 배열에 담아야 GuessFamily의 DH 접두 패스가 두 키워드 모두에 먼저 걸린다 —
    /// 호출을 쪼개면 "DH 없는 소켓곡관"이 "DH_곡관"을 이긴다(docs/145 § 1).</summary>
    private static readonly string[] BendFamilyDefaultKeywords = ["소켓곡관", "곡관"];
    /// <summary>`T자관` = 강관 패밀리 이름 규칙(2026-10-30).</summary>
    private static readonly string[] TeeFamilyDefaultKeywords = ["T형관", "T자관", "T형", "티", "TEE"];
    /// <summary>강관 패밀리 표지 단어. 관종 강관이면 이 단어 든 패밀리만, 주철관이면 이 단어 없는 패밀리만 추정 후보(2026-10-30).</summary>
    private const string SteelFamilyToken = "강관";
    /// <summary>Y형관 추정 키워드(docs/187 § 3-4). T형 키워드에서 떼어냈다 —
    /// 남겨 두면 Y형관 패밀리가 T형 칸으로 추정돼 두 칸이 같은 패밀리를 가리킨다.</summary>
    private static readonly string[] WyeFamilyDefaultKeywords = ["Y형관", "Y형", "WYE", "와이"];
    /// <summary>이형관 종류별 기본값 추정 키워드(docs/175 § B-5). 저장값이 없을 때만 쓴다.</summary>
    private static readonly string[] CrossFamilyDefaultKeywords = ["십자관", "십자", "CROSS"];
    /// <summary>이경관 추정 키워드. 동심·편심을 한 칸으로 통합했으므로 편락관 이름도 여기서 받는다(docs/188).</summary>
    private static readonly string[] ReducerFamilyDefaultKeywords = ["이경관", "편락관", "REDUCER"];
    /// <summary>이경관·편락관의 작은 쪽 직경 파라미터 후보 키워드. 못 찾으면 비워 둔다(주관 구경이 들어가면 안 된다).</summary>
    private static readonly string[] ReducerSmallDiameterKeywords = ["소구경", "작은", "축소", "Small", "Minor", "2"];
    private static readonly string[] DiameterParameterDefaultKeywords = ["공칭지름", "공칭구경", "구경", "관경", "직경", "지름", "호칭", "Nominal Diameter", "NominalDiameter", "Nominal Dia", "Nominal", "Diameter", "DN"];
    /// <summary>영문 약어 직경 파라미터. Contains로 찾으면 "Bend_길이"·"End_Offset"·"Round"가 걸리므로
    /// 반드시 완전일치로만 본다(docs/145 § 2). 선례: PipeAlignmentModelingViewModel.cs:450.</summary>
    private static readonly string[] ExactDiameterParameterNames = ["ND", "DN", "NB"];
    private static readonly string[] BendAngleParameterDefaultKeywords = ["각도"];
    private const string RadiusToken = "반지름";
    // 볼트내밈_길이 등 볼트 치수는 관 길이 아님 — 추정·저장값 복원에서 제외(docs/309).
    private const string BoltToken = "볼트";
    /// <summary>밸브부관 A·B 기본 패밀리명(유형명 제외, 2026-09-28 사용자 결정, docs/232 § A). 완전일치 → 없으면 종전 A/B 글자 규칙.</summary>
    private const string ValveTeeDefaultFamilyName = "DH_특수_밸브부관";
    private const string ValveBendDefaultFamilyName = "DH_특수_밸브부관(D형)";
    /// <summary>밸브부관 파라미터 기본값 우선순위(docs/232 § A-3, 2026-09-28 사용자 결정 — 완전일치 하드코딩 대신 키워드 포함 여부,
    /// 사용자가 제시한 단어가 1순위). 앞 키워드부터 차례로 이름에 포함된 것을 고른다.
    /// 직경 키워드는 <see cref="DiameterTokens"/> 중 하나가 함께 든 이름만 받는다 — `주관`만 보면 `주관_길이`가 잡힌다.</summary>
    private static readonly string[] ValveTeeMainDiameterKeywords = ["주관_구경", "주관", "본관", "Main", "Run"];
    private static readonly string[] ValveTeeBranchDiameterKeywords = ["수평분기_구경", "수평분기", "분기", "Branch"];
    private static readonly string[] ValveBendDiameterKeywords = ["부관_구경", "부관", "호칭지름", "호칭구경", "호칭"];
    /// <summary>수평·수직분기 예/아니오. 구경·직경이 든 이름은 뺀다(`수평분기_구경`과 겹친다).</summary>
    private static readonly string[] ValveTeeHorizontalFlagKeywords = ["수평분기", "수평", "Horizontal"];
    private static readonly string[] ValveTeeVerticalFlagKeywords = ["수직분기", "수직", "Vertical"];
    /// <summary>"직경 계열 이름"으로 볼 토큰. 키워드 우선순위 추정에서 길이·개수 같은 다른 파라미터를 거른다.</summary>
    private static readonly string[] DiameterTokens = ["구경", "직경", "지름", "관경", "Diameter", "Nominal", "Dia"];
    /// <summary>인라인 부속의 TypeKey. 캔버스에서 직접 배치하는 경로와 역가져오기 경로가 같은 값을 써야
    /// 같은 부속으로 취급된다(docs/158 § E-3).</summary>
    private const string InlineFittingTypeKey = "배관 밸브류";
    /// <summary>"허용굴곡각도"처럼 프로젝트가 카테고리에 바인딩해 둔 판정용 파라미터는 곡관 각도로 자동선택하지 않는다(docs/138 § B).</summary>
    private const string AllowanceToken = "허용";
    private readonly IElementTypeQueryRepo _typeRepo;
    /// <summary>패밀리·파라미터 매핑 저장소(docs/175 § E). null이면(테스트) 저장값 없이 이름 추정만 한다.</summary>
    private readonly IPipeLayoutFamilySettingsRepo? _settingsRepo;
    /// <summary>프로젝트 저장값을 읽어 적용했는지. true면 역가져오기가 채워진 칸을 덮지 않는다(docs/175 § E-4 6).</summary>
    private bool _familySettingsLoaded;
    private PipeNetwork _network = new();
    /// <summary>편집 직전 상태. 그래프뿐 아니라 "가져온 원본 목록"까지 함께 되돌려야
    /// Ctrl+Z로 가져오기를 취소했을 때 삭제 예약도 풀린다(docs/163 § C).
    /// <para>포트 목록도 함께 담는다 — 추천 배치 적용이 포트를 바꾸므로 되돌릴 때 화살표도 원복돼야 한다(docs/172 § C-10).</para></summary>
    private readonly Stack<(PipeNetwork Network, IReadOnlyList<long> ImportedSourceIds, IReadOnlyList<PipePort> Ports)> _undoHistory = new();
    private Point? _segmentStart;
    private PipeEdgeItem? _selectedEdge;
    private PipeLayoutMode _mode = PipeLayoutMode.Drawing;
    private InlineFittingItem? _selectedFitting;
    private string? _selectedFamilyTypeName;
    private double _gridSnapMm = 10;
    private Guid? _fittingPreviewEdgeId;
    private double _fittingPreviewT;
    private bool _isFittingPreviewVisible;
    private double _fittingPreviewX, _fittingPreviewY, _fittingDimX1, _fittingDimY1, _fittingDimX2, _fittingDimY2;
    // 바이패스 세트 배치(docs/228 § B). 배치 대상 밸브·관경은 다음 세트에 쓸 "현재 선택"이고, 놓인 세트는 자기 값을 들고 있다.
    private bool _isPlacingBypassSet;
    // 본관 밸브는 세트가 아니다(docs/230) — A–A 사이에 일반 밸브로 놓는다. 여기에는 바이패스 밸브만 둔다.
    private string? _bypassValveFamilyName, _bypassValveTypeName;
    // 파라미터 후보 갱신 지연(docs/258 § B). 0보다 크면 갱신을 미루고 "대기"만 표시한다 — 생성자 추정·저장값 적용 중에
    // 패밀리 세터마다 전체 후보를 다시 읽어(최대 44회) 추정했다가 버릴 패밀리까지 EditFamily로 열던 것을 막는다.
    private int _parameterRefreshDeferral;
    private bool _pendingSegmentRefresh, _pendingFittingRefresh, _pendingBypassRefresh;
    // 밸브 2개 세트 입력(docs/255 § D-1). 1개 세트면 V2·단관은 세트에 싣지 않는다.
    private int _bypassValveCount = 1;
    private string? _bypassSecondValveFamilyName;
    private double _bypassSpoolLengthMm = BypassSet.DefaultSpoolLengthMm;
    /// <summary>바이패스 밸브 직경 파라미터(docs/232 § C). 4번 섹션 밸브와 같은 패밀리별 사전에 기억한다.</summary>
    private string? _bypassValveDiameterParameterName;
    /// <summary>마지막 미리보기 커서(모델 mm). 세트를 본관 어느 쪽으로 뺄지 정한다.</summary>
    private DHBIMWATER.Core.Geometry.Point2D? _fittingPreviewCursor;
    private System.Windows.Media.Geometry? _bypassPreviewLine, _bypassPreviewMarkers;
    private bool _isBypassPreviewVisible;
    private string _fittingDimLabel = "0";
    private string _status = "캔버스 클릭 → 배관 시작점";
    private double _elevation;
    private PipeElevationMode _elevationMode = PipeElevationMode.Relative;
    private LevelInfo? _elevationLevel;
    private double _elevationOffsetMm;
    private string _elevationLevelHint = string.Empty;
    private bool _savedElevationLevelRestored;
    private bool _savedElevationLevelIdSpecified;
    private bool _savedElevationLevelMissing;
    private double _referenceX, _referenceY;
    private string _junctionId = string.Empty;
    /// <summary>격점번호를 사용자가 직접 손댔는가(docs/177 § B). 서면 유형을 바꿔도 자동값으로 덮지 않는다.
    /// 포트 이격의 <see cref="PipePortItem.UserEdited"/>와 같은 규약이다.</summary>
    private bool _junctionIdUserEdited;
    /// <summary>배치할 배관 부재에 기록할 밸브실 유형(`DH_Zone`). 외곽 벽체에서 읽어 채운다(docs/161).</summary>
    private string _valveRoomZone = string.Empty;
    private PipeMaterial _pipeMaterial = PipeMaterial.DuctileIron;
    private SteelFittingClass _steelFittingClass = SteelFittingClass.F12;
    /// <summary>관 등급(docs/260 § C). 빈 문자열은 강관 "DN별 기본 등급"이다.</summary>
    private string _pipeKind = PipeKindCatalog.Water1;
    /// <summary>이 배치의 기본 접합 방식(docs/170 § E). 기본값이 플랜지인 이유는
    /// 원장에서 밸브실이 실당 용접 1개소를 빼면 전량 플랜지이기 때문이다(docs/168 § 5-3).</summary>
    private BendConnection _jointConnection = BendConnection.Flanged;
    // 기본 관경 300. 덕타일 밸브부관 규격의 최소 관경이다(docs/281 § A).
    private double _diameterMm = 300;
    private List<string> _accessoryFamilyTypeNames = [];
    private string? _selectedValveFamilyName;
    private string? _segmentFamilyTypeName, _lengthParameterName, _diameterParameterName;
    private double _straightLengthMm = PipeSegmentPlan.StraightLengthMm;
    private string? _bendFamilyTypeName, _teeFamilyTypeName;
    private string? _bendDiameterParameterName, _bendAngleParameterName, _teeDiameterParameterName, _teeBranchDiameterParameterName, _valveDiameterParameterName;
    // 이형관 종류별 패밀리·파라미터(docs/175 § B-5). 이경관·편락관은 매핑만 보관하고 배치하지 않는다(D1).
    private string? _crossFamilyTypeName, _crossDiameterParameterName, _crossBranchDiameterParameterName;
    private string? _wyeFamilyTypeName, _wyeDiameterParameterName, _wyeBranchDiameterParameterName, _wyeBranchAngleParameterName;
    private string? _reducerFamilyTypeName, _reducerLargeDiameterParameterName, _reducerSmallDiameterParameterName;
    private bool _crossDiameterParameterPinned, _crossBranchDiameterParameterPinned;
    private bool _wyeDiameterParameterPinned, _wyeBranchDiameterParameterPinned, _wyeBranchAngleParameterPinned;
    // 밸브부관(docs/225 § B). 배관 부속류 절점 부속이라 이형관 칸과 같은 모양으로 둔다.
    private string? _valveBendFamilyTypeName, _valveBendDiameterParameterName;
    private bool _valveBendDiameterParameterPinned;
    // 밸브부관 A(T자, docs/227 § B). 주관·분기 직경 + 수평·수직분기 예/아니오 파라미터를 든다.
    private string? _valveTeeFamilyTypeName, _valveTeeMainDiameterParameterName, _valveTeeBranchDiameterParameterName;
    private string? _valveTeeHorizontalBranchParameterName, _valveTeeVerticalBranchParameterName;
    private bool _valveTeeMainDiameterParameterPinned, _valveTeeBranchDiameterParameterPinned;
    private bool _valveTeeHorizontalBranchParameterPinned, _valveTeeVerticalBranchParameterPinned;
    private bool _reducerLargeDiameterParameterPinned, _reducerSmallDiameterParameterPinned;
    /// <summary>배치용으로 고른 이음관 패밀리(docs/175 § C-3). 밸브 유형 선택과 상호 배타다.</summary>
    private string? _selectedJointFamilyTypeName;
    /// <summary>1단 콤보에서 고른 이음관 패밀리명(유형명 제외). 2단 유형 리스트를 이 값으로 거른다(docs/200).</summary>
    private string? _selectedJointFamilyName;
    /// <summary>밸브 패밀리명 → 직경 파라미터명. 패밀리를 오가도 각자 고른 값을 잃지 않는다(docs/135 § B).</summary>
    private readonly Dictionary<string, string> _valveDiameterParameterByFamily = [];
    /// <summary>밸브 패밀리명 → <b>유형</b> 직경 매개변수명(docs/233 § B-3). 게이트밸브처럼 공칭지름이 유형 매개변수(`Nominal pipe size`)인 패밀리에서
    /// 고른 유형의 관경을 대조하는 확인용이다. 값을 쓰지 않으므로 배치 경계에 넘기지 않고, 저장도 하지 않는다(세션 한정).</summary>
    private readonly Dictionary<string, string> _valveTypeDiameterParameterByFamily = [];
    /// <summary>유형별 길이형 유형 매개변수 조회 캐시(docs/233 § B-3). 검증 문구가 자주 재계산되므로 Revit 조회를 유형당 한 번으로 줄인다.
    /// <see cref="RefreshFamilyTypeNames"/>에서 비운다.</summary>
    private readonly Dictionary<string, IReadOnlyList<PipeAccessoryTypeParameterDto>> _typeLengthParameterCache = new(StringComparer.Ordinal);
    private string? _valveTypeDiameterParameterName;
    private string? _jointDiameterParameterName;
    private bool _jointDiameterParameterPinned;
    /// <summary>관 길이 파라미터를 사용자가 직접 골랐는지. true면 관 패밀리를 바꿔도 자동 추정으로 덮어쓰지 않는다.</summary>
    private bool _lengthParameterPinned;
    private bool _diameterParameterPinned;
    private bool _bendDiameterParameterPinned;
    private bool _bendAngleParameterPinned;
    private bool _teeDiameterParameterPinned;
    private bool _teeBranchDiameterParameterPinned;
    private bool _valveDiameterParameterPinned;
    private bool _isRefreshingParameterNames;
    private bool _isCreating;
    private readonly List<PipeConnectionFailure> _failures = [];
    private readonly HashSet<Guid> _errorEdgeIds = [];
    private int _inputValidationErrorCount;
    // 근처점(_snapNearest)만 기본 해제다(2026-09-16 사용자 지시). 근처점은 선 위 어디에나 걸려
    // 끝점·중간점·교차점을 가로채므로, 켜 두면 정확한 점을 집으려 할 때 오히려 방해가 된다.
    private bool _useAngleSnap = true, _isPreviewVisible, _snapReferencePoint = true, _snapEndpoint = true, _snapMidpoint = true, _snapQuadrant, _snapIntersection = true, _snapPerpendicular = true, _snapNearest, _isSnapMarkerVisible, _isCursorVisible;
    private double _previewX1, _previewY1, _previewX2, _previewY2, _previewLengthX, _previewLengthY, _snapMarkerX, _snapMarkerY, _cursorX, _cursorY;
    private double _subLineAX1, _subLineAY1, _subLineAX2, _subLineAY2, _subLineBX1, _subLineBY1, _subLineBX2, _subLineBY2;
    private bool _isSubLineVisible;
    private System.Windows.Media.Geometry? _subLineTicks;
    private double _splitMarkerX1, _splitMarkerY1, _splitMarkerX2, _splitMarkerY2;
    private bool _isSplitMarkerVisible;
    private Point? _selectionBoxStart;
    private PipeDragState? _drag;
    private bool _isSelectionBoxVisible, _isCrossingSelection;
    private double _selectionBoxX, _selectionBoxY, _selectionBoxWidth, _selectionBoxHeight;
    private string _previewLength = "0 mm";
    private string _previewAngle = "0.0°";
    private string _snapMarkerSymbol = "□";
    private string _snapMarkerLabel = "끝점";

    /// <summary>Revit 프로젝트 좌표(mm) 기준 원본 외곽. 캔버스 좌표는 기준점을 뺀 <see cref="_canvasOutline"/>이다.</summary>
    private ValveRoomOutline _outline = ValveRoomOutline.Empty;
    private ValveRoomOutline _canvasOutline = ValveRoomOutline.Empty;
    /// <summary>저장본에서 읽은 외곽 부재 Id(벽+기둥). 형상 재해석은 Revit 계층이 한다(docs/204 § D-2).
    /// 복원이 끝나도 비우지 않는다 — 재해석 실패 시 다음 저장에서 Id가 날아가면 안 된다.</summary>
    private IReadOnlyList<long> _savedOutlineElementIds = [];
    private double _canvasWidth, _canvasHeight;
    private bool _hasCanvasSize;
    // IN/OUT 이격 스칼라(_inOffsetMm/_outOffsetMm)와 편집 플래그는 포트 목록(Ports)으로 옮겼다(docs/172 § C-2).
    /// <summary>IN/OUT 화살표가 벽 내측면에 닿는 접점(캔버스 좌표, mm). 끝점 스냅 후보로 쓴다.</summary>
    private readonly List<DHBIMWATER.Core.Geometry.Point2D> _arrowAnchors = [];
    private bool _arrowFromTop;
    private bool _snapOutline = true;

    /// <summary>역가져오기로 캔버스에 올린 Revit 요소 Id(docs/158 § E-1). 생성할 때 전부 지우고 다시 만든다(docs/163 § B).</summary>
    private IReadOnlyList<long> _importedSourceIds = [];

    /// <summary>캔버스 Guid(엣지·인라인 부속·절점) → Revit ElementId(docs/179 § E-1).
    /// 모델 생성·역가져오기가 성공하면 통째로 교체된다.
    /// <para>캔버스를 편집해도 비우지 않는다 — 선을 옮겨도 Revit 부재는 그대로이고,
    /// "지금 이 선이 모델의 어느 부재였나"를 되짚는 것이 이 기능의 목적이기 때문이다.
    /// 그 사이 지워진 부재는 Revit 쪽에서 <c>doc.GetElement(id)</c>로 거른다.</para></summary>
    private Dictionary<Guid, IReadOnlyList<long>> _revitElementIds = [];

    /// <summary>이 창에서 직전에 생성한 Revit 부재 Id 전부(docs/235 § C). 다시 [모델 생성]하면 지우고 다시 만든다 —
    /// 종전에는 삭제 대상이 가져온 원본뿐이라 같은 배관이 한 벌 더 생겼다.
    /// <para>Undo 스냅샷에 넣지 않는다 — 캔버스가 아니라 Revit 모델 상태다. 캔버스 초기화·추천 배치·기존 배관 가져오기에서 잊는다.</para></summary>
    private IReadOnlyList<long> _createdElementIds = [];

    /// <summary>분할점 클릭을 기다리는 대상 엣지(docs/179 § H-3). null이면 분할 중이 아니다.</summary>
    private Guid? _splitTargetEdgeId;

    /// <summary>분할점이 선 끝·인라인 부속에서 떨어져 있어야 하는 최소 거리(mm).
    /// <see cref="PipeTopologyBuilder"/>의 부속 간 최소 이격(100mm)과 같은 값이다(docs/179 § H-4).</summary>
    private const double MinSplitOffsetMm = 100;

    /// <summary>"같은 자리"로 볼 거리(mm). 수동 분할이 만든 겹친 끝점을 찾는 데 쓴다(docs/181 § B).
    /// <see cref="PipeTopologyBuilder"/>의 기하 허용오차와 같은 값이다.</summary>
    private const double CoincidentNodeToleranceMm = 1e-6;

    /// <summary>분할 표식(선에 수직한 짧은 선분)의 반 길이(화면 px). 십자선 커서와 같이
    /// <b>화면 고정 크기</b>다 — 확대율이 달라도 표식 크기가 변하지 않아야 어디를 자르는지 읽기 쉽다.</summary>
    private const double SplitMarkerHalfPx = 12;

    /// <param name="settingsRepo">패밀리 설정 저장소(docs/175 § E). 생략하면(테스트) 이름 추정만으로 시작한다.
    /// 생성자는 Revit 커맨드 실행 중(API 컨텍스트)에 돌므로 여기서 Load해도 된다 — 이미 RefreshFamilyTypeNames가 같은 자리에서 Revit을 읽는다.</param>
    public PipeLayoutViewModel(IElementTypeQueryRepo typeRepo, IPipeLayoutFamilySettingsRepo? settingsRepo = null)
    {
        _typeRepo = typeRepo;
        _settingsRepo = settingsRepo;
        Levels = new ObservableCollection<LevelInfo>(_typeRepo.GetLevels());
        FamilyTypeNames = [];
        ValveFamilyNames = [];
        SegmentFamilyTypeNames = [];
        LengthParameterNames = [];
        BendParameterNames = [];
        TeeParameterNames = [];
        TeeBranchParameterNames = [];
        CrossParameterNames = [];
        CrossBranchParameterNames = [];
        WyeParameterNames = [];
        WyeBranchParameterNames = [];
        WyeAngleParameterNames = [];
        ValveBendFamilyTypeNames = [];
        ValveBendParameterNames = [];
        ValveTeeMainParameterNames = [];
        ValveTeeBranchParameterNames = [];
        BypassValveTypeNames = [];
        BypassValveParameterNames = [];
        ValveTypeParameterNames = [];
        BypassSetItems = [];
        ReducerLargeParameterNames = [];
        ReducerSmallParameterNames = [];
        ValveParameterNames = [];
        JointFamilyTypeNames = [];
        JointFamilyNames = [];
        JointTypeNames = [];
        JointParameterNames = [];
        Edges = [];
        Nodes = [];
        InlineFittings = [];
        SelectedEdgeIds = [];
        SelectedFittingIds = [];
        OutlineWalls = [];
        OutlineColumns = [];
        OutlineArrows = [];
        TempDimensions = [];
        Ports = [];
        // 기본 포트 = IN 좌 1 + OUT 우 1. 종전 화살표 2개 고정 동작과 같다(docs/172 § C-2).
        foreach (var port in PipePortDefaults.Standard()) AttachPort(new PipePortItem(port));
        UpdatePortRemovability();
        // UI 개편 — 캔버스 선택 속성 카드 표시/문구는 선택 목록 변화만 따라간다
        SelectedEdgeIds.CollectionChanged += (_, _) => NotifySelectionCardChanged();
        SelectedFittingIds.CollectionChanged += (_, _) => NotifySelectionCardChanged();
        // 생성 대기 중(IsCreating)에는 그래프·외곽을 바꾸는 명령을 막는다(docs/235 § D).
        PickOutlineCommand = new RelayCommand(_ => PickOutline(), _ => PickOutlineAction is not null && !IsCreating);
        PickPipingCommand = new RelayCommand(_ => PickPiping(), _ => PickPipingAction is not null && !IsCreating);
        ClearOutlineCommand = new RelayCommand(_ => ClearOutline(), _ => HasOutline);
        RemoveFittingCommand = new RelayCommand(x => RemoveFitting(x as InlineFittingItem));
        ToggleBypassPlacementCommand = new RelayCommand(_ => IsPlacingBypassSet = !IsPlacingBypassSet, _ => !IsCreating);
        FlipBypassSetCommand = new RelayCommand(x => FlipBypassSet(x as InlineFittingItem));
        SwapBypassValvesCommand = new RelayCommand(x => SwapBypassValves(x as InlineFittingItem));
        ApplyBypassValvesCommand = new RelayCommand(x => ApplyBypassValves(x as InlineFittingItem));
        ClearCommand = new RelayCommand(_ => Clear(), _ => !IsCreating);
        CancelDrawingCommand = new RelayCommand(_ => CancelDrawing(), _ => IsDrawing);
        UndoCommand = new RelayCommand(_ => Undo(), _ => CanUndo && !IsCreating);
        DeleteSelectedEdgeCommand = new RelayCommand(_ => DeleteSelectedEdge(), _ => HasSelection && !IsCreating);
        SelectModeCommand = new RelayCommand(_ => Mode = PipeLayoutMode.Selection);
        DrawModeCommand = new RelayCommand(_ => Mode = PipeLayoutMode.Drawing);
        DeleteSelectedFittingCommand = new RelayCommand(_ => DeleteSelectedFitting(), _ => HasSelection && !IsCreating);
        CloseCommand = new RelayCommand(_ => CloseAction?.Invoke());
        CreateModelCommand = new RelayCommand(_ => CreateModel(), _ => CanCreateModel());
        SelectInRevitCommand = new RelayCommand(_ => SelectInRevit(), _ => CanSelectInRevit());
        SplitEdgeCommand = new RelayCommand(_ => BeginSplitEdge(), _ => CanBeginSplitEdge());
        AddInPortCommand = new RelayCommand(_ => AddPort(PortFlow.In), _ => HasOutline);
        AddOutPortCommand = new RelayCommand(_ => AddPort(PortFlow.Out), _ => HasOutline);
        RemovePortCommand = new RelayCommand(x => RemovePort(x as PipePortItem), x => (x as PipePortItem)?.CanRemove == true);
        ApplyTemplateCommand = new RelayCommand(_ => ApplyTemplate(), _ => CanApplyTemplate);
        // 임시 도구(2026-09-18) — 캔버스를 프리셋 제출 양식 텍스트로 뽑는다. 유형별 기본값 확정 후 제거한다.
        ExportTemplateTextCommand = new RelayCommand(_ => OpenTemplateTextAction?.Invoke(BuildTemplateText()),
            _ => OpenTemplateTextAction is not null && _network.Edges.Count > 0);
        OpenFamilySettingsCommand = new RelayCommand(_ => OpenFamilySettingsAction?.Invoke(), _ => OpenFamilySettingsAction is not null && !IsCreating);
        // 수직분기(docs/301) — 관 패밀리·길이 파라미터가 있어야 단관을 쌓음.
        AddVerticalBranchCommand = new RelayCommand(_ => PickVerticalBranch(),
            _ => PickVerticalBranchAction is not null && !IsCreating && !string.IsNullOrWhiteSpace(SegmentFamilyTypeName) && !string.IsNullOrWhiteSpace(LengthParameterName));
        // 설정 창 숫자 칸(정척 길이·격자·기준점)에 변환 오류가 남아 있으면 VM은 옛 값이라 그 값이 저장된다 — 막는다(docs/239 § A).
        SaveFamilySettingsCommand = new RelayCommand(_ => SaveFamilySettings(), _ => SaveFamilySettingsAction is not null && !IsCreating && SettingsInputErrorCount == 0);
        // 추정 단계의 후보 갱신은 미룬다(docs/258 § B) — 저장값이 있으면 ApplyFamilySettings가 패밀리를 다 넣은 뒤 한 번만 읽는다.
        _parameterRefreshDeferral++;
        try { RefreshFamilyTypeNames(); }
        finally { _parameterRefreshDeferral--; }
        // 저장값은 이름 추정 위에 덮어쓴다 — 추정은 저장값이 없는 칸에만 남는다(docs/175 D6).
        if (_settingsRepo?.Load() is { } saved) ApplyFamilySettings(saved, fromStorage: true);
        FlushDeferredParameterRefresh();
        ApplyStartupDefaults();
    }

    /// <summary>밸브실 유형 기본값. 외곽 벽 DH_Zone이 있으면 외곽 피킹이 덮는다.</summary>
    private const string DefaultValveRoomZone = "제수밸브실";

    /// <summary>창 열 때 빈 칸 기본값 보강(2026-11-16 사용자 지시 "기본값 다 넣어").
    /// 저장값·외곽 자동값이 먼저고, 그래도 빈 칸만 채운다.
    /// - 밸브실 유형: 빈 값이면 제수밸브실 → 추천 배치·격점번호 기본값이 바로 동작. 종전(docs/161 확정 2)엔 사용자가 직접 골라야 했음.
    /// - 기준 레벨: 비었으면 EL 0(내부 원점)에 가장 가까운 레벨, 같으면 낮은 쪽. 외곽 피킹 시 벽 기준 레벨이 덮는다(ApplyAutomaticElevationLevel).</summary>
    private void ApplyStartupDefaults()
    {
        if (string.IsNullOrWhiteSpace(_valveRoomZone)) ValveRoomZone = DefaultValveRoomZone;
        if (_elevationLevel is null && Levels.Count > 0)
        {
            ElevationLevel = Levels.OrderBy(level => Math.Abs(level.ElevationMm)).ThenBy(level => level.ElevationMm).First();
            // 저장 레벨이 문서에 없던 경우 — 종전엔 비워 두고 재선택 요구. 이제 기본 레벨로 대체하고 알림만.
            if (_savedElevationLevelMissing) Status = $"저장된 기준 레벨 없음 — '{ElevationLevel.Name}'(으)로 대체";
        }
    }

    public CanvasModelTransform Transform { get; } = new();
    public ObservableCollection<PipeEdgeItem> Edges { get; }
    public ObservableCollection<PipeNodeItem> Nodes { get; }
    public ObservableCollection<InlineFittingItem> InlineFittings { get; }
    public ObservableCollection<Guid> SelectedEdgeIds { get; }
    public ObservableCollection<Guid> SelectedFittingIds { get; }
    /// <summary>선택 패밀리 유형. 값은 "패밀리명 : 유형명" 전체 키.</summary>
    public ObservableCollection<string> FamilyTypeNames { get; }
    /// <summary>배관 밸브류 패밀리 목록. DN은 패밀리명에서 읽는다.</summary>
    public ObservableCollection<FittingFamilyItem> ValveFamilyNames { get; }
    /// <summary>관·곡관·T형 콤보의 공용 배관 부속류 목록.</summary>
    public ObservableCollection<string> SegmentFamilyTypeNames { get; }
    /// <summary>선택된 관 패밀리의 인스턴스 파라미터 후보.</summary>
    public ObservableCollection<string> LengthParameterNames { get; }
    public ObservableCollection<string> BendParameterNames { get; }
    public ObservableCollection<string> TeeParameterNames { get; }
    public ObservableCollection<string> TeeBranchParameterNames { get; }
    /// <summary>십자관·이경관·편락관 인스턴스 파라미터 후보(docs/175 § B-5). 이경·편락은 큰 쪽·작은 쪽이 한 목록을 공유한다.</summary>
    public ObservableCollection<string> CrossParameterNames { get; }
    public ObservableCollection<string> CrossBranchParameterNames { get; }
    /// <summary>Y형관 인스턴스 파라미터 후보(docs/187 § 3-4). 주관·분기·분기각이 같은 패밀리에서 나온다.</summary>
    public ObservableCollection<string> WyeParameterNames { get; }
    public ObservableCollection<string> WyeBranchParameterNames { get; }
    public ObservableCollection<string> WyeAngleParameterNames { get; }
    /// <summary>밸브부관 A·B 기본값 추정용 후보. 배관 부속류 중 <see cref="PipeValveBend.FamilyKeyword"/>가 든 것만 모은다.
    /// <para>콤보 자체는 부속류 전체(<see cref="SegmentFamilyTypeNames"/>)를 보여 준다(2026-09-28 사용자 요청).
    /// 단, 역가져오기·재료표는 같은 키워드로 밸브부관을 판정하므로 키워드 없는 패밀리를 고르면 되가져올 때 곡관·십자로 오인된다.</para></summary>
    public ObservableCollection<string> ValveBendFamilyTypeNames { get; }
    public ObservableCollection<string> ValveBendParameterNames { get; }
    /// <summary>밸브부관 A 인스턴스 파라미터 후보. 주관·분기 콤보가 컬렉션을 따로 갖는다(docs/232 § B) —
    /// 한 컬렉션을 여러 콤보가 공유하면 갱신 때 <c>Clear()</c>마다 WPF 콤보가 다른 칸의 선택을 null로 밀어낸다.
    /// 수평·수직분기 예/아니오는 콤보가 없고(docs/232 § A-4) 주관 목록에서 추정만 한다.</summary>
    public ObservableCollection<string> ValveTeeMainParameterNames { get; }
    public ObservableCollection<string> ValveTeeBranchParameterNames { get; }
    /// <summary>이경관 큰 쪽·작은 쪽 후보. 밸브부관 A와 같은 이유로 콤보마다 컬렉션을 따로 둔다(docs/232 § B).</summary>
    public ObservableCollection<string> ReducerLargeParameterNames { get; }
    public ObservableCollection<string> ReducerSmallParameterNames { get; }
    public ObservableCollection<string> ValveParameterNames { get; }
    /// <summary>4번 섹션 밸브 유형의 길이형 <b>유형 매개변수</b> 후보(docs/233 § B-3). 확인용 — 값을 쓰지 않는다.</summary>
    public ObservableCollection<string> ValveTypeParameterNames { get; }
    /// <summary>4번 섹션 밸브 유형 직경 매개변수(docs/233 § B-3). 패밀리별 사전(<see cref="_valveTypeDiameterParameterByFamily"/>)에 기억한다(세션 한정).</summary>
    public string? ValveTypeDiameterParameterName
    {
        get => _valveTypeDiameterParameterName;
        set
        {
            if (!SetProperty(ref _valveTypeDiameterParameterName, value)) return;
            if (!_isRefreshingParameterNames) RememberValveTypeDiameterParameter(CurrentValveFamilyTypeName, value);
            NotifyTypeDiameterChanged();
        }
    }
    /// <summary>밸브 유형 직경 매개변수 값과 관경(선택 관경 또는 기본 관경)의 대조 결과 문구(docs/233 § B-3).</summary>
    public string ValveTypeDiameterText => TypeDiameterText(CurrentValveFamilyTypeName, _valveTypeDiameterParameterName, DiameterMm, out _);
    public bool IsValveTypeDiameterMismatch { get { TypeDiameterText(CurrentValveFamilyTypeName, _valveTypeDiameterParameterName, DiameterMm, out var mismatch); return mismatch; } }
    /// <summary>이음관 패밀리 후보. 배관 이음쇠 목록에서 <see cref="InlineJoint.FamilyKeyword"/>가 든 것만 남긴다.
    /// <para>키워드로 거르는 이유가 표시상의 편의가 아니다 — 역가져오기가 같은 키워드로 이음관을 판정하므로
    /// 키워드 없는 패밀리를 고를 수 있게 두면 되가져올 때 직관으로 오인된다(docs/167 § E-1).</para></summary>
    public ObservableCollection<string> JointFamilyTypeNames { get; }
    /// <summary>이음관 패밀리명 목록(유형명 제외). 1단 콤보가 본다(docs/200).</summary>
    // TODO(docs/200): 필요하면 밸브처럼 관경 일치 DN 굵게 표시를 붙인다.
    public ObservableCollection<string> JointFamilyNames { get; }
    /// <summary>고른 이음관 패밀리의 "패밀리명 : 유형명" 목록. 2단 리스트가 본다.
    /// <para><see cref="JointFamilyTypeNames"/>는 전 패밀리를 담는 원본이라 그대로 두고, 화면에는 이 걸러낸 목록을 준다
    /// (밸브의 <c>_accessoryFamilyTypeNames</c> ↔ <c>FamilyTypeNames</c> 관계와 같다).</para></summary>
    public ObservableCollection<string> JointTypeNames { get; }
    /// <summary>선택된 이음관 패밀리의 인스턴스 파라미터 후보(`이음관_구경`).</summary>
    public ObservableCollection<string> JointParameterNames { get; }
    public ObservableCollection<OutlineWallItem> OutlineWalls { get; }
    /// <summary>함께 가져온 기둥의 화면 표시(docs/184). 간섭 확인용이라 그리기·스냅에만 쓰고 생성 대상은 아니다.</summary>
    public ObservableCollection<OutlineColumnItem> OutlineColumns { get; }
    public ObservableCollection<OutlineArrowItem> OutlineArrows { get; }
    public ObservableCollection<TempDimensionItem> TempDimensions { get; }
    /// <summary>Revit 계층이 재해석할 대상 Id. 비어 있으면 복원할 외곽이 없다.</summary>
    public IReadOnlyList<long> SavedOutlineElementIds => _savedOutlineElementIds;
    public ICommand PickOutlineCommand { get; }
    public ICommand PickPipingCommand { get; }
    public ICommand ClearOutlineCommand { get; }
    public ICommand RemoveFittingCommand { get; }
    /// <summary>바이패스 세트 배치 모드 켜기/끄기(docs/228 § B).</summary>
    public ICommand ToggleBypassPlacementCommand { get; }
    /// <summary>세트 목록의 [방향 뒤집기] — 바이패스를 본관 반대쪽으로 옮긴다.</summary>
    public ICommand FlipBypassSetCommand { get; }
    /// <summary>세트 목록의 [순서 바꾸기] — 밸브 2개 세트의 V1·V2를 맞바꾼다(docs/255 § D-4).</summary>
    public ICommand SwapBypassValvesCommand { get; }
    /// <summary>세트 목록의 [밸브 적용] — 지금 고른 바이패스 밸브 개수·패밀리·단관 길이를 그 세트에 넣는다. 유형·관경은 모델 생성 때 정한다(docs/244 § D-2, docs/255 § D-3).</summary>
    public ICommand ApplyBypassValvesCommand { get; }
    /// <summary>바이패스 밸브(B–B 사이) 유형 후보. 화면에는 없다(docs/244 § D-1) — 인스턴스 직경 파라미터 후보를 읽을 대표 유형을 고르는 내부용이다.</summary>
    public ObservableCollection<string> BypassValveTypeNames { get; }
    /// <summary>캔버스에 놓인 바이패스 세트 목록(오른쪽 패널 5번).</summary>
    public ObservableCollection<InlineFittingItem> BypassSetItems { get; }

    public string? BypassValveFamilyName
    {
        get => _bypassValveFamilyName;
        set
        {
            if (!SetProperty(ref _bypassValveFamilyName, value)) return;
            RefreshBypassValveTypeNames();
            // V2 기본값은 V1과 같은 패밀리다(docs/255 § 0).
            if (string.IsNullOrWhiteSpace(_bypassSecondValveFamilyName)) BypassSecondValveFamilyName = value;
        }
    }

    /// <summary>B–B 사이 밸브 개수(1 또는 2, docs/255 § D-1). 배치 패널과 [밸브 적용]이 쓴다.</summary>
    public int BypassValveCount
    {
        get => _bypassValveCount;
        set
        {
            if (!SetProperty(ref _bypassValveCount, value == 2 ? 2 : 1)) return;
            OnPropertyChanged(nameof(IsBypassSingleValve));
            OnPropertyChanged(nameof(IsBypassTwoValves));
        }
    }
    /// <summary>라디오 버튼 바인딩용(밸브 1개).</summary>
    public bool IsBypassSingleValve { get => _bypassValveCount == 1; set { if (value) BypassValveCount = 1; } }
    /// <summary>라디오 버튼 바인딩용(밸브 2개 + 사이 단관). 켜지면 V2·단관 칸이 보인다.</summary>
    public bool IsBypassTwoValves { get => _bypassValveCount == 2; set { if (value) BypassValveCount = 2; } }

    /// <summary>밸브 2개 세트의 V2(엣지 끝 쪽) 패밀리(docs/255). 직경 파라미터는 4번 섹션과 같은 패밀리별 사전을 쓴다(G-4).</summary>
    public string? BypassSecondValveFamilyName
    {
        get => _bypassSecondValveFamilyName;
        set => SetProperty(ref _bypassSecondValveFamilyName, value);
    }

    /// <summary>밸브 2개 세트의 V1–V2 사이 단관 길이(mm). 0 = 직결, 아니면 관 최소 조각 이상(docs/255 G-2).</summary>
    public double BypassSpoolLengthMm
    {
        get => _bypassSpoolLengthMm;
        set
        {
            if (!BypassSet.IsValidSpoolLength(value))
                throw new ArgumentOutOfRangeException(nameof(value), FormattableString.Invariant(
                    $"단관 길이는 0(직결) 또는 {PipeSegmentPlan.MinSegmentMm:0}mm 이상이어야 합니다."));
            SetProperty(ref _bypassSpoolLengthMm, value);
        }
    }

    /// <summary>패널 값으로 만든 밸브 구성(docs/255 § D-3). 2개인데 V2가 비었으면 오류 문구를 돌려준다.</summary>
    private (string? Second, double SpoolMm, string? Error) BypassValvePanelConfig()
    {
        if (_bypassValveCount != 2) return (null, 0, null);
        if (string.IsNullOrWhiteSpace(_bypassSecondValveFamilyName)) return (null, 0, "바이패스 밸브 2 패밀리 선택 필요");
        return (_bypassSecondValveFamilyName, _bypassSpoolLengthMm, null);
    }
    /// <summary>바이패스 밸브 대표 유형(패밀리 첫 유형). 내부용 — 직경 파라미터 후보를 읽고, 세트 배치 모드의 "놓을 대상 있음" 판단
    /// (<see cref="PlacingFamilyTypeName"/>)에 쓴다. 세트에는 패밀리명만 싣고 실제 유형은 모델 생성 때 DN으로 고른다(docs/244).</summary>
    public string? BypassValveTypeName
    {
        get => _bypassValveTypeName;
        set { if (SetProperty(ref _bypassValveTypeName, value)) { RefreshBypassValveParameterNames(); OnPropertyChanged(nameof(IsPlacingFitting)); NotifyValidationChanged(); } }
    }
    /// <summary>바이패스 밸브 인스턴스 파라미터 후보(docs/232 § C).</summary>
    public ObservableCollection<string> BypassValveParameterNames { get; }
    /// <summary>바이패스 밸브 직경 파라미터(docs/232 § C). 4번 섹션 밸브와 같은 패밀리별 사전(<see cref="_valveDiameterParameterByFamily"/>)을
    /// 공유한다 — 배치 경계 <c>ValveDiameterParameterNameFor</c>가 그 사전을 패밀리명으로 찾는다. 사전에 없으면 이제 다른 패밀리 값을 빌리지 않는다.</summary>
    public string? BypassValveDiameterParameterName
    {
        get => _bypassValveDiameterParameterName;
        set
        {
            if (!SetProperty(ref _bypassValveDiameterParameterName, value)) return;
            // 유형이 비면 ValveFamilyKey가 4번 섹션 패밀리로 내려가므로 기억하지 않는다.
            if (!_isRefreshingParameterNames && !string.IsNullOrWhiteSpace(BypassValveTypeName)) RememberValveDiameterParameter(BypassValveTypeName, value);
            NotifyValidationChanged();
        }
    }
    // 바이패스 관경 입력·바이패스 밸브 유형 직경 대조는 없앴다(docs/244 § D) — 관경은 모델 생성 때 밸브부관 B가 본관 구경으로 정하고,
    // 밸브 유형은 그 관경과 DN이 같은 것을 고른다.

    /// <summary>바이패스 세트 배치 모드. 켜면 그리기 모드로 바꾸고 밸브·이음관 배치 선택을 비운다 — 배치 대상은 하나다.</summary>
    public bool IsPlacingBypassSet
    {
        get => _isPlacingBypassSet;
        set
        {
            if (_isPlacingBypassSet == value) return;
            _isPlacingBypassSet = value;
            if (value)
            {
                if (IsDrawing) CancelDrawing();
                Mode = PipeLayoutMode.Drawing;
                if (_selectedFamilyTypeName is not null) SelectedFamilyTypeName = null;
                if (_selectedJointFamilyTypeName is not null) SelectedJointFamilyTypeName = null;
                Status = string.IsNullOrWhiteSpace(BypassValveFamilyName)
                    ? "바이패스 밸브 패밀리 선택 필요 — [부속] 탭 바이패스 칸 · Esc 종료"
                    : "본관 클릭 → 바이패스 세트 (커서 쪽 분기) · Esc 종료";
            }
            else HideFittingPreview();
            OnPropertyChanged(nameof(IsPlacingBypassSet));
            OnPropertyChanged(nameof(BypassPlacementButtonText));
            OnPropertyChanged(nameof(IsPlacingFitting));
            OnPropertyChanged(nameof(InteractionModeText));
        }
    }
    public string BypassPlacementButtonText => IsPlacingBypassSet ? "세트 배치 끝내기 (Esc)" : "세트 배치 (본관 위 클릭)";
    public System.Windows.Media.Geometry? BypassPreviewLine { get => _bypassPreviewLine; private set => SetProperty(ref _bypassPreviewLine, value); }
    public System.Windows.Media.Geometry? BypassPreviewMarkers { get => _bypassPreviewMarkers; private set => SetProperty(ref _bypassPreviewMarkers, value); }
    public bool IsBypassPreviewVisible { get => _isBypassPreviewVisible; private set => SetProperty(ref _isBypassPreviewVisible, value); }
    public ICommand ClearCommand { get; }
    public ICommand CancelDrawingCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand DeleteSelectedEdgeCommand { get; }
    public ICommand SelectModeCommand { get; }
    public ICommand DrawModeCommand { get; }
    public ICommand DeleteSelectedFittingCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand CreateModelCommand { get; }
    /// <summary>고른 배관·부속을 Revit에서 선택하고 해당 뷰로 옮긴다(docs/179 § E-2).</summary>
    public ICommand SelectInRevitCommand { get; }
    /// <summary>고른 선 하나를 나눌 지점 지정을 시작한다(docs/179 § H-3).</summary>
    public ICommand SplitEdgeCommand { get; }
    /// <summary>IN 포트 추가(좌측 벽). 외곽이 있을 때만(docs/172 § C-4).</summary>
    public ICommand AddInPortCommand { get; }
    /// <summary>OUT 포트 추가(우측 벽).</summary>
    public ICommand AddOutPortCommand { get; }
    /// <summary>포트 삭제. 파라미터는 <see cref="PipePortItem"/>. 같은 흐름의 마지막 1개는 지울 수 없다.</summary>
    public ICommand RemovePortCommand { get; }
    /// <summary>밸브실 유형의 추천 배치를 캔버스에 그린다. 기존 배관은 대체되며 Undo로 되돌린다(docs/172 § C-11).</summary>
    public ICommand ApplyTemplateCommand { get; }
    /// <summary>임시 도구(2026-09-18) — 캔버스 내용을 프리셋 제출 양식 텍스트로 보여 준다. 유형별 기본값 확정 후 제거한다.</summary>
    public ICommand ExportTemplateTextCommand { get; }
    /// <summary>임시 도구(2026-09-18) — 만든 텍스트를 띄울 창. View가 넣는다.</summary>
    public Action<string>? OpenTemplateTextAction { get; set; }
    /// <summary>패밀리 설정 창을 연다(docs/175 § E-5). View가 창을 띄우므로 Action으로 위임한다.</summary>
    public ICommand OpenFamilySettingsCommand { get; }
    /// <summary>6번 섹션 [수직분기 추가…](docs/301 § A).</summary>
    public ICommand AddVerticalBranchCommand { get; }
    /// <summary>Revit 피킹 왕복. 결과는 <see cref="ApplyVerticalBranchStart"/>로 돌아옴.</summary>
    public Action? PickVerticalBranchAction { get; set; }
    /// <summary>입면 창(모달)을 띄움. [Revit에 배치]면 true. View가 넣음.</summary>
    public Func<VerticalBranchViewModel, bool>? OpenVerticalBranchDialog { get; set; }
    /// <summary>배치 요청을 ExternalEvent로 보냄. 결과는 <see cref="ApplyVerticalBranchResult"/>.</summary>
    public Action<VerticalBranchRequest>? PlaceVerticalBranchAction { get; set; }
    /// <summary>6번 섹션 최근 결과 1줄.</summary>
    public string VerticalBranchLastResult { get => _verticalBranchLastResult; private set => SetProperty(ref _verticalBranchLastResult, value); }
    private string _verticalBranchLastResult = "";
    /// <summary>현재 패밀리·파라미터 매핑을 프로젝트에 저장한다(docs/175 § E-6). 모델리스 창이라 ExternalEvent로 보낸다.</summary>
    public ICommand SaveFamilySettingsCommand { get; }
    public Action? OpenFamilySettingsAction { get; set; }
    /// <summary>선택 모드에서 선이 선택됐을 때(클릭·박스) View가 "3. 배관 기준값"을 펼치고 관경 칸에 포커스를 준다(docs/233 § A).
    /// 부속만 선택되면 부르지 않는다.</summary>
    public Action? EdgeSelectedAction { get; set; }
    public Action<PipeLayoutFamilySettings>? SaveFamilySettingsAction { get; set; }
    /// <summary>패밀리 설정 창을 닫는다. 창이 열려 있는 동안만 View가 넣어 둔다(docs/175 § E-6 보완).
    /// [저장]은 창을 닫아야 한다 — 모달 창(ShowDialog)이 떠 있는 동안 Revit은 유휴 상태가 아니라서 ExternalEvent가 실행되지 않고,
    /// 창을 닫은 뒤에야 저장 요청이 처리된다.</summary>
    public Action? CloseFamilySettingsAction { get; set; }
    public Action? CloseAction { get; set; }
    public Action<PipeNetworkDefinition>? CreateModelAction { get; set; }
    /// <summary>고른 부재를 Revit에서 선택·표시하도록 요청한다. 위임이 없으면 버튼이 비활성이다(docs/179 § E-2).</summary>
    public Action<IReadOnlyList<long>>? SelectInRevitAction { get; set; }
    /// <summary>타이밸브실 최소 폭 실측(docs/323). 본관 DN·관종·강관 등급 → 벽 여유 c·최소 폭. 프로브 놓고 롤백 — API 컨텍스트 필요.
    /// null이면 실측 안 함(비율 0.25/0.75 배치 그대로, 테스트·Sandbox).</summary>
    public Func<double, PipeMaterial, SteelFittingClass, TieValveRoomLayout>? MeasureTieLayout { get; set; }
    /// <summary>모델리스 창이라 실측을 ExternalEvent로 돌림(ValveRoomCommand와 같은 RevitActionHandler 경로). null이면 바로 실행.</summary>
    public Action<Action>? RunInRevitContext { get; set; }
    /// <summary>Revit 뷰에서 외곽 벽체 피킹을 시작한다. 결과는 <see cref="ApplyOutline"/>으로 되돌아온다.</summary>
    public Action? PickOutlineAction { get; set; }
    /// <summary>Revit 뷰에서 기존 배관 피킹을 시작한다. 결과는 <see cref="ApplyPickedPiping"/>으로 되돌아온다(docs/158).</summary>
    public Action? PickPipingAction { get; set; }
    /// <summary>역가져오기로 올린 원본이 남아 있는지. 안내 문구 표시 조건이자 생성 시 삭제 대상 유무다(docs/163 § B).</summary>
    public bool HasImportedSource => _importedSourceIds.Count > 0;
    /// <summary>생성 시 지우고 다시 만들 부재(가져온 원본 또는 직전 생성분)가 있는가 — 하단 안내 문구(docs/235 § C).</summary>
    public bool HasReplaceTarget => _importedSourceIds.Count > 0 || _createdElementIds.Count > 0;
    public PipeElevationMode ElevationMode
    {
        get => _elevationMode;
        set
        {
            if (!SetProperty(ref _elevationMode, value)) return;
            OnPropertyChanged(nameof(IsRelativeElevation));
            OnPropertyChanged(nameof(IsAbsoluteElevation));
            if (value == PipeElevationMode.Relative) RecalculateElevationOffsetFromCurrent();
            NotifyValidationChanged();
        }
    }
    public bool IsRelativeElevation { get => ElevationMode == PipeElevationMode.Relative; set { if (value) ElevationMode = PipeElevationMode.Relative; } }
    public bool IsAbsoluteElevation { get => ElevationMode == PipeElevationMode.Absolute; set { if (value) ElevationMode = PipeElevationMode.Absolute; } }
    public ObservableCollection<LevelInfo> Levels { get; }
    public LevelInfo? ElevationLevel
    {
        get => _elevationLevel;
        set
        {
            if (!SetProperty(ref _elevationLevel, value)) return;
            if (ElevationMode == PipeElevationMode.Relative && value is not null) SetElevationSilently(value.ElevationMm + ElevationOffsetMm);
            NotifyValidationChanged();
        }
    }
    public double ElevationOffsetMm
    {
        get => _elevationOffsetMm;
        set
        {
            if (!SetProperty(ref _elevationOffsetMm, value)) return;
            // m 칸도 같이 알림. 빠지면 코드로 넣은 값이 Offset 칸에 안 뜸.
            OnPropertyChanged(nameof(ElevationOffsetM));
            if (ElevationMode == PipeElevationMode.Relative && ElevationLevel is not null) SetElevationSilently(ElevationLevel.ElevationMm + value);
            NotifyValidationChanged();
        }
    }
    public double ElevationOffsetM { get => ElevationOffsetMm / 1000d; set => ElevationOffsetMm = value * 1000d; }
    public string ElevationLevelHint { get => _elevationLevelHint; private set { if (SetProperty(ref _elevationLevelHint, value)) OnPropertyChanged(nameof(IsElevationLevelHintVisible)); } }
    public bool IsElevationLevelHintVisible => !string.IsNullOrWhiteSpace(ElevationLevelHint);
    public double Elevation { get => _elevation; set { if (SetProperty(ref _elevation, value)) { _network.Elevation = value; OnPropertyChanged(nameof(ElevationM)); if (ElevationMode == PipeElevationMode.Relative && ElevationLevel is not null) SetOffsetFromCurrentElevation(); NotifyValidationChanged(); } } }
    /// <summary>관 중심 높이(EL) 입력 칸 — m 단위(docs/251 § C). 내부·저장·Revit 배치는 <see cref="Elevation"/>(mm) 그대로다.</summary>
    public double ElevationM { get => Elevation / 1000d; set => Elevation = value * 1000d; }
    /// <summary>격점상세도 배관재료표에 쓰는 격점 식별자(docs/131 § B-1). 필수 입력이 아니다.
    /// <para>이 setter는 창의 TextBox(TwoWay)만 부른다 — 값이 들어오면 사용자 편집으로 본다.
    /// 자동 채움은 setter를 거치지 않고 <see cref="ApplyDefaultJunctionId"/>가 백킹 필드에 직접 넣는다(docs/177 § B-3).
    /// 사용자가 <b>비운</b> 것도 편집이다. 지웠는데 유형을 건드렸다고 다시 채워지면 안 된다.</para></summary>
    public string JunctionId
    {
        get => _junctionId;
        set { if (SetProperty(ref _junctionId, value ?? string.Empty)) _junctionIdUserEdited = true; }
    }
    /// <summary>관 재료. `DH_관종`으로 기록되고, 직관·단관의 `DH_비고`(관 재료 코드)를 구동한다(docs/141 § B).</summary>
    public PipeMaterial PipeMaterial
    {
        get => _pipeMaterial;
        set
        {
            if (!SetProperty(ref _pipeMaterial, value)) return;
            OnPropertyChanged(nameof(IsDuctileIronSelected));
            OnPropertyChanged(nameof(IsSteelSelected));
            EnsurePipeKindForMaterial();
            NotifyJointConnectionLabels();
            // 관종 바뀌면 관·곡관·T·Y·십자·이경 칸도 그 관종 패밀리로(2026-10-30 사용자 결정). 가져오기 중엔 가져온 값 우선.
            if (!_isApplyingImport) ApplyJointFamilyDefaults();
        }
    }

    /// <summary>기존 객체 가져오기가 관종을 바꾸는 중. 이때는 관종 기본 패밀리로 덮지 않는다 — 뒤이어 가져온 패밀리를 고른다.</summary>
    private bool _isApplyingImport;

    /// <summary>강관 관 등급 콤보의 "DN별 기본" 항목 — DN≤300 STWW 290, 그 밖 STWW 400 A(<see cref="PipeKindCatalog.DefaultSteelGrade"/>).</summary>
    public const string AutoSteelGradeLabel = "자동 (DN별 기본)";

    /// <summary>관 등급 콤보 항목(docs/260 § C). 직관·단관 단위중량(`DH_무게`) 조회 키이며 `DH_관종등급`에 기록된다.
    /// 등급명은 정식 명칭 그대로 둔다(`STWW 400 A`).</summary>
    public IReadOnlyList<string> PipeKindLabels => _pipeMaterial == PipeMaterial.Steel
        ? new[] { AutoSteelGradeLabel }.Concat(PipeKindCatalog.SteelAll).ToList()
        : PipeKindCatalog.AllFor(_pipeMaterial);

    public string PipeKindLabel
    {
        get => _pipeKind.Length == 0 ? AutoSteelGradeLabel : _pipeKind;
        set
        {
            var kind = value == AutoSteelGradeLabel ? string.Empty : value;
            if (!PipeKindLabels.Contains(value ?? string.Empty) || _pipeKind == kind) return;
            _pipeKind = kind;
            OnPropertyChanged();
        }
    }

    /// <summary>관종이 바뀌어 현재 등급이 목록에 없으면 기본값으로 — 주철 `상수 1종관`, 강관 "DN별 기본".</summary>
    private void EnsurePipeKindForMaterial()
    {
        if (!PipeKindLabels.Contains(PipeKindLabel))
            _pipeKind = _pipeMaterial == PipeMaterial.Steel ? string.Empty : PipeKindCatalog.AllFor(_pipeMaterial).FirstOrDefault() ?? string.Empty;
        OnPropertyChanged(nameof(PipeKindLabels));
        OnPropertyChanged(nameof(PipeKindLabel));
    }

    /// <summary>강관은 소켓 대신 용접이라 접합 라벨 어휘가 바뀐다(docs/211 § I). 저장 값(<see cref="BendConnection"/>)은 그대로다.
    /// <para>관종이 바뀌면 강관 등급 콤보 활성 여부와 바이패스 세트 핸드북 관경도 바뀐다(docs/256 § D-2).</para></summary>
    private void NotifyJointConnectionLabels()
    {
        OnPropertyChanged(nameof(JointConnectionLabels));
        OnPropertyChanged(nameof(JointConnectionLabel));
        OnPropertyChanged(nameof(IsSteelFittingClassEnabled));
        RefreshFittings();
    }

    /// <summary>강관 밸브부관 등급(docs/256). 바이패스 지관 규격표 조회에 쓰고, 프로젝트 설정에 저장한다.
    /// 관로 모델링의 곡관 등급(<c>ActiveSteelFittingClass</c>)과는 따로 둔다(2026-10-01 사용자 결정).</summary>
    public SteelFittingClass SteelFittingClass
    {
        get => _steelFittingClass;
        set
        {
            var normalized = ValveBypassSpecTable.NormalizeSteelClass(value);
            if (!SetProperty(ref _steelFittingClass, normalized)) return;
            OnPropertyChanged(nameof(SteelFittingClassLabel));
            RefreshFittings();
        }
    }

    /// <summary>강관 등급 콤보 항목. <see cref="SteelFittingClass.None"/>은 넣지 않는다.</summary>
    public IReadOnlyList<string> SteelFittingClassLabels { get; } =
        [nameof(SteelFittingClass.F12), nameof(SteelFittingClass.F15), nameof(SteelFittingClass.F20)];

    /// <summary>콤보 바인딩용 라벨. 라벨을 못 되돌리면 현재 값을 유지한다(관종 콤보와 같은 안전판).</summary>
    public string SteelFittingClassLabel
    {
        get => _steelFittingClass.ToString();
        set
        {
            if (Enum.TryParse<SteelFittingClass>(value, out var parsed) && parsed != SteelFittingClass.None)
                SteelFittingClass = parsed;
        }
    }

    /// <summary>강관일 때만 등급을 고른다.</summary>
    public bool IsSteelFittingClassEnabled => _pipeMaterial == PipeMaterial.Steel;

    /// <summary>현재 관종·등급의 핸드북 지관 관경(docs/256). 규격이 없으면 null.</summary>
    private double? HandbookBypassDiameterMm(double mainMm)
        => ValveBypassSpecTable.BranchDiameterMm(_pipeMaterial, _steelFittingClass, mainMm);

    /// <summary>캔버스 개략 그림용 바이패스 관경 — 핸드북 값, 없으면 그림만 그리려고 옛 추정값을 쓴다(docs/256 § D-3).</summary>
    private double BypassPreviewDiameterMm(double mainMm)
        => HandbookBypassDiameterMm(mainMm) ?? BypassSet.PreviewBypassDiameterMm(mainMm);
    /// <summary>관종 토글(docs/263 § C). 주철관·강관만 고른다 — PE·PVC는 규격 자료가 없어 노출하지 않는다(<see cref="PipeMaterial"/> 주석).
    /// 세터는 <see cref="PipeMaterial"/>을 거쳐 관 등급·접합 라벨·강관 등급 가시성을 함께 맞춘다.
    /// 모양은 계획관로 창과 같다(PipeAlignmentModelingViewModel.IsDuctileIronSelected).</summary>
    public bool IsDuctileIronSelected { get => _pipeMaterial == PipeMaterial.DuctileIron; set { if (value) PipeMaterial = PipeMaterial.DuctileIron; } }
    public bool IsSteelSelected { get => _pipeMaterial == PipeMaterial.Steel; set { if (value) PipeMaterial = PipeMaterial.Steel; } }
    /// <summary>이 배치의 기본 접합 방식(docs/170 § E). 부재가 자기 패밀리 파라미터로 접합을 밝히면 그쪽이 이긴다.</summary>
    public BendConnection JointConnection { get => _jointConnection; set => SetProperty(ref _jointConnection, value); }

    /// <summary>접합 종류 콤보 항목.
    /// <para>계획관로 창은 같은 축을 `소켓곡관`·`플랜지곡관`으로 부르지만(<c>BendConnectionOptions</c>),
    /// 여기는 곡관만이 아니라 직관·단관·T형에도 걸리므로 곡관을 뺀 어휘를 쓴다. <b>라벨만 다르고 저장되는
    /// 값은 같은 <see cref="BendConnection"/></b>이라 두 창이 갈릴 여지는 없다.</para></summary>
    /// <para>강관은 소켓 접합이 없고 <see cref="BendConnection.Socket"/>이 용접을 뜻하므로(강관+소켓=용접합수) 라벨만 "용접"으로 보인다(docs/211 § I).</para>
    public IReadOnlyList<string> JointConnectionLabels => _pipeMaterial == PipeMaterial.Steel ? ["플랜지", "용접"] : ["플랜지", "소켓"];

    /// <summary>콤보 바인딩용 라벨. 라벨을 못 되돌리면 현재 값을 유지한다(관종 콤보와 같은 안전판).</summary>
    public string JointConnectionLabel
    {
        get => _jointConnection == BendConnection.Socket ? (_pipeMaterial == PipeMaterial.Steel ? "용접" : "소켓") : "플랜지";
        set
        {
            var connection = string.Equals(value, "소켓", StringComparison.Ordinal) || string.Equals(value, "용접", StringComparison.Ordinal) ? BendConnection.Socket
                : string.Equals(value, "플랜지", StringComparison.Ordinal) ? BendConnection.Flanged
                : (BendConnection?)null;
            if (connection is not { } resolved || resolved == _jointConnection) return;
            _jointConnection = resolved;
            OnPropertyChanged();
            OnPropertyChanged(nameof(JointConnection));
            ApplyJointFamilyDefaults();
        }
    }


    /// <summary>밸브실 유형 선택지. 빈 항목은 "기록하지 않음"이다(docs/161 § F-1).
    /// 값은 밸브실 빌더가 <c>DH_Zone</c>에 찍는 문자열과 <b>글자 하나까지 같아야</b> 한다(ValveRoomView.xaml 라디오).</summary>
    public IReadOnlyList<string> ValveRoomZones { get; } = ["", "이토밸브실", "공기밸브실", "타이밸브실", "제수밸브실"];

    /// <summary>배치할 배관 부재의 <c>DH_Zone</c>. 외곽 피킹이 채우고, 사용자가 바꿀 수 있다(docs/161 확정 1).</summary>
    public string ValveRoomZone
    {
        get => _valveRoomZone;
        // 유형이 바뀌면 추천 배치 버튼 활성·안내문도 바뀐다(docs/172 § C-11). NotifyValidationChanged가 둘을 통지한다.
        set
        {
            if (!SetProperty(ref _valveRoomZone, value ?? string.Empty)) return;
            // 공기밸브실 여부로 IN/OUT 접점 면(기초/벽)이 바뀜 — 화살표 다시 그림.
            if (!_outline.IsEmpty && _canvasOutline.UseFoundationFaces != _outline.WithFoundationFaces(IsAirValveRoom).UseFoundationFaces) RefreshOutline();
            NotifyValidationChanged();
            // 유형이 정해지면 격점번호 기본값도 따라간다(docs/177 § B-4).
            ApplyDefaultJunctionId();
        }
    }

    /// <summary>밸브실 유형에 맞는 격점번호 기본값을 채운다(docs/177 § B).
    /// 사용자가 손댔으면 아무것도 하지 않고, 유형이 비었거나 접두가 없는 유형이면 기존 값을 지우지 않는다.
    /// <para>프로퍼티 setter를 쓰지 않는 이유: setter가 <c>_junctionIdUserEdited</c>를 세우므로
    /// 자동값 한 번에 이후 자동 채움이 전부 막힌다(<see cref="PipePortItem.SetOffsetSilently"/>와 같은 이유).</para></summary>
    private void ApplyDefaultJunctionId()
    {
        if (_junctionIdUserEdited) return;
        var proposed = ValveRoomJunctionIdCatalog.Default(_valveRoomZone);
        if (string.IsNullOrEmpty(proposed)) return;
        if (string.Equals(_junctionId, proposed, StringComparison.Ordinal)) return;
        _junctionId = proposed;
        OnPropertyChanged(nameof(JunctionId));
    }

    /// <summary>추천 배치를 적용할 수 있는가 — 외곽이 있고, 유형에 해당하는 프리셋이 카탈로그에 있을 때.</summary>
    public bool CanApplyTemplate => HasOutline && !IsCreating && ValveRoomPipingTemplateCatalog.Default(ValveRoomZone) is not null;

    /// <summary>추천 배치 버튼 옆 안내. 유형이 비었거나 프리셋이 없으면 그 이유를 적는다.</summary>
    public string TemplateHint => string.IsNullOrWhiteSpace(ValveRoomZone) ? "밸브실 유형을 먼저 고르세요."
        : ValveRoomPipingTemplateCatalog.Default(ValveRoomZone) is { } template ? $"{template.Name} · 기존 배관은 대체되며 Ctrl+Z로 되돌릴 수 있습니다."
        : "이 유형의 추천 배치가 아직 없습니다.";

    public double ReferenceX { get => _referenceX; set { if (SetProperty(ref _referenceX, value)) { RefreshOutline(); NotifyValidationChanged(); } } }
    public double ReferenceY { get => _referenceY; set { if (SetProperty(ref _referenceY, value)) { RefreshOutline(); NotifyValidationChanged(); } } }

    public bool HasOutline => !_outline.IsEmpty;
    /// <summary>IN/OUT 포트 목록(docs/172 § C-2). IN은 좌측 벽, OUT은 우측 벽에 붙는다.
    /// 화살표·끝점 스냅·이격 검증·프리셋이 모두 이 목록을 본다. 항목의 이격이 바뀌면 <see cref="OnPortPropertyChanged"/>가 외곽을 다시 그린다.</summary>
    public ObservableCollection<PipePortItem> Ports { get; }
    /// <summary>첫 IN 포트의 이격(mm). 포트 목록으로 옮긴 뒤 남긴 호환 래퍼(docs/172 § C-3) — XAML은 더 쓰지 않지만 테스트·외부 호출이 남아 있다.</summary>
    public double InOffsetMm { get => FirstPort(PortFlow.In)?.OffsetMm ?? 0; set { if (FirstPort(PortFlow.In) is { } port) port.OffsetMm = value; } }
    /// <summary>첫 OUT 포트의 이격(mm). <see cref="InOffsetMm"/>과 같은 호환 래퍼.</summary>
    public double OutOffsetMm { get => FirstPort(PortFlow.Out)?.OffsetMm ?? 0; set { if (FirstPort(PortFlow.Out) is { } port) port.OffsetMm = value; } }
    private PipePortItem? FirstPort(PortFlow flow) => Ports.FirstOrDefault(x => x.Flow == flow);
    /// <summary>true면 상단 벽 내측면, false면 하단 벽 내측면에서 이격을 잰다.</summary>
    public bool ArrowFromTop { get => _arrowFromTop; set { if (SetProperty(ref _arrowFromTop, value)) RefreshOutline(); } }
    /// <summary>외곽선 변에도 OSNAP(끝점·중간점·근처점)을 적용할지 여부.</summary>
    public bool SnapOutline { get => _snapOutline; set => SetProperty(ref _snapOutline, value); }
    public double ReferenceScreenX => Transform.PanOrigin.X;
    public double ReferenceScreenY => Transform.PanOrigin.Y;
    /// <summary>입력칸 한 칸을 겸용한다 — 선택이 없으면 앞으로 그릴 선의 <b>기본 관경</b>,
    /// 선택이 있으면 <b>선택한 선의 관경</b>을 읽고 쓴다(docs/152 § D-1).
    /// <para>세터의 <see cref="ArgumentOutOfRangeException"/>은 XAML의 ValidatesOnExceptions가 의존하므로 없애지 말 것.</para></summary>
    public double DiameterMm
    {
        get
        {
            if (SelectedEdgeIds.Count == 0) return _diameterMm;
            // 값이 섞여 있으면 첫 엣지 값을 보여준다. 빈 칸으로 두면 사용자가 입력 상태를 오해한다.
            var selected = Edges.Where(x => SelectedEdgeIds.Contains(x.Id)).Select(x => x.DiameterMm).ToList();
            return selected.Count > 0 ? selected[0] : _diameterMm;
        }
        set
        {
            if (!double.IsFinite(value) || value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), $"[{ErrorCodes.PL_InvalidDiameter}] 관경은 0보다 큰 숫자여야 합니다.");
            if (SelectedEdgeIds.Count > 0)
            {
                ApplyDiameterToSelection(value);
                return;
            }
            SetDefaultDiameter(value);
        }
    }

    /// <summary>앞으로 그릴 선의 <b>기본 관경</b>만 읽고 쓴다 — 선택과 무관(UI 개편 동작 변경 A).
    /// 선택 관경 편집은 캔버스 속성 카드의 <see cref="DiameterMm"/>이 맡는다.
    /// <para>세터 예외는 XAML ValidatesOnExceptions용 — 지우지 말 것.</para></summary>
    public double DefaultDiameterMm
    {
        get => _diameterMm;
        set
        {
            if (!double.IsFinite(value) || value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), $"[{ErrorCodes.PL_InvalidDiameter}] 관경은 0보다 큰 숫자여야 합니다.");
            SetDefaultDiameter(value);
        }
    }

    private void SetDefaultDiameter(double value)
    {
        if (!SetProperty(ref _diameterMm, value, nameof(DefaultDiameterMm))) return;
        // 선택 없으면 DiameterMm도 같은 값 — 같이 알린다
        if (SelectedEdgeIds.Count == 0) OnPropertyChanged(nameof(DiameterMm));
        RefreshDiameterMatch();
        RefreshGraph();
        NotifyValidationChanged();
    }

    /// <summary>캔버스 선택 속성 카드 표시 여부(선 또는 부속 선택).</summary>
    public bool HasCanvasSelection => HasSelection;
    /// <summary>선 선택 있으면 카드에 관경 칸 보임.</summary>
    public bool HasSelectedEdges => SelectedEdgeIds.Count > 0;
    public string SelectionSummaryText => $"선택: 관 {SelectedEdgeIds.Count}개 · 부속 {SelectedFittingIds.Count}개";

    private void NotifySelectionCardChanged()
    {
        OnPropertyChanged(nameof(HasCanvasSelection));
        OnPropertyChanged(nameof(HasSelectedEdges));
        OnPropertyChanged(nameof(SelectionSummaryText));
    }

    /// <summary>선택한 선들의 관경만 바꾼다. 기본 관경(<see cref="_diameterMm"/>)은 건드리지 않는다.</summary>
    private void ApplyDiameterToSelection(double value)
    {
        var targets = SelectedEdgeIds.Where(id => _network.Edges.Any(x => x.Id == id)).ToList();
        if (targets.Count == 0) return;
        var before = Snapshot();
        foreach (var edgeId in targets) _network.SetEdgeDiameter(edgeId, value);
        BeginEditing();
        RecordUndoIfChanged(before);
        RefreshGraph();
        OnPropertyChanged(nameof(DiameterMm));
        Status = $"관경 {value:N0}mm 적용 — 배관 {targets.Count}개";
    }

    /// <summary>관경 입력칸 앞의 라벨. 지금 무엇을 편집 중인지 알려 준다(docs/152 § D-2).</summary>
    public string DiameterCaption => SelectedEdgeIds.Count > 0 ? $"선택 관경 ({SelectedEdgeIds.Count})" : "기본 관경";

    /// <summary>선택이 바뀌면 관경 입력칸이 가리키는 대상도 바뀐다. 두 프로퍼티를 함께 알린다.</summary>
    private void NotifyDiameterTargetChanged()
    {
        OnPropertyChanged(nameof(DiameterMm));
        OnPropertyChanged(nameof(DiameterCaption));
        // 4번 섹션 유형 직경 대조는 이 관경을 기준으로 한다(docs/233 § B-3).
        OnPropertyChanged(nameof(ValveTypeDiameterText));
        OnPropertyChanged(nameof(IsValveTypeDiameterMismatch));
    }
    public string? SelectedValveFamilyName
    {
        get => _selectedValveFamilyName;
        set
        {
            if (!SetProperty(ref _selectedValveFamilyName, value)) return;
            RefreshValveTypeNames();
            RefreshFittingDiameterParameterNames();
        }
    }
    /// <summary>직관·단관을 모두 만드는 단일 관 패밀리. 길이는 <see cref="LengthParameterName"/>으로 구동한다.</summary>
    public string? SegmentFamilyTypeName
    {
        get => _segmentFamilyTypeName;
        set
        {
            if (!SetProperty(ref _segmentFamilyTypeName, value)) return;
            RefreshSegmentParameterNames();
            NotifyValidationChanged();
        }
    }
    public string? LengthParameterName
    {
        get => _lengthParameterName;
        set
        {
            if (!SetProperty(ref _lengthParameterName, value)) return;
            _lengthParameterPinned = !string.IsNullOrWhiteSpace(value);
            NotifyValidationChanged();
        }
    }
    /// <summary>관 인스턴스에 직경(mm)을 기록할 파라미터명. 비워 두면 직경을 구동하지 않는다.</summary>
    public string? DiameterParameterName
    {
        get => _diameterParameterName;
        set
        {
            if (!SetProperty(ref _diameterParameterName, value)) return;
            _diameterParameterPinned = !string.IsNullOrWhiteSpace(value);
            NotifyValidationChanged();
        }
    }
    public string? BendFamilyTypeName { get => _bendFamilyTypeName; set { if (SetProperty(ref _bendFamilyTypeName, value)) { RefreshFittingDiameterParameterNames(); NotifyValidationChanged(); } } }
    public string? TeeFamilyTypeName { get => _teeFamilyTypeName; set { if (SetProperty(ref _teeFamilyTypeName, value)) { RefreshFittingDiameterParameterNames(); NotifyValidationChanged(); } } }
    public string? BendDiameterParameterName { get => _bendDiameterParameterName; set { if (SetProperty(ref _bendDiameterParameterName, value)) { if (!_isRefreshingParameterNames && !string.IsNullOrWhiteSpace(value)) _bendDiameterParameterPinned = true; NotifyValidationChanged(); } } }
    public string? BendAngleParameterName { get => _bendAngleParameterName; set { if (SetProperty(ref _bendAngleParameterName, value)) { if (!_isRefreshingParameterNames && !string.IsNullOrWhiteSpace(value)) _bendAngleParameterPinned = true; NotifyValidationChanged(); } } }
    public string? TeeDiameterParameterName { get => _teeDiameterParameterName; set { if (SetProperty(ref _teeDiameterParameterName, value)) { if (!_isRefreshingParameterNames && !string.IsNullOrWhiteSpace(value)) _teeDiameterParameterPinned = true; NotifyValidationChanged(); } } }
    public string? TeeBranchDiameterParameterName { get => _teeBranchDiameterParameterName; set { if (SetProperty(ref _teeBranchDiameterParameterName, value)) { if (!_isRefreshingParameterNames && !string.IsNullOrWhiteSpace(value)) _teeBranchDiameterParameterPinned = true; NotifyValidationChanged(); } } }
    public string? ValveDiameterParameterName { get => _valveDiameterParameterName; set { if (SetProperty(ref _valveDiameterParameterName, value)) { if (!_isRefreshingParameterNames) { if (!string.IsNullOrWhiteSpace(value)) _valveDiameterParameterPinned = true; RememberValveDiameterParameter(CurrentValveFamilyTypeName, value); } NotifyValidationChanged(); } } }
    /// <summary>이음관 인스턴스에 직경(mm)을 기록할 파라미터명(`이음관_구경`). 밸브와 달리 전역 1개다(docs/167 § C).</summary>
    public string? JointDiameterParameterName { get => _jointDiameterParameterName; set { if (SetProperty(ref _jointDiameterParameterName, value)) { if (!_isRefreshingParameterNames && !string.IsNullOrWhiteSpace(value)) _jointDiameterParameterPinned = true; NotifyValidationChanged(); } } }

    // ── 이형관 종류별 패밀리(docs/175 § B-5). T형과 같은 모양이다. 이경관·편락관은 매핑만 보관한다(D1). ──
    public string? CrossFamilyTypeName { get => _crossFamilyTypeName; set { if (SetProperty(ref _crossFamilyTypeName, value)) { RefreshFittingDiameterParameterNames(); NotifyValidationChanged(); } } }
    public string? CrossDiameterParameterName { get => _crossDiameterParameterName; set { if (SetProperty(ref _crossDiameterParameterName, value)) { if (!_isRefreshingParameterNames && !string.IsNullOrWhiteSpace(value)) _crossDiameterParameterPinned = true; NotifyValidationChanged(); } } }
    public string? CrossBranchDiameterParameterName { get => _crossBranchDiameterParameterName; set { if (SetProperty(ref _crossBranchDiameterParameterName, value)) { if (!_isRefreshingParameterNames && !string.IsNullOrWhiteSpace(value)) _crossBranchDiameterParameterPinned = true; NotifyValidationChanged(); } } }
    // ── Y형관(docs/187 § 3-4). 십자관과 같은 모양에 분기각 한 칸이 더 붙는다. ──
    public string? WyeFamilyTypeName { get => _wyeFamilyTypeName; set { if (SetProperty(ref _wyeFamilyTypeName, value)) { RefreshFittingDiameterParameterNames(); NotifyValidationChanged(); } } }
    public string? WyeDiameterParameterName { get => _wyeDiameterParameterName; set { if (SetProperty(ref _wyeDiameterParameterName, value)) { if (!_isRefreshingParameterNames && !string.IsNullOrWhiteSpace(value)) _wyeDiameterParameterPinned = true; NotifyValidationChanged(); } } }
    public string? WyeBranchDiameterParameterName { get => _wyeBranchDiameterParameterName; set { if (SetProperty(ref _wyeBranchDiameterParameterName, value)) { if (!_isRefreshingParameterNames && !string.IsNullOrWhiteSpace(value)) _wyeBranchDiameterParameterPinned = true; NotifyValidationChanged(); } } }
    public string? WyeBranchAngleParameterName { get => _wyeBranchAngleParameterName; set { if (SetProperty(ref _wyeBranchAngleParameterName, value)) { if (!_isRefreshingParameterNames && !string.IsNullOrWhiteSpace(value)) _wyeBranchAngleParameterPinned = true; NotifyValidationChanged(); } } }
    // ── 밸브부관(docs/225 § B). 직경 한 칸만 있다 — 90° 고정 형상이라 각도를 구동하지 않는다. ──
    public string? ValveBendFamilyTypeName { get => _valveBendFamilyTypeName; set { if (SetProperty(ref _valveBendFamilyTypeName, value)) { RefreshFittingDiameterParameterNames(); NotifyValidationChanged(); } } }
    public string? ValveBendDiameterParameterName { get => _valveBendDiameterParameterName; set { if (SetProperty(ref _valveBendDiameterParameterName, value)) { if (!_isRefreshingParameterNames && !string.IsNullOrWhiteSpace(value)) _valveBendDiameterParameterPinned = true; NotifyValidationChanged(); } } }
    // ── 밸브부관 A(docs/227 § B). T형과 같은 모양에 수평·수직분기 예/아니오 두 칸이 붙는다. ──
    public string? ValveTeeFamilyTypeName { get => _valveTeeFamilyTypeName; set { if (SetProperty(ref _valveTeeFamilyTypeName, value)) { RefreshFittingDiameterParameterNames(); NotifyValidationChanged(); } } }
    public string? ValveTeeMainDiameterParameterName { get => _valveTeeMainDiameterParameterName; set { if (SetProperty(ref _valveTeeMainDiameterParameterName, value)) { if (!_isRefreshingParameterNames && !string.IsNullOrWhiteSpace(value)) _valveTeeMainDiameterParameterPinned = true; NotifyValidationChanged(); } } }
    public string? ValveTeeBranchDiameterParameterName { get => _valveTeeBranchDiameterParameterName; set { if (SetProperty(ref _valveTeeBranchDiameterParameterName, value)) { if (!_isRefreshingParameterNames && !string.IsNullOrWhiteSpace(value)) _valveTeeBranchDiameterParameterPinned = true; NotifyValidationChanged(); } } }
    public string? ValveTeeHorizontalBranchParameterName { get => _valveTeeHorizontalBranchParameterName; set { if (SetProperty(ref _valveTeeHorizontalBranchParameterName, value)) { if (!_isRefreshingParameterNames && !string.IsNullOrWhiteSpace(value)) _valveTeeHorizontalBranchParameterPinned = true; NotifyValidationChanged(); } } }
    public string? ValveTeeVerticalBranchParameterName { get => _valveTeeVerticalBranchParameterName; set { if (SetProperty(ref _valveTeeVerticalBranchParameterName, value)) { if (!_isRefreshingParameterNames && !string.IsNullOrWhiteSpace(value)) _valveTeeVerticalBranchParameterPinned = true; NotifyValidationChanged(); } } }
    public string? ReducerFamilyTypeName { get => _reducerFamilyTypeName; set { if (SetProperty(ref _reducerFamilyTypeName, value)) { RefreshFittingDiameterParameterNames(); NotifyValidationChanged(); } } }
    public string? ReducerLargeDiameterParameterName { get => _reducerLargeDiameterParameterName; set { if (SetProperty(ref _reducerLargeDiameterParameterName, value)) { if (!_isRefreshingParameterNames && !string.IsNullOrWhiteSpace(value)) _reducerLargeDiameterParameterPinned = true; NotifyValidationChanged(); } } }
    public string? ReducerSmallDiameterParameterName { get => _reducerSmallDiameterParameterName; set { if (SetProperty(ref _reducerSmallDiameterParameterName, value)) { if (!_isRefreshingParameterNames && !string.IsNullOrWhiteSpace(value)) _reducerSmallDiameterParameterPinned = true; NotifyValidationChanged(); } } }

    // ── 패밀리 설정 저장·요약(docs/175 § E-4) ──────────────────────────────────

    /// <summary>현재 매핑을 저장 단위로 뜬다. 창의 콤보가 이 VM에 직접 바인딩되므로 별도 편집 사본은 없다.</summary>
    public PipeLayoutFamilySettings ToFamilySettings() => new(
        SegmentFamilyTypeName ?? "", LengthParameterName ?? "", DiameterParameterName ?? "", StraightLengthMm,
        BendFamilyTypeName ?? "", BendDiameterParameterName ?? "", BendAngleParameterName ?? "",
        TeeFamilyTypeName ?? "", TeeDiameterParameterName ?? "", TeeBranchDiameterParameterName ?? "",
        CrossFamilyTypeName ?? "", CrossDiameterParameterName ?? "", CrossBranchDiameterParameterName ?? "",
        WyeFamilyTypeName ?? "", WyeDiameterParameterName ?? "", WyeBranchDiameterParameterName ?? "", WyeBranchAngleParameterName ?? "",
        ReducerFamilyTypeName ?? "", ReducerLargeDiameterParameterName ?? "", ReducerSmallDiameterParameterName ?? "",
        new Dictionary<string, string>(_valveDiameterParameterByFamily, StringComparer.Ordinal),
        JointDiameterParameterName ?? "",
        OutlineWallElementIds: _outline.Walls.Select(x => x.ElementId).Distinct().ToList(),
        OutlineColumnElementIds: _outline.Columns.Select(x => x.ElementId).Distinct().ToList(),
        OutlineFoundationElementIds: _outline.Foundations.Select(x => x.ElementId).Distinct().ToList(),
        ReferenceX: ReferenceX,
        ReferenceY: ReferenceY,
        ArrowFromTop: ArrowFromTop,
        Ports: Ports.Select(x => new StoredPipePort(x.ToPort(), x.UserEdited)).ToList(),
        ValveBendFamilyTypeName: ValveBendFamilyTypeName ?? "",
        ValveBendDiameterParameterName: ValveBendDiameterParameterName ?? "",
        ValveTeeFamilyTypeName: ValveTeeFamilyTypeName ?? "",
        ValveTeeMainDiameterParameterName: ValveTeeMainDiameterParameterName ?? "",
        ValveTeeBranchDiameterParameterName: ValveTeeBranchDiameterParameterName ?? "",
        ValveTeeHorizontalBranchParameterName: ValveTeeHorizontalBranchParameterName ?? "",
        ValveTeeVerticalBranchParameterName: ValveTeeVerticalBranchParameterName ?? "",
        SteelFittingClass: _steelFittingClass,
        PipeKind: _pipeKind,
        ElevationMode: ElevationMode,
        ElevationLevelId: ElevationLevel?.Id,
        ElevationOffsetMm: ElevationOffsetMm);

    /// <summary>저장값(또는 외부에서 만든 설정)을 적용한다. <b>비어 있지 않은 칸만</b> 덮고 그 칸은 핀 고정한다 —
    /// 빈 칸은 이름 추정 결과를 그대로 둔다(docs/175 D6). 콤보 목록에 없는 패밀리명은 그대로 넣어 사용자가 알아채게 한다.</summary>
    /// <param name="fromStorage">프로젝트 저장값이면 true. 이후 역가져오기가 채워진 칸을 덮지 않는다(§ E-4 6).</param>
    public void ApplyFamilySettings(PipeLayoutFamilySettings settings, bool fromStorage = false)
    {
        if (fromStorage) _familySettingsLoaded = true;
        // 패밀리 세터 묶음은 후보 갱신을 미뤘다가 마지막 패밀리 기준으로 한 번만 읽는다(docs/258 § B).
        // 종전에는 세터마다 전체 후보를 다시 읽었다 — 최종 결과는 마지막 패밀리 기준 갱신과 같다.
        _parameterRefreshDeferral++;
        try
        {
            if (!string.IsNullOrWhiteSpace(settings.SegmentFamilyTypeName)) SegmentFamilyTypeName = settings.SegmentFamilyTypeName;
            if (!string.IsNullOrWhiteSpace(settings.BendFamilyTypeName)) BendFamilyTypeName = settings.BendFamilyTypeName;
            if (!string.IsNullOrWhiteSpace(settings.TeeFamilyTypeName)) TeeFamilyTypeName = settings.TeeFamilyTypeName;
            if (!string.IsNullOrWhiteSpace(settings.CrossFamilyTypeName)) CrossFamilyTypeName = settings.CrossFamilyTypeName;
            if (!string.IsNullOrWhiteSpace(settings.WyeFamilyTypeName)) WyeFamilyTypeName = settings.WyeFamilyTypeName;
            if (!string.IsNullOrWhiteSpace(settings.ReducerFamilyTypeName)) ReducerFamilyTypeName = settings.ReducerFamilyTypeName;
            if (!string.IsNullOrWhiteSpace(settings.ValveBendFamilyTypeName)) ValveBendFamilyTypeName = settings.ValveBendFamilyTypeName;
            if (!string.IsNullOrWhiteSpace(settings.ValveTeeFamilyTypeName)) ValveTeeFamilyTypeName = settings.ValveTeeFamilyTypeName;
        }
        finally { _parameterRefreshDeferral--; }
        FlushDeferredParameterRefresh();
        // 패밀리를 넣으면 세터가 파라미터 후보를 다시 추정하므로, 파라미터는 패밀리 뒤(위 갱신 뒤)에 넣어야 덮이지 않는다.
        // 볼트 치수 이름 저장값은 PL-303 원인 — 무시하고 추정값 유지(docs/309).
        if (!string.IsNullOrWhiteSpace(settings.LengthParameterName)
            && !settings.LengthParameterName.Contains(BoltToken, StringComparison.Ordinal))
            LengthParameterName = settings.LengthParameterName;
        if (!string.IsNullOrWhiteSpace(settings.DiameterParameterName)) DiameterParameterName = settings.DiameterParameterName;
        if (double.IsFinite(settings.StraightLengthMm) && settings.StraightLengthMm > 0) StraightLengthMm = settings.StraightLengthMm;
        if (!string.IsNullOrWhiteSpace(settings.BendDiameterParameterName)) BendDiameterParameterName = settings.BendDiameterParameterName;
        if (!string.IsNullOrWhiteSpace(settings.BendAngleParameterName)) BendAngleParameterName = settings.BendAngleParameterName;
        if (!string.IsNullOrWhiteSpace(settings.TeeDiameterParameterName)) TeeDiameterParameterName = settings.TeeDiameterParameterName;
        if (!string.IsNullOrWhiteSpace(settings.TeeBranchDiameterParameterName)) TeeBranchDiameterParameterName = settings.TeeBranchDiameterParameterName;
        if (!string.IsNullOrWhiteSpace(settings.CrossDiameterParameterName)) CrossDiameterParameterName = settings.CrossDiameterParameterName;
        if (!string.IsNullOrWhiteSpace(settings.CrossBranchDiameterParameterName)) CrossBranchDiameterParameterName = settings.CrossBranchDiameterParameterName;
        if (!string.IsNullOrWhiteSpace(settings.WyeDiameterParameterName)) WyeDiameterParameterName = settings.WyeDiameterParameterName;
        if (!string.IsNullOrWhiteSpace(settings.WyeBranchDiameterParameterName)) WyeBranchDiameterParameterName = settings.WyeBranchDiameterParameterName;
        if (!string.IsNullOrWhiteSpace(settings.WyeBranchAngleParameterName)) WyeBranchAngleParameterName = settings.WyeBranchAngleParameterName;
        if (!string.IsNullOrWhiteSpace(settings.ReducerLargeDiameterParameterName)) ReducerLargeDiameterParameterName = settings.ReducerLargeDiameterParameterName;
        if (!string.IsNullOrWhiteSpace(settings.ReducerSmallDiameterParameterName)) ReducerSmallDiameterParameterName = settings.ReducerSmallDiameterParameterName;
        if (!string.IsNullOrWhiteSpace(settings.JointDiameterParameterName)) JointDiameterParameterName = settings.JointDiameterParameterName;
        if (!string.IsNullOrWhiteSpace(settings.ValveBendDiameterParameterName)) ValveBendDiameterParameterName = settings.ValveBendDiameterParameterName;
        if (!string.IsNullOrWhiteSpace(settings.ValveTeeMainDiameterParameterName)) ValveTeeMainDiameterParameterName = settings.ValveTeeMainDiameterParameterName;
        if (!string.IsNullOrWhiteSpace(settings.ValveTeeBranchDiameterParameterName)) ValveTeeBranchDiameterParameterName = settings.ValveTeeBranchDiameterParameterName;
        if (!string.IsNullOrWhiteSpace(settings.ValveTeeHorizontalBranchParameterName)) ValveTeeHorizontalBranchParameterName = settings.ValveTeeHorizontalBranchParameterName;
        if (!string.IsNullOrWhiteSpace(settings.ValveTeeVerticalBranchParameterName)) ValveTeeVerticalBranchParameterName = settings.ValveTeeVerticalBranchParameterName;
        foreach (var (family, parameter) in settings.ValveDiameterParameters)
            if (!string.IsNullOrWhiteSpace(parameter)) _valveDiameterParameterByFamily[family] = parameter;
        RestoreValveDiameterParameterForFamily(CurrentValveFamilyTypeName);
        // 바이패스 밸브 콤보도 저장된 사전 값을 따라간다(docs/232 § C-1).
        RefreshBypassValveParameterNames();

        _savedOutlineElementIds = (settings.OutlineWallElementIds ?? [])
            .Concat(settings.OutlineColumnElementIds ?? [])
            .Concat(settings.OutlineFoundationElementIds ?? [])
            .Distinct()
            .ToList();
        if (settings.ReferenceX is { } savedX) { _referenceX = savedX; OnPropertyChanged(nameof(ReferenceX)); }
        if (settings.ReferenceY is { } savedY) { _referenceY = savedY; OnPropertyChanged(nameof(ReferenceY)); }
        _arrowFromTop = settings.ArrowFromTop; OnPropertyChanged(nameof(ArrowFromTop));
        // 강관 등급(docs/256). 옛 저장본은 키가 없어 F12로 읽힌다.
        if (settings.SteelFittingClass != SteelFittingClass.None) SteelFittingClass = settings.SteelFittingClass;
        // 관 등급(docs/260 § C). 옛 저장본(null)은 현재 값을 유지한다. 관종과 맞지 않으면 다음 관종 변경 때 기본값으로 바뀐다.
        if (settings.PipeKind is { } savedKind && (savedKind.Length == 0 || PipeKindCatalog.Normalize(savedKind) is not null))
        {
            _pipeKind = PipeKindCatalog.Normalize(savedKind) ?? string.Empty;
            EnsurePipeKindForMaterial();
        }
        if (settings.Ports is { Count: > 0 } storedPorts)
        {
            DetachAllPorts();
            foreach (var stored in storedPorts)
                AttachPort(new PipePortItem(stored.Port) { UserEdited = stored.UserEdited });
            UpdatePortRemovability();
        }
        // 높이 3항목도 "값 있는 칸만 덮기" 규약 따름. null(옛 저장본)이면 현재 값 유지 — 기본 모드는 이미 Relative.
        if (settings.ElevationMode is { } savedMode) ElevationMode = savedMode;
        if (settings.ElevationOffsetMm is { } savedOffset) ElevationOffsetMm = savedOffset;
        _savedElevationLevelIdSpecified = settings.ElevationLevelId is not null;
        _savedElevationLevelRestored = settings.ElevationLevelId is { } savedLevelId
            && Levels.Any(level => level.Id == savedLevelId);
        // Id 저장됐는데 문서에 없으면 비움 → 사용자 재선택. Id 없으면 현재 선택 유지.
        if (_savedElevationLevelIdSpecified)
            ElevationLevel = _savedElevationLevelRestored
                ? Levels.First(level => level.Id == settings.ElevationLevelId)
                : null;
        _savedElevationLevelMissing = settings.ElevationLevelId is not null && !_savedElevationLevelRestored;
        if (_savedElevationLevelMissing) Status = "저장된 기준 레벨 없음 — 다시 선택";
        if (ElevationMode == PipeElevationMode.Relative && ElevationLevel is not null) SetElevationSilently(ElevationLevel.ElevationMm + ElevationOffsetMm);
        NotifyFamilySettingsChanged();
    }

    private void SaveFamilySettings()
    {
        var settings = ToFamilySettings();
        SaveFamilySettingsAction?.Invoke(settings);
        // 저장은 ExternalEvent로 Revit에 넘어가므로 여기서는 "요청"만 알린다. 실제 결과는 ApplyFamilySettingsSaveResult로 돌아온다.
        // 모달 창을 닫아야 Revit이 유휴 상태가 되어 요청이 처리된다 — 닫기까지가 저장의 일부다.
        Status = "패밀리 설정 저장하는 중…";
        CloseFamilySettingsAction?.Invoke();
    }

    /// <summary>ExternalEvent에서 저장을 마친 뒤 되돌아오는 결과. 호출측이 UI 스레드로 마샬링한다.</summary>
    public void ApplyFamilySettingsSaveResult(bool succeeded, string message)
    {
        if (succeeded) _familySettingsLoaded = true;
        Status = message;
    }

    private void NotifyFamilySettingsChanged()
    {
        NotifyValidationChanged();
    }

    /// <summary>접합 종류·관종이 바뀌면 관·곡관·T·Y·십자·이경 칸을 그 접합·관종의 패밀리로 바꾼다(docs/263 § D, 2026-10-02·2026-10-30 사용자 결정).
    /// 사용자가 고른 값도 덮는다. 매칭이 없는 칸은 그대로 두고, 사용자는 이후 설정 창에서 다시 고를 수 있다.
    /// 밸브부관 A·B는 대상이 아니다.</summary>
    private void ApplyJointFamilyDefaults()
    {
        var names = SegmentFamilyTypeNames.ToList();
        // 세터 묶음은 후보 갱신을 미뤘다가 한 번만 읽는다(ApplyFamilySettings와 같은 패턴, docs/258 § B).
        _parameterRefreshDeferral++;
        try
        {
            if (GuessFamilyForJoint(names, PipeFamilyDefaultKeywords, _jointConnection, _pipeMaterial) is { } segment) SegmentFamilyTypeName = segment;
            if (GuessFamilyForJoint(names, BendFamilyDefaultKeywords, _jointConnection, _pipeMaterial) is { } bend) BendFamilyTypeName = bend;
            if (GuessFamilyForJoint(names, TeeFamilyDefaultKeywords, _jointConnection, _pipeMaterial) is { } tee) TeeFamilyTypeName = tee;
            if (GuessFamilyForJoint(names, CrossFamilyDefaultKeywords, _jointConnection, _pipeMaterial) is { } cross) CrossFamilyTypeName = cross;
            if (GuessFamilyForJoint(names, WyeFamilyDefaultKeywords, _jointConnection, _pipeMaterial) is { } wye) WyeFamilyTypeName = wye;
            if (GuessFamilyForJoint(names, ReducerFamilyDefaultKeywords, _jointConnection, _pipeMaterial) is { } reducer) ReducerFamilyTypeName = reducer;
        }
        finally { _parameterRefreshDeferral--; }
        FlushDeferredParameterRefresh();
        NotifyFamilySettingsChanged();
    }

    // ── 이음관 배치 선택(docs/175 § C-3). 밸브처럼 선 위를 클릭해 놓는다. ────────────

    /// <summary>1단 콤보의 선택. 바꾸면 2단 유형 목록을 다시 채우고 유형 선택은 비운다(밸브 <see cref="SelectedValveFamilyName"/>과 같은 규칙).</summary>
    public string? SelectedJointFamilyName
    {
        get => _selectedJointFamilyName;
        set
        {
            if (!SetProperty(ref _selectedJointFamilyName, value)) return;
            RefreshJointTypeNames();
        }
    }

    /// <summary>배치할 이음관 패밀리("패밀리명 : 유형명"). 고르면 밸브 유형 선택은 비운다 — 배치 대상은 하나여야 한다.</summary>
    public string? SelectedJointFamilyTypeName
    {
        get => _selectedJointFamilyTypeName;
        set
        {
            if (!SetProperty(ref _selectedJointFamilyTypeName, value)) return;
            if (!string.IsNullOrWhiteSpace(value) && SelectedFamilyTypeName is not null) SelectedFamilyTypeName = null;
            if (!string.IsNullOrWhiteSpace(value)) IsPlacingBypassSet = false;
            // 동작 변경 B — 밸브와 같음
            if (!string.IsNullOrWhiteSpace(value) && !IsDrawMode) Mode = PipeLayoutMode.Drawing;
            RefreshFittingDiameterParameterNames();
            OnPropertyChanged(nameof(IsPlacingFitting));
            OnPropertyChanged(nameof(InteractionModeText));
            if (string.IsNullOrWhiteSpace(value)) HideFittingPreview();
        }
    }

    /// <summary>지금 캔버스 클릭으로 놓을 패밀리. 이음관 선택이 밸브 선택보다 우선한다(둘이 동시에 차지 않는다).</summary>
    private string? PlacingFamilyTypeName => _isPlacingBypassSet ? BypassValveTypeName : SelectedJointFamilyTypeName ?? SelectedFamilyTypeName;
    /// <summary>놓을 부속의 TypeKey. 이음관과 밸브를 가르는 유일한 표식이다(docs/175 § C-1).</summary>
    private string PlacingTypeKey => _isPlacingBypassSet ? BypassSet.TypeKey
        : SelectedJointFamilyTypeName is not null ? InlineJoint.TypeKey : InlineFittingTypeKey;

    /// <summary>이음관 패밀리를 하나도 못 찾았을 때만 안내를 띄운다.</summary>
    public bool HasNoJointFamily => JointFamilyTypeNames.Count == 0;

    /// <summary>직관 1본의 정척 길이(mm). 관 패밀리 수식의 직관/단관 분기 기준과 같아야 한다.</summary>
    public double StraightLengthMm
    {
        get => _straightLengthMm;
        set
        {
            if (!double.IsFinite(value) || value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), $"[{ErrorCodes.PL_InvalidStraightLength}] 정척 길이는 0보다 큰 숫자여야 합니다.");
            // 정척 수직보조선(docs/245)이 새 간격을 따라오도록 화면을 다시 그린다. 검증 알림은 앞 줄이 이미 한다.
            if (SetProperty(ref _straightLengthMm, value)) { NotifyValidationChanged(); RefreshViewOnly(); }
        }
    }

    public bool IsCreating { get => _isCreating; private set { if (SetProperty(ref _isCreating, value)) NotifyValidationChanged(); } }
    public bool CanUndo => _undoHistory.Count > 0;
    public string InteractionModeText => IsCreating ? "모델 생성 중" : IsSplittingEdge ? "분할점 지정" : IsPlacingFitting ? (_isPlacingBypassSet ? "바이패스 세트 배치" : SelectedJointFamilyTypeName is not null ? "이음관 배치" : "부속 배치") : IsDrawing ? "선 그리기" : SelectedFitting is not null ? "부속 선택" : SelectedEdge is not null ? "선 선택" : IsSelectionMode ? "선택 대기" : "그리기 대기";
    public string NetworkSummary => $"배관 {_network.Edges.Count} · Tee {_network.Nodes.Count(x => x.NodeKind == NodeKind.Tee)} · Y형 {_network.Nodes.Count(x => x.NodeKind == NodeKind.Wye)} · 십자 {_network.Nodes.Count(x => x.NodeKind == NodeKind.Cross)} · 밸브부관 A {_network.Nodes.Count(IsValveTeeNode)} · B {_network.Nodes.Count(IsValveBendNode)} · 세트 {_network.Edges.Sum(x => x.InlineFittings.Count(f => f.IsBypassSet))} · 부속 {_network.Edges.Sum(x => x.InlineFittings.Count)}";
    /// <summary>오류(생성 차단) 뒤에 경고(차단 안 함, docs/233 § B-3)를 "경고:" 접두로 이어 붙인다. <see cref="CanCreateModel"/>은 오류만 본다.</summary>
    public string ValidationSummary => string.Join("  ·  ", GetValidationErrors().Concat(GetValidationWarnings().Select(x => "경고: " + x)));
    /// <summary>선 그리기·부속 배치 시 치수가 반올림되는 격자 단위(mm). 프리셋 외의 양수도 직접 입력할 수 있다.</summary>
    public double[] GridSnapOptions { get; } = [1, 10, 100];
    public double GridSnapMm
    {
        get => _gridSnapMm;
        set
        {
            if (!double.IsFinite(value) || value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), $"[{ErrorCodes.PL_InvalidGridSnap}] 격자 스냅 단위는 0보다 큰 숫자여야 합니다.");
            if (SetProperty(ref _gridSnapMm, value)) NotifyValidationChanged();
        }
    }
    public bool UseAngleSnap { get => _useAngleSnap; set => SetProperty(ref _useAngleSnap, value); }
    public bool SnapReferencePoint { get => _snapReferencePoint; set => SetProperty(ref _snapReferencePoint, value); }
    public bool SnapEndpoint { get => _snapEndpoint; set => SetProperty(ref _snapEndpoint, value); }
    public bool SnapMidpoint { get => _snapMidpoint; set => SetProperty(ref _snapMidpoint, value); }
    public bool SnapQuadrant { get => _snapQuadrant; set => SetProperty(ref _snapQuadrant, value); }
    public bool SnapIntersection { get => _snapIntersection; set => SetProperty(ref _snapIntersection, value); }
    public bool SnapPerpendicular { get => _snapPerpendicular; set => SetProperty(ref _snapPerpendicular, value); }
    public bool SnapNearest { get => _snapNearest; set => SetProperty(ref _snapNearest, value); }
    public bool IsPreviewVisible { get => _isPreviewVisible; private set => SetProperty(ref _isPreviewVisible, value); }
    public bool IsDrawing => _segmentStart is not null;
    public bool IsSnapMarkerVisible { get => _isSnapMarkerVisible; private set => SetProperty(ref _isSnapMarkerVisible, value); }
    public double SnapMarkerX { get => _snapMarkerX; private set => SetProperty(ref _snapMarkerX, value); }
    public double SnapMarkerY { get => _snapMarkerY; private set => SetProperty(ref _snapMarkerY, value); }
    public string SnapMarkerSymbol { get => _snapMarkerSymbol; private set => SetProperty(ref _snapMarkerSymbol, value); }
    public string SnapMarkerLabel { get => _snapMarkerLabel; private set => SetProperty(ref _snapMarkerLabel, value); }
    public double CursorX { get => _cursorX; private set => SetProperty(ref _cursorX, value); }
    public double CursorY { get => _cursorY; private set => SetProperty(ref _cursorY, value); }
    public bool IsCursorVisible { get => _isCursorVisible; private set => SetProperty(ref _isCursorVisible, value); }
    public double PreviewX1 { get => _previewX1; private set => SetProperty(ref _previewX1, value); }
    public double PreviewY1 { get => _previewY1; private set => SetProperty(ref _previewY1, value); }
    public double PreviewX2 { get => _previewX2; private set => SetProperty(ref _previewX2, value); }
    public double PreviewY2 { get => _previewY2; private set => SetProperty(ref _previewY2, value); }
    public double SubLineAX1 { get => _subLineAX1; private set => SetProperty(ref _subLineAX1, value); }
    public double SubLineAY1 { get => _subLineAY1; private set => SetProperty(ref _subLineAY1, value); }
    public double SubLineAX2 { get => _subLineAX2; private set => SetProperty(ref _subLineAX2, value); }
    public double SubLineAY2 { get => _subLineAY2; private set => SetProperty(ref _subLineAY2, value); }
    public double SubLineBX1 { get => _subLineBX1; private set => SetProperty(ref _subLineBX1, value); }
    public double SubLineBY1 { get => _subLineBY1; private set => SetProperty(ref _subLineBY1, value); }
    public double SubLineBX2 { get => _subLineBX2; private set => SetProperty(ref _subLineBX2, value); }
    public double SubLineBY2 { get => _subLineBY2; private set => SetProperty(ref _subLineBY2, value); }
    public bool IsSubLineVisible { get => _isSubLineVisible; private set => SetProperty(ref _isSubLineVisible, value); }
    /// <summary>미리보기 정척 수직보조선(docs/245). 가시성은 <see cref="IsSubLineVisible"/>를 따른다.</summary>
    public System.Windows.Media.Geometry? SubLineTicks { get => _subLineTicks; private set => SetProperty(ref _subLineTicks, value); }
    // 분할 표식: 커서가 올라탄 지점에서 선에 수직하게 긋는 짧은 선분(docs/181 § A). 분할 중에만 보인다.
    public double SplitMarkerX1 { get => _splitMarkerX1; private set => SetProperty(ref _splitMarkerX1, value); }
    public double SplitMarkerY1 { get => _splitMarkerY1; private set => SetProperty(ref _splitMarkerY1, value); }
    public double SplitMarkerX2 { get => _splitMarkerX2; private set => SetProperty(ref _splitMarkerX2, value); }
    public double SplitMarkerY2 { get => _splitMarkerY2; private set => SetProperty(ref _splitMarkerY2, value); }
    public bool IsSplitMarkerVisible { get => _isSplitMarkerVisible; private set => SetProperty(ref _isSplitMarkerVisible, value); }
    public double PreviewLengthX { get => _previewLengthX; private set => SetProperty(ref _previewLengthX, value); }
    public double PreviewLengthY { get => _previewLengthY; private set => SetProperty(ref _previewLengthY, value); }
    public string PreviewLength { get => _previewLength; private set => SetProperty(ref _previewLength, value); }
    public string PreviewAngle { get => _previewAngle; private set => SetProperty(ref _previewAngle, value); }
    public bool IsSelectionBoxVisible { get => _isSelectionBoxVisible; private set => SetProperty(ref _isSelectionBoxVisible, value); }
    public double SelectionBoxX { get => _selectionBoxX; private set => SetProperty(ref _selectionBoxX, value); }
    public double SelectionBoxY { get => _selectionBoxY; private set => SetProperty(ref _selectionBoxY, value); }
    public double SelectionBoxWidth { get => _selectionBoxWidth; private set => SetProperty(ref _selectionBoxWidth, value); }
    public double SelectionBoxHeight { get => _selectionBoxHeight; private set => SetProperty(ref _selectionBoxHeight, value); }
    public bool IsCrossingSelection { get => _isCrossingSelection; private set => SetProperty(ref _isCrossingSelection, value); }

    public PipeEdgeItem? SelectedEdge
    {
        get => _selectedEdge;
        private set { if (SetProperty(ref _selectedEdge, value)) { RefreshFittings(); OnPropertyChanged(nameof(InteractionModeText)); CommandManager.InvalidateRequerySuggested(); } }
    }
    public PipeLayoutMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            if (value != PipeLayoutMode.Drawing && IsDrawing) CancelDrawing();
            if (_drag is not null) CancelDrag();
            if (value != PipeLayoutMode.Selection) ClearSelection();
            _mode = value;
            if (value != PipeLayoutMode.Drawing && _isPlacingBypassSet) IsPlacingBypassSet = false;
            // 동작 변경 B — 유형 목록이 항상 켜져 있으므로, 그리기 모드를 떠나면 유형 선택도 비운다.
            // 안 비우면 같은 유형 다시 눌러도 SelectedItem 안 바뀌어 배치 진입 안 됨.
            if (value != PipeLayoutMode.Drawing)
            {
                if (_selectedFamilyTypeName is not null) SelectedFamilyTypeName = null;
                if (_selectedJointFamilyTypeName is not null) SelectedJointFamilyTypeName = null;
            }
            RefreshGraph();
            OnPropertyChanged(nameof(Mode));
            OnPropertyChanged(nameof(IsSelectionMode));
            OnPropertyChanged(nameof(IsDrawMode));
            OnPropertyChanged(nameof(IsPlacingFitting));
            OnPropertyChanged(nameof(InteractionModeText));
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public bool IsSelectionMode => Mode == PipeLayoutMode.Selection;
    public bool IsDrawMode => Mode == PipeLayoutMode.Drawing;
    private bool HasSelection => SelectedEdgeIds.Count > 0 || SelectedFittingIds.Count > 0;

    public InlineFittingItem? SelectedFitting
    {
        get => _selectedFitting;
        private set { if (SetProperty(ref _selectedFitting, value)) { RefreshFittings(); OnPropertyChanged(nameof(InteractionModeText)); CommandManager.InvalidateRequerySuggested(); } }
    }
    /// <summary>배치할 배관부속 FamilySymbol("패밀리명 : 타입명"). 리스트에서 고르면 배치 모드로 들어간다(카탈로그 매핑 없음).</summary>
    public string? SelectedFamilyTypeName
    {
        get => _selectedFamilyTypeName;
        set
        {
            if (!SetProperty(ref _selectedFamilyTypeName, value)) return;
            // 밸브를 고르면 이음관 선택은 비운다 — 배치 대상은 하나다(docs/175 § C-3).
            if (!string.IsNullOrWhiteSpace(value) && _selectedJointFamilyTypeName is not null) SelectedJointFamilyTypeName = null;
            if (!string.IsNullOrWhiteSpace(value)) IsPlacingBypassSet = false;
            // 동작 변경 B — 유형 고르면 바로 배치 도구(그리기 모드) 진입. 코드에서 넣는 값은 모두 null이라 사용자 선택만 해당
            if (!string.IsNullOrWhiteSpace(value) && !IsDrawMode) Mode = PipeLayoutMode.Drawing;
            RefreshFittingDiameterParameterNames();
            OnPropertyChanged(nameof(IsPlacingFitting));
            OnPropertyChanged(nameof(InteractionModeText));
            if (string.IsNullOrWhiteSpace(value)) HideFittingPreview();
        }
    }
    /// <summary>true면 리스트에서 패밀리를 선택한 상태 — 캔버스 클릭이 선 그리기가 아니라 부속 배치로 동작한다.</summary>
    /// 바이패스 세트 배치 켜짐이면 밸브 유형 유무와 상관없이 배치 상태 — 유형 비면 선 그리기로 빠지던 버그 막음. 패밀리 없으면 클릭 때 상태줄로 알림.
    public bool IsPlacingFitting => IsDrawMode && (_isPlacingBypassSet || !string.IsNullOrWhiteSpace(PlacingFamilyTypeName));
    public bool IsFittingPreviewVisible { get => _isFittingPreviewVisible; private set => SetProperty(ref _isFittingPreviewVisible, value); }
    public double FittingPreviewX { get => _fittingPreviewX; private set => SetProperty(ref _fittingPreviewX, value); }
    public double FittingPreviewY { get => _fittingPreviewY; private set => SetProperty(ref _fittingPreviewY, value); }
    public double FittingDimX1 { get => _fittingDimX1; private set => SetProperty(ref _fittingDimX1, value); }
    public double FittingDimY1 { get => _fittingDimY1; private set => SetProperty(ref _fittingDimY1, value); }
    public double FittingDimX2 { get => _fittingDimX2; private set => SetProperty(ref _fittingDimX2, value); }
    public double FittingDimY2 { get => _fittingDimY2; private set => SetProperty(ref _fittingDimY2, value); }
    public string FittingDimLabel { get => _fittingDimLabel; private set => SetProperty(ref _fittingDimLabel, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    /// <summary>창 단위 안전망(PipeLayoutView의 Dispatcher.UnhandledException)이 잡은 예외를 상태줄에 알린다(docs/235 § B).
    /// 편집 도중 끊겼을 수 있으므로 그리기·드래그 상태를 정리한다.</summary>
    /// <summary>생성 외 요청(선택·피킹)의 거부·실패 문구를 상태줄에 띄운다(docs/239 § C).
    /// 종전에는 <see cref="ApplyModelCreationResult(bool, string)"/>로 보내 "배관 생성 실패:"가 붙고 오류 마커·생성 상태가 초기화됐다.</summary>
    public void ApplyRequestError(string message) => Status = message;

    /// <summary>설정 창(PipeLayoutFamilySettingsView)의 숫자 칸 변환 오류 수(docs/239 § A). 설정 창이 올리고 내리며, 창을 닫으면 0으로 되돌린다 —
    /// 오류 칸의 값은 VM에 들어오지 않았으므로 닫은 뒤에는 막을 이유가 없다. 본창 <c>_inputValidationErrorCount</c>와 따로 센다.</summary>
    public int SettingsInputErrorCount
    {
        get => _settingsInputErrorCount;
        set { _settingsInputErrorCount = Math.Max(0, value); CommandManager.InvalidateRequerySuggested(); }
    }
    private int _settingsInputErrorCount;

    public void ReportUnhandledError(Exception ex)
    {
        CancelDrawing();
        CancelDrag();
        RefreshGraph();
        Status = ErrorText.Format(ex);
    }

    public void HandleCanvasClick(Point point)
    {
        // 생성 대기 중에는 그래프를 바꾸는 입력을 받지 않는다(docs/235 § D). 줌·팬·선택은 허용한다.
        if (IsCreating) return;
        // 분할은 선택 모드에서 시작하므로 IsDrawMode 가드보다 먼저 받아야 한다(docs/179 § H-4).
        if (IsSplittingEdge) { CommitSplitEdge(point); return; }
        if (!IsDrawMode) return;
        if (IsPlacingFitting) { PlacePreviewedFitting(); return; }
        if (_segmentStart is null)
        {
            var rawStart = Transform.ToModel(point);
            var snappedStart = FindSnapPoint(rawStart)?.Point ?? rawStart;
            var screenStart = Transform.ToScreen(snappedStart);
            SetSegmentStart(screenStart);
            PreviewX1 = PreviewX2 = screenStart.X; PreviewY1 = PreviewY2 = screenStart.Y;
            PreviewLengthX = screenStart.X; PreviewLengthY = screenStart.Y; PreviewLength = "0.000 m"; PreviewAngle = "0.0°"; IsPreviewVisible = true;
            IsSubLineVisible = false;
            Status = "끝점 클릭 → 배관 확정";
            return;
        }
        var start = Transform.ToModel(_segmentStart.Value);
        var raw = Transform.ToModel(point);
        var end = ResolveDrawingEnd(start, raw);
        // Core가 100mm 이하 선을 조용히 버린다(PipeTopologyBuilder.AddSegment). 버렸다고 알린다(docs/239 § C).
        var tooShort = start.DistanceTo(end) <= PipeTopologyBuilder.SnapTolerance;
        var before = Snapshot();
        // 선택이 남아 있으면 DiameterMm 프로퍼티는 선택 엣지 값을 돌려준다. 작도는 반드시 기본 관경을 쓴다(docs/152 § D-4).
        _network.AddSegment(start, end, _diameterMm);
        BeginEditing();
        RecordUndoIfChanged(before);
        SetSegmentStart(null); IsPreviewVisible = false; IsSubLineVisible = false; IsSnapMarkerVisible = false; TempDimensions.Clear();
        RefreshGraph();
        Status = tooShort
            ? $"{PipeTopologyBuilder.SnapTolerance:0}mm 이하 선 — 무시"
            : "배관 추가 · 기존 선 중간 클릭 → T 접점";
    }

    public void HandleCanvasMove(Point point)
    {
        var raw = Transform.ToModel(point);
        var drawingStart = _segmentStart is null ? null : Transform.ToModel(_segmentStart.Value);
        // 분할 중에도 스냅 마커가 보여야 어디서 끊기는지 알 수 있다(docs/179 § H-5).
        var snapped = IsDrawMode || IsSplittingEdge ? FindSnapPoint(raw, drawingStart) : null;
        var cursor = Transform.ToScreen(snapped?.Point ?? raw);
        CursorX = cursor.X;
        CursorY = cursor.Y;
        IsCursorVisible = true;
        // 분할 표식은 스냅 표식보다 먼저 갱신한다 — 스냅이 안 잡혀도(아래 else에서 꺼진다) 표식은 계속 보여야 한다.
        if (IsSplittingEdge) UpdateSplitMarker(point); else IsSplitMarkerVisible = false;
        if (IsPlacingFitting && _segmentStart is null)
        {
            // 바이패스 세트만 OSNAP을 쓴다(docs/259). 최근접(Nearest)은 배관 위 최근접점 자체라 격자 반올림보다 못하므로 뺀다.
            // 일반 밸브·이음관은 종전처럼 격자 반올림뿐이다.
            var setSnap = _isPlacingBypassSet && snapped is { Kind: not PipeSnapKind.Nearest } ? snapped : null;
            UpdateFittingPreview(raw, setSnap?.Point);
            UpdateSnapMarker(IsFittingPreviewVisible ? setSnap : null);
            return;
        }
        HideFittingPreview();
        UpdateSnapMarker(snapped);

        if (drawingStart is null) { TempDimensions.Clear(); return; }
        var end = ResolveDrawingEnd(drawingStart, raw);
        UpdateTempDimensions(end, drawingStart);   // 임시치수는 그리는 중에만 표시한다
        var startScreen = Transform.ToScreen(drawingStart);
        var screen = Transform.ToScreen(end);
        PreviewX1 = startScreen.X; PreviewY1 = startScreen.Y; PreviewX2 = screen.X; PreviewY2 = screen.Y;
        UpdatePreviewSubLines(drawingStart, end);
        PreviewLengthX = (PreviewX1 + PreviewX2) / 2; PreviewLengthY = (PreviewY1 + PreviewY2) / 2; PreviewLength = $"{drawingStart.DistanceTo(end) / 1000:N3} m";
        var angle = Math.Atan2(end.Y - drawingStart.Y, end.X - drawingStart.X) * 180.0 / Math.PI;
        if (angle < 0) angle += 360;
        PreviewAngle = $"{angle:N1}°";
        IsPreviewVisible = true;
        Status = "끝점 클릭 → 배관 확정";
    }

    public void HandleCanvasLeave()
    {
        IsCursorVisible = false;
        IsSnapMarkerVisible = false;
        IsSplitMarkerVisible = false;
    }

    /// <summary>작도 중 끝점을 구한다. 길이 상한은 없다 — 정척 분할은 배치 단계(PipeSegmentPlan)가 한다(docs/204 § A).</summary>
    private DHBIMWATER.Core.Geometry.Point2D ResolveDrawingEnd(
        DHBIMWATER.Core.Geometry.Point2D start,
        DHBIMWATER.Core.Geometry.Point2D raw)
    {
        var snapped = FindSnapPoint(raw, start);
        return snapped?.Point ?? Constrain(start, raw);
    }

    /// <summary>OSNAP으로 스냅되지 않은 자유 좌표를 <see cref="GridSnapMm"/> 격자에 맞춘다.
    /// 각도 스냅이 켜져 있으면 각도는 그대로 두고 길이만 반올림하고, 꺼져 있으면 X·Y를 각각 반올림한다.
    /// OSNAP 결과(끝점·중간점 등 기존 지오메트리에 정확히 붙는 점)에는 적용하지 않는다(호출부에서 snapped ?? Constrain(...) 순서로 보장).</summary>
    private DHBIMWATER.Core.Geometry.Point2D Constrain(DHBIMWATER.Core.Geometry.Point2D start, DHBIMWATER.Core.Geometry.Point2D end)
    {
        var dx = end.X - start.X; var dy = end.Y - start.Y; var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < double.Epsilon) return end;
        if (!UseAngleSnap)
        {
            var x = start.X + Math.Round(dx / GridSnapMm) * GridSnapMm;
            var y = start.Y + Math.Round(dy / GridSnapMm) * GridSnapMm;
            return new(x, y);
        }
        var angle = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * Math.PI / 4;
        var snappedLength = Math.Round(length / GridSnapMm) * GridSnapMm;
        return new(start.X + snappedLength * Math.Cos(angle), start.Y + snappedLength * Math.Sin(angle));
    }

    /// <summary>커서에 가장 가까운 엣지 위의 점을 찾고(허용오차 <see cref="PipeTopologyBuilder.SnapTolerance"/>),
    /// 엣지 시작점부터의 거리를 <see cref="GridSnapMm"/> 격자로 반올림해 배치 미리보기를 갱신한다.
    /// <para><paramref name="snapPoint"/>가 있으면(바이패스 세트 OSNAP, docs/259) 격자 대신 그 점을 커서 최근접 엣지 위로
    /// 수직 투영한 자리를 쓴다. 엣지는 여전히 커서 기준으로 고른다 — 스냅점이 배관 밖(벽 모서리·기준점)일 수 있다.</para></summary>
    private void UpdateFittingPreview(DHBIMWATER.Core.Geometry.Point2D cursor, DHBIMWATER.Core.Geometry.Point2D? snapPoint = null)
    {
        var target = FindNearestEdgePoint(cursor);
        if (target is null) { HideFittingPreview(); return; }
        if (snapPoint is not null)
        {
            // T 절점에 잡히면 본관 엣지에 올린다 — 동거리 동률에서 분기 엣지가 뽑히면 세트 축이 분기 방향이 된다(docs/273 § C-1).
            var snapEdge = _isPlacingBypassSet ? PreferTeeMainEdge(snapPoint, cursor, target.Value.Edge) : target.Value.Edge;
            var edgeStart = _network.Nodes.First(x => x.Id == snapEdge.StartNodeId).Position;
            var edgeEnd = _network.Nodes.First(x => x.Id == snapEdge.EndNodeId).Position;
            var snapT = Math.Clamp(Segment2D.ParameterOnSegment(snapPoint, edgeStart, edgeEnd), 0, 1);
            target = (snapEdge, snapT, new DHBIMWATER.Core.Geometry.Point2D(
                edgeStart.X + (edgeEnd.X - edgeStart.X) * snapT, edgeStart.Y + (edgeEnd.Y - edgeStart.Y) * snapT), edgeStart);
        }
        var (edge, t, point, startPosition) = target.Value;
        _fittingPreviewEdgeId = edge.Id; _fittingPreviewT = t;
        _fittingPreviewCursor = cursor;
        var screen = Transform.ToScreen(point);
        FittingPreviewX = screen.X; FittingPreviewY = screen.Y;
        IsFittingPreviewVisible = true;
        if (_isPlacingBypassSet)
        {
            // 개략 모양 — 실제 치수는 Revit이 패밀리를 재서 정한다(docs/228 § 0-5).
            var side = BypassSideAt(edge, cursor);
            (BypassPreviewLine, BypassPreviewMarkers) = BuildBypassGeometry(point, EdgeDirection(edge), side, edge.DiameterMm,
                BypassPreviewDiameterMm(edge.DiameterMm), _bypassValveCount);   // 핸드북 지관 관경, 없으면 개략 추정값(docs/256 § D-3)
            IsBypassPreviewVisible = true;
        }

        var dimStart = Transform.ToScreen(startPosition);
        FittingDimX1 = dimStart.X; FittingDimY1 = dimStart.Y; FittingDimX2 = screen.X; FittingDimY2 = screen.Y;
        FittingDimLabel = $"{point.DistanceTo(startPosition):N0} mm";
        // 표에 없는 주관이어도 배치는 막지 않는다 — 모델 생성 때 건너뛴다(2026-10-01 사용자 결정, docs/256 § D-3).
        Status = _isPlacingBypassSet
            ? "클릭 → 바이패스 세트 (커서 쪽 분기) · Esc 종료"
              + (HandbookBypassDiameterMm(edge.DiameterMm) is null ? " · 이 주관은 핸드북 규격이 없어 생성 시 건너뜁니다" : "")
            : "클릭 → 부속 배치 · Esc 취소";
    }

    /// <summary>스냅 표식 갱신. 바이패스 세트 배치(docs/259)와 그리기 모드가 같이 쓴다 — 표식은 투영 전 원래 스냅점에 띄운다.</summary>
    private void UpdateSnapMarker(PipeSnapResult? snapped)
    {
        if (snapped is null) { IsSnapMarkerVisible = false; return; }
        var marker = Transform.ToScreen(snapped.Value.Point);
        SnapMarkerX = marker.X;
        SnapMarkerY = marker.Y;
        var isOrigin = snapped.Value.Point.DistanceTo(new DHBIMWATER.Core.Geometry.Point2D(0, 0)) <= double.Epsilon;
        // 기준점 OSNAP만 +로 표시한다. 교차 OSNAP은 원점에서도 ×를 유지한다.
        SnapMarkerSymbol = isOrigin && SnapReferencePoint ? "+" : GetSnapMarkerSymbol(snapped.Value);
        SnapMarkerLabel = isOrigin && SnapReferencePoint ? "기준점" : GetSnapMarkerLabel(snapped.Value);
        IsSnapMarkerVisible = true;
    }

    private void HideFittingPreview()
    {
        IsBypassPreviewVisible = false;
        if (!IsFittingPreviewVisible) return;
        IsFittingPreviewVisible = false;
        _fittingPreviewEdgeId = null;
    }

    /// <summary>스냅 점이 본관 직선 T 절점이면 본관 두 엣지 중 커서에 가까운 쪽을 돌려준다. 그 외에는 <paramref name="picked"/> 그대로.</summary>
    private PipeEdge PreferTeeMainEdge(
        DHBIMWATER.Core.Geometry.Point2D snapPoint, DHBIMWATER.Core.Geometry.Point2D cursor, PipeEdge picked)
    {
        var node = _network.Nodes.FirstOrDefault(x => x.NodeKind == NodeKind.Tee && x.Position.DistanceTo(snapPoint) <= 0.01);
        if (node is null || _network.ResolveTeeMainEdges(node.Id) is not { } main) return picked;
        if (picked.Id == main.First || picked.Id == main.Second) return picked;
        double Distance(Guid id)
        {
            var edge = _network.Edges.First(x => x.Id == id);
            return DistanceToSegment(cursor, _network.Nodes.First(x => x.Id == edge.StartNodeId).Position,
                _network.Nodes.First(x => x.Id == edge.EndNodeId).Position);
        }
        var chosen = Distance(main.First) <= Distance(main.Second) ? main.First : main.Second;
        return _network.Edges.First(x => x.Id == chosen);
    }

    private (PipeEdge Edge, double T, DHBIMWATER.Core.Geometry.Point2D Point, DHBIMWATER.Core.Geometry.Point2D StartPosition)? FindNearestEdgePoint(DHBIMWATER.Core.Geometry.Point2D cursor)
    {
        PipeEdge? bestEdge = null; double bestT = 0, bestDistance = double.MaxValue;
        DHBIMWATER.Core.Geometry.Point2D? bestStart = null, bestEnd = null;
        foreach (var edge in _network.Edges)
        {
            var start = _network.Nodes.First(x => x.Id == edge.StartNodeId).Position;
            var end = _network.Nodes.First(x => x.Id == edge.EndNodeId).Position;
            var t = Math.Clamp(Segment2D.ParameterOnSegment(cursor, start, end), 0, 1);
            var point = new DHBIMWATER.Core.Geometry.Point2D(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t);
            var distance = point.DistanceTo(cursor);
            if (distance > PipeTopologyBuilder.SnapTolerance || distance >= bestDistance) continue;
            bestEdge = edge; bestT = t; bestDistance = distance; bestStart = start; bestEnd = end;
        }
        if (bestEdge is null) return null;

        var startPosition = bestStart!;
        var endPosition = bestEnd!;
        var length = startPosition.DistanceTo(endPosition);
        if (length < double.Epsilon) return (bestEdge, bestT, startPosition, startPosition);
        var distanceFromStart = length * bestT;
        var snappedDistance = Math.Clamp(Math.Round(distanceFromStart / GridSnapMm) * GridSnapMm, 0, length);
        var snappedT = snappedDistance / length;
        var snappedPoint = new DHBIMWATER.Core.Geometry.Point2D(startPosition.X + (endPosition.X - startPosition.X) * snappedT, startPosition.Y + (endPosition.Y - startPosition.Y) * snappedT);
        return (bestEdge, snappedT, snappedPoint, startPosition);
    }

    private void PlacePreviewedFitting()
    {
        if (_isPlacingBypassSet)
        {
            // 본관 밖 클릭 — 선 그리기로 안 넘기고 안내만
            if (_fittingPreviewEdgeId is null) { Status = "바이패스 세트는 본관 선 위를 클릭해 놓습니다 · Esc 종료"; return; }
            PlacePreviewedBypassSet();
            return;
        }
        if (_fittingPreviewEdgeId is null || PlacingFamilyTypeName is not { } placingFamily) return;
        var before = Snapshot();
        if (!TryEditNetwork(before, () => _network.AddInlineFitting(_fittingPreviewEdgeId.Value, PlacingTypeKey, placingFamily, _fittingPreviewT))) return;
        BeginEditing();
        RecordUndoIfChanged(before);
        RefreshGraph();
        Status = "부속 배치 · 계속 클릭 또는 Esc 종료";
    }

    /// <summary>클릭 한 번으로 그래프를 바꾸는 편집을 감싼다(docs/235 § B). Core는 규칙 위반을 예외로 알린다 —
    /// 예: 짧은 선에 부속을 두 개 넣으면 <c>PL-113</c>(PipeTopologyBuilder.FindAvailableFittingT).
    /// 잡지 않으면 WPF 이벤트 핸들러 밖으로 새어 Revit 메시지 루프까지 올라간다. 실패하면 편집 전 그래프로 되돌리고 상태줄에 알린다.</summary>
    private bool TryEditNetwork((PipeNetwork Network, IReadOnlyList<long> ImportedSourceIds, IReadOnlyList<PipePort> Ports) before, Action edit)
    {
        try
        {
            edit();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _network = before.Network;
            RefreshGraph();
            Status = ErrorText.Format(ex);
            return false;
        }
    }
    public void HandleCanvasSizeChanged(double width, double height)
    {
        if (width <= 0 || height <= 0) return;
        var drawingStart = _segmentStart is null ? null : Transform.ToModel(_segmentStart.Value);
        var previewEnd = _segmentStart is not null && IsPreviewVisible
            ? Transform.ToModel(new Point(PreviewX2, PreviewY2))
            : null;
        if (!_hasCanvasSize)
        {
            Transform.PanOrigin = new Point(width / 2, height / 2);
            _hasCanvasSize = true;
        }
        else
        {
            Transform.PanOrigin = new Point(
                Transform.PanOrigin.X + (width - _canvasWidth) / 2,
                Transform.PanOrigin.Y + (height - _canvasHeight) / 2);
        }
        _canvasWidth = width; _canvasHeight = height;
        RepositionDrawingPreview(drawingStart, previewEnd);
        IsSnapMarkerVisible = false;
        RefreshView();
    }

    public void HandleCanvasZoom(Point cursor, int wheelDelta)
    {
        if (wheelDelta == 0) return;
        var drawingStart = _segmentStart is null ? null : Transform.ToModel(_segmentStart.Value);
        var previewEnd = _segmentStart is not null && IsPreviewVisible
            ? Transform.ToModel(new Point(PreviewX2, PreviewY2))
            : null;
        var modelUnderCursor = Transform.ToModel(cursor);
        var factor = Math.Pow(1.1, wheelDelta / 120.0);
        Transform.PixelsPerMillimeter = Math.Clamp(Transform.PixelsPerMillimeter * factor, 0.005, 2.0);
        var screenAfter = Transform.ToScreen(modelUnderCursor);
        Transform.PanOrigin = new Point(
            Transform.PanOrigin.X + (cursor.X - screenAfter.X),
            Transform.PanOrigin.Y + (cursor.Y - screenAfter.Y));
        RepositionDrawingPreview(drawingStart, previewEnd);
        CursorX = cursor.X;
        CursorY = cursor.Y;
        IsCursorVisible = true;
        IsSnapMarkerVisible = false;
        RefreshViewOnly();   // 화면 변환만 — 검증 생략(docs/239 § D)
    }

    public void HandleCanvasPan(double dxPixels, double dyPixels)
    {
        if (dxPixels == 0 && dyPixels == 0) return;
        var drawingStart = _segmentStart is null ? null : Transform.ToModel(_segmentStart.Value);
        var previewEnd = _segmentStart is not null && IsPreviewVisible
            ? Transform.ToModel(new Point(PreviewX2, PreviewY2))
            : null;
        Transform.PanOrigin = new Point(Transform.PanOrigin.X + dxPixels, Transform.PanOrigin.Y + dyPixels);
        RepositionDrawingPreview(drawingStart, previewEnd);
        CursorX += dxPixels;
        CursorY += dyPixels;
        IsCursorVisible = true;
        IsSnapMarkerVisible = false;
        RefreshViewOnly();   // 화면 변환만 — 검증 생략(docs/239 § D)
    }

    private void RepositionDrawingPreview(
        DHBIMWATER.Core.Geometry.Point2D? drawingStart,
        DHBIMWATER.Core.Geometry.Point2D? previewEnd)
    {
        if (drawingStart is null) { IsSubLineVisible = false; return; }
        var startScreen = Transform.ToScreen(drawingStart);
        _segmentStart = startScreen;
        PreviewX1 = startScreen.X;
        PreviewY1 = startScreen.Y;
        if (previewEnd is null) { IsSubLineVisible = false; return; }
        var endScreen = Transform.ToScreen(previewEnd);
        PreviewX2 = endScreen.X;
        PreviewY2 = endScreen.Y;
        UpdatePreviewSubLines(drawingStart, previewEnd);
        PreviewLengthX = (PreviewX1 + PreviewX2) / 2;
        PreviewLengthY = (PreviewY1 + PreviewY2) / 2;
        UpdateTempDimensions(previewEnd, drawingStart);
    }

    private void RefreshView()
    {
        OnPropertyChanged(nameof(ReferenceScreenX));
        OnPropertyChanged(nameof(ReferenceScreenY));
        RefreshGraph();
        RefreshOutline();
    }

    private PipeSnapResult? FindSnapPoint(
        DHBIMWATER.Core.Geometry.Point2D point,
        DHBIMWATER.Core.Geometry.Point2D? drawingStart = null)
    {
        var mode = PipeSnapMode.None;
        if (SnapEndpoint) mode |= PipeSnapMode.Endpoint;
        if (SnapMidpoint) mode |= PipeSnapMode.Midpoint;
        if (SnapQuadrant) mode |= PipeSnapMode.Quadrant;
        if (SnapIntersection) mode |= PipeSnapMode.Intersection;
        if (SnapPerpendicular) mode |= PipeSnapMode.Perpendicular;
        if (SnapNearest) mode |= PipeSnapMode.Nearest;
        var networkSnap = _network.FindSnapPoint(point, mode, drawingStart);
        var referencePoint = new DHBIMWATER.Core.Geometry.Point2D(0, 0);
        PipeSnapResult? referenceSnap = (SnapReferencePoint || SnapIntersection) && point.DistanceTo(referencePoint) <= PipeTopologyBuilder.SnapTolerance
            ? new PipeSnapResult(referencePoint, PipeSnapKind.Intersection, point.DistanceTo(referencePoint))
            : null;

        // 종류 우선순위, 같은 종류에서는 거리 순으로 고른다.
        PipeSnapResult? best = null;
        foreach (var candidate in OutlineSnapCandidates(point).Concat(ArrowSnapCandidates(point)).Concat(ReferenceLineSnapCandidates(point, drawingStart)).Append(networkSnap).Append(referenceSnap))
        {
            if (candidate is null) continue;
            if (best is null
                || PipeTopologyBuilder.Rank(candidate.Value.Kind) < PipeTopologyBuilder.Rank(best.Value.Kind)
                || PipeTopologyBuilder.Rank(candidate.Value.Kind) == PipeTopologyBuilder.Rank(best.Value.Kind)
                    && candidate.Value.Distance < best.Value.Distance)
                best = candidate;
        }
        return best;
    }

    /// <summary>연두색 X/Y 기준선 OSNAP 후보. 두 기준선은 캔버스 모델 좌표의 X=0, Y=0이다.</summary>
    private IEnumerable<PipeSnapResult?> ReferenceLineSnapCandidates(
        DHBIMWATER.Core.Geometry.Point2D point,
        DHBIMWATER.Core.Geometry.Point2D? drawingStart = null)
    {
        // 기준선 무한 직선. 끝점·중간점·사분점 없음.
        if (SnapNearest && Math.Abs(point.X) <= PipeTopologyBuilder.SnapTolerance)
            yield return new PipeSnapResult(new DHBIMWATER.Core.Geometry.Point2D(0, point.Y), PipeSnapKind.Nearest, Math.Abs(point.X));
        if (SnapNearest && Math.Abs(point.Y) <= PipeTopologyBuilder.SnapTolerance)
            yield return new PipeSnapResult(new DHBIMWATER.Core.Geometry.Point2D(point.X, 0), PipeSnapKind.Nearest, Math.Abs(point.Y));
        if (SnapPerpendicular && drawingStart is not null)
        {
            var perpendicularToYAxis = new DHBIMWATER.Core.Geometry.Point2D(0, drawingStart.Y);
            var perpendicularToXAxis = new DHBIMWATER.Core.Geometry.Point2D(drawingStart.X, 0);
            var xAxisDistance = point.DistanceTo(perpendicularToXAxis);
            var yAxisDistance = point.DistanceTo(perpendicularToYAxis);
            if (xAxisDistance <= PipeTopologyBuilder.SnapTolerance)
                yield return new PipeSnapResult(perpendicularToXAxis, PipeSnapKind.Perpendicular, xAxisDistance);
            if (yAxisDistance <= PipeTopologyBuilder.SnapTolerance)
                yield return new PipeSnapResult(perpendicularToYAxis, PipeSnapKind.Perpendicular, yAxisDistance);
        }
    }

    private IEnumerable<PipeSnapResult?> OutlineSnapCandidates(DHBIMWATER.Core.Geometry.Point2D point)
    {
        if (!SnapOutline || _canvasOutline.IsEmpty) yield break;
        var candidates = Enumerable.Empty<(DHBIMWATER.Core.Geometry.Point2D Point, PipeSnapKind Kind)>();
        if (SnapEndpoint) candidates = candidates.Concat(_canvasOutline.Endpoints().Select(x => (x, PipeSnapKind.Endpoint)));
        if (SnapMidpoint) candidates = candidates.Concat(_canvasOutline.Midpoints().Select(x => (x, PipeSnapKind.Midpoint)));
        if (SnapNearest) candidates = candidates.Concat(_canvasOutline.NearestPoints(point).Select(x => (x, PipeSnapKind.Nearest)));
        foreach (var candidate in candidates)
        {
            var distance = point.DistanceTo(candidate.Point);
            if (distance <= PipeTopologyBuilder.SnapTolerance)
                yield return new PipeSnapResult(candidate.Point, candidate.Kind, distance);
        }
    }

    /// <summary>IN/OUT 화살표의 벽면 접점을 끝점 스냅 후보로 낸다.</summary>
    private IEnumerable<PipeSnapResult?> ArrowSnapCandidates(DHBIMWATER.Core.Geometry.Point2D point)
    {
        if (!SnapEndpoint) yield break;
        foreach (var anchor in _arrowAnchors)
        {
            var distance = point.DistanceTo(anchor);
            if (distance <= PipeTopologyBuilder.SnapTolerance)
                yield return new PipeSnapResult(anchor, PipeSnapKind.Endpoint, distance);
        }
    }

    private void PickOutline()
    {
        CancelDrawing();
        Status = "Revit에서 외곽 벽체·기둥 선택";
        PickOutlineAction?.Invoke();
    }

    /// <summary>피킹 결과를 반영한다. <paramref name="outline"/>가 null이면 사용자가 취소한 것으로 본다.</summary>
    public void ApplyOutline(ValveRoomOutline? outline)
    {
        if (outline is null)
        {
            Status = "외곽 선택 취소";
            return;
        }
        // 기둥만 골랐다면 외곽 바운딩박스를 잡을 수 없다 — 취소와 구분해 이유를 알린다(docs/184 § E-4).
        if (outline.IsEmpty)
        {
            Status = "벽체 1장 이상 필요 — 기둥만으로는 외곽 불가";
            return;
        }
        _outline = outline;
        ApplyAutomaticElevationLevel(outline);

        // 실내 중심이 원점(0,0)에 오도록 기준점을 자동 설정한다. 모델 생성 시 이 값이 다시 더해져 원위치된다.
        var centroid = outline.Centroid;
        _referenceX = Math.Round(centroid.X, 1); OnPropertyChanged(nameof(ReferenceX));
        _referenceY = Math.Round(centroid.Y, 1); OnPropertyChanged(nameof(ReferenceY));

        // 화살표 상하 위치 기본값: 편집 안 한 포트를 변별로 벽 중심 기준 균등 배치(docs/174 § A-1). 1개면 중앙 — 종전 "내부 높이의 절반"과 같다.
        // 사용자가 직접 입력한 값은 덮어쓰지 않는다(확정 사항). SetOffsetSilently라 UserEdited는 서지 않는다.
        // _canvasOutline은 아래 RefreshView()→RefreshOutline()에서야 갱신되므로 여기서는 방금 받은 원본 outline을 넘긴다(길이는 평행이동과 무관).
        // 변 길이는 SideLengthOf가 고르므로 Top/Bottom이 생겨도 그대로 쓴다.
        foreach (var side in Ports.Select(x => x.Side).Distinct().ToList()) DistributePortsEvenly(side, outline);
        OnPropertyChanged(nameof(InOffsetMm)); OnPropertyChanged(nameof(OutOffsetMm));

        // 벽에서 유형을 읽었으면 채운다. 혼재·미기록이면 outline.Zone이 빈 문자열이므로 사용자가 직접 고른다(docs/161 확정 2).
        // 빈 값으로 덮지 않는다 — 고른 뒤 다시 피킹했다고 선택이 지워지면 안 된다.
        if (!string.IsNullOrWhiteSpace(outline.Zone)) ValveRoomZone = outline.Zone;

        Transform.PanOrigin = new Point(_canvasWidth / 2, _canvasHeight / 2);
        ZoomToFit(outline);
        RefreshView();
        // Columns는 단면 목록이라 항목 수가 곧 기둥 본 수가 아니다 — ElementId로 센다(docs/189 § C-3).
        var columnCount = outline.Columns.Select(c => c.ElementId).Distinct().Count();
        var columnSuffix = columnCount > 0 ? $" · 기둥 {columnCount}본" : string.Empty;
        columnSuffix += outline.Foundations.Count > 0 ? $" · 기초 {outline.Foundations.Count}개" : string.Empty;
        Status = $"외곽 반영 — 벽체 {outline.Walls.Count}장{columnSuffix}"
            + $" · 내부 {outline.WidthMm / 1000:N2} × {outline.HeightMm / 1000:N2} m"
            + ZoneStatusSuffix(outline)
            + ElevationLevelStatusSuffix(outline);
    }

    /// <summary>프로젝트 저장본에서 복원한 외곽을 반영한다(docs/204 § D-3).
    /// <para><see cref="ApplyOutline"/>과 달리 기준점과 포트 이격을 덮지 않는다.</para></summary>
    public void ApplyRestoredOutline(ValveRoomOutline? outline, int missingCount)
    {
        if (outline is null || outline.IsEmpty)
        {
            ElevationLevelHint = string.Empty;
            Status = missingCount > 0
                ? $"외곽 복원 실패 — 저장 부재 {missingCount}개 없음 · [기존 객체 가져오기]로 재지정"
                : "저장된 외곽 없음";
            if (_savedElevationLevelMissing) Status += " · 저장된 기준 레벨 없음 — 다시 선택";
            return;
        }

        _outline = outline;
        if (!_savedElevationLevelIdSpecified) ApplyAutomaticElevationLevel(outline);
        if (!string.IsNullOrWhiteSpace(outline.Zone)) ValveRoomZone = outline.Zone;
        Transform.PanOrigin = new Point(_canvasWidth / 2, _canvasHeight / 2);
        ZoomToFit(outline);
        RefreshView();

        var columnCount = outline.Columns.Select(c => c.ElementId).Distinct().Count();
        var columnSuffix = columnCount > 0 ? $" · 기둥 {columnCount}본" : string.Empty;
        columnSuffix += outline.Foundations.Count > 0 ? $" · 기초 {outline.Foundations.Count}개" : string.Empty;
        var missingSuffix = missingCount > 0 ? $" (삭제 {missingCount}개 제외)" : string.Empty;
        Status = $"외곽 복원 — 벽체 {outline.Walls.Count}장{columnSuffix}{missingSuffix}"
            + $" · 내부 {outline.WidthMm / 1000:N2} × {outline.HeightMm / 1000:N2} m"
            + ZoneStatusSuffix(outline)
            + ElevationLevelStatusSuffix(outline)
            + (_savedElevationLevelMissing ? " · 저장된 기준 레벨 없음 — 다시 선택" : string.Empty);
    }

    private void ApplyAutomaticElevationLevel(ValveRoomOutline outline)
    {
        var id = PipeElevationReference.PickBaseLevelId(outline.Walls.Select(wall => wall.BaseLevelId), Levels, out _);
        if (id is { } levelId && Levels.FirstOrDefault(level => level.Id == levelId) is { } level)
            ElevationLevel = level;
    }

    private string ElevationLevelStatusSuffix(ValveRoomOutline outline)
    {
        if (outline.IsEmpty) { ElevationLevelHint = string.Empty; return string.Empty; }
        var id = PipeElevationReference.PickBaseLevelId(outline.Walls.Select(wall => wall.BaseLevelId), Levels, out var mixed);
        if (id is not { } levelId || Levels.FirstOrDefault(level => level.Id == levelId) is not { } level)
        {
            ElevationLevelHint = string.Empty;
            return string.Empty;
        }
        ElevationLevelHint = $"벽 하단 레벨: {level.Name}" + (mixed ? " (혼재 — 확인 필요)" : string.Empty);
        // 콤보 실제 선택만 표시. 비었는데 벽 레벨명 쓰면 선택된 걸로 오해함.
        var selected = ElevationLevel is { } current ? $" · 기준 레벨 {current.Name}" : " · 기준 레벨 미선택";
        return selected + (mixed ? " · 하단 레벨 혼재 — 확인 필요" : string.Empty);
    }

    private void SetElevationSilently(double value)
    {
        _elevation = value;
        _network.Elevation = value;
        OnPropertyChanged(nameof(Elevation));
        OnPropertyChanged(nameof(ElevationM));
    }

    private void SetOffsetFromCurrentElevation()
    {
        var offset = _elevation - ElevationLevel!.ElevationMm;
        if (SetProperty(ref _elevationOffsetMm, offset)) OnPropertyChanged(nameof(ElevationOffsetM));
    }

    private void RecalculateElevationOffsetFromCurrent()
    {
        if (ElevationLevel is not null) SetOffsetFromCurrentElevation();
    }

    /// <summary>외곽 피킹 결과 문구 뒤에 붙일 밸브실 유형 안내(docs/161 § F-2).</summary>
    private string ZoneStatusSuffix(ValveRoomOutline outline)
    {
        if (!string.IsNullOrWhiteSpace(outline.Zone)) return $" · 유형 {outline.Zone}";
        var distinct = outline.Walls.Select(x => x.Zone).Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal).Count();
        return distinct > 1 ? " · 밸브실 유형이 섞여 있어 직접 선택" : string.Empty;
    }

    // ── IN/OUT 포트 관리(docs/172 § C-4) ─────────────────────────────────────────────

    /// <summary>포트 항목을 목록에 넣고 이격 변경을 구독한다. 목록에 넣는 경로는 반드시 이 메서드를 거친다.
    /// 목록은 항상 IN, IN2, IN3…, OUT, OUT2… 순으로 유지한다(사용자 확정 2026-09-15) — 맨 뒤에 붙이지 않고 자리를 찾아 넣는다.</summary>
    private void AttachPort(PipePortItem item)
    {
        item.PropertyChanged += OnPortPropertyChanged;
        var key = PortSortKey(item);
        var index = 0;
        while (index < Ports.Count && PortSortKey(Ports[index]) <= key) index++;
        Ports.Insert(index, item);
    }

    /// <summary>정렬 키. 흐름(IN 먼저)이 상위, 번호가 하위.</summary>
    private static int PortSortKey(PipePortItem item)
        => (item.Flow == PortFlow.In ? 0 : 1_000_000) + PipePortDefaults.OrdinalOf(item.Flow, item.Label);

    private void DetachAllPorts()
    {
        foreach (var port in Ports) port.PropertyChanged -= OnPortPropertyChanged;
        Ports.Clear();
    }

    /// <summary>포트 이격이 바뀌면 화살표·스냅 앵커·검증을 다시 잡는다(종전 InOffsetMm 세터가 하던 일).</summary>
    private void OnPortPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PipePortItem.OffsetMm)) return;
        OnPropertyChanged(nameof(InOffsetMm));
        OnPropertyChanged(nameof(OutOffsetMm));
        RefreshOutline();
        NotifyValidationChanged();
    }

    /// <summary>IN은 좌측 벽, OUT은 우측 벽에 추가한다(docs/172 Q2). 이격은 같은 벽 포트를 N+1 등분해 자동 배치한다(docs/174, ApplyOutline과 같은 규칙).</summary>
    private void AddPort(PortFlow flow)
    {
        var ordinal = NextPortOrdinal(flow);
        var side = PipePortDefaults.SideFor(flow);
        // 초기값은 아무 값이나 — 바로 아래 DistributePortsEvenly가 덮는다(docs/174 § C-2).
        var port = PipePort.Create(flow, side, PipePortDefaults.DefaultOffsetMm, PipePortDefaults.LabelFor(flow, ordinal));
        AttachPort(new PipePortItem(port));
        UpdatePortRemovability();
        DistributePortsEvenly(side, _canvasOutline);   // 동작 변경(docs/174): 같은 벽의 미편집 포트가 함께 재배치된다
        RefreshOutline();
        NotifyValidationChanged();
        Status = $"{port.Label} 포트 추가 · 같은 벽 포트 균등 배치";
    }

    /// <summary>한 변의 미편집 포트를 벽 중심 기준 균등 위치로 다시 놓는다(docs/174 § A-1). 편집한 포트(UserEdited)는 자리만 세고 값은 두며,
    /// 자리(rank)는 목록 순서(IN, IN2, IN3…)로 센다. SetOffsetSilently라 UserEdited가 서지 않는다 — 호출자가 RefreshOutline()·NotifyValidationChanged()를 직접 부른다.</summary>
    /// <param name="outline">변 길이를 읽을 외곽. AddPort/RemovePort는 _canvasOutline, ApplyOutline은 방금 받은 원본 outline을 넘긴다 —
    /// _canvasOutline은 RefreshOutline() 안에서만 갱신되므로 ApplyOutline 시점에는 아직 이전 외곽이기 때문. 길이는 평행이동과 무관하므로 어느 쪽을 써도 값은 같다.</param>
    private void DistributePortsEvenly(OutlineSide side, ValveRoomOutline outline)
    {
        var onSide = Ports.Where(x => x.Side == side).ToList();   // Ports는 AttachPort가 정렬을 유지하므로 목록 순서가 곧 순위
        if (onSide.Count == 0) return;
        var length = outline.IsEmpty ? 0 : outline.SideLengthOf(side);
        // TODO: SetOffsetSilently는 포트마다 PropertyChanged→RefreshOutline을 한 번씩 태운다. 포트가 많아지면 알림을 묶는 편이 낫다.
        for (var i = 0; i < onSide.Count; i++)
        {
            if (onSide[i].UserEdited) continue;
            var offset = outline.IsEmpty ? PipePortDefaults.DefaultOffsetMm : PipePortDefaults.EvenOffsetMm(length, i + 1, onSide.Count);
            onSide[i].SetOffsetSilently(offset);
        }
    }

    /// <summary>삭제로 비어 있는 가장 작은 번호를 쓴다 — IN·IN2·IN3에서 IN2를 지우고 다시 추가하면 IN2.</summary>
    private int NextPortOrdinal(PortFlow flow)
    {
        var used = Ports.Where(x => x.Flow == flow).Select(x => x.Label).ToHashSet(StringComparer.Ordinal);
        var ordinal = 1;
        while (used.Contains(PipePortDefaults.LabelFor(flow, ordinal))) ordinal++;
        return ordinal;
    }

    private void RemovePort(PipePortItem? item)
    {
        if (item is null || !item.CanRemove || !Ports.Contains(item)) return;
        item.PropertyChanged -= OnPortPropertyChanged;
        Ports.Remove(item);
        UpdatePortRemovability();
        DistributePortsEvenly(item.Side, _canvasOutline);   // 동작 변경(docs/174 D3): 남은 미편집 포트가 다시 균등 배치된다
        RefreshOutline();
        NotifyValidationChanged();
        Status = $"{item.Label} 포트 삭제";
    }

    /// <summary>기본 포트(IN·OUT)는 지울 수 없고, 추가된 포트(IN2·OUT2…)만 지울 수 있다(사용자 확정 2026-09-15 — 종전 "흐름별 마지막 1개 보호" 규칙을 대체).
    /// 기본 포트는 지워지지 않으므로 흐름별 1개 이상이라는 전제도 그대로 지켜진다.</summary>
    private void UpdatePortRemovability()
    {
        foreach (var port in Ports) port.CanRemove = !PipePortDefaults.IsDefault(port.Flow, port.Label);
    }

    /// <summary>포트 목록을 통째로 바꾼다. Undo 복원과 추천 배치 적용이 쓴다.</summary>
    private void RestorePorts(IEnumerable<PipePort> ports, bool userEdited)
    {
        DetachAllPorts();
        foreach (var port in ports) AttachPort(new PipePortItem(port) { UserEdited = userEdited });
        UpdatePortRemovability();
        OnPropertyChanged(nameof(InOffsetMm));
        OnPropertyChanged(nameof(OutOffsetMm));
    }

    // ── 추천 배치(docs/172 § C-11) ────────────────────────────────────────────────────

    private bool IsAirValveRoom => string.Equals(ValveRoomZone, ValveRoomPipingTemplateCatalog.AirValveRoom, StringComparison.Ordinal);

    /// <summary>추천 배치 적용 시 상대 모드 Offset 기본값(mm, 2026-10-29 사용자 지정). 공기밸브실 제외.</summary>
    private const double TemplateElevationOffsetMm = 600;

    /// <summary>밸브실 유형의 프리셋을 캔버스에 그린다. "비우고 새로 그리기"라 Clear()와 같이 가져온 원본 연결을 끊고,
    /// 스냅샷 한 장으로 그래프·원본 목록·포트를 함께 되돌릴 수 있게 한다.</summary>
    private void ApplyTemplate()
    {
        // 타이밸브실 = 로드된 패밀리로 벽 여유 c 실측 후 그림(docs/323). 실측은 API 컨텍스트라 ExternalEvent 뒤에 적용
        var isTieRoom = string.Equals(ValveRoomZone, ValveRoomPipingTemplateCatalog.TieValveRoom, StringComparison.Ordinal);
        if (!isTieRoom || MeasureTieLayout is null) { ApplyTemplateCore(null, null); return; }
        if (RunInRevitContext is { } run) run(MeasureTieAndApply);
        else MeasureTieAndApply();
    }

    private void MeasureTieAndApply()
    {
        TieValveRoomLayout? layout = null;
        string? error = null;
        // Revit API 예외 형식이 여럿이라 전부 받음 — 실측 실패는 비율 배치 폴백일 뿐, 창 죽이면 안 됨
        try { layout = MeasureTieLayout?.Invoke(_diameterMm, _pipeMaterial, _steelFittingClass); }
        catch (Exception ex) { error = ex.Message; }
        ApplyTemplateCore(layout, error ?? (layout is null ? "실측 경로 없음" : null));
    }

    /// <param name="tie">타이밸브실 실측 결과. null + <paramref name="tieError"/> null = 실측 안 함(타이 아님·경로 없음).</param>
    private void ApplyTemplateCore(TieValveRoomLayout? tie, string? tieError)
    {
        var template = ValveRoomPipingTemplateCatalog.Default(ValveRoomZone);
        if (template is null || _canvasOutline.IsEmpty) return;
        // 타이밸브실: 실측 c로 위·아래 본관을 벽에서 띄움. 실패·c ≥ 폭/2면 비율 배치 폴백 + 상태 문구
        var tieNote = string.Empty;
        if (tie is not null)
        {
            var heightMm = _canvasOutline.HeightMm;
            if (ValveRoomPipingTemplateCatalog.TieWithWallClearance(_canvasOutline, tie.WallClearanceMm) is { } tieTemplate)
            {
                template = tieTemplate;
                tieNote = $" · 실측 벽 여유 {tie.WallClearanceMm:0} mm · 최소 폭 {tie.MinWidthMm:0} mm"
                    + (heightMm + 0.5 < tie.MinWidthMm
                        ? $" — 밸브실 내부 폭 {heightMm:0} mm가 최소 폭보다 작아 바이패스 세트가 빠질 수 있음"
                        : string.Empty);
            }
            else
                tieNote = $" · 실측 벽 여유 {tie.WallClearanceMm:0} mm가 내부 폭 {heightMm:0} mm의 절반 이상 — 비율(0.25/0.75) 배치로 그림(최소 폭 {tie.MinWidthMm:0} mm)";
        }
        else if (tieError is not null)
            tieNote = $" · 최소 폭 실측 실패({tieError}) — 비율(0.25/0.75) 배치로 그림";
        // 공기밸브실은 기초 본관 관통 Void 자리·관경·높이를 따른다(2026-10-29). Void 못 찾으면 고정 프리셋(중앙 직관) 폴백.
        var isAirRoom = IsAirValveRoom;
        var pipeVoid = isAirRoom ? _canvasOutline.MainPipeVoid : null;
        if (pipeVoid is not null && ValveRoomPipingTemplateCatalog.ForMainPipeVoid(template.Zone, _canvasOutline, pipeVoid, _diameterMm) is { } voidTemplate)
            template = voidTemplate;
        else pipeVoid = null;
        CancelDrawing();
        // 해석(Resolve)을 먼저 한다 — 종전에는 원본 연결 해제·선택 해제를 먼저 해서, 해석이 실패하면
        // Undo 기록 없이 "가져온 원본 연결"만 끊긴 채 남았다(docs/239 § C). Resolve는 캔버스를 바꾸지 않는다.
        ResolvedPipingTemplate resolved;
        try
        {
            resolved = ValveRoomPipingTemplateResolver.Resolve(template, _canvasOutline, ArrowFromTop, _diameterMm);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            Status = UiErrors.Report("PipeLayout", ex);
            return;
        }

        BeginEditing();
        var before = Snapshot();
        ClearSelection();
        ForgetReplaceTargets();
        _network.Clear();
        var dropped = 0;
        foreach (var segment in resolved.Segments)
        {
            // AddSegment는 100mm 이하 선을 버린다(PipeTopologyBuilder.SnapTolerance). 조용히 사라지지 않게 센다.
            if (segment.Start.DistanceTo(segment.End) <= PipeTopologyBuilder.SnapTolerance) { dropped++; continue; }
            _network.AddSegment(segment.Start, segment.End, segment.DiameterMm);
        }
        // 프리셋 부속(밸브·바이패스 세트) — 소속 선을 양 끝 좌표로 다시 찾는다.
        var placedFittings = 0;
        var placedBypassSets = 0;
        var unassignedValves = 0;
        var gateFamilyName = ValveFamilyNames.FirstOrDefault(x => x.Name.Contains("게이트", StringComparison.Ordinal))?.Name;
        var butterflyFamilyName = ValveFamilyNames.FirstOrDefault(x => x.Name.Contains("버터플라이", StringComparison.Ordinal))?.Name;
        foreach (var fitting in resolved.Fittings ?? [])
        {
            var edge = FindEdgeBetween(fitting.SegmentStart, fitting.SegmentEnd);
            if (edge is null) continue;
            if (fitting.IsBypassSet)
            {
                // 2개 세트 프리셋 = V2 = V1 패밀리, 단관 기본 300(docs/322). T 몸통·세트 안 밸브 폭 보정(docs/297 § G)이 2개 세트만 됨.
                var bypassFamily = BypassValveFamilyName ?? string.Empty;
                var second = fitting.TwoValves && bypassFamily.Length > 0 ? bypassFamily : null;
                _network.AddInlineFitting(edge.Id, BypassSet.TypeKey, string.Empty, fitting.T, preservePosition: true,
                    bypass: new BypassSetSpec(bypassFamily, 0, fitting.Side, second, second is null ? 0 : BypassSet.DefaultSpoolLengthMm));
                placedBypassSets++;
            }
            else
            {
                // 버터플라이 역할 = 버터플라이 패밀리, 나머지 = 게이트(종전)
                var familyName = fitting.Role == ValveRole.Butterfly ? butterflyFamilyName : gateFamilyName;
                var familyTypeName = _accessoryFamilyTypeNames.FirstOrDefault(x =>
                    familyName is not null && GetFamilyName(x) == familyName
                    && NominalDiameterParser.Parse(x) is { } diameter
                    && Math.Abs(diameter - edge.DiameterMm) <= 0.5) ?? string.Empty;
                _network.AddInlineFitting(edge.Id, InlineFittingTypeKey, familyTypeName, fitting.T, preservePosition: true);
                if (familyTypeName.Length == 0) unassignedValves++;
            }
            placedFittings++;
        }
        // 프리셋이 정한 이격은 사용자 편집값으로 취급한다 — 이후 외곽 재피킹이 중앙값으로 덮지 않게(docs/172 § 0-4 A5).
        RestorePorts(resolved.Ports, userEdited: true);
        RecordUndoIfChanged(before);
        // 높이: 공기밸브실 = Void 중심(모드 무관, 상대 모드면 Offset 자동 재계산) / 그 외 = 상대 모드일 때만 Offset 600(덮어씀). 절대 모드는 안 건드림.
        if (pipeVoid is not null)
        {
            DiameterMm = pipeVoid.NominalDiameterMm;
            Elevation = pipeVoid.CenterElevationMm;
        }
        else if (!isAirRoom && ElevationMode == PipeElevationMode.Relative) ElevationOffsetMm = TemplateElevationOffsetMm;
        SetSegmentStart(null);
        IsPreviewVisible = false;
        IsSubLineVisible = false;
        IsSnapMarkerVisible = false;
        HideFittingPreview();
        TempDimensions.Clear();
        RefreshOutline();
        RefreshGraph();
        Status = $"{template.Name} 적용 — 배관 {_network.Edges.Count}개 · 포트 {Ports.Count}개"
            + (placedFittings > 0 ? $" · 부속 {placedFittings}개" : string.Empty)
            + (pipeVoid is not null ? $" · 본관 Void DN{pipeVoid.NominalDiameterMm:0} · EL {pipeVoid.CenterElevationMm / 1000d:0.###} m" : string.Empty)
            + (pipeVoid is { HasKnownNominal: false } ? $" · Void 지름 {pipeVoid.DiameterMm:0.#} mm에 맞는 DN 없음 — 관경 확인" : string.Empty)
            + (isAirRoom && pipeVoid is null ? " · 본관 관통 Void를 못 찾아 중앙 직관으로 그림 — 높이·관경 직접 확인" : string.Empty)
            + (dropped > 0 ? $" · 짧은 선 {dropped}개 제외" : string.Empty)
            + (unassignedValves > 0 ? $" · 맞는 밸브 유형(게이트·버터플라이)을 찾지 못해 밸브 {unassignedValves}개는 패밀리 미지정" : string.Empty)
            + (placedBypassSets > 0 && HandbookBypassDiameterMm(_diameterMm) is null
                ? $" · 바이패스 세트는 {ValveBypassSpecTable.RangeText(_pipeMaterial, _steelFittingClass)} 본관에서만 생성됩니다(현재 DN{_diameterMm:0}) — 기본 관경을 바꾼 뒤 다시 적용하세요."
                : string.Empty)
            + tieNote;
    }

    /// <summary>양 끝점이 주어진 두 좌표와 같은 선(방향 무관). 없으면 null.</summary>
    private PipeEdge? FindEdgeBetween(DHBIMWATER.Core.Geometry.Point2D a, DHBIMWATER.Core.Geometry.Point2D b)
    {
        const double tolerance = 1.0;
        var positions = _network.Nodes.ToDictionary(x => x.Id, x => x.Position);
        foreach (var edge in _network.Edges)
        {
            var s = positions[edge.StartNodeId];
            var e = positions[edge.EndNodeId];
            if ((s.DistanceTo(a) <= tolerance && e.DistanceTo(b) <= tolerance)
                || (s.DistanceTo(b) <= tolerance && e.DistanceTo(a) <= tolerance)) return edge;
        }
        return null;
    }

    // ── 프리셋 텍스트 내보내기(임시 도구, 2026-09-18 요청) ──────────────────────
    //  밸브실 유형별 기본 배관을 지시서로 넘기려고 캔버스 내용을 텍스트로 뽑는다.
    //  4개 유형 값이 ValveRoomPipingTemplateCatalog에 들어가면 이 블록과
    //  ExportTemplateTextCommand · OpenTemplateTextAction · PipeLayoutTemplateTextView를 함께 지운다.

    /// <summary>캔버스의 선·부속·연결단을 프리셋 제출 양식 텍스트로 만든다.
    /// <para>좌표는 <b>그려진 선들</b>의 경계상자 좌하단을 (0,0)으로 옮긴 mm이며 1mm로 반올림한다 —
    /// 외곽 크기·기준점과 무관해야 유형끼리 비교할 수 있다.</para>
    /// <para>포트 접점에 닿은 끝점은 잘라내지 않고 표시만 붙인다(사용자 확정 A안) —
    /// 잘라내면 그 선분에 달린 밸브까지 함께 사라지기 때문이다.</para></summary>
    public string BuildTemplateText()
    {
        if (_network.Edges.Count == 0) return "캔버스에 그려진 선이 없습니다. 선을 그린 뒤 다시 누르세요.";

        var positions = _network.Nodes.ToDictionary(x => x.Id, x => x.Position);
        var points = _network.Edges.SelectMany(e => new[] { positions[e.StartNodeId], positions[e.EndNodeId] }).ToList();
        var minX = points.Min(p => p.X);
        var minY = points.Min(p => p.Y);

        // 포트 접점(벽 내측면) ↔ 절점 대응. 외곽이 없으면 연결단을 판정할 수 없다.
        var anchors = new List<(PipePortItem Port, DHBIMWATER.Core.Geometry.Point2D Anchor)>();
        if (!_canvasOutline.IsEmpty)
            foreach (var port in Ports) anchors.Add((port, _canvasOutline.AnchorOf(port.ToPort(), ArrowFromTop)));

        // 절점 Id → "좌측벽(IN)". 접점에서 1mm 이내면 그 포트에 붙은 것으로 본다(끝점 스냅 오차 허용).
        var terminals = new Dictionary<Guid, string>();
        var matched = new HashSet<Guid>();
        foreach (var node in _network.Nodes)
        {
            var hit = anchors.FirstOrDefault(a => a.Anchor.DistanceTo(node.Position) <= 1.0);
            if (hit.Port is null) continue;
            terminals[node.Id] = $"{SideName(hit.Port.Side)}({hit.Port.Label})";
            matched.Add(hit.Port.Id);
        }

        var text = new System.Text.StringBuilder();
        text.AppendLine($"■ {(string.IsNullOrWhiteSpace(ValveRoomZone) ? "(유형 미지정)" : ValveRoomZone)}");
        text.AppendLine(_canvasOutline.IsEmpty
            ? "· 외곽 없음 — 연결단을 판정하지 못합니다"
            : $"· 외곽 내측 {_canvasOutline.WidthMm:0} × {_canvasOutline.HeightMm:0}mm");
        text.AppendLine($"· 기준 관경 DN{_diameterMm:0} · 표고 {Elevation:0}mm");
        text.AppendLine("· 좌표는 그린 선들의 좌하단을 (0,0)으로 옮긴 mm");
        text.AppendLine();

        text.AppendLine("선   (시작 → 끝, mm)");
        for (var i = 0; i < _network.Edges.Count; i++)
        {
            var edge = _network.Edges[i];
            var start = positions[edge.StartNodeId];
            var end = positions[edge.EndNodeId];
            var marks = new List<string>();
            if (terminals.TryGetValue(edge.StartNodeId, out var startMark)) marks.Add($"시작 → {startMark}");
            if (terminals.TryGetValue(edge.EndNodeId, out var endMark)) marks.Add($"끝 → {endMark}");
            text.AppendLine($"  {i + 1}) ({start.X - minX:0},{start.Y - minY:0}) → ({end.X - minX:0},{end.Y - minY:0})   DN{edge.DiameterMm:0}"
                + (marks.Count > 0 ? "   [" + string.Join(" · ", marks) + "]" : string.Empty));
        }

        text.AppendLine();
        text.AppendLine("밸브·부속   (선번호 | 선 시작에서 mm | 종류 | 패밀리)");
        var hasFitting = false;
        for (var i = 0; i < _network.Edges.Count; i++)
        {
            var edge = _network.Edges[i];
            var length = positions[edge.StartNodeId].DistanceTo(positions[edge.EndNodeId]);
            foreach (var fitting in edge.InlineFittings.OrderBy(x => x.T))
            {
                hasFitting = true;
                // 바이패스 세트는 패밀리명이 비어 있고 구성이 Bypass에 있다 — 일반 밸브처럼 적으면 줄이 공란이 된다(docs/241).
                if (fitting.Bypass is { } bypass)
                {
                    var valve = string.IsNullOrWhiteSpace(bypass.BypassValveFamilyTypeName) ? "(미지정)" : bypass.BypassValveFamilyTypeName;
                    // 밸브 2개 세트는 V1 → V2 순서와 단관 길이를 함께 적는다(docs/255 § D-7).
                    if (bypass.HasTwoValves)
                        valve = FormattableString.Invariant($"{valve} → {bypass.SecondValveFamilyTypeName} · 단관 {bypass.SpoolLengthMm:0}mm");
                    // Side +1 = 선 진행 방향(시작→끝)의 왼쪽(BypassSetSpec.Side).
                    var side = bypass.NormalizedSide > 0 ? "좌측" : "우측";
                    // 관경 0 = 핸드북 규격표가 정한다(docs/256). 되가져온 세트는 모델 실제값이다.
                    var diameter = bypass.BypassDiameterMm > 0 ? $"DN{bypass.BypassDiameterMm:0}"
                        : HandbookBypassDiameterMm(edge.DiameterMm) is { } handbookMm ? $"DN{handbookMm:0}(핸드북)" : "핸드북 규격 없음 — 생성 시 건너뜀";
                    text.AppendLine($"  선{i + 1} | {fitting.T * length:0}mm | 바이패스 세트 | 바이패스 밸브 {valve} · {diameter} · 진행 방향 {side}");
                    continue;
                }
                var kind = fitting.TypeKey == InlineJoint.TypeKey ? "이음관" : "밸브";
                text.AppendLine($"  선{i + 1} | {fitting.T * length:0}mm | {kind} | {fitting.FamilyTypeName}");
            }
        }
        if (!hasFitting) text.AppendLine("  (없음)");

        // 벽까지 긋지 않은 포트는 연결단이 잡히지 않는다. 조용히 빠지지 않게 알린다.
        var unmatched = Ports.Where(x => !matched.Contains(x.Id)).Select(x => x.Label).ToList();
        if (unmatched.Count > 0)
        {
            text.AppendLine();
            text.AppendLine($"※ 연결단으로 잡히지 않은 포트: {string.Join(", ", unmatched)} — 그 포트 접점까지 선을 그어야 잡힙니다.");
        }
        return text.ToString();
    }

    private static string SideName(OutlineSide side) => side switch
    {
        OutlineSide.Left => "좌측벽",
        OutlineSide.Right => "우측벽",
        OutlineSide.Top => "상단벽",
        _ => "하단벽",
    };

    private void ClearOutline()
    {
        // 밸브실 유형(ValveRoomZone)은 지우지 않는다 — 외곽을 다시 잡아도 사용자가 고른 유형은 남는 편이 낫다(docs/161 § F-3).
        _outline = ValveRoomOutline.Empty;
        ElevationLevelHint = string.Empty;
        _savedOutlineElementIds = [];
        // 다음 피킹 때 다시 자동값이 들어오도록 초기화. 포트 목록 자체는 지우지 않는다(유형을 지우지 않는 것과 같은 이유, docs/172 § C-6).
        foreach (var port in Ports) port.UserEdited = false;
        RefreshOutline();
        Status = "가져온 벽체·기둥 제거";
    }

    private void PickPiping()
    {
        CancelDrawing();
        Status = "Revit에서 가져올 배관 선택";
        PickPipingAction?.Invoke();
    }

    /// <summary>모델에서 읽어 온 배관망을 캔버스에 그린다(docs/158 § E-3).
    /// <paramref name="import"/>가 null이거나 배관이 없으면 사용자가 취소한 것으로 본다.
    /// <para>기존 그래프는 <b>대체</b>한다. 가져오기 직전 상태는 Undo 스택에 남으므로 Ctrl+Z로 되돌릴 수 있다.</para></summary>
    public void ApplyPickedPiping(PipeLayoutImport? import)
    {
        // null = Esc 취소, 빈 결과 = 배관 부속·밸브류를 하나도 안 고름(docs/239 § C). 문서 없음은 예외로 따로 온다.
        if (import is null)
        {
            Status = "배관 가져오기 취소";
            return;
        }
        if (import.IsEmpty)
        {
            Status = "가져올 배관 없음 — 배관 부속·밸브류를 선택";
            return;
        }

        BeginEditing();
        var before = Snapshot();
        ClearSelection();

        // 기준점: 외곽이 이미 있으면 그 기준을 그대로 쓴다. 없으면 가져온 범위 중심을 기준점으로 삼아
        // 캔버스 원점 근처에 오게 한다 — 생성 시 이 값이 다시 더해져 원위치된다(ApplyOutline과 같은 규약).
        if (!HasOutline)
        {
            var xs = import.Edges.SelectMany(x => new[] { x.Start.X, x.End.X }).ToList();
            var ys = import.Edges.SelectMany(x => new[] { x.Start.Y, x.End.Y }).ToList();
            // 프로퍼티 setter를 쓰면 RefreshOutline이 두 번 돈다. 백킹 필드에 직접 넣고 통지만 한다.
            _referenceX = Math.Round((xs.Min() + xs.Max()) / 2, 1); OnPropertyChanged(nameof(ReferenceX));
            _referenceY = Math.Round((ys.Min() + ys.Max()) / 2, 1); OnPropertyChanged(nameof(ReferenceY));
        }

        _network.Clear();
        var droppedEdges = 0;
        foreach (var edge in import.Edges)
        {
            var start = ToCanvasPoint(edge.Start);
            var end = ToCanvasPoint(edge.End);
            // AddSegment는 100mm 이하 선을 버린다(PipeTopologyBuilder.SnapTolerance). 조용히 사라지지 않게 센다.
            if (start.DistanceTo(end) <= PipeTopologyBuilder.SnapTolerance) { droppedEdges++; continue; }
            _network.AddSegment(start, end, edge.DiameterMm);
        }

        // 가져온 엣지 → 캔버스 엣지의 ElementId 매핑(docs/179 § E-5).
        // 인덱스로 짝지을 수 없다. ① 위에서 버린 선 때문에 인덱스가 밀리고
        // ② AddSegment가 교차점에서 기존 엣지를 쪼개 하나가 여럿이 되기 때문이다.
        // 그래서 기하로 찾는다 — 가져온 선 위에 통째로 얹힌 캔버스 엣지 전부가 그 선의 부재를 물려받는다.
        var importedIds = new Dictionary<Guid, IReadOnlyList<long>>();
        foreach (var edge in import.Edges)
        {
            if (edge.ElementIds.Count == 0) continue;
            var start = ToCanvasPoint(edge.Start);
            var end = ToCanvasPoint(edge.End);
            if (start.DistanceTo(end) <= PipeTopologyBuilder.SnapTolerance) continue;
            foreach (var candidate in _network.Edges)
            {
                var a = _network.Nodes.First(x => x.Id == candidate.StartNodeId).Position;
                var b = _network.Nodes.First(x => x.Id == candidate.EndNodeId).Position;
                if (!IsOnSegment(a, start, end) || !IsOnSegment(b, start, end)) continue;
                // 쪼개진 조각이 여럿이면 모두 같은 목록을 갖는다 — 어느 쪽을 골라도 원본 관이 잡혀야 한다.
                importedIds[candidate.Id] = edge.ElementIds;
            }
        }

        var droppedFittings = 0;
        foreach (var fitting in import.Fittings)
        {
            if (fitting.EdgeIndex < 0 || fitting.EdgeIndex >= import.Edges.Count) { droppedFittings++; continue; }
            var source = import.Edges[fitting.EdgeIndex];
            var point = ToCanvasPoint(new DHBIMWATER.Core.Geometry.Point2D(
                source.Start.X + (source.End.X - source.Start.X) * fitting.T,
                source.Start.Y + (source.End.Y - source.Start.Y) * fitting.T));
            var target = FindEdgeAt(point);
            if (target is null) { droppedFittings++; continue; }
            if (fitting.Bypass is { } bypass)
            {
                // 바이패스 세트로 묶여 온 부속(docs/229 § D). Side는 바이패스 방향 벡터와 이 캔버스 엣지 방향으로 정한다 —
                // 캔버스 엣지는 가져온 선과 방향이 뒤집혀 있을 수 있어 가져온 쪽 Side를 그대로 쓰면 반대로 선다.
                var direction = EdgeDirection(target.Value.Edge);
                var side = direction.X * bypass.Normal.Y - direction.Y * bypass.Normal.X < 0 ? -1 : 1;
                // 밸브 2개 세트(docs/255 § D-5) — 두 밸브를 이 캔버스 엣지 방향에 투영해 시작 쪽을 V1으로 둔다. 같은 이유로 가져온 순서를 믿지 않는다.
                var (firstValve, secondValve) = (bypass.BypassValveFamilyTypeName, bypass.SecondValveFamilyTypeName);
                if (!string.IsNullOrWhiteSpace(secondValve) && bypass.ValvePositions.Count == 2)
                {
                    var p0 = ToCanvasPoint(bypass.ValvePositions[0]);
                    var p1 = ToCanvasPoint(bypass.ValvePositions[1]);
                    if ((p1.X - p0.X) * direction.X + (p1.Y - p0.Y) * direction.Y < 0) (firstValve, secondValve) = (secondValve, firstValve);
                }
                _network.AddInlineFitting(target.Value.Edge.Id, BypassSet.TypeKey, fitting.FamilyTypeName, target.Value.T,
                    preservePosition: true, bypass: new BypassSetSpec(firstValve, bypass.BypassDiameterMm, side,
                        string.IsNullOrWhiteSpace(secondValve) ? null : secondValve, string.IsNullOrWhiteSpace(secondValve) ? 0 : bypass.SpoolLengthMm));
                if (_network.Edges.FirstOrDefault(x => x.Id == target.Value.Edge.Id) is { InlineFittings.Count: > 0 } setEdge)
                    importedIds[setEdge.InlineFittings[^1].Id] = bypass.ElementIds;
                continue;
            }
            // 이음관은 밸브와 같은 독립 부속이다 — TypeKey만 다르다(docs/175 § C-4).
            _network.AddInlineFitting(target.Value.Edge.Id,
                fitting.IsJoint ? InlineJoint.TypeKey : InlineFittingTypeKey, fitting.FamilyTypeName, target.Value.T,
                preservePosition: true);
            // AddFitting은 목록 끝에 붙인다. 방금 올린 부속이 마지막 원소이므로 그 Id에 부재를 건다(docs/179 § E-5).
            if (fitting.ElementId != 0 && _network.Edges.FirstOrDefault(x => x.Id == target.Value.Edge.Id)
                    is { InlineFittings.Count: > 0 } placed)
                importedIds[placed.InlineFittings[^1].Id] = [fitting.ElementId];
        }

        // 절점 부속(곡관·T형·Y형·십자)은 가장 가까운 캔버스 절점 Guid에 건다(docs/216 § A-3).
        // 모델 생성 경로(PlaceNodeFittings → node.Id)와 같은 키라 SelectedEdgeNodeIds가 그대로 찾는다.
        foreach (var nodeFitting in import.NodeFittings ?? [])
        {
            if (nodeFitting.ElementId == 0) continue;
            var point = ToCanvasPoint(nodeFitting.Position);
            var node = _network.Nodes
                .Select(x => (Node: x, Distance: x.Position.DistanceTo(point)))
                .Where(x => x.Distance <= PipeTopologyBuilder.SnapTolerance)
                .OrderBy(x => x.Distance)
                .Select(x => x.Node)
                .FirstOrDefault();
            if (node is null) continue;
            // 밸브부관은 절점 부속 종류와 밸브 쪽 방향까지 되살린다 — 안 그러면 재생성이 곡관으로 바꿔 놓는다(docs/225 § E).
            // 밸브부관 A도 종류를 되살린다(docs/227 § E). 절점 모양과 맞지 않으면 SetNodeFittingKind가 거절한다.
            if (nodeFitting.FittingKind != PipeNodeFittingKind.Bend && _network.SetNodeFittingKind(node.Id, nodeFitting.FittingKind)
                && nodeFitting.FittingKind == PipeNodeFittingKind.ValveBend && nodeFitting.ValveSideDirection is { } valveSide)
                _network.SetValveBendSide(node.Id, valveSide);
            // 한 절점에 부재가 둘 걸리면 덮지 않고 합친다.
            importedIds[node.Id] = importedIds.TryGetValue(node.Id, out var existing)
                ? [.. existing, nodeFitting.ElementId]
                : [nodeFitting.ElementId];
        }

        _network.Elevation = import.ElevationMm;
        _elevation = import.ElevationMm; OnPropertyChanged(nameof(Elevation)); OnPropertyChanged(nameof(ElevationM));
        if (ElevationMode == PipeElevationMode.Relative && ElevationLevel is not null) SetOffsetFromCurrentElevation();
        var keptFamilies = ApplyImportedSettings(import);

        _importedSourceIds = import.SourceElementIds;
        // 가져오기는 그래프를 통째로 대체하므로 매핑도 통째로 바꾼다(docs/179 § E-5).
        _revitElementIds = importedIds;
        // 교체 대상도 가져온 원본으로 바뀐다 — 직전 생성분을 가져왔다면 이미 원본 목록에 들어 있다(docs/235 § C).
        _createdElementIds = [];
        OnPropertyChanged(nameof(HasImportedSource));
        OnPropertyChanged(nameof(HasReplaceTarget));

        RecordUndoIfChanged(before);
        SetSegmentStart(null);
        IsPreviewVisible = false;
        IsSubLineVisible = false;
        IsSnapMarkerVisible = false;
        HideFittingPreview();
        TempDimensions.Clear();
        ZoomToImported();
        RefreshOutline();
        RefreshGraph();

        var dropped = droppedEdges + droppedFittings;
        Status = $"가져오기 — 배관 {_network.Edges.Count}개 · 부속 {_network.Edges.Sum(x => x.InlineFittings.Count)}개"
            + (dropped > 0 ? $" · {dropped}개 제외(짧거나 올릴 배관 없음)" : "")
            + (import.Warnings.Count > 0 ? $" · 확인 필요 {import.Warnings.Count}건" : "")
            // 저장된 패밀리 설정을 지킨 칸이 있으면 함께 알린다(docs/175 § E-4 6).
            + (keptFamilies.Count > 0 ? $" · 가져온 배관의 {string.Join("·", keptFamilies)} 패밀리가 저장된 설정과 다릅니다(설정 유지)" : "")
            // 원본의 DH_격점번호는 읽어 오지 않으므로(PipeLayoutImport에 필드 없음) 유형 기본값이 들어간다.
            // 원본과 다를 수 있어 알린다(docs/177 § C).
            + (!_junctionIdUserEdited && !string.IsNullOrEmpty(_junctionId)
                ? $" · 격점번호 = 유형 기본값 {_junctionId} (원본과 다르면 수정)" : "");
    }

    /// <summary>프로젝트 좌표(mm) → 캔버스 좌표(mm). 외곽과 같은 규약으로 기준점을 뺀다.</summary>
    private DHBIMWATER.Core.Geometry.Point2D ToCanvasPoint(DHBIMWATER.Core.Geometry.Point2D point)
        => new(point.X - ReferenceX, point.Y - ReferenceY);

    /// <summary>가져온 부속을 올릴 엣지와 그 위치를 찾는다. <see cref="FindNearestEdgePoint"/>와 달리
    /// 격자 스냅을 하지 않는다 — 모델에 실제로 있는 위치를 옮기면 안 된다(docs/158 § E-3).</summary>
    /// <summary>점이 선분 위(허용오차 안)에 있는지. 역가져오기 매핑에서 "가져온 선에 얹힌 캔버스 엣지"를
    /// 가려내는 데 쓴다(docs/179 § E-5). 좌표는 같은 계산으로 만들어진 값이라 오차가 크지 않다.</summary>
    private static bool IsOnSegment(DHBIMWATER.Core.Geometry.Point2D point,
        DHBIMWATER.Core.Geometry.Point2D start, DHBIMWATER.Core.Geometry.Point2D end)
    {
        const double toleranceMm = 1.0;
        var t = Math.Clamp(Segment2D.ParameterOnSegment(point, start, end), 0, 1);
        var projected = new DHBIMWATER.Core.Geometry.Point2D(
            start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t);
        return projected.DistanceTo(point) <= toleranceMm;
    }

    private (PipeEdge Edge, double T)? FindEdgeAt(DHBIMWATER.Core.Geometry.Point2D point)
    {
        PipeEdge? bestEdge = null;
        double bestT = 0, bestDistance = double.MaxValue;
        foreach (var edge in _network.Edges)
        {
            var start = _network.Nodes.First(x => x.Id == edge.StartNodeId).Position;
            var end = _network.Nodes.First(x => x.Id == edge.EndNodeId).Position;
            var t = Math.Clamp(Segment2D.ParameterOnSegment(point, start, end), 0, 1);
            var distance = new DHBIMWATER.Core.Geometry.Point2D(
                start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t).DistanceTo(point);
            if (distance >= bestDistance) continue;
            bestEdge = edge; bestT = t; bestDistance = distance;
        }
        return bestEdge is null ? null : (bestEdge, bestT);
    }

    /// <summary>모델에서 읽어 온 값으로 창 입력을 채운다(docs/158 § E-3).
    /// 패밀리는 콤보 목록에 실제로 있는 것만 고른다 — 없는 이름을 넣으면 선택이 비어 버린다.
    /// 파라미터명 콤보는 인스턴스에서 알 수 없으므로 건드리지 않는다.</summary>
    /// <returns>저장값을 지키느라 가져온 값을 쓰지 않은 칸의 라벨(관·곡관·T형·십자). 완료 문구에 붙인다.</returns>
    private IReadOnlyList<string> ApplyImportedSettings(PipeLayoutImport import)
    {
        if (import.DominantDiameterMm is { } diameter && double.IsFinite(diameter) && diameter > 0)
            DiameterMm = diameter;
        // 가져온 부재의 유형을 그대로 이어받는다. 비어 있으면(미기록·혼재) 기존 선택을 지우지 않는다(docs/161 § G-5).
        if (!string.IsNullOrWhiteSpace(import.Zone)) ValveRoomZone = import.Zone;
        // 관종도 같은 규칙으로 이어받는다. 안 하면 강관 밸브실이 재생성에서 DCIP로 덮어쓰인다(docs/170 § G).
        if (PipeAlignmentParameters.MaterialFromModelValue(import.Material) is { } importedMaterial)
        {
            _isApplyingImport = true;
            try { PipeMaterial = importedMaterial; }
            finally { _isApplyingImport = false; }
        }
        // 프로젝트 저장값이 있으면 채워진 칸은 덮지 않는다 — 저장값은 사용자가 확정한 값이고 가져온 값은 최빈값 추정이다(docs/175 § E-4 6).
        var kept = new List<string>();
        SelectImportedFamily(import.SegmentFamilyTypeName, SegmentFamilyTypeName, x => SegmentFamilyTypeName = x, "관", kept);
        SelectImportedFamily(import.BendFamilyTypeName, BendFamilyTypeName, x => BendFamilyTypeName = x, "곡관", kept);
        SelectImportedFamily(import.TeeFamilyTypeName, TeeFamilyTypeName, x => TeeFamilyTypeName = x, "T형", kept);
        SelectImportedFamily(import.CrossFamilyTypeName, CrossFamilyTypeName, x => CrossFamilyTypeName = x, "십자", kept);
        SelectImportedFamily(import.WyeFamilyTypeName, WyeFamilyTypeName, x => WyeFamilyTypeName = x, "Y형", kept);
        // 밸브부관은 배관 부속류라 다른 이형관과 같은 목록(SegmentFamilyTypeNames)에서 찾는다.
        SelectImportedFamily(import.ValveBendFamilyTypeName, ValveBendFamilyTypeName, x => ValveBendFamilyTypeName = x, PipeValveBend.PartNameB, kept);
        SelectImportedFamily(import.ValveTeeFamilyTypeName, ValveTeeFamilyTypeName, x => ValveTeeFamilyTypeName = x, PipeValveBend.PartNameA, kept);
        NotifyFamilySettingsChanged();
        return kept;
    }

    /// <param name="candidates">콤보 후보 목록. 기본은 배관 이음쇠 목록(<see cref="SegmentFamilyTypeNames"/>)이다.</param>
    private void SelectImportedFamily(string? familyTypeName, string? current, Action<string> assign, string label, ICollection<string> kept,
        ICollection<string>? candidates = null)
    {
        if (familyTypeName is not { Length: > 0 } name || !(candidates ?? SegmentFamilyTypeNames).Contains(name)) return;
        if (_familySettingsLoaded && !string.IsNullOrWhiteSpace(current))
        {
            if (!string.Equals(current, name, StringComparison.Ordinal)) kept.Add(label);
            return;
        }
        assign(name);
    }

    /// <summary>가져온 원본과의 연결을 끊는다. 캔버스를 비웠거나, 이미 지우고 다시 만든 뒤다(docs/158 § E-4, § E-6).</summary>
    private void ForgetImportedSource()
    {
        if (_importedSourceIds.Count == 0) return;
        _importedSourceIds = [];
        OnPropertyChanged(nameof(HasImportedSource));
        OnPropertyChanged(nameof(HasReplaceTarget));
    }

    /// <summary>캔버스를 새로 시작할 때(초기화·추천 배치) 가져온 원본과 직전 생성분 연결을 모두 끊는다(docs/235 § C).</summary>
    private void ForgetReplaceTargets()
    {
        ForgetImportedSource();
        if (_createdElementIds.Count == 0) return;
        _createdElementIds = [];
        OnPropertyChanged(nameof(HasReplaceTarget));
    }

    /// <summary>가져온 배관 전체가 캔버스에 들어오도록 축척과 원점을 맞춘다.</summary>
    private void ZoomToImported()
    {
        if (_canvasWidth <= 0 || _canvasHeight <= 0 || _network.Nodes.Count == 0) return;
        var minX = _network.Nodes.Min(x => x.Position.X); var maxX = _network.Nodes.Max(x => x.Position.X);
        var minY = _network.Nodes.Min(x => x.Position.Y); var maxY = _network.Nodes.Max(x => x.Position.Y);
        var width = maxX - minX; var height = maxY - minY;
        if (width <= 0 && height <= 0) return;

        var scale = Math.Min(
            width > 0 ? _canvasWidth * 0.8 / width : double.MaxValue,
            height > 0 ? _canvasHeight * 0.8 / height : double.MaxValue);
        Transform.PixelsPerMillimeter = Math.Clamp(scale, 0.005, 2.0);
        // 범위 중심이 캔버스 중앙에 오도록 PanOrigin을 잡는다(CanvasModelTransform.ToScreen의 역산).
        Transform.PanOrigin = new Point(
            _canvasWidth / 2 - (minX + maxX) / 2 * Transform.PixelsPerMillimeter,
            _canvasHeight / 2 + (minY + maxY) / 2 * Transform.PixelsPerMillimeter);
    }

    /// <summary>외곽 전체가 캔버스에 여유 있게 들어오도록 축척을 맞춘다.</summary>
    private void ZoomToFit(ValveRoomOutline outline)
    {
        if (_canvasWidth <= 0 || _canvasHeight <= 0) return;
        if (outline.WidthMm <= 0 || outline.HeightMm <= 0) return;
        var scale = Math.Min(_canvasWidth * 0.8 / outline.WidthMm, _canvasHeight * 0.8 / outline.HeightMm);
        Transform.PixelsPerMillimeter = Math.Clamp(scale, 0.005, 2.0);
    }

    /// <summary>원본 외곽을 기준점 기준 캔버스 좌표로 옮기고 벽·화살표 표시 항목을 다시 만든다.</summary>
    private void RefreshOutline()
    {
        // 공기밸브실만 IN/OUT 접점을 기초 외측면에 둔다(2026-10-29). 기초 안 가져왔으면 벽 내측면 그대로.
        _canvasOutline = _outline.IsEmpty
            ? ValveRoomOutline.Empty
            : _outline.Translate(new DHBIMWATER.Core.Geometry.Point2D(-ReferenceX, -ReferenceY)).WithFoundationFaces(IsAirValveRoom);

        OutlineWalls.Clear();
        foreach (var wall in _canvasOutline.Walls)
        {
            var innerA = Transform.ToScreen(wall.InnerStart); var innerB = Transform.ToScreen(wall.InnerEnd);
            var outerA = Transform.ToScreen(wall.OuterStart); var outerB = Transform.ToScreen(wall.OuterEnd);
            OutlineWalls.Add(new OutlineWallItem(innerA.X, innerA.Y, innerB.X, innerB.Y, outerA.X, outerA.Y, outerB.X, outerB.Y, wall.ThicknessMm));
        }

        // 기둥(docs/184 § E-2, docs/189 § C-2). 외곽 치수에도 스냅에도 관여하지 않는 표시 전용이다.
        // 헌치(점선)를 먼저 깔고 본체(실선)를 위에 얹어 본체 선이 가려지지 않게 한다.
        OutlineColumns.Clear();
        foreach (var column in _canvasOutline.Columns.OrderBy(c => c.IsCore))
        {
            var screen = column.Corners.Select(Transform.ToScreen).Select(p => (p.X, p.Y)).ToArray();
            if (screen.Length < 3) continue;
            OutlineColumns.Add(new OutlineColumnItem(Points(screen), column.IsCore));
        }

        RefreshArrows();
        OnPropertyChanged(nameof(HasOutline));
        NotifyValidationChanged();
    }

    private void RefreshArrows()
    {
        OutlineArrows.Clear();
        _arrowAnchors.Clear();
        if (_canvasOutline.IsEmpty) return;

        // 화살표는 좌/우 내측면에 고정하고, 사용자는 상하 위치만 지정한다(확정 사항). 개수는 포트 목록이 정한다(docs/172 § C-7).
        foreach (var port in Ports) OutlineArrows.Add(BuildArrow(port.ToPort()));
    }

    /// <summary>IN은 좌측 벽 바깥에서 벽면까지, OUT은 우측 벽면에서 바깥으로. 둘 다 화살표는 +X를 향한다(Left/Right 전용, docs/172 Q2).</summary>
    private OutlineArrowItem BuildArrow(PipePort port)
    {
        if (port.Side is not (OutlineSide.Left or OutlineSide.Right))
            throw new NotSupportedException("Top/Bottom 포트 화살표는 아직 그리지 않는다(docs/172 § 0-5).");   // 조용히 잘못 그리지 않게
        const double ShaftPx = 46, HeadPx = 10;
        var anchorModel = _canvasOutline.AnchorOf(port, ArrowFromTop);
        _arrowAnchors.Add(anchorModel);   // 벽면 접점을 끝점 스냅 후보로 등록
        var anchor = Transform.ToScreen(anchorModel);

        var isIn = port.Flow == PortFlow.In;
        var tail = isIn ? anchor.X - ShaftPx : anchor.X;
        var tip = isIn ? anchor.X : anchor.X + ShaftPx;
        // Polygon.Points는 문자열 TypeConverter로 파싱되므로 로캘 무관하게 InvariantCulture로 만든다.
        var head = Points((tip, anchor.Y), (tip - HeadPx, anchor.Y - 5), (tip - HeadPx, anchor.Y + 5));
        var labelX = isIn ? tail - 26 : tip + 4;
        return new OutlineArrowItem(port.Label, port.Flow, tail, anchor.Y, tip, anchor.Y, head, labelX, anchor.Y - 9, port.OffsetMm);
    }

    private static string Points(params (double X, double Y)[] points)
        => string.Join(" ", points.Select(p => FormattableString.Invariant($"{p.X},{p.Y}")));

    /// <summary>미리보기 중심선 양쪽에 관 반경(DiameterMm/2)만큼 떨어진 SubLine 2개를 갱신한다.
    /// 길이 0이거나 관경이 유효하지 않으면 숨긴다.</summary>
    private void UpdatePreviewSubLines(
        DHBIMWATER.Core.Geometry.Point2D start,
        DHBIMWATER.Core.Geometry.Point2D end)
    {
        var lines = CalculateSubLines(start, end, _diameterMm);
        if (lines is null)
        {
            SubLineTicks = null;
            IsSubLineVisible = false;
            return;
        }

        var value = lines.Value;
        SubLineAX1 = value.AX1; SubLineAY1 = value.AY1; SubLineAX2 = value.AX2; SubLineAY2 = value.AY2;
        SubLineBX1 = value.BX1; SubLineBY1 = value.BY1; SubLineBX2 = value.BX2; SubLineBY2 = value.BY2;
        SubLineTicks = BuildStraightLengthTicks(start, end, _diameterMm);
        IsSubLineVisible = true;
    }

    /// <param name="diameterMm">이 선의 관경. 작도 미리보기는 기본 관경, 확정된 선은 엣지 관경을 넘긴다(docs/152 § D-3).</param>
    private (double AX1, double AY1, double AX2, double AY2, double BX1, double BY1, double BX2, double BY2)? CalculateSubLines(
        DHBIMWATER.Core.Geometry.Point2D start,
        DHBIMWATER.Core.Geometry.Point2D end,
        double diameterMm)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < double.Epsilon || !double.IsFinite(diameterMm) || diameterMm <= 0) return null;

        var radius = diameterMm / 2;
        var offsetX = -dy / length * radius;
        var offsetY = dx / length * radius;
        var a1 = Transform.ToScreen(new DHBIMWATER.Core.Geometry.Point2D(start.X + offsetX, start.Y + offsetY));
        var a2 = Transform.ToScreen(new DHBIMWATER.Core.Geometry.Point2D(end.X + offsetX, end.Y + offsetY));
        var b1 = Transform.ToScreen(new DHBIMWATER.Core.Geometry.Point2D(start.X - offsetX, start.Y - offsetY));
        var b2 = Transform.ToScreen(new DHBIMWATER.Core.Geometry.Point2D(end.X - offsetX, end.Y - offsetY));
        return (a1.X, a1.Y, a2.X, a2.Y, b1.X, b1.Y, b2.X, b2.Y);
    }

    /// <summary>한 선에 그릴 정척 눈금 상한. 정척을 1mm처럼 잘못 넣고 긴 선을 그리면 눈금이 수만 개가 돼 UI가 멈춘다.</summary>
    private const int MaxStraightLengthTicks = 500;

    /// <summary>정척 길이마다 SubLine A–B를 잇는 수직보조선(docs/245). 시작점부터 단순 간격이며,
    /// 인라인 부속 중심점에서 정척을 다시 센다(docs/314, 폭은 0으로 봄) — 실제 이음 위치(PipeSegmentPlan)와
    /// 부속 폭만큼 어긋날 수 있는 참고 눈금이다.
    /// 끝점과 겹치는 눈금은 그리지 않는다(PipeSegmentPlan의 허용오차 관례와 같다).</summary>
    /// <param name="restartTs">정척을 다시 셀 엣지 위 비율(0~1). 부속 위치. null이면 재시작 없음.</param>
    private System.Windows.Media.Geometry? BuildStraightLengthTicks(
        DHBIMWATER.Core.Geometry.Point2D start,
        DHBIMWATER.Core.Geometry.Point2D end,
        double diameterMm,
        IEnumerable<double>? restartTs = null)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < double.Epsilon || !double.IsFinite(diameterMm) || diameterMm <= 0) return null;

        var distances = StraightLengthTickDistances(length, _straightLengthMm, restartTs);
        if (distances is null) return null;

        var ux = dx / length;
        var uy = dy / length;
        var radius = diameterMm / 2;
        var offsetX = -uy * radius;
        var offsetY = ux * radius;
        var path = new System.Text.StringBuilder();
        foreach (var d in distances)
        {
            var cx = start.X + ux * d;
            var cy = start.Y + uy * d;
            var a = Transform.ToScreen(new DHBIMWATER.Core.Geometry.Point2D(cx + offsetX, cy + offsetY));
            var b = Transform.ToScreen(new DHBIMWATER.Core.Geometry.Point2D(cx - offsetX, cy - offsetY));
            path.Append(FormattableString.Invariant($"M {a.X},{a.Y} L {b.X},{b.Y} "));
        }
        if (path.Length == 0) return null;

        var geometry = System.Windows.Media.Geometry.Parse(path.ToString());
        geometry.Freeze();
        return geometry;
    }

    /// <summary>정척 눈금의 시작점 기준 거리(mm). 재시작 지점으로 구간을 나누고 구간마다 정척을 처음부터 센다(docs/314).
    /// 구간 끝·부속 중심과 겹치는 눈금은 뺀다. 눈금이 없거나 상한을 넘으면 null.</summary>
    private static List<double>? StraightLengthTickDistances(double length, double straightLengthMm, IEnumerable<double>? restartTs)
    {
        const double tolerance = 1e-6;
        if (!double.IsFinite(straightLengthMm) || straightLengthMm <= 0) return null;

        // 경계: 0, 부속 위치, 끝. 끝점 밖·같은 자리 부속은 버리고 하나로 합친다.
        var bounds = new List<double> { 0 };
        foreach (var d in (restartTs ?? []).Where(double.IsFinite).Select(t => t * length).OrderBy(x => x))
            if (d > tolerance && d < length - tolerance && d - bounds[^1] > tolerance) bounds.Add(d);
        bounds.Add(length);

        // 상한은 엣지 전체 합으로 먼저 본다 — 정척을 1mm처럼 넣으면 루프 전에 끊는다.
        var total = 0.0;
        for (var i = 1; i < bounds.Count; i++)
            total += Math.Max(0, Math.Floor((bounds[i] - bounds[i - 1] - tolerance) / straightLengthMm));
        if (total <= 0 || total > MaxStraightLengthTicks) return null;

        var distances = new List<double>((int)total);
        for (var i = 1; i < bounds.Count; i++)
            for (var k = 1; bounds[i - 1] + k * straightLengthMm < bounds[i] - tolerance; k++)
                distances.Add(bounds[i - 1] + k * straightLengthMm);
        return distances;
    }

    /// <summary>커서(또는 스냅된 점)에서 X·Y 각 방향으로 가장 가까운 외곽선까지의 중심선·SubLine 임시치수를 만든다.</summary>
    private void UpdateTempDimensions(
        DHBIMWATER.Core.Geometry.Point2D point,
        DHBIMWATER.Core.Geometry.Point2D? drawingStart)
    {
        TempDimensions.Clear();
        if (_canvasOutline.IsEmpty) return;
        AddDirection(_canvasOutline.MeasureHorizontal, point, drawingStart);
        AddDirection(_canvasOutline.MeasureVertical, point, drawingStart);

        void AddDirection(
            Func<DHBIMWATER.Core.Geometry.Point2D, OutlineMeasure?> measure,
            DHBIMWATER.Core.Geometry.Point2D center,
            DHBIMWATER.Core.Geometry.Point2D? start)
        {
            var centerMeasure = measure(center);
            Add(centerMeasure);
            if (start is null || !double.IsFinite(DiameterMm) || DiameterMm <= 0) return;

            var dx = center.X - start.X;
            var dy = center.Y - start.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length < double.Epsilon) return;

            var radius = DiameterMm / 2;
            var offsetX = -dy / length * radius;
            var offsetY = dx / length * radius;
            var measureA = measure(new DHBIMWATER.Core.Geometry.Point2D(center.X + offsetX, center.Y + offsetY));
            var measureB = measure(new DHBIMWATER.Core.Geometry.Point2D(center.X - offsetX, center.Y - offsetY));
            var subLineMeasure = measureA is null ? measureB
                : measureB is null || measureA.Value.DistanceMm <= measureB.Value.DistanceMm ? measureA : measureB;
            if (subLineMeasure is null) return;
            if (centerMeasure is not null && Math.Abs(subLineMeasure.Value.DistanceMm - centerMeasure.Value.DistanceMm) <= ValveRoomOutline.Tolerance) return;
            Add(subLineMeasure, true);
        }

        void Add(OutlineMeasure? measure, bool isSubLine = false)
        {
            if (measure is null) return;
            var a = Transform.ToScreen(measure.Value.From);
            var b = Transform.ToScreen(measure.Value.Hit);
            if (isSubLine)
            {
                const double BaseOffsetMm = 75;
                const double MinOffsetPx = 4;
                const double MaxOffsetPx = 20;
                var screenOffsetPx = Math.Clamp(BaseOffsetMm * Transform.PixelsPerMillimeter, MinOffsetPx, MaxOffsetPx);
                var ldx = b.X - a.X;
                var ldy = b.Y - a.Y;
                var llen = Math.Sqrt(ldx * ldx + ldy * ldy);
                if (llen > double.Epsilon)
                {
                    var px = -ldy / llen * screenOffsetPx;
                    var py = ldx / llen * screenOffsetPx;
                    a = new System.Windows.Point(a.X + px, a.Y + py);
                    b = new System.Windows.Point(b.X + px, b.Y + py);
                }
            }
            TempDimensions.Add(new TempDimensionItem(a.X, a.Y, b.X, b.Y, measure.Value.DistanceMm, (a.X + b.X) / 2, (a.Y + b.Y) / 2, isSubLine));
        }
    }
    // ── 드래그 이동 (docs/152 § E) ───────────────────────────────────────────────
    // 미리보기는 그래프를 고치지 않고 화면 좌표만 민다. 매 프레임 토폴로지를 재계산하면
    // 교차 분할이 수십 번 일어나 Undo 스택과 엣지 Id가 망가진다.

    /// <summary>클릭과 드래그를 가르는 화면 거리(px). 선택박스의 클릭 판정과 같은 값이다.</summary>
    private const double DragThresholdPx = 4;

    /// <summary>임계값을 넘겨 실제로 이동 중인가. 미리보기 표시 판정에 쓴다.</summary>
    public bool IsDragging => _drag is { Exceeded: true };
    /// <summary>마우스 버튼을 누른 채 드래그 후보 상태인가. View의 마우스 이동·해제 분기에 쓴다.</summary>
    public bool IsDraggingCandidate => _drag is not null;

    /// <summary>선을 잡는다. 이미 선택된 선이면 선택 전체를, 아니면 그 선만 단독 선택해 끈다.</summary>
    public bool TryBeginEdgeDrag(Point screenPoint, Guid edgeId)
    {
        if (!IsSelectionMode || _network.Edges.All(x => x.Id != edgeId)) return false;
        if (!SelectedEdgeIds.Contains(edgeId)) SelectEdge(edgeId);
        // 생성 대기 중에는 고르기만 하고 끌지 않는다([Revit에서 보기]용 선택은 허용, docs/235 § D).
        if (IsCreating) return false;
        var targets = SelectedEdgeIds.ToList();
        if (targets.Count == 0) return false;
        _drag = new PipeDragState(PipeDragKind.Edge, screenPoint, Transform.ToModel(screenPoint), targets, null, null, null, null);
        OnPropertyChanged(nameof(InteractionModeText));
        return true;
    }

    /// <summary>같은 자리에 겹친 절점이 여럿이면(수동 분할 결과, docs/181 § B) 어느 것을 잡을지 정한다.
    /// <b>선택된 선에 물린 절점을 우선한다</b> — 두 절점이 화면에서 똑같이 보이므로,
    /// "선을 먼저 고른다"가 사용자가 줄 수 있는 유일하게 예측 가능한 지시다(2026-09-17 사용자 확정).
    /// <para>선택이 없거나 겹친 절점 중 선택된 선에 물린 것이 없으면 클릭으로 잡힌 절점을 그대로 쓴다.</para></summary>
    private Guid ResolveCoincidentNodePick(Guid nodeId)
    {
        if (SelectedEdgeIds.Count == 0) return nodeId;
        var node = _network.Nodes.FirstOrDefault(x => x.Id == nodeId);
        if (node is null) return nodeId;

        var coincident = _network.Nodes
            .Where(x => x.Position.DistanceTo(node.Position) <= CoincidentNodeToleranceMm)
            .ToList();
        if (coincident.Count < 2) return nodeId;

        var preferred = coincident.FirstOrDefault(x => _network.Edges.Any(
            edge => SelectedEdgeIds.Contains(edge.Id) && (edge.StartNodeId == x.Id || edge.EndNodeId == x.Id)));
        return preferred?.Id ?? nodeId;
    }

    /// <summary>Shift를 누른 채 끝점을 끌 때 맞출 좌표 — 반대쪽 끝을 기준으로 <b>45° 배수 8방향</b>
    /// (수평·수직·대각)에 붙인다(docs/181 § B-3, 45° 추가는 docs/191).
    /// <para>물린 선이 <b>정확히 1개</b>일 때만 동작한다. 여러 선이 물린 절점은 어느 선에 맞출지 정할 수 없다.</para></summary>
    private DHBIMWATER.Core.Geometry.Point2D? OrthoSnap(Guid nodeId, DHBIMWATER.Core.Geometry.Point2D free)
    {
        var connected = _network.Edges.Where(x => x.StartNodeId == nodeId || x.EndNodeId == nodeId).ToList();
        if (connected.Count != 1) return null;
        var edge = connected[0];
        var anchorId = edge.StartNodeId == nodeId ? edge.EndNodeId : edge.StartNodeId;
        var anchor = _network.Nodes.FirstOrDefault(x => x.Id == anchorId)?.Position;
        if (anchor is null) return null;

        var dx = free.X - anchor.X;
        var dy = free.Y - anchor.Y;
        if (Math.Abs(dx) <= 1e-9 && Math.Abs(dy) <= 1e-9) return null;   // 제자리면 방향을 못 정한다

        // 8방향(45° 배수) 중 커서에 가장 가까운 쪽.
        // cos/sin을 반올림하면 각 방향이 (±1,0)·(0,±1)·(±1,±1)로 정확히 떨어진다(cos45°=0.707→1, cos90°≈0→0).
        var stepRad = Math.PI / 4;
        var index = Math.Round(Math.Atan2(dy, dx) / stepRad);
        var lx = Math.Round(Math.Cos(index * stepRad));
        var ly = Math.Round(Math.Sin(index * stepRad));

        // 격자벡터를 정규화해 단위벡터로 만든다. Math.Cos/Sin을 그대로 쓰지 않는 이유 —
        // cos(90°)가 6.1e-17로 나와 수직 이동에 미세한 X 흘림이 남는다. 반올림 → 정규화면 0·1·0.70710678로 떨어진다.
        var norm = Math.Sqrt(lx * lx + ly * ly);      // 1 또는 √2
        var ux = lx / norm;
        var uy = ly / norm;

        // 커서를 그 방향에 정사영한 '선 길이'를 격자에 맞춘다(docs/193 § A).
        // 대각선은 끝점 좌표가 격자를 벗어나지만 길이가 우선이다(사용자 확정).
        // 수평·수직은 norm=1, ux/uy ∈ {0,±1}이라 식이 RoundToGrid(dx)/RoundToGrid(dy)로 축약된다 — 종전과 같다.
        var length = RoundToGrid(dx * ux + dy * uy);
        return new(anchor.X + ux * length, anchor.Y + uy * length);
    }

    /// <summary>끝점이 선 정확히 1개에 물렸으면 그 선의 반대쪽 끝(각도 기준점). 아니면 null — 어느 선에 맞출지 못 정한다.</summary>
    private DHBIMWATER.Core.Geometry.Point2D? SingleEdgeAnchor(Guid nodeId)
    {
        var connected = _network.Edges.Where(x => x.StartNodeId == nodeId || x.EndNodeId == nodeId).Take(2).ToList();
        if (connected.Count != 1) return null;
        var anchorId = connected[0].StartNodeId == nodeId ? connected[0].EndNodeId : connected[0].StartNodeId;
        return _network.Nodes.FirstOrDefault(x => x.Id == anchorId)?.Position;
    }

    /// <summary>Shift 각도 구속점 옆에 있는 다른 선의 끝점(docs/193 § B). 없으면 null.
    /// <para>끝점 OSNAP만 본다 — 중간점·직교점까지 끌어들이면 Shift로 준 각도 지시가 자주 깨진다.
    /// 거리 제한은 <see cref="PipeTopologyBuilder.SnapTolerance"/>가 건다.</para>
    /// <para>끝점 OSNAP을 꺼 둔 상태면 동작하지 않는다 — 사용자 설정을 존중한다.</para></summary>
    private DHBIMWATER.Core.Geometry.Point2D? EndpointSnapNear(
        DHBIMWATER.Core.Geometry.Point2D constrained, DHBIMWATER.Core.Geometry.Point2D draggedOrigin)
    {
        if (!SnapEndpoint) return null;
        var snap = _network.FindSnapPoint(constrained, PipeSnapMode.Endpoint);
        if (snap is not { Kind: PipeSnapKind.Endpoint }) return null;
        // 끌고 있는 절점 자신은 아직 옛 좌표로 남아 있다 — 제자리 스냅은 버린다(Shift 아닌 경로와 같은 규칙).
        return snap.Value.Point.DistanceTo(draggedOrigin) <= 1e-6 ? null : snap.Value.Point;
    }

    /// <summary>절점(끝점)을 잡는다. 절점 자체를 끄는 조작이므로 물린 선이 모두 따라온다.</summary>
    public bool TryBeginNodeDrag(Point screenPoint, Guid nodeId)
    {
        if (!IsSelectionMode || IsCreating) return false;
        nodeId = ResolveCoincidentNodePick(nodeId);
        var origin = _network.Nodes.FirstOrDefault(x => x.Id == nodeId)?.Position;
        if (origin is null) return false;
        _drag = new PipeDragState(PipeDragKind.Node, screenPoint, Transform.ToModel(screenPoint), [], nodeId, origin, null, null);
        OnPropertyChanged(nameof(InteractionModeText));
        return true;
    }

    /// <summary>인라인 부속을 잡는다. 자기가 올라탄 선 위에서만 미끄러진다(docs/152 가정 B).</summary>
    public bool TryBeginFittingDrag(Point screenPoint, Guid fittingId)
    {
        if (!IsSelectionMode || IsCreating) return false;
        var edge = _network.Edges.FirstOrDefault(x => x.InlineFittings.Any(f => f.Id == fittingId));
        if (edge is null) return false;
        if (!SelectedFittingIds.Contains(fittingId)) SelectFitting(fittingId);
        var fitting = edge.InlineFittings.First(x => x.Id == fittingId);
        _drag = new PipeDragState(PipeDragKind.Fitting, screenPoint, Transform.ToModel(screenPoint), [], null, null, fittingId, edge.Id)
        { T = fitting.T, GrabOffsetT = fitting.T - ProjectOntoEdge(edge.Id, Transform.ToModel(screenPoint)) };
        OnPropertyChanged(nameof(InteractionModeText));
        return true;
    }

    /// <param name="orthoSnap">Shift 상태. 끝점을 끌 때 그 선을 수평·수직에 맞춘다(docs/181 § B-3).
    /// View가 <c>Keyboard.Modifiers</c>로 읽어 넘긴다 — ViewModel은 입력 장치를 직접 보지 않는다.</param>
    public void UpdateDrag(Point screenPoint, bool orthoSnap = false)
    {
        if (_drag is not { } drag) return;
        var offset = screenPoint - drag.StartScreen;
        if (!drag.Exceeded)
        {
            if (offset.Length < DragThresholdPx) return;
            drag.Exceeded = true;
        }

        var model = Transform.ToModel(screenPoint);
        switch (drag.Kind)
        {
            case PipeDragKind.Node:
            {
                var free = new DHBIMWATER.Core.Geometry.Point2D(
                    drag.NodeOrigin!.X + (model.X - drag.StartModel.X),
                    drag.NodeOrigin!.Y + (model.Y - drag.StartModel.Y));
                // Shift는 OSNAP보다 앞선다 — 직각으로 맞추라는 명시적 지시인데 근처 OSNAP이 가로채면
                // 선이 다시 비스듬해져 지시가 무시된 것처럼 보인다(docs/181 § B-3).
                // 단 하나의 예외 — 각도 구속점 바로 옆에 다른 선의 끝점이 있으면 그 끝점으로 확정한다.
                // 절점이 병합되는 편이 길이를 격자에 맞추는 것보다 쓸모 있다(docs/193 § B).
                // 멀리 있는 끝점은 잡지 않는다. 그래야 Shift로 준 각도 지시가 살아 있다.
                if (orthoSnap && OrthoSnap(drag.NodeId!.Value, free) is { } ortho)
                {
                    var orthoTarget = EndpointSnapNear(ortho, drag.NodeOrigin!) ?? ortho;
                    drag.Dx = orthoTarget.X - drag.NodeOrigin!.X;
                    drag.Dy = orthoTarget.Y - drag.NodeOrigin!.Y;
                    IsSnapMarkerVisible = false;
                    break;
                }
                // OSNAP이 잡히면 그 점에 정확히 맞춘다 — 다른 선 끝점에 붙여야 절점이 병합된다.
                // 끌고 있는 절점 자신은 아직 옛 좌표로 남아 있으므로 제자리 스냅은 버린다.
                // 선 1개에 물린 끝점이면 그리기와 같은 규칙(2026-10-29): 반대쪽 끝 기준 OSNAP(직교점 등) → 각도 스냅(45° 배수)·길이 격자.
                var anchor = SingleEdgeAnchor(drag.NodeId!.Value);
                var snap = FindSnapPoint(model, anchor);
                if (snap is { } s && s.Point.DistanceTo(drag.NodeOrigin!) <= 1e-6) snap = null;
                var target = snap?.Point ?? (anchor is not null ? Constrain(anchor, free) : SnapToGrid(free));
                drag.Dx = target.X - drag.NodeOrigin!.X;
                drag.Dy = target.Y - drag.NodeOrigin!.Y;
                UpdateSnapMarker(snap);
                break;
            }
            case PipeDragKind.Edge:
                drag.Dx = RoundToGrid(model.X - drag.StartModel.X);
                drag.Dy = RoundToGrid(model.Y - drag.StartModel.Y);
                break;
            case PipeDragKind.Fitting:
                drag.T = Math.Clamp(ProjectOntoEdge(drag.FittingEdgeId!.Value, model) + drag.GrabOffsetT, 0, 1);
                break;
        }
        RefreshGraph();
        OnPropertyChanged(nameof(InteractionModeText));
    }

    /// <inheritdoc cref="UpdateDrag(Point, bool)" path="/param[@name='orthoSnap']"/>
    public void CommitDrag(Point screenPoint, bool orthoSnap = false)
    {
        if (_drag is not { } drag) return;
        // 확정도 같은 제약으로 한 번 더 계산한다. 빠뜨리면 놓는 순간 직각 제약이 풀려 선이 비스듬해진다.
        UpdateDrag(screenPoint, orthoSnap);
        // 임계값을 넘지 못했으면 이동이 아니라 단순 클릭 선택이다.
        if (!drag.Exceeded) { CancelDrag(); return; }

        var before = Snapshot();
        var status = "";
        try
        {
            switch (drag.Kind)
            {
                case PipeDragKind.Node:
                    _network.MoveNode(drag.NodeId!.Value,
                        new DHBIMWATER.Core.Geometry.Point2D(drag.NodeOrigin!.X + drag.Dx, drag.NodeOrigin!.Y + drag.Dy));
                    status = "절점 이동";
                    break;
                case PipeDragKind.Edge:
                    _network.MoveEdges(drag.EdgeIds, drag.Dx, drag.Dy);
                    status = $"배관 {drag.EdgeIds.Count}개 이동";
                    break;
                case PipeDragKind.Fitting:
                    _network.MoveInlineFitting(drag.FittingEdgeId!.Value, drag.FittingId!.Value, drag.T);
                    status = "부속 이동";
                    break;
            }
        }
        catch (Exception ex)
        {
            _drag = null;
            RefreshGraph();
            Status = $"이동 실패 · {UiErrors.Report("PipeLayout", ex)}";
            return;
        }

        _drag = null;
        IsSnapMarkerVisible = false;
        BeginEditing();
        RecordUndoIfChanged(before);
        DropMissingSelection();
        RefreshGraph();
        SelectedEdge = Edges.FirstOrDefault(x => SelectedEdgeIds.Contains(x.Id));
        SelectedFitting = InlineFittings.FirstOrDefault(x => SelectedFittingIds.Contains(x.Id));
        Status = status;
        CommandManager.InvalidateRequerySuggested();
    }

    public void CancelDrag()
    {
        if (_drag is null) return;
        _drag = null;
        IsSnapMarkerVisible = false;
        RefreshGraph();
        OnPropertyChanged(nameof(InteractionModeText));
    }

    /// <summary>교차 분할·절점 병합으로 사라진 Id를 선택 목록에서 뺀다.
    /// 선례: <see cref="RemoveFitting"/>도 분할 후 선택을 다시 잡는다.</summary>
    private void DropMissingSelection()
    {
        foreach (var id in SelectedEdgeIds.ToList())
            if (_network.Edges.All(x => x.Id != id)) SelectedEdgeIds.Remove(id);
        foreach (var id in SelectedFittingIds.ToList())
            if (_network.Edges.All(x => x.InlineFittings.All(f => f.Id != id))) SelectedFittingIds.Remove(id);
    }

    private double RoundToGrid(double value)
        => double.IsFinite(GridSnapMm) && GridSnapMm > 0 ? Math.Round(value / GridSnapMm) * GridSnapMm : value;

    private DHBIMWATER.Core.Geometry.Point2D SnapToGrid(DHBIMWATER.Core.Geometry.Point2D point)
        => new(RoundToGrid(point.X), RoundToGrid(point.Y));

    /// <summary>커서를 엣지 축에 정사영해 [0,1] 파라미터로 만든다.</summary>
    private double ProjectOntoEdge(Guid edgeId, DHBIMWATER.Core.Geometry.Point2D cursor)
    {
        var edge = _network.Edges.FirstOrDefault(x => x.Id == edgeId);
        if (edge is null) return 0;
        var start = _network.Nodes.First(x => x.Id == edge.StartNodeId).Position;
        var end = _network.Nodes.First(x => x.Id == edge.EndNodeId).Position;
        return Math.Clamp(Segment2D.ParameterOnSegment(cursor, start, end), 0, 1);
    }

    /// <summary>엣지를 그릴 때 쓸 절점 좌표. 그 엣지가 이동 대상일 때만 오프셋을 더한다.</summary>
    private DHBIMWATER.Core.Geometry.Point2D DraggedEdgePoint(PipeEdge edge, PipeNode node)
    {
        if (_drag is not { Exceeded: true } drag) return node.Position;
        var moves = drag.Kind switch
        {
            PipeDragKind.Node => node.Id == drag.NodeId,
            PipeDragKind.Edge => drag.EdgeIds.Contains(edge.Id),
            _ => false,
        };
        return moves ? new(node.Position.X + drag.Dx, node.Position.Y + drag.Dy) : node.Position;
    }

    /// <summary>절점 표식 좌표. 선 드래그에서는 그 절점에 물린 엣지가 <b>전부</b> 이동 대상일 때만 따라간다 —
    /// 일부만 대상이면 분리되어 원래 자리에 남는 쪽을 그린다(docs/152 § B-3).</summary>
    private DHBIMWATER.Core.Geometry.Point2D DraggedNodePoint(PipeNode node)
    {
        if (_drag is not { Exceeded: true } drag) return node.Position;
        var connected = _network.Edges.Where(x => x.StartNodeId == node.Id || x.EndNodeId == node.Id).ToList();
        var moves = drag.Kind switch
        {
            PipeDragKind.Node => node.Id == drag.NodeId,
            PipeDragKind.Edge => connected.Count > 0 && connected.All(x => drag.EdgeIds.Contains(x.Id)),
            _ => false,
        };
        return moves ? new(node.Position.X + drag.Dx, node.Position.Y + drag.Dy) : node.Position;
    }

    public void SelectEdge(Guid edgeId)
    {
        if (!IsSelectionMode) return;
        SelectedEdgeIds.Clear();
        SelectedFittingIds.Clear();
        SelectedEdgeIds.Add(edgeId);
        SelectedEdge = Edges.FirstOrDefault(x => x.Id == edgeId);
        SelectedFitting = null;
        RefreshGraph();
        CommandManager.InvalidateRequerySuggested();
        EdgeSelectedAction?.Invoke();
    }

    public void SelectFitting(Guid fittingId)
    {
        if (!IsSelectionMode) return;
        SelectedEdgeIds.Clear();
        SelectedFittingIds.Clear();
        SelectedFittingIds.Add(fittingId);
        SelectedFitting = InlineFittings.FirstOrDefault(x => x.Id == fittingId);
        SelectedEdge = null;
        RefreshGraph();
        CommandManager.InvalidateRequerySuggested();
    }

    public void BeginSelectionBox(Point start)
    {
        if (!IsSelectionMode) return;
        _selectionBoxStart = start;
        IsSelectionBoxVisible = true;
        UpdateSelectionBox(start);
    }

    public void UpdateSelectionBox(Point current)
    {
        if (_selectionBoxStart is not Point start) return;
        SelectionBoxX = Math.Min(start.X, current.X);
        SelectionBoxY = Math.Min(start.Y, current.Y);
        SelectionBoxWidth = Math.Abs(current.X - start.X);
        SelectionBoxHeight = Math.Abs(current.Y - start.Y);
        IsCrossingSelection = current.X < start.X;
    }

    public void CommitSelectionBox(Point end)
    {
        if (_selectionBoxStart is not Point start) return;
        UpdateSelectionBox(end);
        var drag = end - start;
        if (drag.Length < 4)
        {
            CancelSelectionBox();
            ClearSelection();
            RefreshGraph();
            Status = "선택 해제";
            return;
        }

        var box = new Rect(SelectionBoxX, SelectionBoxY, SelectionBoxWidth, SelectionBoxHeight);
        SelectedEdgeIds.Clear();
        SelectedFittingIds.Clear();
        foreach (var edge in Edges)
        {
            var selected = IsCrossingSelection
                ? SegmentIntersectsRectangle(new Point(edge.X1, edge.Y1), new Point(edge.X2, edge.Y2), box)
                : box.Contains(edge.X1, edge.Y1) && box.Contains(edge.X2, edge.Y2);
            if (selected) SelectedEdgeIds.Add(edge.Id);
        }
        foreach (var fitting in InlineFittings)
        {
            var bounds = FittingBounds(fitting);
            var selected = bounds is null
                ? box.Contains(fitting.X, fitting.Y)
                : IsCrossingSelection ? box.IntersectsWith(bounds.Value) : box.Contains(bounds.Value);
            if (selected) SelectedFittingIds.Add(fitting.Id);
        }

        SelectedEdge = Edges.FirstOrDefault(x => SelectedEdgeIds.Contains(x.Id));
        SelectedFitting = InlineFittings.FirstOrDefault(x => SelectedFittingIds.Contains(x.Id));
        CancelSelectionBox();
        RefreshGraph();
        Status = $"선택 — 배관 {SelectedEdgeIds.Count}개 · 부속 {SelectedFittingIds.Count}개";
        CommandManager.InvalidateRequerySuggested();
        // 박스로 선이 하나라도 잡히면 클릭 선택과 같이 관경 칸으로 간다(docs/233 § A-1). 부속만 잡히면 그대로.
        if (SelectedEdgeIds.Count > 0) EdgeSelectedAction?.Invoke();
    }

    public void CancelSelectionBox()
    {
        _selectionBoxStart = null;
        IsSelectionBoxVisible = false;
    }

    private static bool SegmentIntersectsRectangle(Point start, Point end, Rect box)
    {
        if (box.Contains(start) || box.Contains(end)) return true;
        var topLeft = box.TopLeft;
        var topRight = box.TopRight;
        var bottomRight = box.BottomRight;
        var bottomLeft = box.BottomLeft;
        return SegmentsIntersect(start, end, topLeft, topRight)
            || SegmentsIntersect(start, end, topRight, bottomRight)
            || SegmentsIntersect(start, end, bottomRight, bottomLeft)
            || SegmentsIntersect(start, end, bottomLeft, topLeft);
    }

    private static Rect? FittingBounds(InlineFittingItem fitting)
    {
        if (!fitting.IsBypassSet) return null;
        var bounds = Rect.Empty;
        if (fitting.BypassLine is { } line) bounds.Union(line.Bounds);
        if (fitting.BypassMarkers is { } markers) bounds.Union(markers.Bounds);
        bounds.Union(new Rect(fitting.X - 7, fitting.Y - 7, 14, 14));
        return bounds;
    }

    private static bool SegmentsIntersect(Point a, Point b, Point c, Point d)
    {
        const double epsilon = 1e-9;
        static double Cross(Point p, Point q, Point r)
            => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
        static bool InRange(double value, double first, double second)
            => value >= Math.Min(first, second) - epsilon && value <= Math.Max(first, second) + epsilon;
        static bool OnSegment(Point p, Point q, Point r)
            => Math.Abs(Cross(p, q, r)) <= epsilon && InRange(r.X, p.X, q.X) && InRange(r.Y, p.Y, q.Y);

        var abC = Cross(a, b, c);
        var abD = Cross(a, b, d);
        var cdA = Cross(c, d, a);
        var cdB = Cross(c, d, b);
        if (((abC > epsilon && abD < -epsilon) || (abC < -epsilon && abD > epsilon))
            && ((cdA > epsilon && cdB < -epsilon) || (cdA < -epsilon && cdB > epsilon))) return true;
        return OnSegment(a, b, c) || OnSegment(a, b, d) || OnSegment(c, d, a) || OnSegment(c, d, b);
    }

    public void HandleEscape()
    {
        if (_drag is not null) { CancelDrag(); Status = "이동 취소"; return; }
        if (IsSplittingEdge) { EndSplitEdge("분할 취소"); return; }
        if (IsSelectionBoxVisible) { ClearSelection(); RefreshGraph(); Status = "선택 취소"; return; }
        if (IsDrawing) { CancelDrawing(); return; }
        if (IsPlacingBypassSet) { IsPlacingBypassSet = false; Status = "바이패스 세트 배치 종료"; return; }
        if (IsPlacingFitting) { SelectedFamilyTypeName = null; SelectedJointFamilyTypeName = null; Status = "부속 배치 취소"; return; }
        if (!HasSelection) return;
        ClearSelection();
        RefreshGraph();
        Status = "선택 취소";
    }

    public void DeleteSelectedEdge()
    {
        if (IsCreating) return;   // docs/235 § D
        DeleteSelection();
    }

    private void DeleteSelectedFitting()
    {
        if (IsCreating) return;   // docs/235 § D
        DeleteSelection();
    }

    private void DeleteSelection()
    {
        if (!HasSelection) return;
        var before = Snapshot();
        var selectedEdges = _network.Edges
            .Where(x => SelectedEdgeIds.Contains(x.Id))
            .Select(x => (x.StartNodeId, x.EndNodeId))
            .ToList();

        foreach (var fittingId in SelectedFittingIds.ToList())
        {
            var edge = _network.Edges.FirstOrDefault(x => x.InlineFittings.Any(f => f.Id == fittingId));
            if (edge is not null) _network.RemoveInlineFitting(edge.Id, fittingId);
        }
        foreach (var (startNodeId, endNodeId) in selectedEdges)
        {
            var edge = _network.Edges.FirstOrDefault(x =>
                (x.StartNodeId == startNodeId && x.EndNodeId == endNodeId)
                || (x.StartNodeId == endNodeId && x.EndNodeId == startNodeId));
            if (edge is not null) _network.RemoveSegment(edge.Id);
        }

        BeginEditing();
        RecordUndoIfChanged(before);
        ClearSelection();
        RefreshGraph();
        Status = "선택 항목 삭제";
    }

    private void ClearSelection()
    {
        SelectedEdgeIds.Clear();
        SelectedFittingIds.Clear();
        SelectedEdge = null;
        SelectedFitting = null;
        CancelSelectionBox();
        NotifyDiameterTargetChanged();
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>인라인 밸브는 배관 밸브류, 관·절점부속은 배관 부속류 패밀리를 보여준다.</summary>
    private void RefreshFamilyTypeNames()
    {
        _typeLengthParameterCache.Clear();   // 패밀리 목록이 바뀌면 유형 매개변수 값도 다시 읽는다(docs/233 § B-3).
        _accessoryFamilyTypeNames = _typeRepo.GetPipeAccessoryTypeNames().ToList();
        var fittingNames = _typeRepo.GetPipeFittingTypeNames().ToList();       // 관·곡관·T형
        ValveFamilyNames.Clear();
        // 문자열 정렬이면 자릿수부터 비교해 "65A"가 "200A" 아래로 간다 — 호칭지름을 숫자로 읽어 정렬한다(docs/192).
        foreach (var familyName in _accessoryFamilyTypeNames.Select(GetFamilyName).Distinct()
                     .OrderBy(x => x, NominalDiameterParser.NameComparer))
            ValveFamilyNames.Add(new FittingFamilyItem(familyName, NominalDiameterParser.Parse(familyName)));

        // 관·곡관·T형·Y형·십자·이경 콤보가 공유하는 목록. 여러 패밀리가 섞여 있으므로 호칭지름만으로 줄 세우면
        // 같은 패밀리가 흩어진다 — 패밀리명으로 묶고 그 안에서 호칭지름 순으로 둔다(docs/192 § D).
        SegmentFamilyTypeNames.Clear();
        foreach (var name in fittingNames.OrderBy(x => x, NominalDiameterParser.FamilyGroupedComparer))
            SegmentFamilyTypeNames.Add(name);

        // 밸브부관 A·B는 배관 부속류 패밀리다(2026-09-28 사용자 정정). 콤보는 부속류 전체(SegmentFamilyTypeNames)를 보여 주고,
        // 여기에는 기본값 추정용으로 키워드(역가져오기 판정과 같은 `밸브부관`)가 든 것만 모은다.
        ValveBendFamilyTypeNames.Clear();
        foreach (var name in fittingNames.Where(PipeValveBend.IsValveBendFamily)
                     .OrderBy(x => x, NominalDiameterParser.FamilyGroupedComparer))
            ValveBendFamilyTypeNames.Add(name);

        // 이음관은 직관과 같은 이음쇠 카테고리다. 역가져오기와 같은 키워드로만 거른다(docs/167 § E-1).
        JointFamilyTypeNames.Clear();
        foreach (var name in fittingNames.Where(InlineJoint.IsJointFamily)
                     .OrderBy(x => x, NominalDiameterParser.FamilyGroupedComparer))
            JointFamilyTypeNames.Add(name);
        OnPropertyChanged(nameof(HasNoJointFamily));

        // 1단 콤보용 패밀리명 목록. 유형명을 떼고 중복을 지운다(밸브 ValveFamilyNames와 같은 방식, docs/200).
        JointFamilyNames.Clear();
        foreach (var familyName in JointFamilyTypeNames.Select(GetFamilyName).Distinct()
                     .OrderBy(x => x, NominalDiameterParser.NameComparer))
            JointFamilyNames.Add(familyName);
        _selectedJointFamilyName ??= JointFamilyNames.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedJointFamilyName));
        RefreshJointTypeNames();

        _selectedValveFamilyName ??= ValveFamilyNames.FirstOrDefault()?.Name;
        OnPropertyChanged(nameof(SelectedValveFamilyName));
        // 바이패스 밸브 기본값(docs/228 § B) — 게이트밸브를 먼저 찾는다. 없으면 첫 밸브류.
        _bypassValveFamilyName ??= ValveFamilyNames.FirstOrDefault(x => x.Name.Contains("게이트", StringComparison.Ordinal))?.Name
            ?? ValveFamilyNames.FirstOrDefault()?.Name;
        OnPropertyChanged(nameof(BypassValveFamilyName));
        _bypassSecondValveFamilyName ??= _bypassValveFamilyName;
        OnPropertyChanged(nameof(BypassSecondValveFamilyName));
        RefreshBypassValveTypeNames();
        RefreshValveTypeNames();
        RefreshDiameterMatch();

        // 이름으로 기본값을 추정한다. 못 찾으면 비워 두고 검증에서 선택을 요구한다.
        // 현재 접합 종류(기본 플랜지)의 패밀리를 먼저 찾는다(docs/263 § D-2). 저장값은 이 뒤 ApplyFamilySettings가 덮는다.
        _segmentFamilyTypeName ??= GuessFamilyForJoint(fittingNames, PipeFamilyDefaultKeywords, _jointConnection, _pipeMaterial) ?? GuessFamily(MaterialPool(fittingNames, _pipeMaterial), PipeFamilyDefaultKeywords);
        _bendFamilyTypeName ??= GuessFamilyForJoint(fittingNames, BendFamilyDefaultKeywords, _jointConnection, _pipeMaterial) ?? GuessFamily(MaterialPool(fittingNames, _pipeMaterial), BendFamilyDefaultKeywords);
        _teeFamilyTypeName ??= GuessFamilyForJoint(fittingNames, TeeFamilyDefaultKeywords, _jointConnection, _pipeMaterial) ?? GuessFamily(MaterialPool(fittingNames, _pipeMaterial), TeeFamilyDefaultKeywords);
        _crossFamilyTypeName ??= GuessFamilyForJoint(fittingNames, CrossFamilyDefaultKeywords, _jointConnection, _pipeMaterial) ?? GuessFamily(MaterialPool(fittingNames, _pipeMaterial), CrossFamilyDefaultKeywords);
        _wyeFamilyTypeName ??= GuessFamilyForJoint(fittingNames, WyeFamilyDefaultKeywords, _jointConnection, _pipeMaterial) ?? GuessFamily(MaterialPool(fittingNames, _pipeMaterial), WyeFamilyDefaultKeywords);
        _reducerFamilyTypeName ??= GuessFamilyForJoint(fittingNames, ReducerFamilyDefaultKeywords, _jointConnection, _pipeMaterial) ?? GuessFamily(MaterialPool(fittingNames, _pipeMaterial), ReducerFamilyDefaultKeywords);
        // 후보가 이미 키워드로 걸러져 있다. A·B는 커넥터 수로만 갈리므로(docs/227 § 0-6) 이름으로는 패밀리명(유형명 제외)의
        // A/B 표기만 본다. B는 못 찾으면 첫 항목, A는 못 찾으면 비워 두고 검증에서 선택을 요구한다.
        // 기본 패밀리명 완전일치가 먼저다(docs/232 § A-2) — `DH_특수_밸브부관`·`(D형)`에는 A/B 글자가 없어 종전 규칙이 못 잡는다.
        _valveTeeFamilyTypeName ??= ValveBendFamilyTypeNames.FirstOrDefault(x => GetFamilyName(x) == ValveTeeDefaultFamilyName)
            ?? ValveBendFamilyTypeNames.FirstOrDefault(x => GetFamilyName(x).Contains('A'));
        _valveBendFamilyTypeName ??= ValveBendFamilyTypeNames.FirstOrDefault(x => GetFamilyName(x) == ValveBendDefaultFamilyName)
            ?? ValveBendFamilyTypeNames.FirstOrDefault(x => GetFamilyName(x).Contains('B'))
            ?? ValveBendFamilyTypeNames.FirstOrDefault(x => x != _valveTeeFamilyTypeName);
        OnPropertyChanged(nameof(SegmentFamilyTypeName));
        OnPropertyChanged(nameof(BendFamilyTypeName));
        OnPropertyChanged(nameof(TeeFamilyTypeName));
        OnPropertyChanged(nameof(CrossFamilyTypeName));
        OnPropertyChanged(nameof(WyeFamilyTypeName));
        OnPropertyChanged(nameof(ReducerFamilyTypeName));
        OnPropertyChanged(nameof(ValveBendFamilyTypeName));
        OnPropertyChanged(nameof(ValveTeeFamilyTypeName));
        RefreshSegmentParameterNames();
        RefreshFittingDiameterParameterNames();
    }

    /// <summary>선택 패밀리 유형만 표시. 전체 키 유지. 정렬은 호칭지름 숫자 기준이다(docs/192).</summary>
    private void RefreshValveTypeNames()
    {
        FamilyTypeNames.Clear();
        if (!string.IsNullOrWhiteSpace(_selectedValveFamilyName))
            foreach (var name in _accessoryFamilyTypeNames.Where(x => GetFamilyName(x) == _selectedValveFamilyName)
                         .OrderBy(x => x, NominalDiameterParser.NameComparer))
                FamilyTypeNames.Add(name);
        SelectedFamilyTypeName = null;
        RefreshFittingDiameterParameterNames();
    }

    /// <summary>선택한 이음관 패밀리의 유형만 2단 리스트에 남긴다. 정렬은 밸브와 같은 호칭지름 숫자 기준이다(docs/192).</summary>
    private void RefreshJointTypeNames()
    {
        JointTypeNames.Clear();
        if (!string.IsNullOrWhiteSpace(_selectedJointFamilyName))
            foreach (var name in JointFamilyTypeNames.Where(x => GetFamilyName(x) == _selectedJointFamilyName)
                         .OrderBy(x => x, NominalDiameterParser.NameComparer))
                JointTypeNames.Add(name);
        // 패밀리가 바뀌면 이전 유형 선택은 무효다. 세터가 배치 모드 해제·미리보기 숨김까지 처리한다.
        // RefreshValveTypeNames와 달리 RefreshFittingDiameterParameterNames를 여기서 부르지 않는다 — 세터가 이미 부른다.
        SelectedJointFamilyTypeName = null;
    }

    private void RefreshDiameterMatch()
    {
        var hasValidDiameter = double.IsFinite(DiameterMm) && DiameterMm > 0;
        var roundedDiameter = hasValidDiameter ? (int)Math.Round(DiameterMm) : 0;
        foreach (var item in ValveFamilyNames)
            item.IsDiameterMatched = hasValidDiameter && item.DnMm.HasValue && (int)Math.Round(item.DnMm.Value) == roundedDiameter;

        var selected = ValveFamilyNames.FirstOrDefault(x => x.Name == _selectedValveFamilyName);
        if (selected is null || selected.IsDiameterMatched) return;
        var replacement = ValveFamilyNames.FirstOrDefault(x => x.IsDiameterMatched
            && StripDiameterToken(x.Name) == StripDiameterToken(selected.Name));
        if (replacement is not null) SelectedValveFamilyName = replacement.Name;
    }

    /// <summary>DN 토큰 제거. 밸브 계열 비교용.</summary>
    private static string StripDiameterToken(string familyName)
        => System.Text.RegularExpressions.Regex.Replace(familyName, @"\s*\d{2,4}\s*[AaØøΦφ]?\s*", " ").Trim();

    private static string GetFamilyName(string familyTypeName)
    {
        var separatorIndex = familyTypeName.LastIndexOf(" : ", StringComparison.Ordinal);
        return separatorIndex < 0 ? familyTypeName : familyTypeName[..separatorIndex];
    }

    /// <summary>접합 종류에 맞는 패밀리만 남긴 뒤 부재 키워드 추정(<see cref="GuessFamily"/>)을 돌린다(docs/263 § D-1).
    /// 이름 규칙이 `DH_{접합}_{부재}`라 1순위는 `소켓_`·`플랜지_` 접두다 — `DH_소켓_플랜지T형관`처럼 두 단어가 다 든 패밀리가 있어
    /// 단어 포함만 보면 소켓 T형관이 플랜지로 잡힌다. 2순위는 패밀리명에 접합 단어 포함. 둘 다 없으면 null(호출부가 현재 값 유지).
    /// 강관 `용접`은 값이 <see cref="BendConnection.Socket"/>이라 소켓 패밀리로 매칭한다(2026-10-02 사용자 결정).
    /// <para>관종으로 먼저 거른다(<see cref="MaterialPool"/>). 강관 패밀리는 접합 단어가 없을 수 있어 — 접합 매칭 실패 시 강관 후보 안 키워드 추정.
    /// 강관 `용접`은 `용접` 단어 먼저, 없으면 `소켓`.</para></summary>
    private static string? GuessFamilyForJoint(IReadOnlyList<string> names, IReadOnlyList<string> keywords, BendConnection joint, PipeMaterial material)
    {
        var pool = MaterialPool(names, material);
        var steel = material == PipeMaterial.Steel && pool.Count > 0 && IsSteelFamily(pool[0]);
        if (!steel) return GuessFamilyForJoint(pool, keywords, joint);
        if (joint == BendConnection.Socket && GuessFamilyForJointWord(pool, keywords, "용접") is { } weld) return weld;
        return GuessFamilyForJoint(pool, keywords, joint) ?? GuessFamily(pool, keywords);
    }

    /// <summary>관종 후보. 강관 = `강관` 든 패밀리, 그 밖 = `강관` 없는 패밀리. 해당 후보가 하나도 없으면 전체(종전 동작).</summary>
    private static IReadOnlyList<string> MaterialPool(IReadOnlyList<string> names, PipeMaterial material)
    {
        var pool = names.Where(x => IsSteelFamily(x) == (material == PipeMaterial.Steel)).ToList();
        return pool.Count > 0 ? pool : names;
    }

    private static bool IsSteelFamily(string familyTypeName) => GetFamilyName(familyTypeName).Contains(SteelFamilyToken, StringComparison.Ordinal);

    private static string? GuessFamilyForJoint(IReadOnlyList<string> names, IReadOnlyList<string> keywords, BendConnection joint)
    {
        var word = joint switch
        {
            BendConnection.Socket => "소켓",
            BendConnection.Flanged => "플랜지",
            _ => null
        };
        return word is null ? null : GuessFamilyForJointWord(names, keywords, word);
    }

    private static string? GuessFamilyForJointWord(IReadOnlyList<string> names, IReadOnlyList<string> keywords, string word)
    {
        var prefixed = names.Where(x => GetFamilyName(x).Contains(word + "_", StringComparison.Ordinal)).ToList();
        if (GuessFamily(prefixed, keywords) is { } byPrefix) return byPrefix;
        var contained = names.Where(x => GetFamilyName(x).Contains(word, StringComparison.Ordinal)).ToList();
        return GuessFamily(contained, keywords);
    }

    private static string? GuessFamily(IReadOnlyList<string> names, IReadOnlyList<string> keywords)
        => GuessByKeywords(names, keywords, name => name.StartsWith("DH", StringComparison.OrdinalIgnoreCase))
            ?? GuessByKeywords(names, keywords, _ => true);

    private static string? GuessByKeywords(IEnumerable<string> names, IReadOnlyList<string> keywords, Func<string, bool> condition)
        => keywords.Select(keyword => names.FirstOrDefault(name => condition(name)
                && name.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(name => name is not null);

    /// <summary>선택된 관 패밀리의 인스턴스 파라미터를 다시 읽고, 사용자가 직접 고르지 않은 길이·직경 이름을 자동 추정한다.</summary>
    /// <summary>미뤄 둔 후보 갱신을 한 번씩 실행한다(docs/258 § B). 지연 중이면 아무것도 하지 않는다.
    /// 순서는 종전 추정 경로(<see cref="RefreshFamilyTypeNames"/>)와 같다 — 관 → 이형관·밸브 → 바이패스 밸브.</summary>
    private void FlushDeferredParameterRefresh()
    {
        if (_parameterRefreshDeferral > 0) return;
        if (_pendingSegmentRefresh) RefreshSegmentParameterNames();
        if (_pendingFittingRefresh) RefreshFittingDiameterParameterNames();
        if (_pendingBypassRefresh) RefreshBypassValveParameterNames();
    }

    private void RefreshSegmentParameterNames()
    {
        if (_parameterRefreshDeferral > 0) { _pendingSegmentRefresh = true; return; }   // docs/258 § B
        _pendingSegmentRefresh = false;
        LengthParameterNames.Clear();
        // TODO: 실제로는 카테고리 공통 조회이므로 인터페이스 메서드명을 별도 작업에서 일반화한다.
        var names = string.IsNullOrWhiteSpace(SegmentFamilyTypeName)
            ? []
            : _typeRepo.GetPipeAccessoryInstanceParameterNames(SegmentFamilyTypeName).ToList();
        foreach (var name in names) LengthParameterNames.Add(name);

        // 사용자가 고른 값이 새 패밀리에도 있으면 그대로 둔다.
        if (!(_lengthParameterPinned && _lengthParameterName is not null && names.Contains(_lengthParameterName)))
        {
            // 볼트 이름 고르면 형상 안 바뀌고 PL-303 남(docs/309).
            var lengthCandidates = names.Where(x => !x.Contains(BoltToken, StringComparison.Ordinal)).ToList();
            _lengthParameterName = names.FirstOrDefault(x => string.Equals(x, "직관_길이", StringComparison.Ordinal))
                ?? lengthCandidates.FirstOrDefault(x => x.Contains("길이", StringComparison.Ordinal))
                ?? lengthCandidates.FirstOrDefault(x => x.Contains("Length", StringComparison.OrdinalIgnoreCase))
                ?? lengthCandidates.FirstOrDefault(x => string.Equals(x, "L", StringComparison.OrdinalIgnoreCase));
            _lengthParameterPinned = false;
            OnPropertyChanged(nameof(LengthParameterName));
        }

        if (!(_diameterParameterPinned && _diameterParameterName is not null && names.Contains(_diameterParameterName)))
        {
            _diameterParameterName = GuessDiameterParameter(names);
            _diameterParameterPinned = false;
            OnPropertyChanged(nameof(DiameterParameterName));
        }
    }

    private void RefreshFittingDiameterParameterNames()
    {
        if (_parameterRefreshDeferral > 0) { _pendingFittingRefresh = true; return; }   // docs/258 § B
        _pendingFittingRefresh = false;
        _isRefreshingParameterNames = true;
        try
        {
            var previousBendAngleParameterName = _bendAngleParameterName;
            var wasBendAngleParameterPinned = _bendAngleParameterPinned;
            RefreshFittingDiameterParameterNames(BendParameterNames, BendFamilyTypeName,
                ref _bendDiameterParameterName, ref _bendDiameterParameterPinned, nameof(BendDiameterParameterName));
            if (wasBendAngleParameterPinned && previousBendAngleParameterName is not null && BendParameterNames.Contains(previousBendAngleParameterName))
            {
                _bendAngleParameterName = previousBendAngleParameterName;
                _bendAngleParameterPinned = true;
            }
            else
            {
                _bendAngleParameterName = GuessBendAngleParameter(BendParameterNames);
                _bendAngleParameterPinned = false;
            }
            OnPropertyChanged(nameof(BendAngleParameterName));
            RefreshFittingDiameterParameterNames(TeeParameterNames, TeeFamilyTypeName,
                ref _teeDiameterParameterName, ref _teeDiameterParameterPinned, nameof(TeeDiameterParameterName));
            RefreshFittingDiameterParameterNames(TeeBranchParameterNames, TeeFamilyTypeName,
                ref _teeBranchDiameterParameterName, ref _teeBranchDiameterParameterPinned, nameof(TeeBranchDiameterParameterName),
                GuessTeeBranchDiameterParameter);
            // 십자관은 T형과 같은 규칙(주관·분기), 이경관·편락관은 큰 쪽·작은 쪽이다(docs/175 § B-5).
            RefreshFittingDiameterParameterNames(CrossParameterNames, CrossFamilyTypeName,
                ref _crossDiameterParameterName, ref _crossDiameterParameterPinned, nameof(CrossDiameterParameterName), GuessTeeDiameterParameter);
            RefreshFittingDiameterParameterNames(CrossBranchParameterNames, CrossFamilyTypeName,
                ref _crossBranchDiameterParameterName, ref _crossBranchDiameterParameterPinned, nameof(CrossBranchDiameterParameterName),
                GuessTeeBranchDiameterParameter);
            // Y형관도 주관·분기는 T형과 같은 규칙이고, 분기각만 곡관 각도 추정을 쓴다(docs/187 § 3-4).
            RefreshFittingDiameterParameterNames(WyeParameterNames, WyeFamilyTypeName,
                ref _wyeDiameterParameterName, ref _wyeDiameterParameterPinned, nameof(WyeDiameterParameterName), GuessTeeDiameterParameter);
            RefreshFittingDiameterParameterNames(WyeBranchParameterNames, WyeFamilyTypeName,
                ref _wyeBranchDiameterParameterName, ref _wyeBranchDiameterParameterPinned, nameof(WyeBranchDiameterParameterName),
                GuessTeeBranchDiameterParameter);
            var previousWyeAngleParameterName = _wyeBranchAngleParameterName;
            var wasWyeAngleParameterPinned = _wyeBranchAngleParameterPinned;
            WyeAngleParameterNames.Clear();
            foreach (var name in WyeParameterNames) WyeAngleParameterNames.Add(name);
            if (wasWyeAngleParameterPinned && previousWyeAngleParameterName is not null && WyeAngleParameterNames.Contains(previousWyeAngleParameterName))
            {
                _wyeBranchAngleParameterName = previousWyeAngleParameterName;
                _wyeBranchAngleParameterPinned = true;
            }
            else
            {
                _wyeBranchAngleParameterName = GuessBendAngleParameter(WyeAngleParameterNames);
                _wyeBranchAngleParameterPinned = false;
            }
            OnPropertyChanged(nameof(WyeBranchAngleParameterName));
            // 이경관·밸브부관 A는 콤보마다 컬렉션을 따로 쓴다(docs/232 § B) — 한 컬렉션을 두 번 Clear하면 앞 칸의 선택이 지워진다.
            RefreshFittingDiameterParameterNames(ReducerLargeParameterNames, ReducerFamilyTypeName,
                ref _reducerLargeDiameterParameterName, ref _reducerLargeDiameterParameterPinned, nameof(ReducerLargeDiameterParameterName));
            RefreshFittingDiameterParameterNames(ReducerSmallParameterNames, ReducerFamilyTypeName,
                ref _reducerSmallDiameterParameterName, ref _reducerSmallDiameterParameterPinned, nameof(ReducerSmallDiameterParameterName),
                GuessReducerSmallDiameterParameter);
            RefreshFittingDiameterParameterNames(ValveBendParameterNames, ValveBendFamilyTypeName,
                ref _valveBendDiameterParameterName, ref _valveBendDiameterParameterPinned, nameof(ValveBendDiameterParameterName),
                GuessValveBendDiameterParameter);
            // 밸브부관 A — 주관·분기는 T형 추정 규칙(docs/227 § B).
            RefreshFittingDiameterParameterNames(ValveTeeMainParameterNames, ValveTeeFamilyTypeName,
                ref _valveTeeMainDiameterParameterName, ref _valveTeeMainDiameterParameterPinned, nameof(ValveTeeMainDiameterParameterName),
                GuessTeeDiameterParameter);
            RefreshFittingDiameterParameterNames(ValveTeeBranchParameterNames, ValveTeeFamilyTypeName,
                ref _valveTeeBranchDiameterParameterName, ref _valveTeeBranchDiameterParameterPinned, nameof(ValveTeeBranchDiameterParameterName),
                GuessTeeBranchDiameterParameter);
            // 수평·수직분기(예/아니오)는 UI가 없다(docs/232 § A-4) — 콤보 컬렉션 없이 주관 후보 목록에서 추정만 한다.
            // 이름에 `수평`·`수직`이 들고 구경이 아닌 것. 못 찾으면 배치가 구동을 건너뛰고 경고만 남긴다.
            RefreshHiddenParameterName(ValveTeeMainParameterNames,
                ref _valveTeeHorizontalBranchParameterName, ref _valveTeeHorizontalBranchParameterPinned, nameof(ValveTeeHorizontalBranchParameterName),
                names => GuessFlagParameter(names, ValveTeeHorizontalFlagKeywords));
            RefreshHiddenParameterName(ValveTeeMainParameterNames,
                ref _valveTeeVerticalBranchParameterName, ref _valveTeeVerticalBranchParameterPinned, nameof(ValveTeeVerticalBranchParameterName),
                names => GuessFlagParameter(names, ValveTeeVerticalFlagKeywords));
            var valveFamilyTypeName = CurrentValveFamilyTypeName;
            // 밸브 직경 후보는 실수형 인스턴스 파라미터만(docs/233 § B-1) — 예/아니오가 직경으로 잡혀 세트가 건너뛰어지는 것을 막는다.
            RefreshFittingDiameterParameterNames(ValveParameterNames, valveFamilyTypeName,
                ref _valveDiameterParameterName, ref _valveDiameterParameterPinned, nameof(ValveDiameterParameterName),
                namesSource: NumericParameterNamesFor);
            RestoreValveDiameterParameterForFamily(valveFamilyTypeName);
            RefreshTypeDiameterParameterNames(ValveTypeParameterNames, valveFamilyTypeName,
                ref _valveTypeDiameterParameterName, nameof(ValveTypeDiameterParameterName));
            // 이음관 직경 파라미터는 전역 1개다(docs/167 § C). 후보는 실제로 쓰이는 이음관 패밀리에서 뽑는다.
            RefreshFittingDiameterParameterNames(JointParameterNames, CurrentJointFamilyTypeName,
                ref _jointDiameterParameterName, ref _jointDiameterParameterPinned, nameof(JointDiameterParameterName));
        }
        finally
        {
            _isRefreshingParameterNames = false;
        }
    }

    private void RefreshFittingDiameterParameterNames(ObservableCollection<string> target, string? familyTypeName,
        ref string? selectedParameterName, ref bool pinned, string propertyName, Func<IEnumerable<string>, string?>? guess = null,
        Func<string?, IReadOnlyList<string>>? namesSource = null)
    {
        var previousParameterName = selectedParameterName;
        var wasPinned = pinned;
        target.Clear();
        // 후보 공급자는 기본이 전체 인스턴스 파라미터. 밸브 직경만 실수형 공급자를 넘긴다(docs/233 § B-1).
        var names = (namesSource ?? ParameterNamesFor)(familyTypeName);
        foreach (var name in names) target.Add(name);
        if (wasPinned && previousParameterName is not null && names.Contains(previousParameterName))
        {
            selectedParameterName = previousParameterName;
            pinned = true;
        }
        else
        {
            selectedParameterName = guess is not null
                ? guess(names)
                : propertyName == nameof(TeeDiameterParameterName)
                    ? GuessTeeDiameterParameter(names)
                    : GuessDiameterParameter(names);
            pinned = false;
        }
        OnPropertyChanged(propertyName);
    }

    /// <summary>콤보가 없는 파라미터(밸브부관 A 수평·수직분기)의 선택값 갱신(docs/232 § B-2). Clear/Add 없이
    /// 이미 채워진 후보를 읽어 "핀 + 후보에 있으면 유지, 아니면 추정"만 한다 — <see cref="RefreshFittingDiameterParameterNames(ObservableCollection{string}, string?, ref string?, ref bool, string, Func{IEnumerable{string}, string?}?)"/>의 else 분기와 같다.</summary>
    private void RefreshHiddenParameterName(IReadOnlyCollection<string> names,
        ref string? selectedParameterName, ref bool pinned, string propertyName, Func<IEnumerable<string>, string?> guess)
    {
        if (!(pinned && selectedParameterName is not null && names.Contains(selectedParameterName)))
        {
            selectedParameterName = guess(names);
            pinned = false;
        }
        OnPropertyChanged(propertyName);
    }

    /// <summary>바이패스 밸브 직경 파라미터 후보·선택값 갱신(docs/232 § C-1). 패밀리별 사전에 기억된 값이 후보에 있으면 그것,
    /// 없으면 일반 직경 추정값을 쓰고 그 추정값을 사전에 넣는다(4번 섹션 <see cref="RestoreValveDiameterParameterForFamily"/>와 같은 규칙).</summary>
    private void RefreshBypassValveParameterNames()
    {
        if (_parameterRefreshDeferral > 0) { _pendingBypassRefresh = true; return; }   // docs/258 § B
        _pendingBypassRefresh = false;
        var wasRefreshing = _isRefreshingParameterNames;   // 다른 갱신 안에서 불려도 바깥 플래그를 되돌린다.
        _isRefreshingParameterNames = true;
        try
        {
            BypassValveParameterNames.Clear();
            var names = NumericParameterNamesFor(BypassValveTypeName);   // 실수형만(docs/233 § B-1)
            foreach (var name in names) BypassValveParameterNames.Add(name);
            string? selected = null;
            if (!string.IsNullOrWhiteSpace(BypassValveTypeName) && ValveFamilyKey(BypassValveTypeName) is { } key)
            {
                selected = _valveDiameterParameterByFamily.TryGetValue(key, out var remembered) && names.Contains(remembered)
                    ? remembered
                    : GuessDiameterParameter(names);
                RememberValveDiameterParameter(BypassValveTypeName, selected);
            }
            _bypassValveDiameterParameterName = selected;
            OnPropertyChanged(nameof(BypassValveDiameterParameterName));
        }
        finally
        {
            _isRefreshingParameterNames = wasRefreshing;
        }
    }

    // ── 유형 직경 매개변수 확인(docs/233 § B-3) ────────────────────────────────

    /// <summary>유형의 길이형 유형 매개변수 후보를 채우고 선택값을 정한다 — 패밀리별 기억값이 후보에 있으면 그것, 없으면 <see cref="GuessDiameterParameter"/> 추정.
    /// 인스턴스 직경 파라미터(<see cref="RefreshBypassValveParameterNames"/>)와 같은 규칙이다. 호출부가 <see cref="_isRefreshingParameterNames"/>를 켜 둔 상태여야
    /// Clear()가 밀어 넣는 null이 사전에 기록되지 않는다.</summary>
    private void RefreshTypeDiameterParameterNames(ObservableCollection<string> target, string? familyTypeName,
        ref string? selectedParameterName, string propertyName)
    {
        var names = TypeLengthParametersFor(familyTypeName).Select(x => x.Name).ToList();
        target.Clear();
        foreach (var name in names) target.Add(name);
        string? selected = null;
        if (!string.IsNullOrWhiteSpace(familyTypeName) && ValveFamilyKey(familyTypeName) is { } key)
        {
            selected = _valveTypeDiameterParameterByFamily.TryGetValue(key, out var remembered) && names.Contains(remembered)
                ? remembered
                : GuessDiameterParameter(names);
            RememberValveTypeDiameterParameter(familyTypeName, selected);
        }
        selectedParameterName = selected;
        OnPropertyChanged(propertyName);
        NotifyTypeDiameterChanged();
    }

    private void RememberValveTypeDiameterParameter(string? familyTypeName, string? parameterName)
    {
        if (string.IsNullOrWhiteSpace(familyTypeName) || ValveFamilyKey(familyTypeName) is not { } key) return;
        if (string.IsNullOrWhiteSpace(parameterName)) _valveTypeDiameterParameterByFamily.Remove(key);
        else _valveTypeDiameterParameterByFamily[key] = parameterName;
    }

    private IReadOnlyList<PipeAccessoryTypeParameterDto> TypeLengthParametersFor(string? familyTypeName)
    {
        if (string.IsNullOrWhiteSpace(familyTypeName)) return [];
        if (!_typeLengthParameterCache.TryGetValue(familyTypeName, out var parameters))
            _typeLengthParameterCache[familyTypeName] = parameters = _typeRepo.GetPipeAccessoryTypeLengthParameters(familyTypeName).ToList();
        return parameters;
    }

    /// <summary>유형 직경 매개변수 값과 대상 관경을 대조한 안내 문구. 0.5mm 미만 차이는 같은 값으로 본다(유형 자동 선택 <c>Fill</c>과 같은 허용치).</summary>
    private string TypeDiameterText(string? familyTypeName, string? parameterName, double targetMm, out bool mismatch)
    {
        mismatch = false;
        if (string.IsNullOrWhiteSpace(familyTypeName)) return "유형을 고르면 유형 매개변수로 관경을 대조합니다.";
        var parameters = TypeLengthParametersFor(familyTypeName);
        if (parameters.Count == 0) return "길이형 유형 매개변수가 없어 유형으로 관경을 확인할 수 없습니다.";
        if (string.IsNullOrWhiteSpace(parameterName) || parameters.FirstOrDefault(x => x.Name == parameterName) is not { } parameter)
            return "유형 직경 매개변수를 고르면 관경과 대조합니다.";
        if (Math.Abs(parameter.ValueMm - targetMm) < 0.5) return $"유형 {parameter.Name} = {parameter.ValueMm:0.#} mm ✓ 관경 일치";
        mismatch = true;
        return $"유형 {parameter.Name} = {parameter.ValueMm:0.#} mm ⚠ 관경 {targetMm:0.#} mm와 다릅니다";
    }

    /// <summary>놓인 밸브·세트가 쓰는 유형의 직경값이 대상 관경과 다른지. 매개변수명은 패밀리별 기억값 → 추정 순(콤보와 같은 규칙).</summary>
    private bool IsTypeDiameterMismatch(string familyTypeName, double targetMm)
    {
        var parameters = TypeLengthParametersFor(familyTypeName);
        if (parameters.Count == 0) return false;
        var names = parameters.Select(x => x.Name).ToList();
        var name = ValveFamilyKey(familyTypeName) is { } key && _valveTypeDiameterParameterByFamily.TryGetValue(key, out var remembered) && names.Contains(remembered)
            ? remembered
            : GuessDiameterParameter(names);
        return name is not null && parameters.First(x => x.Name == name) is { } parameter && Math.Abs(parameter.ValueMm - targetMm) >= 0.5;
    }

    private void NotifyTypeDiameterChanged()
    {
        OnPropertyChanged(nameof(ValveTypeDiameterText));
        OnPropertyChanged(nameof(IsValveTypeDiameterMismatch));
        OnPropertyChanged(nameof(ValidationSummary));
    }

    /// <summary>생성을 막지 않는 경고(docs/233 § B-3). 놓인 인라인 밸브는 올라탄 선의 관경과 유형 직경값을 대조한다.
    /// 바이패스 세트는 대조하지 않는다 — 밸브 유형을 모델 생성 때 바이패스 관경과 같은 DN으로 고른다(docs/244).</summary>
    private List<string> GetValidationWarnings()
    {
        var warnings = new List<string>();
        var valveMismatch = 0;
        foreach (var edge in _network.Edges)
            foreach (var fitting in edge.InlineFittings)
            {
                if (fitting.IsJoint || fitting.IsBypassSet) continue;
                if (!string.IsNullOrWhiteSpace(fitting.FamilyTypeName) && IsTypeDiameterMismatch(fitting.FamilyTypeName, edge.DiameterMm)) valveMismatch++;
            }
        if (valveMismatch > 0) warnings.Add($"유형 직경이 선 관경과 다른 밸브 {valveMismatch}개 — 5번 섹션 유형 직경 매개변수 확인");

        // 되가져오기는 패밀리명 키워드로 밸브부관을 가린다(RevitValveRoomPipingPickRepo.Classify). 키워드 없는 패밀리로 만들면
        // 모델은 생기지만 되가져올 때 A는 T형·십자, B는 곡관으로 돌아와 세트가 풀린다(2026-09-29).
        var hasSet = _network.Edges.Any(e => e.InlineFittings.Any(x => x.IsBypassSet));
        if ((hasSet || _network.Nodes.Any(IsValveTeeNode)) && !string.IsNullOrWhiteSpace(ValveTeeFamilyTypeName)
            && !PipeValveBend.IsValveBendFamily(ValveTeeFamilyTypeName))
            warnings.Add($"밸브부관 A 패밀리명에 '{PipeValveBend.FamilyKeyword}'가 없어 되가져오기 시 T형·십자로 돌아옵니다");
        if ((hasSet || _network.Nodes.Any(IsValveBendNode)) && !string.IsNullOrWhiteSpace(ValveBendFamilyTypeName)
            && !PipeValveBend.IsValveBendFamily(ValveBendFamilyTypeName))
            warnings.Add($"밸브부관 B 패밀리명에 '{PipeValveBend.FamilyKeyword}'가 없어 되가져오기 시 곡관으로 돌아옵니다");
        return warnings;
    }

    /// <summary>밸브 직경 파라미터 후보를 뽑는 기준이 되는 "패밀리 : 유형". 패밀리별 저장 키도 여기서 나온다(docs/135 § B).</summary>
    private string? CurrentValveFamilyTypeName => SelectedFamilyTypeName ?? FamilyTypeNames.FirstOrDefault();

    /// <summary>이음관 직경 파라미터 후보를 뽑는 기준. 실제로 배치에 쓰이는 패밀리를 먼저 보고,
    /// 아직 없으면 마지막에 쓴 것 → 목록 첫 항목 순으로 내려간다.</summary>
    private string? CurrentJointFamilyTypeName
        => _network.Edges.SelectMany(x => x.InlineFittings)
               .Where(x => x.IsJoint && !string.IsNullOrWhiteSpace(x.FamilyTypeName))
               .Select(x => x.FamilyTypeName)
               .FirstOrDefault()
           ?? SelectedJointFamilyTypeName
           ?? JointFamilyTypeNames.FirstOrDefault();

    /// <summary>패밀리별 저장 키. 유형명을 뺀 패밀리명이며 리포지토리의 <c>symbol.Family.Name</c>과 같은 값이다.</summary>
    private string? ValveFamilyKey(string? familyTypeName)
        => !string.IsNullOrWhiteSpace(familyTypeName) ? GetFamilyName(familyTypeName)
            : string.IsNullOrWhiteSpace(_selectedValveFamilyName) ? null : _selectedValveFamilyName;

    private void RememberValveDiameterParameter(string? familyTypeName, string? parameterName)
    {
        if (ValveFamilyKey(familyTypeName) is not { } key) return;
        // 빈 값은 "이 패밀리는 직경을 구동하지 않음"이므로 기억해 둘 것이 없다.
        if (string.IsNullOrWhiteSpace(parameterName)) _valveDiameterParameterByFamily.Remove(key);
        else _valveDiameterParameterByFamily[key] = parameterName;
    }

    /// <summary>이 패밀리에 저장된 값이 후보에 있으면 추정값보다 우선해 복원한다.
    /// 저장된 값이 없으면 이번 추정값을 그 패밀리 값으로 기억해 둔다 — 사용자가 한 번도 손대지 않은
    /// 패밀리도 자기 추정값으로 구동되게 하려는 것이다(docs/135 § B-5).</summary>
    private void RestoreValveDiameterParameterForFamily(string? familyTypeName)
    {
        if (ValveFamilyKey(familyTypeName) is not { } key) return;
        if (_valveDiameterParameterByFamily.TryGetValue(key, out var remembered) && ValveParameterNames.Contains(remembered))
        {
            if (string.Equals(_valveDiameterParameterName, remembered, StringComparison.Ordinal)) return;
            _valveDiameterParameterName = remembered;
            OnPropertyChanged(nameof(ValveDiameterParameterName));
            return;
        }
        RememberValveDiameterParameter(familyTypeName, _valveDiameterParameterName);
    }

    private IReadOnlyList<string> ParameterNamesFor(string? familyTypeName)
        => string.IsNullOrWhiteSpace(familyTypeName)
            ? []
            : _typeRepo.GetPipeAccessoryInstanceParameterNames(familyTypeName).ToList();

    /// <summary>실수형 인스턴스 파라미터만(docs/233 § B-1). 밸브 직경 콤보 2곳(4번·5번) 전용.</summary>
    private IReadOnlyList<string> NumericParameterNamesFor(string? familyTypeName)
        => string.IsNullOrWhiteSpace(familyTypeName)
            ? []
            : _typeRepo.GetPipeAccessoryNumericInstanceParameterNames(familyTypeName).ToList();

    /// <summary>밸브부관 A 수평·수직분기 예/아니오 파라미터 추정 — 키워드 우선순위(docs/232 § A-3).
    /// `수평분기_구경` 같은 직경 파라미터와 이름이 겹치므로 구경·직경이 든 이름은 뺀다.</summary>
    private static string? GuessFlagParameter(IEnumerable<string> names, IReadOnlyList<string> keywords)
    {
        var candidates = (names as IReadOnlyCollection<string> ?? names.ToList())
            .Where(name => !name.Contains("구경", StringComparison.Ordinal)
                        && !name.Contains("직경", StringComparison.Ordinal)
                        && !name.Contains(RadiusToken, StringComparison.Ordinal)).ToList();
        // 키워드와 이름이 정확히 같은 파라미터(`수평분기`)를 먼저 고른다(docs/241) — `수평분기_길이` 같은 이름이 앞에 있어도 밀리지 않게.
        return keywords.Select(keyword => candidates.FirstOrDefault(name => string.Equals(name, keyword, StringComparison.OrdinalIgnoreCase)))
            .Concat(keywords.Select(keyword => candidates.FirstOrDefault(name => name.Contains(keyword, StringComparison.OrdinalIgnoreCase))))
            .FirstOrDefault(name => name is not null);
    }

    /// <summary>키워드 우선순위로 직경 파라미터를 고른다(docs/232 § A-3). 앞 키워드부터, 이름에 키워드가 포함되고
    /// <see cref="DiameterTokens"/> 중 하나가 함께 들었으며 반지름이 아닌 첫 이름. 못 찾으면 null(호출부가 폴백을 정한다).</summary>
    private static string? GuessDiameterByKeywords(IEnumerable<string> names, IReadOnlyList<string> keywords)
    {
        var candidates = (names as IReadOnlyCollection<string> ?? names.ToList())
            .Where(name => !name.Contains(RadiusToken, StringComparison.Ordinal)
                        && DiameterTokens.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase))).ToList();
        return keywords.Select(keyword => candidates.FirstOrDefault(name => name.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(name => name is not null);
    }

    /// <summary>밸브부관 B 호칭지름 — <see cref="ValveBendDiameterKeywords"/>(`부관_구경` 1순위) → 일반 직경 추정.</summary>
    private static string? GuessValveBendDiameterParameter(IEnumerable<string> names)
    {
        var candidates = names as IReadOnlyCollection<string> ?? names.ToList();
        return GuessDiameterByKeywords(candidates, ValveBendDiameterKeywords) ?? GuessDiameterParameter(candidates);
    }

    /// <summary>주관 직경 — <see cref="ValveTeeMainDiameterKeywords"/>(`주관_구경` 1순위) → 공칭 → "구경" 포함 → 일반 규칙.
    /// 밸브부관 A와 T형·십자·Y형 주관이 함께 쓴다.</summary>
    private static string? GuessTeeDiameterParameter(IEnumerable<string> names)
    {
        var candidates = names as IReadOnlyCollection<string> ?? names.ToList();
        return GuessDiameterByKeywords(candidates, ValveTeeMainDiameterKeywords)
            ?? GuessNominalDiameterParameter(candidates)
            ?? candidates.FirstOrDefault(name => name.Contains("구경", StringComparison.Ordinal)
                                      && !name.Contains(RadiusToken, StringComparison.Ordinal))
            ?? GuessDiameterParameter(candidates);
    }

    /// <summary>분기 직경 — <see cref="ValveTeeBranchDiameterKeywords"/>(`수평분기_구경` 1순위, 직경 토큰이 함께 든 이름만).
    /// 못 찾으면 비워 두는 것이 정상이다(docs/130 § A) — GuessDiameterParameter로 폴백시키면 주관 구경이 분기 칸에 들어간다.</summary>
    private static string? GuessTeeBranchDiameterParameter(IEnumerable<string> names)
        => GuessDiameterByKeywords(names, ValveTeeBranchDiameterKeywords);

    /// <summary>이경관·편락관의 작은 쪽 직경. 못 찾으면 비워 둔다 — 큰 쪽 추정값을 여기에 넣으면 두 칸이 같은 파라미터를 구동한다(docs/175 § B-5).</summary>
    private static string? GuessReducerSmallDiameterParameter(IEnumerable<string> names)
    {
        var candidates = (names as IReadOnlyCollection<string> ?? names.ToList())
            .Where(name => !name.Contains(RadiusToken, StringComparison.Ordinal)).ToList();
        return ReducerSmallDiameterKeywords
            .Select(keyword => candidates.FirstOrDefault(name => name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                && (name.Contains("구경", StringComparison.Ordinal) || name.Contains("지름", StringComparison.Ordinal)
                    || name.Contains("Diameter", StringComparison.OrdinalIgnoreCase) || name.Contains("DN", StringComparison.OrdinalIgnoreCase))))
            .FirstOrDefault(name => name is not null);
    }

    /// <summary>이름에 "공칭"과 "지름"·"구경"이 함께 든 파라미터를 최우선으로 고른다(docs/138 § A).
    /// 프로젝트가 카테고리에 바인딩한 공유 파라미터("O0201 - 관경")나 부속 파라미터("가스켓_구경")가
    /// 키워드 순서·이름 정렬 때문에 먼저 잡히던 것을 막는다.
    /// 구분자는 따로 처리하지 않아도 된다 — "공칭"과 "지름"을 각각 검사하므로 "공칭_지름"·"공칭 지름"이 모두 걸린다.
    /// "반지름" 제외를 여기에도 거는 이유: "공칭_반지름"이 "지름"을 포함해 docs/133 § 2의 방어가 뚫린다.</summary>
    private static string? GuessNominalDiameterParameter(IEnumerable<string> names)
        => names.FirstOrDefault(name => name.Contains("공칭", StringComparison.Ordinal)
            && (name.Contains("지름", StringComparison.Ordinal) || name.Contains("구경", StringComparison.Ordinal))
            && !name.Contains(RadiusToken, StringComparison.Ordinal));

    /// <summary>ND·DN·NB처럼 이름 전체가 약어인 직경 파라미터. 완전일치로만 찾는다(docs/145 § 2).</summary>
    private static string? GuessExactDiameterParameter(IEnumerable<string> names)
    {
        var candidates = names as IReadOnlyCollection<string> ?? names.ToList();
        return ExactDiameterParameterNames
            .Select(token => candidates.FirstOrDefault(name => string.Equals(name, token, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(name => name is not null);
    }

    /// <summary>직경 파라미터 자동추정의 단일 진입점. 공칭 이름 → 영문 약어 완전일치 → 기존 키워드 순서다(docs/138 § A, docs/145 § 2).</summary>
    private static string? GuessDiameterParameter(IEnumerable<string> names)
    {
        var candidates = names as IReadOnlyCollection<string> ?? names.ToList();
        return GuessNominalDiameterParameter(candidates)
            ?? GuessExactDiameterParameter(candidates)
            ?? GuessParameter(candidates, DiameterParameterDefaultKeywords);
    }

    /// <summary>곡관 각도는 "곡관_각도" 완전일치를 먼저 보고, 없으면 "각도"를 포함하되 "허용*"이 아닌 이름을 고른다(docs/138 § B).
    /// 목록을 패밀리 파라미터로 좁혀도(docs/138 § C) 패밀리 문서를 못 여는 폴백 경로가 남아 있어 이 규칙이 필요하다.</summary>
    private static string? GuessBendAngleParameter(IEnumerable<string> names)
        => names.FirstOrDefault(name => string.Equals(name, "곡관_각도", StringComparison.Ordinal))
            ?? GuessByKeywords(names, BendAngleParameterDefaultKeywords,
                name => !name.Contains(AllowanceToken, StringComparison.Ordinal));

    /// <summary>이름에 "반지름"이 든 파라미터는 자동선택하지 않는다(docs/133).
    /// 관경(지름)을 반지름 파라미터에 쓰면 형상이 2배가 되므로, 못 찾으면 빈 값으로 두고 사용자가 직접 고르게 한다.</summary>
    private static string? GuessParameter(IEnumerable<string> names, IReadOnlyList<string> keywords)
        => GuessByKeywords(names, keywords, name => !name.Contains(RadiusToken, StringComparison.Ordinal));

    private void RemoveFitting(InlineFittingItem? fitting)
    {
        if (fitting is null || IsCreating) return;   // docs/235 § D
        // TODO: 분할 후에도 엣지 ID를 보존하는 Phase 2 편집 API를 추가할 수 있다.
        var edge = _network.Edges.FirstOrDefault(x => x.Id == fitting.EdgeId);
        if (edge is null) return;
        var before = Snapshot();
        var wasSelected = SelectedEdgeIds.Contains(edge.Id);
        _network.RemoveInlineFitting(edge.Id, fitting.Id);
        BeginEditing();
        RecordUndoIfChanged(before);
        SelectedFittingIds.Remove(fitting.Id);
        var replacement = _network.Edges.FirstOrDefault(x =>
            x.StartNodeId == edge.StartNodeId && x.EndNodeId == edge.EndNodeId);
        if (wasSelected)
        {
            SelectedEdgeIds.Remove(edge.Id);
            if (replacement is not null) SelectedEdgeIds.Add(replacement.Id);
        }
        RefreshGraph();
        if (wasSelected) SelectedEdge = Edges.FirstOrDefault(x => x.Id == replacement?.Id);
        SelectedFitting = InlineFittings.FirstOrDefault(x => SelectedFittingIds.Contains(x.Id));
        CommandManager.InvalidateRequerySuggested();
    }

    public void UpdateInputValidation(bool errorAdded)
    {
        _inputValidationErrorCount = Math.Max(0, _inputValidationErrorCount + (errorAdded ? 1 : -1));
        NotifyValidationChanged();
    }

    public void ApplyModelCreationResult(bool succeeded, string message)
        => ApplyModelCreationResult(succeeded, message, []);

    /// <summary>생성 결과와 함께 캔버스 Guid → Revit ElementId 매핑을 받는다(docs/179 § E-4).
    /// <para>실패는 트랜잭션이 통째로 롤백된 것이므로 예전 매핑을 그대로 둔다 — 모델은 안 바뀌었다.</para></summary>
    public void ApplyModelCreationResult(bool succeeded, string message,
        IReadOnlyList<PipeConnectionFailure> failures,
        IReadOnlyDictionary<Guid, IReadOnlyList<long>> placedElementIds)
    {
        if (succeeded) _revitElementIds = new Dictionary<Guid, IReadOnlyList<long>>(placedElementIds);
        ApplyModelCreationResult(succeeded, message, failures);
        if (!succeeded) return;
        // 다음 [모델 생성]이 이번 생성분을 지우고 다시 만들게 한다(docs/235 § C). 위 호출이 가져온 원본을 이미 잊었다.
        _createdElementIds = placedElementIds.Values.SelectMany(x => x).Distinct().ToList();
        OnPropertyChanged(nameof(HasReplaceTarget));
    }

    public void ApplyModelCreationResult(bool succeeded, string message, IReadOnlyList<PipeConnectionFailure> failures)
    {
        IsCreating = false;
        _failures.Clear();
        _failures.AddRange(failures);
        // 원본을 지우고 다시 만든 뒤에는 삭제 대상이 남아 있으면 안 된다. 두 번 지우려 들면 안 되기 때문이다(docs/158 § E-6).
        if (succeeded) ForgetImportedSource();
        if (!succeeded && _failures.Count > 0) CenterFirstFailureIfOutsideViewport(_failures[0]);
        RefreshGraph();
        // 성공 문구는 호출측이 완성해 보낸다("배치 성공" / "배치 완료 · 실패 N개") — 상세는 로그로만 남긴다(2026-10-01 사용자 결정).
        Status = succeeded
            ? message
            : $"생성 실패 · {message}{FormatFailurePoint(_failures.FirstOrDefault())}";
    }

    private void Undo()
    {
        if (_undoHistory.Count == 0) return;
        var snapshot = _undoHistory.Pop();
        _network = snapshot.Network;
        // 가져오기를 되돌리면 삭제 예약도 함께 풀린다(docs/163 § C-1).
        _importedSourceIds = snapshot.ImportedSourceIds;
        BeginEditing();
        _elevation = _network.Elevation;
        OnPropertyChanged(nameof(Elevation));
        OnPropertyChanged(nameof(ElevationM));
        RecalculateElevationOffsetFromCurrent();
        OnPropertyChanged(nameof(HasImportedSource));
        OnPropertyChanged(nameof(HasReplaceTarget));
        // 되돌린 이격이 다음 외곽 피킹에서 중앙값으로 덮이면 안 되므로 사용자 편집값으로 복원한다(docs/172 § C-10).
        RestorePorts(snapshot.Ports, userEdited: true);
        SetSegmentStart(null);
        ClearSelection();
        IsPreviewVisible = false;
        IsSubLineVisible = false;
        IsSnapMarkerVisible = false;
        HideFittingPreview();
        TempDimensions.Clear();
        RefreshOutline();
        RefreshGraph();
        Status = "되돌리기";
    }

    /// <summary>편집 직전 스냅샷. 그래프 복제본과 그때의 원본 목록·포트 목록을 한 묶음으로 잡는다(docs/163 § C-1, docs/172 § C-10).
    /// <see cref="_importedSourceIds"/>는 항상 통째로 재할당되고 원소를 더하거나 빼지 않으므로 참조만 들고 있어도 안전하다.
    /// 포트는 항목이 바뀌므로 Core 레코드로 복사한다.</summary>
    private (PipeNetwork Network, IReadOnlyList<long> ImportedSourceIds, IReadOnlyList<PipePort> Ports) Snapshot()
        => (_network.DeepClone(), _importedSourceIds, Ports.Select(x => x.ToPort()).ToList());

    /// <summary>비교는 그래프만 본다 — 포트만 바뀐 편집은 종전처럼 Undo 대상이 아니다(docs/172 § C-10).</summary>
    private void RecordUndoIfChanged((PipeNetwork Network, IReadOnlyList<long> ImportedSourceIds, IReadOnlyList<PipePort> Ports) before)
    {
        if (SameNetwork(before.Network, _network)) return;
        _undoHistory.Push(before);
        NotifyValidationChanged();
    }

    private static bool SameNetwork(PipeNetwork a, PipeNetwork b)
    {
        if (a.Nodes.Count != b.Nodes.Count || a.Edges.Count != b.Edges.Count) return false;
        var aNodes = a.Nodes.OrderBy(x => x.Id).ToList();
        var bNodes = b.Nodes.OrderBy(x => x.Id).ToList();
        // 밸브부관 전환·뒤집기만 한 편집도 Undo 대상이다(docs/225 § C).
        if (!aNodes.Zip(bNodes).All(x => x.First.Id == x.Second.Id && x.First.Position == x.Second.Position && x.First.NodeKind == x.Second.NodeKind
                && x.First.FittingKind == x.Second.FittingKind && x.First.ValveSideDirection == x.Second.ValveSideDirection)) return false;
        var aEdges = a.Edges.OrderBy(x => x.Id).ToList();
        var bEdges = b.Edges.OrderBy(x => x.Id).ToList();
        // 관경 비교를 빠뜨리면 "관경만 바꾼 편집"이 Undo 스택에 쌓이지 않는다(docs/152 § E-3).
        return aEdges.Zip(bEdges).All(x => x.First.Id == x.Second.Id &&
            x.First.StartNodeId == x.Second.StartNodeId && x.First.EndNodeId == x.Second.EndNodeId &&
            x.First.DiameterMm == x.Second.DiameterMm &&
            x.First.InlineFittings.SequenceEqual(x.Second.InlineFittings));
    }

    private void SetSegmentStart(Point? value)
    {
        _segmentStart = value;
        OnPropertyChanged(nameof(IsDrawing));
        OnPropertyChanged(nameof(InteractionModeText));
        CommandManager.InvalidateRequerySuggested();
    }

    private bool CanCreateModel() => CreateModelAction is not null && !IsCreating && GetValidationErrors().Count == 0;

    private List<string> GetValidationErrors()
    {
        var errors = new List<string>();
        if (_inputValidationErrorCount > 0) errors.Add("빨간 칸 숫자 확인 필요");
        if (_network.Edges.Count == 0) errors.Add("배관 1개 이상 필요");
        // MEP Pipe를 만들지 않으므로 시스템 타입·PipeType은 필수가 아니다(콤보는 향후 병행을 위해 남겨 둔다).
        if (string.IsNullOrWhiteSpace(SegmentFamilyTypeName)) errors.Add("관 패밀리 선택 필요");
        if (string.IsNullOrWhiteSpace(LengthParameterName)) errors.Add("관 길이 파라미터 선택 필요");
        if (!double.IsFinite(StraightLengthMm) || StraightLengthMm <= 0) errors.Add("정척 길이 > 0 필요");
        if (!double.IsFinite(_diameterMm) || _diameterMm <= 0) errors.Add("기본 관경 > 0 필요");
        var invalidDiameterCount = _network.Edges.Count(x => !double.IsFinite(x.DiameterMm) || x.DiameterMm <= 0);
        if (invalidDiameterCount > 0) errors.Add($"관경 0 이하 배관 {invalidDiameterCount}개");
        if (!double.IsFinite(Elevation) || !double.IsFinite(ReferenceX) || !double.IsFinite(ReferenceY)) errors.Add("좌표·Elev 숫자 확인 필요");
        if (ElevationMode == PipeElevationMode.Relative && ElevationLevel is null) errors.Add("기준 레벨 선택 필요");
        if (!double.IsFinite(ElevationOffsetMm)) errors.Add("좌표·Elev 숫자 확인 필요");
        if (Ports.Any(x => x.OffsetMm < 0)) errors.Add("IN/OUT 이격 ≥ 0 필요");
        if (!_canvasOutline.IsEmpty)
        {
            if (Ports.Any(x => x.OffsetMm > _canvasOutline.SideLengthOf(x.Side))) errors.Add("IN/OUT 이격이 외곽 내부 높이 초과");
            // 동작 변경(docs/172 § C-9) — 같은 벽에 놓인 두 포트가 관경보다 가까우면 관이 겹친다(이토밸브실 IN 2개가 같은 좌측 벽에 놓인다).
            var overlapping = Ports.GroupBy(x => x.Side)
                .SelectMany(g => g.SelectMany((a, i) => g.Skip(i + 1).Select(b => (a, b))))
                .Count(p => Math.Abs(p.a.OffsetMm - p.b.OffsetMm) < _diameterMm);
            if (overlapping > 0) errors.Add($"같은 벽 IN/OUT 포트 {overlapping}쌍 — 관경보다 가까움");
        }

        if (_network.Edges.Count > 0)
        {
            var definition = _network.ToDefinition(DiameterMm, PipeOutputMode.PipeAccessorySegment, new(ReferenceX, ReferenceY));
            var invalidTeeCount = definition.Nodes.Count(x => x.NodeKind == NodeKind.Tee && PipeTeeResolver.Resolve(x, definition.Edges) is null);
            // 밸브부관 A로 바꾼 T는 T형 패밀리를 쓰지 않는다(docs/227 § C). 아래 T형 패밀리 요구가 이 값을 본다.
            var plainTeeCount = definition.Nodes.Count(x => x.NodeKind == NodeKind.Tee && !x.IsValveTee);
            if (invalidTeeCount > 0) errors.Add($"직교 T가 아닌 3방향 분기 {invalidTeeCount}개 수정 필요");

            // 밸브부관으로 바꾼 절점은 곡관 패밀리를 쓰지 않는다(docs/225 § C).
            var elbowCount = definition.Nodes.Count(x => x.NodeKind == NodeKind.Elbow && !x.IsValveBend);
            if (elbowCount > 0 && string.IsNullOrWhiteSpace(BendFamilyTypeName)) errors.Add("곡관 패밀리 선택 필요");
            errors.AddRange(ValveBendValidationErrors(definition));
            errors.AddRange(BypassSetValidationErrors(definition));

            if (plainTeeCount > 0 && string.IsNullOrWhiteSpace(TeeFamilyTypeName)) errors.Add("T형 패밀리 선택 필요");
            // 십자는 직교일 때만 배치한다(docs/175 § B-5). 판정은 배치 경계(RevitPipeSegmentCommandRepo)와 같은 PipeCrossResolver다.
            var invalidCrossCount = definition.Nodes.Count(x => x.NodeKind == NodeKind.Cross && PipeCrossResolver.Resolve(x, definition.Edges) is null);
            if (invalidCrossCount > 0) errors.Add($"직교 십자가 아닌 4방향 분기 {invalidCrossCount}개 수정 필요");
            if (definition.Nodes.Any(x => x.NodeKind == NodeKind.Cross) && string.IsNullOrWhiteSpace(CrossFamilyTypeName)) errors.Add("십자관 패밀리 선택 필요");
            // Y형은 주관 일직선 + 분기 30~60°일 때만 배치한다(docs/187 § 3-5). 판정은 배치 경계와 같은 PipeWyeResolver다.
            var invalidWyeCount = definition.Nodes.Count(x => x.NodeKind == NodeKind.Wye && PipeWyeResolver.Resolve(x, definition.Edges) is null);
            if (invalidWyeCount > 0) errors.Add($"Y형 판별 불가 경사 분기 {invalidWyeCount}개 수정 필요");
            if (definition.Nodes.Any(x => x.NodeKind == NodeKind.Wye) && string.IsNullOrWhiteSpace(WyeFamilyTypeName)) errors.Add("Y형관 패밀리 선택 필요");
        }
        return errors;
    }

    private void NotifyValidationChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        // 외곽 유무·유형·생성 중 상태가 바뀌면 추천 배치 버튼도 바뀐다(docs/172 § C-11). 여기가 셋 모두를 지나는 공통 경로다.
        OnPropertyChanged(nameof(CanApplyTemplate));
        OnPropertyChanged(nameof(TemplateHint));
        OnPropertyChanged(nameof(NetworkSummary));
        OnPropertyChanged(nameof(ValidationSummary));
        OnPropertyChanged(nameof(InteractionModeText));
        CommandManager.InvalidateRequerySuggested();
    }

    private void CreateModel()
    {
        var errors = GetValidationErrors();
        if (errors.Count > 0) { Status = errors[0]; return; }
        IsCreating = true;
        ClearErrorMarkers();
        try
        {
            CreateModelAction?.Invoke(_network.ToDefinition(DiameterMm, PipeOutputMode.PipeAccessorySegment, new(ReferenceX, ReferenceY),
                "", "", "",
                BuildSegmentFamilies(),
                // DH_야간은 항상 0(주간)으로 기록한다. PipeNetworkDefinition.IsNightWork 기본값이 false이므로
                // 인자를 넘기지 않아도 값이 비지 않는다 — 배관재료표 집계의 "값 없음" 방지(docs/141 § C-6, docs/145 § 3).
                JunctionId ?? "", PipeMaterial, false,
                // 가져온 원본은 항상 지우고 다시 만든다. 가져온 게 없으면 빈 목록이라 아무것도 지우지 않는다(docs/163 § B).
                // 직전 생성분도 함께 지운다 — 다시 누르면 한 벌 더 생기던 문제(docs/235 § C).
                _importedSourceIds.Concat(_createdElementIds).Distinct().ToList(),
                // 비어 있으면 배치 경로가 DH_Zone 기록을 건너뛴다(docs/161 § E).
                ValveRoomZone,
                // 부재가 자기 패밀리 파라미터로 접합을 밝히지 않을 때만 쓰이는 기본값이다(docs/170 § A·§ E).
                // 압륜은 창에서 지정하지 않는다(docs/175 § A) — 사용자가 인스턴스 파라미터로 제어한다.
                JointConnection,
                // 바이패스 지관 규격표 등급(docs/256).
                _steelFittingClass,
                // 관 등급 — 직관·단관 단위중량 조회와 DH_관종등급(docs/260 § C).
                _pipeKind));
            Status = "Revit에서 배치 중…";
        }
        catch (Exception ex)
        {
            IsCreating = false;
            Status = $"생성 요청 실패 · {UiErrors.Report("PipeLayout", ex)}";
        }
    }

    /// <summary>모델 생성·수직분기가 같이 쓰는 패밀리 지정(docs/301 § D).</summary>
    private PipeSegmentFamilySelection BuildSegmentFamilies()
        => new PipeSegmentFamilySelection(
            SegmentFamilyTypeName!, LengthParameterName!,
            BendFamilyTypeName ?? "", TeeFamilyTypeName ?? "",
            StraightLengthMm, DiameterParameterName ?? "",
            BendDiameterParameterName ?? "", TeeDiameterParameterName ?? "", ValveDiameterParameterName ?? "",
            BendAngleParameterName ?? "", TeeBranchDiameterParameterName ?? "",
            // 가변 딕셔너리를 그대로 넘기면 ExternalEvent가 처리하는 사이 UI에서 값이 바뀔 수 있다.
            new Dictionary<string, string>(_valveDiameterParameterByFamily),
            JointDiameterParameterName ?? "",
            // 이형관 종류별(docs/175 § B). 이경관·편락관은 전달만 하고 배치 경계가 쓰지 않는다(D1).
            CrossFamilyTypeName ?? "", CrossDiameterParameterName ?? "", CrossBranchDiameterParameterName ?? "",
            ReducerFamilyTypeName ?? "", ReducerLargeDiameterParameterName ?? "", ReducerSmallDiameterParameterName ?? "",
            // Y형관(docs/187 § 3-2). 레코드 맨 뒤 자리다 — 순서를 지켜야 이경관 칸으로 밀리지 않는다.
            WyeFamilyTypeName ?? "", WyeDiameterParameterName ?? "", WyeBranchDiameterParameterName ?? "", WyeBranchAngleParameterName ?? "",
            // 밸브부관(docs/225 § B). Y형 뒤, 레코드 맨 뒤 자리다.
            ValveBendFamilyTypeName ?? "", ValveBendDiameterParameterName ?? "",
            // 밸브부관 A(docs/227 § B).
            ValveTeeFamilyTypeName ?? "", ValveTeeMainDiameterParameterName ?? "", ValveTeeBranchDiameterParameterName ?? "",
            ValveTeeHorizontalBranchParameterName ?? "", ValveTeeVerticalBranchParameterName ?? "");

    // ── 수직분기(docs/301) ────────────────────────────────────────────────

    private void PickVerticalBranch()
    {
        Status = "Revit에서 +Z 커넥터 부속 선택 (Esc 취소)";
        PickVerticalBranchAction?.Invoke();
    }

    /// <summary>피킹 결과. 부속 null = 취소(<paramref name="error"/> 있으면 실패). 입면 창에서 [Revit에 배치]면 요청을 보냄.</summary>
    public void ApplyVerticalBranchStart(VerticalBranchStart? start, string? error = null)
    {
        if (start is null) { Status = error ?? "수직분기 취소"; return; }
        var dialog = new VerticalBranchViewModel(start, _accessoryFamilyTypeNames);
        if (OpenVerticalBranchDialog?.Invoke(dialog) != true) { Status = "수직분기 취소"; return; }
        try
        {
            PlaceVerticalBranchAction?.Invoke(new VerticalBranchRequest(start.ElementId, dialog.ToParts(), BuildSegmentFamilies()));
            Status = "수직분기 배치 중…";
        }
        catch (Exception ex)
        {
            Status = $"수직분기 요청 실패 · {UiErrors.Report("PipeLayout", ex)}";
        }
    }

    /// <summary>배치 결과(성공 여부, 문구). 6번 섹션과 상태줄에 표시.</summary>
    public void ApplyVerticalBranchResult(bool succeeded, string message)
    {
        VerticalBranchLastResult = succeeded ? $"최근: {message}" : $"실패: {message}";
        Status = succeeded ? $"수직분기 {message}" : $"수직분기 실패 · {message}";
    }

    // ── Revit 부재 선택·표시(docs/179 § E-3) ────────────────────────────────────

    /// <summary>고른 배관·부속에 대응하는 Revit ElementId를 모은다. 매핑이 없는 항목은 조용히 빠진다.
    /// <para>절점 부속(곡관·T형·Y형·십자)은 캔버스에서 직접 고를 수단이 없으므로 <b>고른 선의 양 끝 절점</b>으로
    /// 딸려 온다(2026-09-21 사용자 확정 — 종전에는 매핑에 담겨 있어도 조회하지 않았다).
    /// 이웃 선과 공유하는 절점 부속까지 함께 잡히므로 고른 선보다 조금 넓게 선택된다.</para></summary>
    private List<long> SelectedRevitElementIds()
        => SelectedEdgeIds.Concat(SelectedFittingIds).Concat(SelectedEdgeNodeIds())
            .SelectMany(id => _revitElementIds.TryGetValue(id, out var ids) ? ids : [])
            .Distinct()
            .ToList();

    /// <summary>고른 선의 양 끝 절점 Guid. 절점 부속은 배치할 때 이 Guid로 매핑에 담긴다
    /// (RevitPipeSegmentCommandRepo.PlaceNodeFittings의 track(node.Id, instance) 호출).
    /// <para>부속이 없는 절점(끝점·중간점)이나 배치에 실패해 건너뛴 절점은 매핑에 없어 조용히 빠진다.</para></summary>
    private IEnumerable<Guid> SelectedEdgeNodeIds()
        => _network.Edges.Where(x => SelectedEdgeIds.Contains(x.Id))
            .SelectMany(x => new[] { x.StartNodeId, x.EndNodeId })
            .Distinct();

    private bool CanSelectInRevit() => SelectInRevitAction is not null && SelectedRevitElementIds().Count > 0;

    private void SelectInRevit()
    {
        var ids = SelectedRevitElementIds();
        if (ids.Count == 0)
        {
            Status = "Revit 부재 없음 — 생성했거나 가져온 배관만 대상";
            return;
        }
        SelectInRevitAction?.Invoke(ids);
        // 실제 선택 수는 Revit이 삭제된 부재를 거른 뒤 ApplySelectInRevitResult로 돌아온다(docs/216 § B-3).
        Status = $"Revit 선택 요청 — 부재 {ids.Count}개";
    }

    /// <summary>[Revit에서 보기] 결과. <paramref name="selected"/>는 모델에 남아 있어 실제로 선택된 수다(docs/216 § B-3).
    /// 매핑은 무효화하지 않는다(docs/179 § 0).</summary>
    public void ApplySelectInRevitResult(int requested, int selected)
    {
        Status = selected == 0
            ? "Revit 부재 모두 삭제됨 — 다시 생성하거나 가져오기"
            : selected < requested
                ? $"Revit 선택 — 부재 {selected}개 ({requested - selected}개 삭제돼 제외)"
                : $"Revit 선택 — 부재 {selected}개";
    }

    // ── 선 분할(docs/179 § H) ───────────────────────────────────────────────────

    /// <summary>분할점 클릭을 기다리는 중인가. 모드가 아니라 하위 상태다 — <see cref="IsPlacingFitting"/>과 같은 취급이다.</summary>
    public bool IsSplittingEdge => _splitTargetEdgeId is not null;

    // 분할점은 한 엣지 위의 한 점이다. 여러 개를 고른 상태에서는 어느 선을 나눌지 정할 수 없다.
    private bool CanBeginSplitEdge() => !IsCreating && SelectedEdgeIds.Count == 1;

    private void BeginSplitEdge()
    {
        if (SelectedEdgeIds.Count != 1) return;
        CancelDrawing();
        _splitTargetEdgeId = SelectedEdgeIds[0];
        OnPropertyChanged(nameof(IsSplittingEdge));
        OnPropertyChanged(nameof(InteractionModeText));
        Status = "나눌 지점 클릭 · Esc 취소";
    }

    private void EndSplitEdge(string status)
    {
        _splitTargetEdgeId = null;
        IsSnapMarkerVisible = false;
        IsSplitMarkerVisible = false;
        OnPropertyChanged(nameof(IsSplittingEdge));
        OnPropertyChanged(nameof(InteractionModeText));
        Status = status;
    }

    /// <summary>커서 위치에서 정해지는 분할 후보점 — 대상 엣지 위로 정사영한 점과 그 파라미터 t.
    /// <para>미리보기 표식(<see cref="UpdateSplitMarker"/>)과 실제 분할(<see cref="CommitSplitEdge"/>)이
    /// <b>반드시 같은 값</b>을 써야 한다. 표식이 가리킨 자리와 실제로 잘리는 자리가 어긋나면
    /// 표식이 오히려 사람을 속인다(docs/181 § A-2).</para></summary>
    private (PipeEdge Edge, DHBIMWATER.Core.Geometry.Point2D Start, DHBIMWATER.Core.Geometry.Point2D End,
        double T, DHBIMWATER.Core.Geometry.Point2D Point)? ResolveSplitCandidate(Point screenPoint)
    {
        if (_splitTargetEdgeId is not { } edgeId) return null;
        var edge = _network.Edges.FirstOrDefault(x => x.Id == edgeId);
        if (edge is null) return null;

        var raw = Transform.ToModel(screenPoint);
        var snapped = FindSnapPoint(raw)?.Point ?? raw;
        var start = _network.Nodes.First(x => x.Id == edge.StartNodeId).Position;
        var end = _network.Nodes.First(x => x.Id == edge.EndNodeId).Position;
        var t = Math.Clamp(Segment2D.ParameterOnSegment(snapped, start, end), 0, 1);
        var point = new DHBIMWATER.Core.Geometry.Point2D(
            start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t);
        return (edge, start, end, t, point);
    }

    /// <summary>분할 중 커서를 따라 "여기서 자른다"를 보여주는 표식을 갱신한다(docs/181 § A).
    /// 선 위 후보점에서 <b>선 벡터에 수직</b>한 짧은 선분 하나다 — 스냅 표식(어느 점에 붙었나)만으로는
    /// 그 점이 선의 어디를 끊는지 읽기 어렵다.</summary>
    private void UpdateSplitMarker(Point screenPoint)
    {
        if (ResolveSplitCandidate(screenPoint) is not { } candidate) { IsSplitMarkerVisible = false; return; }

        // 수직 방향은 모델이 아니라 화면 좌표에서 만든다 — 확대율·Y축 방향과 무관하게
        // 표식이 화면에서 항상 선과 직각으로, 같은 길이로 보인다.
        var a = Transform.ToScreen(candidate.Start);
        var b = Transform.ToScreen(candidate.End);
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= double.Epsilon) { IsSplitMarkerVisible = false; return; }

        var nx = -dy / length * SplitMarkerHalfPx;
        var ny = dx / length * SplitMarkerHalfPx;
        var center = Transform.ToScreen(candidate.Point);
        SplitMarkerX1 = center.X - nx;
        SplitMarkerY1 = center.Y - ny;
        SplitMarkerX2 = center.X + nx;
        SplitMarkerY2 = center.Y + ny;
        IsSplitMarkerVisible = true;
    }

    /// <summary>분할점 클릭을 받아 선을 나눈다(docs/179 § H-4). 스냅은 그리기와 같은 규칙을 쓴다.</summary>
    private void CommitSplitEdge(Point point)
    {
        var edgeId = _splitTargetEdgeId!.Value;
        if (ResolveSplitCandidate(point) is not { } candidate) { EndSplitEdge("나눌 선 없음"); return; }
        var (edge, start, end, t, _) = candidate;
        var length = start.DistanceTo(end);

        // 끝점에 너무 가까우면 나눌 것이 없다. SplitEdgeAt의 t 가드(0.0001/0.9999)보다 사람 기준으로 먼저 막는다.
        if (length <= 0 || t * length < MinSplitOffsetMm || (1 - t) * length < MinSplitOffsetMm)
        {
            Status = $"선 끝에서 {MinSplitOffsetMm:0}mm 이상 떨어진 곳 클릭";
            return;
        }
        // 부속 바로 옆에서 나누면 부속이 조각 끝에 붙어 모델 생성 때 "부속을 놓을 수 없습니다"로 터진다.
        var tooClose = edge.InlineFittings.FirstOrDefault(x => Math.Abs(x.T - t) * length < MinSplitOffsetMm);
        if (tooClose is not null)
        {
            Status = $"'{tooClose.FamilyTypeName}'에서 {MinSplitOffsetMm:0}mm 이내 — 조금 떨어진 곳 클릭";
            return;
        }

        BeginEditing();
        var before = Snapshot();
        // detach: 두 조각이 같은 자리에 각자 끝점을 갖는다(docs/181 § B) — 끝점 하나를 끌면 그 조각만 움직인다.
        var split = _network.SplitSegment(edgeId, t, detach: true);
        if (split is null) { EndSplitEdge("이 지점은 분할 불가"); return; }

        // 나눈 두 조각은 원본이 가리키던 Revit 부재를 그대로 물려받는다. 어느 쪽을 골라도
        // 원본 관이 잡혀야 하므로 양쪽에 같은 목록을 넣는다(docs/179 § H-4).
        if (_revitElementIds.TryGetValue(edgeId, out var inherited))
        {
            _revitElementIds[split.Value.First] = inherited;
            _revitElementIds[split.Value.Second] = inherited;
            _revitElementIds.Remove(edgeId);
        }

        RecordUndoIfChanged(before);
        ClearSelection();
        RefreshGraph();
        EndSplitEdge("선 분할 · 정척 이음매 위치 변경");
    }

    private void Clear()
    {
        BeginEditing();
        // 스냅샷을 먼저 뜬다 — 원본 목록까지 되돌려야 Ctrl+Z로 가져오기 상태가 되살아난다(docs/163 § C-3).
        var before = Snapshot();
        // 캔버스를 비웠는데 삭제 대상만 남아 있으면 안 된다(docs/158 § E-4). 직전 생성분도 함께 잊는다(docs/235 § C).
        ForgetReplaceTargets();
        if (_network.Edges.Count == 0) return;
        _network.Clear();
        RecordUndoIfChanged(before);
        SetSegmentStart(null);
        ClearSelection();
        IsPreviewVisible = false;
        IsSubLineVisible = false;
        IsSnapMarkerVisible = false;
        HideFittingPreview();
        TempDimensions.Clear();
        RefreshGraph();
        Status = "캔버스 초기화";
    }
    private void CancelDrawing() { SetSegmentStart(null); IsPreviewVisible = false; IsSubLineVisible = false; IsSnapMarkerVisible = false; TempDimensions.Clear(); Status = "그리기 취소"; }

    private static string GetSnapMarkerSymbol(PipeSnapResult snap) => snap.Kind switch
    {
        PipeSnapKind.Intersection => "×",
        PipeSnapKind.Endpoint => "□",
        PipeSnapKind.Midpoint => "△",
        PipeSnapKind.Quadrant => "◇",
        PipeSnapKind.Perpendicular => "⊥",
        _ => "·",
    };

    private static string GetSnapMarkerLabel(PipeSnapResult snap) => snap.Kind switch
    {
        PipeSnapKind.Intersection => "교차점",
        PipeSnapKind.Endpoint => "끝점",
        PipeSnapKind.Midpoint => "중간점",
        PipeSnapKind.Perpendicular => "직교점",
        PipeSnapKind.Quadrant => "사분점",
        _ => "근처점",
    };

    private void RefreshGraph()
    {
        Edges.Clear(); Nodes.Clear();
        RefreshErrorEdges();
        foreach (var edge in _network.Edges)
        {
            // 드래그 중에는 그래프를 고치지 않고 화면 좌표만 민다(docs/152 § E-1).
            var start = DraggedEdgePoint(edge, _network.Nodes.First(x => x.Id == edge.StartNodeId));
            var end = DraggedEdgePoint(edge, _network.Nodes.First(x => x.Id == edge.EndNodeId));
            var a = Transform.ToScreen(start); var b = Transform.ToScreen(end);
            var subLines = CalculateSubLines(start, end, edge.DiameterMm);
            Edges.Add(new PipeEdgeItem(edge.Id, edge.StartNodeId, edge.EndNodeId, a.X, a.Y, b.X, b.Y,
                start.DistanceTo(end), SelectedEdgeIds.Contains(edge.Id),
                subLines?.AX1 ?? 0, subLines?.AY1 ?? 0, subLines?.AX2 ?? 0, subLines?.AY2 ?? 0,
                subLines?.BX1 ?? 0, subLines?.BY1 ?? 0, subLines?.BX2 ?? 0, subLines?.BY2 ?? 0,
                _errorEdgeIds.Contains(edge.Id), edge.DiameterMm)
            {
                // 부속 자리에서 정척을 다시 센다 — 드래그 중인 부속은 미리보기 T를 따른다(docs/314).
                StraightTicks = BuildStraightLengthTicks(start, end, edge.DiameterMm,
                    edge.InlineFittings.Select(DisplayFittingT))
            });
        }
        foreach (var node in _network.Nodes)
        {
            var p = Transform.ToScreen(DraggedNodePoint(node));
            Nodes.Add(new PipeNodeItem(node.Id, p.X, p.Y, node.Degree, node.NodeKind)
            {
                FittingKind = node.FittingKind,
                ValveSideEnd = ValveSideMarkerEnd(node, p)
            });
        }
        RefreshFittings();
        // 드래그 중·줌·팬은 그래프가 바뀌지 않고 화면 좌표만 민다 — 검증(GetValidationErrors: ToDefinition + 리졸버 전부)을
        // 매 마우스 이동마다 ValidationSummary·CanCreateModel에서 두 번씩 다시 돌리지 않는다(docs/239 § D).
        // 드래그가 끝나면(_drag = null 뒤 RefreshGraph) 다시 알린다.
        if (_drag is null && !_isViewOnlyRefresh) NotifyValidationChanged();
        NotifyDiameterTargetChanged();
    }

    /// <summary>줌·팬처럼 화면 변환만 바뀌는 새로고침 중인가(docs/239 § D).</summary>
    private bool _isViewOnlyRefresh;

    /// <summary>화면 변환만 바뀐 새로고침. 검증 알림을 건너뛴다(docs/239 § D).</summary>
    private void RefreshViewOnly()
    {
        _isViewOnlyRefresh = true;
        try { RefreshView(); }
        finally { _isViewOnlyRefresh = false; }
    }

    // ── 절점 부속 종류(docs/225 § C · 우클릭 전환 메뉴는 docs/234로 제거) ──────────────────────────────────────

    /// <summary>밸브부관 표시선 길이(px). 화면 배율과 무관하게 같은 길이로 보인다.</summary>
    private const double ValveSideMarkerLengthPx = 20;

    private static bool IsValveBendNode(PipeNode node)
        => node.NodeKind == NodeKind.Elbow && node.FittingKind == PipeNodeFittingKind.ValveBend;

    private static bool IsValveTeeNode(PipeNode node)
        => node.NodeKind == NodeKind.Tee && node.FittingKind == PipeNodeFittingKind.ValveTee;

    /// <summary>절점 부속을 기본(곡관·T형)/밸브부관 A/밸브부관 B로 바꾼다.
    /// docs/234로 우클릭 메뉴가 없어져 UI에서는 부르지 않는다. 테스트가 역가져오기 없이 A/B 절점을 만들 때 쓴다.</summary>
    public void SetNodeFittingKind(Guid nodeId, PipeNodeFittingKind kind)
    {
        if (IsCreating || _network.Nodes.All(x => x.Id != nodeId)) return;
        var before = Snapshot();
        if (!_network.SetNodeFittingKind(nodeId, kind))
        {
            Status = kind switch
            {
                PipeNodeFittingKind.ValveBend => "밸브부관 B — 꺾인 절점만",
                PipeNodeFittingKind.ValveTee => "밸브부관 A — T 절점만",
                _ => "꺾인 절점·T 절점만 부속 선택 가능"
            };
            return;
        }
        BeginEditing();
        RecordUndoIfChanged(before);
        RefreshGraph();
        Status = kind switch
        {
            PipeNodeFittingKind.ValveBend => "밸브부관 B · 빨간 표시선 = 밸브 쪽",
            PipeNodeFittingKind.ValveTee => "밸브부관 A · 분기 쪽 선을 밸브부관 B와 연결",
            _ => "기본 부속(곡관·T형)"
        };
    }

    /// <summary>밸브부관의 밸브 쪽을 반대 레그로 바꾼다.
    /// docs/234로 우클릭 메뉴가 없어져 UI에서는 부르지 않는다. 테스트가 역가져오기 없이 밸브 쪽을 정할 때 쓴다.</summary>
    public void FlipValveBendSide(Guid nodeId)
    {
        if (IsCreating || _network.Nodes.All(x => x.Id != nodeId)) return;
        var before = Snapshot();
        if (!_network.FlipValveBendSide(nodeId))
        {
            Status = "밸브부관 절점만 뒤집기 가능";
            return;
        }
        BeginEditing();
        RecordUndoIfChanged(before);
        RefreshGraph();
        Status = "밸브 쪽 전환";
    }

    /// <summary>밸브 쪽 표시선의 화면 끝점. 모델 방향을 화면 방향으로 옮겨 고정 길이로 자른다(화면 Y는 뒤집혀 있다).</summary>
    private Point? ValveSideMarkerEnd(PipeNode node, Point screen)
    {
        if (!IsValveBendNode(node) || _network.ResolveValveSideDirection(node.Id) is not { } direction) return null;
        return new Point(screen.X + direction.X * ValveSideMarkerLengthPx, screen.Y - direction.Y * ValveSideMarkerLengthPx);
    }

    /// <summary>밸브부관 A·B 절점 검증(docs/225 § C, docs/227 § C). 배치 경계가 건너뛸 조건을 캔버스에서 먼저 막는다.</summary>
    private IEnumerable<string> ValveBendValidationErrors(PipeNetworkDefinition definition)
    {
        var valveTees = definition.Nodes.Where(x => x.IsValveTee).ToList();
        if (valveTees.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(ValveTeeFamilyTypeName)) yield return "밸브부관 A 패밀리 선택 필요";
            // A 주관은 본관 한 줄이다 — 양쪽 관경이 다르면 주관 구경을 하나로 구동할 수 없다.
            var mixedMain = valveTees.Count(x => PipeTeeResolver.Resolve(x, definition.Edges) is { } order
                && definition.Edges.Where(e => e.Id == order.MainEdge1Id || e.Id == order.MainEdge2Id).Select(e => e.DiameterMm).Distinct().Count() > 1);
            if (mixedMain > 0) yield return $"주관 양쪽 관경이 다른 밸브부관 A {mixedMain}개 수정 필요";
        }

        var valveBends = definition.Nodes.Where(x => x.IsValveBend).ToList();
        if (valveBends.Count == 0) yield break;
        if (string.IsNullOrWhiteSpace(ValveBendFamilyTypeName)) yield return "밸브부관 B 패밀리 선택 필요";

        var badAngle = valveBends.Count(x => !PipeValveBend.HasSupportedAngle(x, definition.Edges));
        if (badAngle > 0) yield return $"90°로 꺾이지 않은 밸브부관 B {badAngle}개 수정 필요";

        var mixedDiameter = valveBends.Count(x => PipeNodeAngle.Legs(x, definition.Edges)
            .Select(leg => leg.Edge.DiameterMm).Distinct().Count() > 1);
        if (mixedDiameter > 0) yield return $"양쪽 관경이 다른 밸브부관 B {mixedDiameter}개 수정 필요";

        var noSide = valveBends.Count(x => x.ValveSideEdgeId is null);
        if (noSide > 0)
            yield return $"밸브 쪽을 정할 수 없는 밸브부관 B {noSide}개 — 한쪽 선에 게이트밸브 필요";

        // 규격 `D주관xD분기`의 주관은 이어진 A에서 온다 — 없으면 규격이 틀리게 기록되므로 막는다(docs/227 § 0-8).
        var noMain = valveBends.Count(x => x.ValveSideEdgeId is not null && x.MainDiameterMm is null);
        if (noMain > 0)
            yield return $"밸브 반대쪽이 밸브부관 A의 분기와 이어지지 않은 밸브부관 B {noMain}개 수정 필요";
    }

    /// <summary>편집 시작 공통 처리. 종전에는 여기서 <see cref="IsCreating"/>도 내렸는데, 그러면 생성 대기 중 편집·Ctrl+Z 한 번에
    /// 생성 버튼이 다시 켜져 두 번째 요청이 대기 슬롯을 덮었다(docs/235 § D). 이제 생성 중에는 편집 입구에서 막고,
    /// <see cref="IsCreating"/>은 생성 결과(<see cref="ApplyModelCreationResult(bool, string, IReadOnlyList{PipeConnectionFailure})"/>)와 요청 실패에서만 내린다.</summary>
    private void BeginEditing()
    {
        ClearErrorMarkers();
    }

    private void ClearErrorMarkers()
    {
        _failures.Clear();
        _errorEdgeIds.Clear();
    }

    private void RefreshErrorEdges()
    {
        _errorEdgeIds.Clear();
        foreach (var failure in _failures)
        {
            if (failure.FittingId is not null) continue;
            var nearestNode = _network.Nodes
                .OrderBy(node => node.Position.DistanceTo(failure.CanvasPoint))
                .FirstOrDefault();
            if (nearestNode is not null && nearestNode.Position.DistanceTo(failure.CanvasPoint) <= 300)
            {
                foreach (var edge in _network.Edges.Where(edge => edge.StartNodeId == nearestNode.Id || edge.EndNodeId == nearestNode.Id))
                    _errorEdgeIds.Add(edge.Id);
                continue;
            }

            var nearestEdge = _network.Edges
                .OrderBy(edge => DistanceToSegment(failure.CanvasPoint,
                    _network.Nodes.First(node => node.Id == edge.StartNodeId).Position,
                    _network.Nodes.First(node => node.Id == edge.EndNodeId).Position))
                .FirstOrDefault();
            if (nearestEdge is not null) _errorEdgeIds.Add(nearestEdge.Id);
        }
    }

    private static double DistanceToSegment(DHBIMWATER.Core.Geometry.Point2D point,
        DHBIMWATER.Core.Geometry.Point2D start, DHBIMWATER.Core.Geometry.Point2D end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 0) return point.DistanceTo(start);
        var t = Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0, 1);
        return point.DistanceTo(new DHBIMWATER.Core.Geometry.Point2D(start.X + t * dx, start.Y + t * dy));
    }

    private void CenterFirstFailureIfOutsideViewport(PipeConnectionFailure failure)
    {
        if (!_hasCanvasSize) return;
        var point = Transform.ToScreen(failure.CanvasPoint);
        if (point.X >= 0 && point.X <= _canvasWidth && point.Y >= 0 && point.Y <= _canvasHeight) return;
        Transform.PanOrigin = new Point(
            Transform.PanOrigin.X + _canvasWidth / 2 - point.X,
            Transform.PanOrigin.Y + _canvasHeight / 2 - point.Y);
    }

    private string FormatFailurePoint(PipeConnectionFailure? failure)
        => failure is null ? "" :
            $"{Environment.NewLine}접점: 캔버스 ({failure.CanvasPoint.X:N0}, {failure.CanvasPoint.Y:N0})mm, Elev {failure.ElevationMm:N0}mm  " +
            $"[Revit 좌표 ({failure.CanvasPoint.X + ReferenceX:N0}, {failure.CanvasPoint.Y + ReferenceY:N0}, {failure.ElevationMm:N0})mm]";

    /// <summary>전체 네트워크의 부속을 보여준다(선택된 엣지만이 아니라). 클릭-배치 흐름에는 엣지 선택 단계가 없으므로
    /// 배치 직후에도 캔버스·사이드바에 남아 있어야 한다.</summary>
    /// <summary>화면에 그릴 부속 T. 드래그 중인 부속만 미리보기 T로 그린다. 그래프는 커밋 시점에만 바뀐다(docs/152 § E-1).</summary>
    private double DisplayFittingT(InlineFitting fitting)
        => _drag is { Exceeded: true, Kind: PipeDragKind.Fitting } drag && drag.FittingId == fitting.Id ? drag.T : fitting.T;

    private void RefreshFittings()
    {
        InlineFittings.Clear();
        foreach (var edge in _network.Edges)
        {
            var display = Edges.FirstOrDefault(x => x.Id == edge.Id);
            if (display is null) continue;
            foreach (var fitting in edge.InlineFittings.OrderBy(x => x.Order))
            {
                var t = DisplayFittingT(fitting);
                var item = new InlineFittingItem(fitting.Id, edge.Id, fitting.TypeKey, fitting.FamilyTypeName, t, fitting.Order,
                    display.X1 + (display.X2 - display.X1) * t, display.Y1 + (display.Y2 - display.Y1) * t,
                    SelectedFittingIds.Contains(fitting.Id))
                {
                    Bypass = fitting.IsBypassSet ? fitting.Bypass : null,
                    IsError = _failures.Any(x => x.FittingId == fitting.Id),
                };
                if (item.IsBypassSet)
                {
                    var start = _network.Nodes.First(x => x.Id == edge.StartNodeId).Position;
                    var end = _network.Nodes.First(x => x.Id == edge.EndNodeId).Position;
                    var center = new DHBIMWATER.Core.Geometry.Point2D(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t);
                    var (line, markers) = BuildBypassGeometry(center, EdgeDirection(edge), fitting.Bypass!.NormalizedSide, edge.DiameterMm,
                        fitting.Bypass.BypassDiameterMm > 0 ? fitting.Bypass.BypassDiameterMm : BypassPreviewDiameterMm(edge.DiameterMm),
                        fitting.Bypass.HasTwoValves ? 2 : 1);
                    item = item with
                    {
                        BypassLine = line, BypassMarkers = markers, MainDiameterMm = edge.DiameterMm,
                        HandbookBranchDiameterMm = HandbookBypassDiameterMm(edge.DiameterMm),
                    };
                }
                InlineFittings.Add(item);
            }
        }
        BypassSetItems.Clear();
        foreach (var item in InlineFittings.Where(x => x.IsBypassSet)) BypassSetItems.Add(item);
    }

    // ── 바이패스 세트(docs/228 § B) ──────────────────────────────────────────────

    private void PlacePreviewedBypassSet()
    {
        if (string.IsNullOrWhiteSpace(BypassValveFamilyName)) { Status = "바이패스 밸브 패밀리 선택 필요"; return; }
        var (second, spoolMm, configError) = BypassValvePanelConfig();
        if (configError is not null) { Status = configError; return; }
        var edge = _network.Edges.FirstOrDefault(x => x.Id == _fittingPreviewEdgeId);
        if (edge is null) return;
        var side = _fittingPreviewCursor is { } cursor ? BypassSideAt(edge, cursor) : 1;
        var before = Snapshot();
        // 세트 부속의 FamilyTypeName은 빈 문자열이다 — 본관 밸브는 세트가 아니다(docs/230 § B).
        if (!TryEditNetwork(before, () => _network.AddInlineFitting(edge.Id, BypassSet.TypeKey, string.Empty, _fittingPreviewT,
            // 밸브는 패밀리명만, 관경 0 = 모델 생성 때 밸브부관 B가 정한다(docs/244 § D-2).
            bypass: new BypassSetSpec(BypassValveFamilyName, 0, side, second, spoolMm)))) return;
        BeginEditing();
        RecordUndoIfChanged(before);
        RefreshGraph();
        Status = "바이패스 세트 배치 · 본관 밸브는 [부속] 탭 밸브로 · 방향은 배치된 부속 목록 ⋯ 메뉴에서";
    }

    private void ApplyBypassValves(InlineFittingItem? item)
    {
        if (item is null || IsCreating) return;
        if (string.IsNullOrWhiteSpace(BypassValveFamilyName))
        {
            Status = "바이패스 밸브 패밀리 선택 필요";
            return;
        }
        var (second, spoolMm, configError) = BypassValvePanelConfig();
        if (configError is not null) { Status = configError; return; }
        var before = Snapshot();
        // 새 세트와 같이 패밀리명·관경 0(자동)을 넣는다(docs/244 § D-2). 밸브 개수·V2·단관도 패널 값으로 바꾼다(docs/255 § D-3).
        if (!_network.UpdateBypassSet(item.EdgeId, item.Id, BypassValveFamilyName, 0, second, spoolMm)) return;
        BeginEditing();
        RecordUndoIfChanged(before);
        RefreshGraph();
        Status = "바이패스 세트 밸브 적용";
    }

    private void FlipBypassSet(InlineFittingItem? item)
    {
        if (item is null || IsCreating) return;
        var before = Snapshot();
        if (!_network.FlipBypassSide(item.EdgeId, item.Id)) return;
        BeginEditing();
        RecordUndoIfChanged(before);
        RefreshGraph();
        Status = "바이패스 세트 방향 전환";
    }

    private void SwapBypassValves(InlineFittingItem? item)
    {
        if (item is null || IsCreating) return;
        var before = Snapshot();
        if (!_network.SwapBypassValves(item.EdgeId, item.Id)) return;
        BeginEditing();
        RecordUndoIfChanged(before);
        RefreshGraph();
        Status = "바이패스 밸브 순서 바꿈 (밸브 1 ↔ 밸브 2)";
    }

    /// <summary>커서가 엣지 진행 방향의 왼쪽이면 +1, 오른쪽이면 -1(<see cref="BypassSetSpec.Side"/>).</summary>
    private int BypassSideAt(PipeEdge edge, DHBIMWATER.Core.Geometry.Point2D cursor)
    {
        var start = _network.Nodes.First(x => x.Id == edge.StartNodeId).Position;
        var end = _network.Nodes.First(x => x.Id == edge.EndNodeId).Position;
        var cross = (end.X - start.X) * (cursor.Y - start.Y) - (end.Y - start.Y) * (cursor.X - start.X);
        return cross < 0 ? -1 : 1;
    }

    private DHBIMWATER.Core.Geometry.Point2D EdgeDirection(PipeEdge edge)
    {
        var start = _network.Nodes.First(x => x.Id == edge.StartNodeId).Position;
        var end = _network.Nodes.First(x => x.Id == edge.EndNodeId).Position;
        var length = start.DistanceTo(end);
        return length < double.Epsilon ? new(1, 0) : new((end.X - start.X) / length, (end.Y - start.Y) / length);
    }

    /// <summary>세트 개략 그림(화면 좌표). 선: A1 → B1 → B2 → A2(바이패스 경로), 표식: A 사각형 · B 원 · 바이패스 밸브 다이아몬드.
    /// 치수는 <see cref="BypassSet.EstimatedHalfSpanMm"/>·<see cref="BypassSet.EstimatedOffsetMm"/> 개략값이다.
    /// 밸브 2개 세트는 다이아몬드를 두 개 그린다(docs/255 § D-7) — 간격도 개략값이다.</summary>
    private (System.Windows.Media.Geometry Line, System.Windows.Media.Geometry Markers) BuildBypassGeometry(
        DHBIMWATER.Core.Geometry.Point2D center, DHBIMWATER.Core.Geometry.Point2D direction, int side, double mainMm, double bypassMm,
        int valveCount = 1)
    {
        var half = BypassSet.EstimatedHalfSpanMm(mainMm, bypassMm);
        var offset = BypassSet.EstimatedOffsetMm(mainMm, bypassMm);
        var normal = new DHBIMWATER.Core.Geometry.Point2D(-direction.Y * side, direction.X * side);
        Point At(double along, double across) => Transform.ToScreen(new DHBIMWATER.Core.Geometry.Point2D(
            center.X + direction.X * along + normal.X * across, center.Y + direction.Y * along + normal.Y * across));
        var a1 = At(-half, 0); var a2 = At(half, 0); var b1 = At(-half, offset); var b2 = At(half, offset); var v = At(0, offset);
        var line = FormattableString.Invariant($"M {a1.X:0.##},{a1.Y:0.##} L {b1.X:0.##},{b1.Y:0.##} L {b2.X:0.##},{b2.Y:0.##} L {a2.X:0.##},{a2.Y:0.##}");
        static string Square(Point p) => FormattableString.Invariant($"M {p.X - 6:0.##},{p.Y - 6:0.##} h 12 v 12 h -12 Z ");
        static string Circle(Point p) => FormattableString.Invariant($"M {p.X - 5:0.##},{p.Y:0.##} A 5,5 0 1 0 {p.X + 5:0.##},{p.Y:0.##} A 5,5 0 1 0 {p.X - 5:0.##},{p.Y:0.##} Z ");
        static string Diamond(Point p) => FormattableString.Invariant($"M {p.X:0.##},{p.Y - 7:0.##} L {p.X + 7:0.##},{p.Y:0.##} L {p.X:0.##},{p.Y + 7:0.##} L {p.X - 7:0.##},{p.Y:0.##} Z ");
        var valves = valveCount == 2 ? Diamond(At(-half * 0.3, offset)) + Diamond(At(half * 0.3, offset)) : Diamond(v);
        var markers = Square(a1) + Square(a2) + Circle(b1) + Circle(b2) + valves;
        var lineGeometry = System.Windows.Media.Geometry.Parse(line); lineGeometry.Freeze();
        var markerGeometry = System.Windows.Media.Geometry.Parse(markers); markerGeometry.Freeze();
        return (lineGeometry, markerGeometry);
    }

    /// <summary>바이패스 밸브 유형 후보를 채운다. 대표 유형은 패밀리 첫 유형이다(docs/244 § D-2) — 실제 유형은 모델 생성 때 DN으로 고른다.</summary>
    private void RefreshBypassValveTypeNames()
    {
        Fill(BypassValveTypeNames, _bypassValveFamilyName, ref _bypassValveTypeName, nameof(BypassValveTypeName));

        void Fill(ObservableCollection<string> target, string? family, ref string? selected, string propertyName)
        {
            target.Clear();
            foreach (var name in _accessoryFamilyTypeNames.Where(x => family is not null && GetFamilyName(x) == family)
                         .OrderBy(x => x, NominalDiameterParser.FamilyGroupedComparer))
                target.Add(name);
            if (selected is null || !target.Contains(selected))
            {
                selected = target.FirstOrDefault();
                OnPropertyChanged(propertyName);
            }
        }
        // Fill이 유형 필드를 직접 바꾸므로 세터가 돌지 않는다 — 직경 파라미터 후보를 여기서 따로 갱신한다(docs/232 § C-1).
        RefreshBypassValveParameterNames();
        OnPropertyChanged(nameof(IsPlacingFitting));
    }

    /// <summary>세트 검증(docs/228 § B). 배치 경계가 세트를 통째로 건너뛸 조건을 캔버스에서 먼저 막는다.</summary>
    private IEnumerable<string> BypassSetValidationErrors(PipeNetworkDefinition definition)
    {
        var sets = definition.InlineFittings.Where(x => x.IsBypassSet).ToList();
        if (sets.Count == 0) yield break;
        if (string.IsNullOrWhiteSpace(ValveTeeFamilyTypeName)) yield return "바이패스 세트용 밸브부관 A 패밀리 선택 필요";
        if (string.IsNullOrWhiteSpace(ValveBendFamilyTypeName)) yield return "바이패스 세트용 밸브부관 B 패밀리 선택 필요";
        var noValve = sets.Count(x => string.IsNullOrWhiteSpace(x.Bypass!.BypassValveFamilyTypeName));
        if (noValve > 0) yield return $"바이패스 밸브 패밀리가 비어 있는 세트 {noValve}개 — 세트 목록 [밸브 적용] 필요";
        // 관경 0(자동)은 생성 때 정하므로 검사하지 않는다 — 되가져온 세트만 해당(docs/244 § D-2).
        var tooLarge = sets.Count(x => definition.Edges.FirstOrDefault(e => e.Id == x.EdgeId) is { } edge
            && x.Bypass!.BypassDiameterMm > 0 && x.Bypass.BypassDiameterMm >= edge.DiameterMm);
        if (tooLarge > 0) yield return $"바이패스 관경 ≥ 본관인 세트 {tooLarge}개 수정 필요";
        // 밸브 2개 세트의 단관(docs/255 § D-6) — 0(직결) 또는 관 최소 조각 이상. 되가져온 모델의 짧은 관이 여기 걸릴 수 있다.
        var badSpool = sets.Count(x => x.Bypass!.HasTwoValves && !BypassSet.IsValidSpoolLength(x.Bypass.SpoolLengthMm));
        if (badSpool > 0)
            yield return FormattableString.Invariant(
                $"바이패스 밸브 사이 단관이 {PipeSegmentPlan.MinSegmentMm:0}mm 미만인 세트 {badSpool}개 — 0(직결) 또는 {PipeSegmentPlan.MinSegmentMm:0}mm 이상으로 [밸브 적용]");
    }
}

public sealed record PipeEdgeItem(
    Guid Id, Guid StartNodeId, Guid EndNodeId,
    double X1, double Y1, double X2, double Y2, double LengthMm, bool IsSelected,
    double SubAX1, double SubAY1, double SubAX2, double SubAY2,
    double SubBX1, double SubBY1, double SubBX2, double SubBY2,
    bool IsError, double DiameterMm)
{
    public double LengthX => (X1 + X2) / 2;
    public double LengthY => (Y1 + Y2) / 2;
    public string LengthLabel => $"{LengthMm / 1000:N3} m";
    /// <summary>선마다 다를 수 있는 관경 표시(docs/152 § D-3).</summary>
    public string DiameterLabel => $"D{DiameterMm:0.##}";
    public string Stroke => IsError ? "#E74C3C" : IsSelected ? "#E67E22" : "#2878B8";
    /// <summary>정척 수직보조선(docs/245). 없으면 null.</summary>
    public System.Windows.Media.Geometry? StraightTicks { get; init; }
}
public sealed record PipeNodeItem(Guid Id, double X, double Y, int Degree, NodeKind NodeKind)
{
    /// <summary>꺾인 절점의 부속 종류(docs/225 § C). Elbow가 아니면 항상 Bend다.</summary>
    public PipeNodeFittingKind FittingKind { get; init; } = PipeNodeFittingKind.Bend;
    /// <summary>밸브부관의 밸브 쪽 표시선 끝점(화면 좌표). 밸브 쪽을 정할 수 없으면 null.</summary>
    public Point? ValveSideEnd { get; init; }
    public bool IsElbow => NodeKind == NodeKind.Elbow;
    public bool IsValveBend => IsElbow && FittingKind == PipeNodeFittingKind.ValveBend;
    public bool IsTee => NodeKind == NodeKind.Tee;
    public bool IsValveTee => IsTee && FittingKind == PipeNodeFittingKind.ValveTee;
    public bool HasValveSide => IsValveBend && ValveSideEnd is not null;
    public double ValveSideX2 => ValveSideEnd?.X ?? X;
    public double ValveSideY2 => ValveSideEnd?.Y ?? Y;
    public string Label => IsValveBend ? PipeValveBend.PartNameB : IsValveTee ? PipeValveBend.PartNameA : $"{NodeKind} ({Degree})";
    public string Color => IsValveBend ? "#C0392B" : IsValveTee ? "#6E2C00" : NodeKind switch { NodeKind.EndPoint => "#7F8C8D", NodeKind.Inline => "#3498DB", NodeKind.Elbow => "#F39C12", NodeKind.Tee => "#9B59B6", NodeKind.Wye => "#16A085", _ => "#E74C3C" };
}
/// <summary>캔버스·목록에 보이는 인라인 부속 하나. 이음관은 <see cref="IsJoint"/>로만 구분한다(docs/175 § C-3) —
/// 밸브와 같은 다이아몬드를 초록으로 그린다.</summary>
public sealed record InlineFittingItem(Guid Id, Guid EdgeId, string TypeKey, string FamilyTypeName, double T, int Order, double X, double Y, bool IsSelected)
{
    public int DisplayOrder => Order + 1;
    public bool IsJoint => string.Equals(TypeKey, InlineJoint.TypeKey, StringComparison.Ordinal);
    /// <summary>바이패스 세트 입력(docs/228). 세트가 아니면 null.</summary>
    public BypassSetSpec? Bypass { get; init; }
    public bool IsError { get; init; }
    public bool IsBypassSet => Bypass is not null;
    /// <summary>밸브 2개 세트인가 — 목록의 [순서 바꾸기] 표시 조건(docs/255 § D-4).</summary>
    public bool HasTwoBypassValves => Bypass?.HasTwoValves == true;
    /// <summary>세트 개략 그림(화면 좌표) — 바이패스 경로 선과 A·B·바이패스 밸브 표식.</summary>
    public System.Windows.Media.Geometry? BypassLine { get; init; }
    public System.Windows.Media.Geometry? BypassMarkers { get; init; }
    public double MainDiameterMm { get; init; }
    /// <summary>핸드북 규격표의 지관 관경(docs/256). 이 레코드는 관종·등급을 모르므로 VM이 계산해 넣는다. null = 규격 없음.</summary>
    public double? HandbookBranchDiameterMm { get; init; }
    /// <summary>세트 목록 한 줄 요약. 되가져온 세트는 모델 실제값, 새 세트는 핸드북 값을 보인다(docs/256 § D-3).</summary>
    public string BypassSummary => Bypass is null ? ""
        : Bypass.HasTwoValves
            // 밸브 2개 세트는 V1 / V2와 단관 길이를 보인다(docs/255 § D-7).
            ? FormattableString.Invariant($"바이패스 {TypeNameOf(Bypass.BypassValveFamilyTypeName)} / {TypeNameOf(Bypass.SecondValveFamilyTypeName!)} · 단관 {Bypass.SpoolLengthMm:0}mm · {BypassDiameterText} · {(Bypass.NormalizedSide > 0 ? "왼쪽" : "오른쪽")}")
            : $"바이패스 {TypeNameOf(Bypass.BypassValveFamilyTypeName)} {BypassDiameterText} · {(Bypass.NormalizedSide > 0 ? "왼쪽" : "오른쪽")}";
    private string BypassDiameterText => Bypass!.BypassDiameterMm > 0 ? $"D{Bypass.BypassDiameterMm:0.##}"
        : HandbookBranchDiameterMm is { } dn ? $"DN{dn:0}(핸드북)" : "핸드북 규격 없음";
    private static string TypeNameOf(string familyTypeName)
    {
        var name = familyTypeName.StartsWith("DH_", StringComparison.Ordinal) ? familyTypeName[3..] : familyTypeName;
        return name;
    }
    public string Fill => IsBypassSet ? BypassColor
        : IsJoint ? (IsSelected ? "#558B2F" : "#7CB342") : (IsSelected ? "#AD1457" : "#D81B60");
    public string BypassStroke => BypassColor;
    public string BypassFill => BypassColor;
    /// <summary>세트 색. 실패 세트는 빨강이고, 실패한 세트를 선택하면 진한 빨강으로 바뀐다 —
    /// 선택 여부가 실패 색에 묻히지 않게 한다.</summary>
    private string BypassColor => IsError ? (IsSelected ? "#B71C1C" : "#E74C3C") : IsSelected ? "#00B8D4" : "#283593";
    /// <summary>캔버스 라벨. 바이패스 세트는 본관 밸브가 없어 패밀리명이 비므로 이름을 따로 단다(docs/230 § C).</summary>
    public string DisplayName => IsBypassSet ? BypassSet.TypeKey : FamilyTypeName;
    /// <summary>목록 배지. 이음관·바이패스 세트만 표시한다.</summary>
    public string KindBadge => IsJoint ? "이음관" : IsBypassSet ? "세트" : "";
}

/// <summary>외곽 벽체 한 장의 화면 표시. 내측면(기준선)은 실선, 외측면은 두께 표현용 보조선이다.</summary>
public sealed record OutlineWallItem(
    double InnerX1, double InnerY1, double InnerX2, double InnerY2,
    double OuterX1, double OuterY1, double OuterX2, double OuterY2,
    double ThicknessMm)
{
    public string ThicknessLabel => $"t={ThicknessMm:N0}";
}

/// <summary>가져온 기둥의 수평 단면 하나(docs/184, docs/189). <see cref="IsCore"/>가 true면 기둥 본체(실선),
/// false면 헌치 등 덧붙은 단면(점선)이다. <see cref="Points"/>는 Polygon.Points용 문자열이며
/// TypeConverter가 파싱하므로 로캘 무관하게 InvariantCulture로 만들어 둔 값이다.</summary>
public sealed record OutlineColumnItem(string Points, bool IsCore);

/// <summary>IN/OUT 유입·유출 화살표의 화면 표시.</summary>
public sealed record OutlineArrowItem(
    string Label, PortFlow Flow, double X1, double Y1, double X2, double Y2,
    string HeadPoints, double LabelX, double LabelY, double OffsetMm)
{
    // "IN2"도 초록이어야 하므로 라벨 문자열이 아니라 흐름으로 색을 정한다(docs/172 § C-8). PipePortItem.Color와 같은 값.
    public string Color => Flow == PortFlow.In ? "#1E8449" : "#B03A2E";
}

/// <summary>스케치 중 외곽선까지의 임시치수 한 개.</summary>
public sealed record TempDimensionItem(double X1, double Y1, double X2, double Y2, double DistanceMm, double LabelX, double LabelY, bool IsSubLine = false)
{
    public string Label => $"{DistanceMm:N0}";
    public string Stroke => IsSubLine ? "#C39BD3" : "#8E44AD";
}
