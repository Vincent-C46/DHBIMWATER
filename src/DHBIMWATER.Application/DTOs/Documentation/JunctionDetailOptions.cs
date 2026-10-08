using DHBIMWATER.Core.Piping;
using DHBIMWATER.Application.Interfaces.Piping;
using DHBIMWATER.Application.Interfaces;

namespace DHBIMWATER.Application.DTOs.Documentation;

public sealed record JunctionDetailOptions(
    bool CreateSchedule = true,
    bool CreateDrafting = true,
    bool CreatePlan = true,
    bool Backfill = false,
    bool ExpandVertical = true,
    double StepPaperMm = JunctionDetailParameters.StepPaperMm,
    int ViewScale = JunctionDetailParameters.ViewScale,
    bool PlaceTags = true,
    long TagTypeId = 0,
    bool UseTypeImage = true,
    bool RedrawExistingDrafting = false);

public sealed record JunctionDetailResult(IReadOnlyList<string> Created, IReadOnlyList<string> Updated,
    IReadOnlyList<string> Reset, IReadOnlyList<string> Skipped, IReadOnlyList<string> Warnings);

public sealed record JunctionDetailViewOptions(
    bool ExpandVertical,
    double StepPaperMm,
    int ViewScale,
    bool PlaceTags,
    long TagTypeId);

public sealed record JunctionDetailRunResult(
    PipeMaterialBackfillResult? Backfill,
    PipeMaterialScheduleResult? Schedule,
    PipeMaterialScheduleResult? Drafting,
    JunctionDetailResult? Plan,
    IReadOnlyList<string> Warnings);

public sealed record JunctionDetailPreviewJunction(string JunctionId, string RoomType,
    int PartCount, IReadOnlyList<JunctionPreviewItem> VerticalItems, IReadOnlyList<PipeMaterialRow> TableRows,
    IReadOnlyList<string> Warnings);

public sealed record JunctionDetailPreview(IReadOnlyList<JunctionDetailPreviewJunction> Junctions,
    IReadOnlyList<PipeMaterialRow> UnassignedRows, IReadOnlyList<JunctionDetailTagType> TagTypes);
