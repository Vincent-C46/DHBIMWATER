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

        var schedule = scheduleUseCase.Execute();
        if (schedule.Scan.TotalPartCount == 0)
        {
            TaskDialog.Show("배관재료표",
                "DHBIMWATER가 만든 배관 밸브류 부재를 찾지 못했습니다.\n\n" +
                "[밸브실 배관]으로 관을 먼저 생성하세요.");
            return Result.Succeeded;
        }

        // 트랜잭션을 나눠 둔다 — 도면 그리기가 실패해도 이미 만든 일람표까지 되돌리면 안 된다.
        var drafting = draftingUseCase.Execute();

        ShowResult(schedule, drafting);
        return Result.Succeeded;
    }

    private static void ShowResult(PipeMaterialScheduleResult schedule, PipeMaterialScheduleResult drafting)
    {
        var lines = new List<string> { $"배관 밸브류 부재 {schedule.Scan.TotalPartCount:N0}개를 확인했습니다." };
        AppendSection(lines, "일람표", schedule);
        AppendSection(lines, "도면(드래프팅뷰)", drafting);

        if (schedule.Scan.UnassignedCount > 0)
            lines.Add($"격점번호가 비어 있는 부재 {schedule.Scan.UnassignedCount:N0}개는 '미지정' 표에 모았습니다. " +
                "격점별로 나누려면 [밸브실 배관] 창에서 격점번호를 넣고 다시 만드세요.");

        var createdCount = schedule.Created.Count + drafting.Created.Count;
        var dialog = new TaskDialog("배관재료표")
        {
            MainInstruction = createdCount > 0
                ? $"뷰 {createdCount}개를 만들었습니다."
                : "새로 만든 뷰가 없습니다.",
            MainContent = string.Join(Environment.NewLine + Environment.NewLine, lines)
        };
        dialog.Show();
    }

    private static void AppendSection(ICollection<string> lines, string label, PipeMaterialScheduleResult result)
    {
        if (result.Created.Count > 0)
            lines.Add($"[{label}] 만든 뷰 {result.Created.Count}개: {string.Join(", ", result.Created)}");
        if (result.Skipped.Count > 0)
            lines.Add($"[{label}] 이미 있어 건너뛴 뷰 {result.Skipped.Count}개: {string.Join(", ", result.Skipped)}");
        if (result.Created.Count == 0 && result.Skipped.Count == 0)
            lines.Add($"[{label}] 만들 대상이 없습니다.");
    }
}
