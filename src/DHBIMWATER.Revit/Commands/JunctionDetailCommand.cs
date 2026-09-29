using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.UseCases.Documentation;
using DHBIMWATER.Revit.DependencyInjection;

namespace DHBIMWATER.Revit.Commands;

/// <summary>
/// 격점상세도 배관재료표를 만든다 — 일람표(docs/132)와 드래프팅뷰 도면(docs/133)을 함께 만든다.
/// <para>창을 띄우지 않는다 — 열·정렬·이름이 전부 규약이고 대상 격점은 모델에서 읽으므로
/// 사용자가 고를 것이 없다. 모달 커맨드는 이미 Revit API 컨텍스트라
/// <see cref="PipeLayoutCommand"/>와 달리 ExternalEvent도 필요 없다.</para>
/// </summary>
[Transaction(TransactionMode.Manual)]
public class JunctionDetailCommand : CommandBase
{
    protected override Result ExecuteInternal(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var scheduleUseCase = ServiceContainer.GetService<CreatePipeMaterialScheduleUseCase>();
        var draftingUseCase = ServiceContainer.GetService<CreatePipeMaterialDraftingUseCase>();
        var backfillUseCase = ServiceContainer.GetService<BackfillPipeMaterialParametersUseCase>();

        // 재기록을 일람표 생성보다 먼저 한다 — 재기록 결과가 곧 일람표 대상이다(docs/136 § 3-9).
        var backfill = OfferBackfill(backfillUseCase);

        var schedule = scheduleUseCase.Execute();
        if (schedule.Scan.TotalPartCount == 0)
        {
            // 재기록 [예]를 눌렀는데 후보가 전부 건너뛰어졌으면 그 사유가 여기서 유실됐다(docs/208 § C-5).
            var backfillNote = backfill is { SkippedCount: > 0 } && backfill.Warnings.Count > 0
                ? "\n\n" + string.Join("\n", backfill.Warnings.Take(15))
                : string.Empty;
            TaskDialog.Show("배관재료표",
                "재료표 값(DH_Addin)이 기록된 배관 부재가 없습니다.\n\n" +
                "[밸브실 배관]으로 관을 새로 생성하거나,\n" +
                "기존 관·곡관·T형을 선택한 뒤 이 버튼을 다시 눌러 값 재기록을 진행하세요." + backfillNote);
            return Result.Succeeded;
        }

        // 이미 있는 도면 뷰를 다시 그릴지 먼저 묻는다 — 뷰 안의 요소가 전부 지워지므로 사용자 승인이 필요하다(docs/173).
        var updateExisting = OfferDraftingUpdate(draftingUseCase);

        // 트랜잭션을 나눠 둔다 — 도면 그리기가 실패해도 이미 만든 일람표까지 되돌리면 안 된다.
        var drafting = draftingUseCase.Execute(updateExisting);

        ShowResult(schedule, drafting, backfill);
        return Result.Succeeded;
    }

    /// <summary>재료표 값이 없는 기존 부재가 있으면 재기록할지 묻는다(docs/136).
    /// <para>모델만 보고 "DHBIMWATER가 만든 부재"를 확정할 방법이 없으므로, 이 확인이 유일한 방어선이다.
    /// 사용자가 요소를 선택해 두면 그 선택분만 대상이 된다.</para>
    /// <returns>재기록을 했으면 그 결과, 안 했으면 null.</returns></summary>
    private static PipeMaterialBackfillResult? OfferBackfill(BackfillPipeMaterialParametersUseCase useCase)
    {
        var scan = useCase.ScanCandidates();
        if (scan.CandidateCount == 0) return null;

        var scopeText = scan.FromSelection ? "선택한 요소 중 " : "";
        var dialog = new TaskDialog("배관재료표")
        {
            MainInstruction = $"{scopeText}재료표 값이 없는 배관 부재 {scan.CandidateCount:N0}개를 찾았습니다.",
            MainContent = "이 부재의 명칭·규격·번호를 실측해 기록한 뒤 재료표를 만듭니다. " +
                "이미 값이 있는 부재는 건드리지 않습니다." + Environment.NewLine + Environment.NewLine +
                "DHBIMWATER가 만들지 않은 배관 이음쇠·밸브류가 이 문서에 있다면 [아니오]를 누르고, " +
                "대상 부재만 선택한 뒤 다시 실행하세요.",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No
        };
        return dialog.Show() == TaskDialogResult.Yes ? useCase.Execute() : null;
    }

