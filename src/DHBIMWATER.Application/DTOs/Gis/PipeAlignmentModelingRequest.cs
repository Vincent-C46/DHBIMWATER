using DHBIMWATER.Core.Gis;

namespace DHBIMWATER.Application.DTOs.Gis;

/// <summary>Adaptive — 직관 2점 가변 + 곡관 5점 가변. (구 Beam: 구조 프레이밍 배치, 2026-08-18 교체)</summary>
public enum PipeAlignmentOutputMode { DirectShape, Adaptive, PipingSystem }

/// <summary>엑셀 시트 1개를 관로 선형으로 읽기 위한 열 매핑. X·Y·Z는 필수, Station은 선택.</summary>
/// <param name="StationColumnIndex">선택. 체이니지 정렬 검증용.</param>
public sealed record ExcelAlignmentMapping(string SheetName, int HeaderRow, int DataStartRow, int XColumnIndex, int YColumnIndex, int ZColumnIndex, int? StationColumnIndex,
    StationParseMode StationMode = StationParseMode.Station, double NumberChainIntervalM = 20d, int? BendKindColumnIndex = null, int? VertexDiameterColumnIndex = null);

/// <summary>입력 파일 1개와 그 파일의 필드 매핑. null 필드는 해당 값을 읽지 않는다. ExcelMapping이 있으면 엑셀 리더가 재읽기 시에도 이를 사용한다.</summary>
/// <param name="ManualDiameterMm">직경 필드로 해석하지 못했을 때(엑셀처럼 필드 자체가 없는 경우 포함) 대신 쓸 수동 입력값. null 또는 0 이하면 쓰지 않는다.</param>
/// <param name="DiameterMappings">사용자가 확정한 원본 직경 표기별 DN·등급. 키는 Trim만 적용한 원문이며 파싱 결과보다 우선한다.</param>
/// <param name="ReversedRecordNumbers">
/// 진행 방향을 뒤집을 레코드번호. Loader가 해당 레코드의 정점 순서를 역순으로 만들어 시작점과 끝점을 교환한다.
/// 레코드번호는 이 파일(엑셀은 이 시트) 안에서만 유일하므로, 파일별로 분리된 목록이어야 한다.
/// </param>
/// <param name="Material">모델링 창의 관종 토글(docs/211 § N). null이면 제한 없음(테스트·구형 호출). 값이 있으면 Loader는 이 관종의 등급만 받아들이고,
/// 필드가 다른 관종 등급·별칭을 주면 이 관종의 기본 등급으로 잡는다(docs/212 § A-2) — 창의 판별과 어긋나지 않게.</param>
/// <param name="ExclusionRanges">관을 두지 않을 측점 구간(docs/219). Loader가 대상 레코드를 경계에서 잘라 여러 선형으로 만든다. 레코드번호는 이 파일(시트) 안 기준.</param>
public sealed record AlignmentSourceFile(string FilePath, string PipeKind, string? DiameterField, string? KindField, IReadOnlyList<string>? Layers = null, ExcelAlignmentMapping? ExcelMapping = null, double? ManualDiameterMm = null, IReadOnlyDictionary<string, ResolvedPipeSpecKey>? DiameterMappings = null, IReadOnlyList<string>? ReversedRecordNumbers = null, PipeMaterial? Material = null,
    IReadOnlyList<AlignmentExclusionRange>? ExclusionRanges = null);

/// <param name="DiameterMm">확정 DN. null이면 사용자가 지정하지 않은 행이다.</param>
public sealed record ResolvedPipeSpecKey(double? DiameterMm, string? PipeKind);

public sealed record PipeAlignmentModelingRequest
{
    public required IReadOnlyList<AlignmentSourceFile> Files { get; init; }
    /// <summary>
    /// 평면 기준점(원본 좌표계, m). 이 점이 프로젝트 기준점(PBP)에 놓이도록 전체 형상을 평행이동한다.
    /// null이면 첫 유효 정점에서 자동 산출한다. (0, 0)은 "이동하지 않음"(원본 좌표 그대로 배치)을 뜻하며,
    /// 자동 산출로 대체되지 않는다 — 호출부가 좌표 이동 여부를 명시적으로 결정할 수 있어야 하기 때문이다.
    /// </summary>
    public double? ReferenceX { get; init; }
    public double? ReferenceY { get; init; }
    /// <summary>기준점을 프로젝트 공유좌표에도 기록할지 여부. 형상 배치 위치는 ReferenceX·Y가 결정하며 이 값과 무관하다.</summary>
    public bool ApplySharedCoordinates { get; init; }
    /// <summary>야간 작업 여부(`DH_야간`). 이 실행으로 만드는 모든 관로 요소에 같은 값이 기록된다(docs/141 § C).</summary>
    public bool IsNightWork { get; init; }
    public ZDatum ZDatum { get; init; } = ZDatum.Invert;
    /// <summary>TODO: ZSource 미소비 — Phase 2.</summary>
    public ZSource ZSource { get; init; } = ZSource.GeometryZ;
    public PipeAlignmentOutputMode OutputMode { get; init; } = PipeAlignmentOutputMode.DirectShape;
    public double IntervalMm { get; init; } = 6000;
    /// <summary>이경관 몸통 길이 S(mm). 제원표 확정 전 임시 입력이며 0 이하면 배치하지 않는다.</summary>
    public double ReducerBodyLengthMm { get; init; } = 250d;
    /// <summary>직관 2점 가변 패밀리·타입("패밀리명 : 타입명").</summary>
    public string? StraightFamilyTypeName { get; init; }
    /// <summary>직관 인스턴스에 호칭지름(mm)을 기록할 파라미터명. null이면 기록하지 않는다.</summary>
    public string? StraightDiameterParameterName { get; init; }
    /// <summary>직관 제원표의 외경 OD를 기록할 파라미터명. null이면 기록하지 않는다.</summary>
    public string? StraightOuterDiameterParameterName { get; init; }
    /// <summary>직관 제원표의 두께를 기록할 파라미터명. null이면 기록하지 않는다.</summary>
    public string? StraightThicknessParameterName { get; init; }
    /// <summary>5점 가변 곡관 패밀리·타입("패밀리명 : 타입명").</summary>
    public string? BendFamilyTypeName { get; init; }
    public string? BendDiameterParameterName { get; init; }
    public string? BendWallThicknessParameterName { get; init; }
    /// <summary>곡관 인스턴스에 외경 OD(mm)를 기록할 파라미터명. 곡관 형상은 OD·thk로 구동되므로 사실상 필수다. null이면 기록하지 않는다.</summary>
    public string? BendOuterDiameterParameterName { get; init; }
    public string? PipingSystemTypeName { get; init; }
    public string? PipeTypeName { get; init; }
    public string? LevelName { get; init; }
    /// <summary>절점 병합 허용오차(mm). 곡관 자리를 찾을 때 진단과 같은 그래프를 재현하려면 진단에서 쓴 값과 같아야 한다.</summary>
    public double SnapToleranceMm { get; init; } = 10;
    /// <summary>
    /// 모델링 창에서 현재 편집 중인 관·곡관 설정. 저장 여부와 관계없이 같은 화면에서 실행한 모델링에는 이 값을 사용한다.
    /// null은 UI 외 호출부와의 호환용이며, 이때 UseCase가 저장소 설정을 읽는다.
    /// </summary>
    public BendSettings? CurrentBendSettings { get; init; }
}

