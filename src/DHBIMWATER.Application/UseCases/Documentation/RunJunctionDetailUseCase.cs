using DHBIMWATER.Application.DTOs.Documentation;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Interfaces.Piping;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Application.UseCases.Documentation;

public sealed class RunJunctionDetailUseCase
{
    private readonly CreatePipeMaterialScheduleUseCase _schedule;
    private readonly CreatePipeMaterialDraftingUseCase _drafting;
    private readonly CreateJunctionDetailViewUseCase _plan;
    private readonly BackfillPipeMaterialParametersUseCase _backfill;
    private readonly IPipeCatalogSource _catalog;

    public RunJunctionDetailUseCase(CreatePipeMaterialScheduleUseCase schedule,
        CreatePipeMaterialDraftingUseCase drafting, CreateJunctionDetailViewUseCase plan,
        BackfillPipeMaterialParametersUseCase backfill, IPipeCatalogSource catalog)
    {
        _schedule = schedule;
        _drafting = drafting;
        _plan = plan;
        _backfill = backfill;
        _catalog = catalog;
    }

    public JunctionDetailRunResult Execute(JunctionDetailOptions options)
    {
        PipeMaterialBackfillResult? backfill = null;
        PipeMaterialScheduleResult? schedule = null;
        PipeMaterialScheduleResult? drafting = null;
        JunctionDetailResult? plan = null;
        var warnings = new List<string>();

        // 체크 = 문서 전체 재산정 포함 재기록. 미체크여도 값 빈 부재(수동 배치 밸브 등)는 자동 기록 — 재료표에 빠지지 않게.
        if (options.Backfill || _backfill.ScanCandidates().CandidateCount > 0)
        {
            try
            {
                if (_catalog.TryEnsureAvailable(out var error))
                {
                    if (!string.IsNullOrWhiteSpace(error)) warnings.Add(error);
                    backfill = options.Backfill ? _backfill.Execute() : _backfill.ExecuteUntagged();
                }
                else if (!string.IsNullOrWhiteSpace(error)) warnings.Add(error);
            }
            catch (DhbimwaterException ex) when (ex.Code == ErrorCodes.NoActiveDocument) { throw; }
            catch (Exception ex) { warnings.Add($"값 재기록 실패: {ex.Message}"); }
        }

        if (options.CreateSchedule)
            try { schedule = _schedule.Execute(); }
            catch (DhbimwaterException ex) when (ex.Code == ErrorCodes.NoActiveDocument) { throw; }
            catch (Exception ex) { warnings.Add($"일람표 생성 실패: {ex.Message}"); }

        if (options.CreateDrafting)
            try { drafting = _drafting.Execute(options.RedrawExistingDrafting, options.UseTypeImage); }
            catch (DhbimwaterException ex) when (ex.Code == ErrorCodes.NoActiveDocument) { throw; }
            catch (Exception ex) { warnings.Add($"도면 생성 실패: {ex.Message}"); }

        if (options.CreatePlan)
        {
            var viewOptions = new JunctionDetailViewOptions(options.ExpandVertical, options.StepPaperMm,
                options.ViewScale, options.PlaceTags, options.TagTypeId, options.ExpandAxis, options.ExpandPositive);
            try { plan = _plan.Execute(viewOptions); }
            catch (DhbimwaterException ex) when (ex.Code == ErrorCodes.NoActiveDocument) { throw; }
            catch (Exception ex) { warnings.Add($"평면 상세도 생성 실패: {ex.Message}"); }
        }

        return new JunctionDetailRunResult(backfill, schedule, drafting, plan, warnings);
    }
}
