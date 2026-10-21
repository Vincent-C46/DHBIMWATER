namespace DHBIMWATER.Shared.Errors;

/// <summary>오류 코드 카탈로그. 형식·범위 규칙과 각 코드의 뜻은 docs/00_오류코드표.md가 정본이다.
/// 코드를 추가하면 그 표에도 같은 행을 추가한다. 코드 문자열 리터럴은 이 파일 밖에 두지 않는다(docs/205 § A-2).
/// <para>범위: 1xx 입력 검증 · 2xx 참조 대상 없음 · 3xx Revit 생성·배치 실패 · 4xx 파라미터 기록 · 5xx 파일/외부 입력 · 9xx 내부.</para></summary>
public static class ErrorCodes
{
    // ── SYS: 공통(문서·트랜잭션·ExternalEvent·DI·기준점 어댑터·공유 매개변수 파일) ──
    public const string NoActiveDocument = "SYS-101";
    public const string TransactionAlreadyStarted = "SYS-102";
    public const string TransactionNotStarted = "SYS-103";
    public const string TransactionNotCommitted = "SYS-104";
    public const string ExternalEventRejected = "SYS-105";
    public const string RequestLost = "SYS-106";
    public const string ServiceContainerNotBuilt = "SYS-107";
    /// <summary>모델리스 창을 연 문서가 활성 문서가 아니거나 닫혔다(docs/237 § A).</summary>
    public const string ActiveDocumentChanged = "SYS-108";
    public const string BasePointQueryFailed = "SYS-301";
    public const string TrueNorthQueryFailed = "SYS-302";
    public const string SharedCoordinateConvertFailed = "SYS-303";
    public const string BasePointSetFailed = "SYS-304";
    public const string DhBoostContractMismatch = "SYS-305";
    public const string CoordinateCountMismatch = "SYS-306";
    public const string SharedParameterFileFailed = "SYS-501";
    public const string Unclassified = "SYS-999";

    // ── PL: pipeLayout ──
    public const string PL_NoSegments = "PL-101";
    public const string PL_SegmentFamilyMissing = "PL-102";
    public const string PL_LengthParameterMissing = "PL-103";
    public const string PL_InvalidStraightLength = "PL-104";
    public const string PL_InvalidDiameter = "PL-105";
    public const string PL_InvalidGridSnap = "PL-106";
    public const string PL_InvalidEdgeLength = "PL-107";
    public const string PL_InvalidMinPieceLength = "PL-108";
    public const string PL_EdgeWithoutFlow = "PL-109";
    public const string PL_PresetNeedsOutline = "PL-110";
    public const string PL_PresetValveUnsupported = "PL-111";
    public const string PL_PresetPortMissing = "PL-112";
    public const string PL_NoRoomForFitting = "PL-113";
    public const string PL_FamilyNotSelected = "PL-201";
    public const string PL_FamilyNameFormat = "PL-202";
    public const string PL_FamilyNotFound = "PL-203";
    public const string PL_PipingSystemTypeNotFound = "PL-204";
    public const string PL_PipeTypeNotFound = "PL-205";
    public const string PL_LevelNotFound = "PL-206";
    public const string PL_NodeFittingUnplaceable = "PL-301";
    public const string PL_ConnectorCountMismatch = "PL-302";
    public const string PL_ConnectorSpanMismatch = "PL-303";
    public const string PL_OriginNotCentered = "PL-304";
    public const string PL_SegmentRegenerationFailed = "PL-305";
    public const string PL_PipeSplitFailed = "PL-306";
    public const string PL_OpenConnectorNotFound = "PL-307";
    public const string PL_NoRoomForAccessory = "PL-308";
    public const string PL_TeeNotOrthogonal = "PL-309";
    public const string PL_NodeConnectFailed = "PL-310";
    /// <summary>기존 객체(벽·기둥) 선택 중 Revit 오류 (205 잔여 정리, 2026-09-22).</summary>
    public const string PL_ExistingObjectPickFailed = "PL-311";
    /// <summary>수직분기 입력 오류 — 부품 없음·단관 길이(docs/301 § C).</summary>
    public const string PL_VerticalBranchInvalid = "PL-312";
    /// <summary>수직분기 시작 부속에 열린 +Z 커넥터 없음(docs/301 § A-2).</summary>
    public const string PL_VerticalBranchNoUpConnector = "PL-313";
    /// <summary>수직분기 쌓기 실패 — 커넥터 수·방향·회전(docs/301 § B).</summary>
    public const string PL_VerticalBranchPlacementFailed = "PL-314";
    public const string PL_DrivenParameterUnwritable = "PL-401";
    public const string PL_LengthParameterUnwritable = "PL-402";
    public const string PL_ParameterUnwritable = "PL-403";
    public const string PL_ParameterSetFailed = "PL-404";
    public const string PL_FamilySettingsSaveFailed = "PL-501";
    public const string PL_Internal = "PL-901";

