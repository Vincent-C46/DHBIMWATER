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
    double StepPaperMm = JunctionDetailParameters.StepPaperMm,
    int ViewScale = JunctionDetailParameters.ViewScale,
    bool PlaceTags = true,
    long TagTypeId = 0,
    bool UseTypeImage = true,
    JunctionExpandAxis ExpandAxis = JunctionExpandAxis.Y,
    bool ExpandPositive = true,
    IReadOnlyList<JunctionSetOffset>? SetOffsets = null);

public sealed record JunctionDetailResult(IReadOnlyList<string> Created, IReadOnlyList<string> Updated,
    IReadOnlyList<string> Reset, IReadOnlyList<string> Skipped, IReadOnlyList<string> Warnings);

public sealed record JunctionDetailViewOptions(
    bool ExpandVertical,
    double StepPaperMm,
    int ViewScale,
    bool PlaceTags,
    long TagTypeId,
    JunctionExpandAxis ExpandAxis = JunctionExpandAxis.Y,
    bool ExpandPositive = true,
    IReadOnlyList<JunctionSetOffset>? SetOffsets = null)
{
    // 세트 설정 없으면 공통 축·간격(ExpandPositive = 부호)
    public JunctionSetOffset For(long setId)
        => JunctionSetOffsets.For(SetOffsets, setId, ExpandAxis, ExpandPositive ? StepPaperMm : -StepPaperMm);
}

public sealed record JunctionDetailRunResult(
    PipeMaterialBackfillResult? Backfill,
    PipeMaterialScheduleResult? Schedule,
    PipeMaterialScheduleResult? Drafting,
    JunctionDetailResult? Plan,
    IReadOnlyList<string> Warnings);

public sealed record JunctionDetailPreviewJunction(string JunctionId, string RoomType,
    int PartCount, IReadOnlyList<JunctionPreviewItem> Items, IReadOnlyList<PipeMaterialRow> TableRows,
    IReadOnlyList<string> Warnings, IReadOnlyList<JunctionPreviewLink>? Links = null);

public sealed record JunctionDetailPreview(IReadOnlyList<JunctionDetailPreviewJunction> Junctions,
    IReadOnlyList<PipeMaterialRow> UnassignedRows, IReadOnlyList<JunctionDetailTagType> TagTypes,
    int MaterialPartCount = 0, IReadOnlyList<JunctionSymbolShapeDiagnostic>? ShapeDiagnostics = null);
