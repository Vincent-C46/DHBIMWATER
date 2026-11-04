using DHBIMWATER.Core.Piping;
using DHBIMWATER.Application.Interfaces.Piping;
using DHBIMWATER.Application.Interfaces;

namespace DHBIMWATER.Application.DTOs.Documentation;

public sealed record JunctionDetailOptions(
    bool CreateSchedule = true,
    bool CreateDrafting = true,
    bool CreatePlan = true,
    // 격점상세도 UI는 false 고정(docs/291). true = 문서 전체 재산정 포함 재기록
    bool Backfill = false,
    bool ExpandVertical = true,
    double StepMm = JunctionDetailParameters.StepMm,
    // Revit 평면·3D 뷰 축척. 미리보기 축척 아님
    int ViewScale = JunctionDetailParameters.SheetScale,
    bool PlaceTags = true,
    long TagTypeId = 0,
    bool UseTypeImage = true,
    JunctionExpandAxis ExpandAxis = JunctionExpandAxis.Y,
    bool ExpandPositive = true,
    IReadOnlyList<JunctionSetOffset>? SetOffsets = null,
    // 미리보기가 정한 번호 태그 위치(docs/296). null이면 Revit 쪽 종전 규칙
    IReadOnlyList<JunctionTagPlacement>? TagPlacements = null,
    // 격점마다 시트 1장 + 평면·3D·드래프팅 배치. 평면 생성 켠 때만 돎
    bool CreateSheet = true,
    // 새 시트 도곽(제목 블록 유형 Id). 0 = A1 자동
    long TitleBlockId = 0);

public sealed record JunctionDetailResult(IReadOnlyList<string> Created, IReadOnlyList<string> Updated,
    IReadOnlyList<string> Reset, IReadOnlyList<string> Skipped, IReadOnlyList<string> Warnings);

public sealed record JunctionDetailViewOptions(
    bool ExpandVertical,
    double StepMm,
    int ViewScale,
    bool PlaceTags,
    long TagTypeId,
    JunctionExpandAxis ExpandAxis = JunctionExpandAxis.Y,
    bool ExpandPositive = true,
    IReadOnlyList<JunctionSetOffset>? SetOffsets = null,
    IReadOnlyList<JunctionTagPlacement>? TagPlacements = null)
{
    // 세트 설정 없으면 공통 축·간격(ExpandPositive = 부호)
    public JunctionSetOffset For(long setId)
        => JunctionSetOffsets.For(SetOffsets, setId, ExpandAxis, ExpandPositive ? StepMm : -StepMm);
}

public sealed record JunctionDetailRunResult(
    PipeMaterialBackfillResult? Backfill,
    PipeMaterialScheduleResult? Schedule,
    PipeMaterialScheduleResult? Drafting,
    JunctionDetailResult? Plan,
    IReadOnlyList<string> Warnings,
    JunctionDetailSheetResult? Sheet = null);

public sealed record JunctionDetailSheetResult(IReadOnlyList<string> Created, IReadOnlyList<string> Updated,
    IReadOnlyList<string> Unchanged, IReadOnlyList<string> Skipped, IReadOnlyList<string> Warnings);

public sealed record JunctionDetailPreviewJunction(string JunctionId, string RoomType,
    int PartCount, IReadOnlyList<JunctionPreviewItem> Items, IReadOnlyList<PipeMaterialRow> TableRows,
    IReadOnlyList<string> Warnings, IReadOnlyList<JunctionPreviewLink>? Links = null,
    JunctionValveRoom? Room = null);

public sealed record JunctionDetailPreview(IReadOnlyList<JunctionDetailPreviewJunction> Junctions,
    IReadOnlyList<PipeMaterialRow> UnassignedRows, IReadOnlyList<JunctionDetailTagType> TagTypes,
    int MaterialPartCount = 0, IReadOnlyList<JunctionSymbolShapeDiagnostic>? ShapeDiagnostics = null);