    // ── VR: ValveRoom ──
    public const string VR_InvalidDimensions = "VR-101";
    public const string VR_MudSpecRequired = "VR-102";
    public const string VR_SluiceTypesRequired = "VR-103";
    public const string VR_InvalidDrainPit = "VR-104";
    public const string VR_DrainPitOutside = "VR-105";
    public const string VR_DrainPitOverlap = "VR-106";
    public const string VR_InvalidAirValveSpec = "VR-107";
    public const string VR_DrainPitMappingRequired = "VR-108";
    public const string VR_InvalidWallHeight = "VR-109";
    public const string VR_IntermediateWallOutside = "VR-110";
    public const string VR_InvalidBeamLayout = "VR-111";
    public const string VR_BeamTypeNotFound = "VR-201";
    public const string VR_ColumnTypeNotFound = "VR-202";
    public const string VR_LevelNotFound = "VR-203";
    public const string VR_ConcreteMaterialNotFound = "VR-204";
    public const string VR_BaseTypeNotFound = "VR-205";
    public const string VR_FoundationTypeNotFound = "VR-206";
    public const string VR_VoidFamilyNotLoaded = "VR-207";
    public const string VR_FoundationParameterMissing = "VR-208";
    public const string VR_WallCreateFailed = "VR-301";
    public const string VR_BeamCreateFailed = "VR-302";
    public const string VR_ColumnCreateFailed = "VR-303";
    public const string VR_FoundationGeometryUnreadable = "VR-304";
    public const string VR_VoidFaceNotFound = "VR-305";
    public const string VR_WallProfileTooFewPoints = "VR-306";
    public const string VR_NoCutHost = "VR-307";
    public const string VR_VoidParameterUnwritable = "VR-401";
    public const string VR_PlacementStationInvalid = "VR-501";
    public const string VR_PlacementStationOutOfRange = "VR-502";
    public const string VR_PlacementProfileInvalid = "VR-503";
    // VR-504(엑셀 Z 기준 미지원)는 docs/304에서 관저고를 목록 시트로 받게 되어 폐기. 번호 재사용 금지.
    public const string VR_PlacementHeightInvalid = "VR-505";
    public const string VR_PlacementListInvalid = "VR-506";
    public const string VR_Internal = "VR-901";

    // ── MT: 격점상세 배관재료표 ──
    public const string MT_ScheduleFieldMissing = "MT-201";
    public const string MT_DraftingViewTypeMissing = "MT-202";
    public const string MT_TextNoteTypeMissing = "MT-203";
    public const string MT_ScheduleCreateFailed = "MT-301";
    public const string MT_DraftingCreateFailed = "MT-302";
    public const string MT_ViewScaleLocked = "MT-303";
    public const string MT_Internal = "MT-901";

    // ── PA: pipeAlignmentModeling ──
    public const string PA_InvalidInterval = "PA-101";
    public const string PA_InvalidSnapTolerance = "PA-102";
    public const string PA_BendFamilyRequired = "PA-103";
    public const string PA_FamilyNameFormat = "PA-104";
    public const string PA_SelectionRequired = "PA-105";
    public const string PA_NoReaderForFile = "PA-106";
    public const string PA_ExcelMappingRequired = "PA-107";
    public const string PA_BendFamilyNotFound = "PA-201";
    public const string PA_StraightFamilyNotFound = "PA-202";
    public const string PA_NotAdaptiveFamily = "PA-203";
    public const string PA_PipingTypeNotFound = "PA-204";
    public const string PA_AdaptivePointCount = "PA-205";
    public const string PA_TooFarFromOrigin = "PA-301";
    public const string PA_StraightDirectShapeFailed = "PA-302";
    public const string PA_RotationCountMismatch = "PA-303";
    public const string PA_Cancelled = "PA-304";
    public const string PA_AngleParameterUnwritable = "PA-401";
    public const string PA_RotationWriteFailed = "PA-402";
    public const string PA_ShpFileMissing = "PA-501";
    public const string PA_ShpFormat = "PA-502";
    public const string PA_DbfFormat = "PA-503";
    public const string PA_DxfFormat = "PA-504";
    public const string PA_DwgFileMissing = "PA-505";
    public const string PA_ExcelFile = "PA-506";
    public const string PA_Internal = "PA-901";
    public const string PA_DispatcherBusy = "PA-902";
    public const string PA_DispatcherTimeout = "PA-903";
}