    /// <summary>이미 있는 도면 뷰를 최신 모델 값으로 다시 그릴지 묻는다(docs/173).
    /// <para>드래프팅뷰는 선·문자를 박아 넣은 정적 도면이라 일람표와 달리 저절로 갱신되지 않는다.
    /// 다시 그리려면 뷰 안을 비워야 하므로, 이 확인이 사용자 주석을 지키는 유일한 방어선이다.</para>
    /// <returns>사용자가 갱신을 승인했으면 true. 이미 있는 뷰가 없으면 묻지 않고 false.</returns></summary>
    private static bool OfferDraftingUpdate(CreatePipeMaterialDraftingUseCase useCase)
    {
        var existing = useCase.FindExistingViews();
        if (existing.Count == 0) return false;

        // 격점이 많으면 대화상자가 넘친다. 앞 15개만 보이고 나머지는 수로 알린다.
        const int previewLimit = 15;
        var names = string.Join(Environment.NewLine, existing.Take(previewLimit));
        if (existing.Count > previewLimit)
            names += $"{Environment.NewLine}… 외 {existing.Count - previewLimit:N0}개";

        var dialog = new TaskDialog("배관재료표")
        {
            MainInstruction = $"이미 있는 도면 뷰 {existing.Count:N0}개를 찾았습니다.",
            MainContent = names + Environment.NewLine + Environment.NewLine +
                "표를 최신 모델 값으로 다시 그립니다. " +
                "이때 뷰 안의 요소를 모두 지우므로, 이 뷰에 직접 넣은 치수·주석·상세항목도 함께 사라집니다." +
                Environment.NewLine + Environment.NewLine +
                "[아니오]를 누르면 기존 뷰를 그대로 둡니다.",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No
        };
        return dialog.Show() == TaskDialogResult.Yes;
    }

    private static void ShowResult(PipeMaterialScheduleResult schedule, PipeMaterialScheduleResult drafting,
        PipeMaterialBackfillResult? backfill)
    {
        var lines = new List<string>();
        if (backfill is not null)
        {
            var backfillLine = $"[값 재기록] 부재 {backfill.WrittenCount:N0}개에 재료표 값을 기록했습니다.";
            if (backfill.SkippedCount > 0)
                backfillLine += $" 판정할 수 없어 건너뛴 부재 {backfill.SkippedCount:N0}개.";
            lines.Add(backfillLine);
            lines.AddRange(backfill.Warnings);
        }
        lines.Add($"배관 밸브류·이음쇠 부재 {schedule.Scan.TotalPartCount:N0}개를 확인했습니다.");
        AppendSection(lines, "일람표", schedule);
        AppendSection(lines, "도면(드래프팅뷰)", drafting);

        // 건너뛴 일람표는 열 구성이 옛날 그대로다(docs/140 § C). 값은 필터로 자동 갱신되지만 열은 아니다.
        // 사유가 붙은 항목("이름 — [MT-301] …")은 실패라 이 안내가 맞지 않는다(docs/208 § C-5).
        if (schedule.Skipped.Any(x => !x.Contains(" — [", StringComparison.Ordinal)))
            lines.Add("이미 있는 일람표는 사용자가 손본 열 폭·서식을 지우지 않으려고 그대로 둡니다. " +
                "최신 열 구성(번호·명칭·규격·형상·수량·비고)으로 다시 만들려면 프로젝트 탐색기에서 해당 일람표를 지우고 다시 실행하세요.");

        // 도면 뷰는 정적이라 건너뛰면 값이 옛날 그대로다 — 다시 실행해 [예]를 누르면 된다(docs/173).
        if (drafting.Skipped.Any(x => !x.Contains(" — [", StringComparison.Ordinal)))
            lines.Add("건너뛴 도면 뷰는 표 내용이 옛날 그대로입니다. " +
                "다시 실행해 갱신 확인에서 [예]를 누르면 최신 모델 값으로 다시 그립니다.");

        if (schedule.Scan.UnassignedCount > 0)
            lines.Add($"격점번호가 비어 있는 부재 {schedule.Scan.UnassignedCount:N0}개는 '미지정' 표에 모았습니다. " +
                "격점별로 나누려면 [밸브실 배관] 창에서 격점번호를 넣고 다시 만드세요.");

        var createdCount = schedule.Created.Count + drafting.Created.Count;
        var updatedCount = drafting.Updated.Count;   // 일람표는 갱신 경로가 없다(docs/173)
        var dialog = new TaskDialog("배관재료표")
        {
            MainInstruction = (createdCount, updatedCount) switch
            {
                (0, 0) => "새로 만들거나 다시 그린 뷰가 없습니다.",
                (> 0, 0) => $"뷰 {createdCount}개를 만들었습니다.",
                (0, > 0) => $"도면 뷰 {updatedCount}개를 다시 그렸습니다.",
                _ => $"뷰 {createdCount}개를 만들고 도면 뷰 {updatedCount}개를 다시 그렸습니다.",
            },
            MainContent = string.Join(Environment.NewLine + Environment.NewLine, lines)
        };
        dialog.Show();
    }

    private static void AppendSection(ICollection<string> lines, string label, PipeMaterialScheduleResult result)
    {
        // 격점이 수십 개면 대화상자가 넘친다 — OfferDraftingUpdate와 같이 15개 + "외 N개"(docs/208 § C-5).
        if (result.Created.Count > 0)
            lines.Add($"[{label}] 만든 뷰 {result.Created.Count}개: {Names(result.Created)}");
        if (result.Updated.Count > 0)
            lines.Add($"[{label}] 다시 그린 뷰 {result.Updated.Count}개: {Names(result.Updated)}");
        if (result.Skipped.Count > 0)
            lines.Add($"[{label}] 건너뛴 뷰 {result.Skipped.Count}개: {Names(result.Skipped)}");
        if (result.Created.Count == 0 && result.Updated.Count == 0 && result.Skipped.Count == 0)
            lines.Add($"[{label}] 만들 대상이 없습니다.");
    }

    private static string Names(IReadOnlyList<string> names)
        => string.Join(", ", names.Take(15)) + (names.Count > 15 ? $" … 외 {names.Count - 15}개" : string.Empty);
}