/// <param name="BendCount">실제로 생성되어 커밋된 5점 가변 곡관 수.</param>
/// <param name="PreviewElementIds">선형 미리보기로 만든 DirectShape ID(ElementId.Value). 미리보기 직후 현재 뷰 Zoom에 쓴다(docs/226 § D). 최종 배치는 null.</param>
public sealed record PipeAlignmentModelingResult(PipeAlignmentOutputMode OutputMode, int CreatedCount, int SkippedSegments, IReadOnlyList<string> Warnings, int BendCount = 0,
    IReadOnlyList<long>? PreviewElementIds = null);

/// <summary>배치되는 관로 요소에 기록할 DH_* 정보 매개변수 중 관로 전체가 공유하는 값.</summary>
public sealed record PipeInfoParameterContext(string JointType, JointApplicationMode ApplicationMode, ZDatum ZDatum, bool Enabled = true,
    /// <summary>야간 작업 여부(`DH_야간`). 실행 단위 값이라 관로 전체가 공유한다(docs/141 § C-4).</summary>
    bool IsNightWork = false,
    /// <summary>곡관 접합(소켓/플랜지). 접합 개소를 어느 매개변수에 적을지는 <b>관종과 이 값</b>이 정한다(docs/166 § 6).
    /// <para>`DH_접합방법`(KP·타이튼)은 소켓 조인트의 <b>종류</b>일 뿐이라 용접을 표현하지 못한다 — docs/168 § 4.</para>
    /// 기본값이 있어 이 축을 모르는 기존 호출부도 그대로 컴파일된다(설정 기본값과 같은 소켓).</summary>
    BendConnection BendConnection = BendConnection.Socket,
    /// <summary>주철관 소켓에 이탈방지압륜을 쓰는지. 접합 개소를 `DH_KP접합수` 대신
    /// `DH_이탈방지압륜접합수`에 적는다(docs/166 § 11-4). 강관·플랜지에는 영향이 없다.</summary>
    bool UseRestraintGland = false,
    /// <summary>강관 곡관 종류(F12/F15/F20, 전역 설정). 강관 곡관의 `DH_관종등급`에는 직관 등급(STWW) 대신 이 값을 적는다
    /// (2026-09-22 사용자 확정 — 곡관 관두께·무게는 STWW가 아니라 F로 정해진다). 직관·이경관은 계속 STWW 등급이다.</summary>
    SteelFittingClass SteelFittingClass = SteelFittingClass.F12);

// Repo 인터페이스 계약을 보존하는 기존 DTO 정의다.
public sealed record PipeAlignmentCreateDefinition(IReadOnlyList<PipeAlignment> Alignments, AlignmentPlacementOrigin Origin);
/// <param name="CreatedElementIds">만든 미리보기 DirectShape ID(ElementId.Value, docs/226 § D).</param>
public sealed record PipeAlignmentCreateResult(int CreatedCount, int SkippedSegments, IReadOnlyList<string> Warnings, IReadOnlyList<long>? CreatedElementIds = null);
public sealed record AlignmentPlacementOrigin(double X, double Y, ZDatum ZDatum, StraightPipeSpecTable StraightPipeSpecs,
    double TrueNorthClockwiseDegrees = 0d)
{
    /// <summary>선형별 직관 제원으로 계산한 중심선 Z 오프셋(m). 모든 배치 경로가 이 값을 공유한다.</summary>
    public double GetZOffsetM(PipeAlignment alignment) => GetZOffsetM(alignment.PipeKind, alignment.DiameterMm);

    public double GetZOffsetM(string pipeKind, double diameterMm)
        => PipeElevationDatum.OffsetMm(ZDatum, StraightPipeSpecs.Find(pipeKind, diameterMm), diameterMm) / 1000d;
}

/// <param name="Count">실제로 배치된 직관 수.</param>
/// <param name="Warnings">직관 규격 또는 매핑 파라미터를 적용하지 못한 경우의 안내.</param>
public sealed record AlignmentStraightPlacementResult(
    int Count,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<(string PipeKind, double DiameterMm), double>? OuterDiametersMm = null);
