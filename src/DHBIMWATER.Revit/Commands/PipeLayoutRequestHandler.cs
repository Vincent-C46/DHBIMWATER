using Autodesk.Revit.UI;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.UseCases.AutoGenerator;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Revit.Commands;

/// <summary>배관 생성 UseCase와 외곽 벽체 피킹을 Revit API 컨텍스트에서 실행한다.</summary>
internal sealed class PipeLayoutRequestHandler : IExternalEventHandler
{
    private readonly CreateValvePipingUseCase _useCase;
    private readonly IValveRoomOutlinePickRepo _outlinePick;

    public PipeLayoutRequest Request { get; } = new();
    public PipeNetworkDefinition? PendingNetwork { get; set; }

    /// <summary>피킹 결과를 ViewModel로 되돌리는 콜백. UI 스레드 마샬링은 호출측이 담당한다.</summary>
    public Action<ValveRoomOutline?>? OutlinePicked { get; set; }
    public Action<bool, string, IReadOnlyList<PipeConnectionFailure>>? ModelCreationCompleted { get; set; }

    public PipeLayoutRequestHandler(CreateValvePipingUseCase useCase, IValveRoomOutlinePickRepo outlinePick)
    {
        _useCase = useCase;
        _outlinePick = outlinePick;
    }

    public void Execute(UIApplication uiapp)
    {
        while (Request.TryTake(out var requestId))
            ExecuteRequest(requestId);
    }

    private void ExecuteRequest(PipeLayoutRequestId requestId)
    {
        var modelCompletionNotified = false;
        try
        {
            switch (requestId)
            {
                case PipeLayoutRequestId.CreateModel:
                    if (PendingNetwork is null)
                    {
                        NotifyModelCreationCompleted(false, "생성 요청이 유실됐습니다. 다시 시도하세요.", []);
                        modelCompletionNotified = true;
                        break;
                    }
                    var network = PendingNetwork;
                    PendingNetwork = null;
                    var result = _useCase.Execute(network);
                    // 경고는 배치 실패가 아니다(트랜잭션은 이미 커밋됐다). 성공으로 알리되 본문을 함께 보여준다.
                    NotifyModelCreationCompleted(true, result.Warnings.Count == 0
                        ? result.Summary
                        : $"{result.Summary} 확인 필요 {result.Warnings.Count}건.", []);
                    modelCompletionNotified = true;
                    if (result.Warnings.Count > 0)
                        TaskDialog.Show("배관 생성 확인 필요", $"{result.Summary}\n\n- " + string.Join("\n- ", result.Warnings.Take(20)));
                    break;

                case PipeLayoutRequestId.PickOutline:
                    OutlinePicked?.Invoke(_outlinePick.PickExteriorWalls());
                    break;
            }
        }
        catch (Exception ex)
        {
            if (requestId == PipeLayoutRequestId.CreateModel)
            {
                var failures = ex is PipeNetworkBuildException build ? build.Failures : [];
                NotifyModelCreationCompleted(false, ex.Message, failures);
                modelCompletionNotified = true;
                var lines = ex.Message.Split([Environment.NewLine], StringSplitOptions.None);
                var dialog = new TaskDialog("배관 생성 오류")
                {
                    MainInstruction = lines[0],
                    MainContent = string.Join(Environment.NewLine, lines.Skip(1))
                };
                dialog.Show();
            }
            else
            {
                OutlinePicked?.Invoke(null);
                TaskDialog.Show("외곽 벽체 선택 오류", ex.Message);
            }
        }
        finally
        {
            if (requestId == PipeLayoutRequestId.CreateModel && !modelCompletionNotified)
                NotifyModelCreationCompleted(false, "생성 요청을 완료하지 못했습니다. 다시 시도하세요.", []);
        }
    }

    private void NotifyModelCreationCompleted(bool succeeded, string message, IReadOnlyList<PipeConnectionFailure> failures)
        => ModelCreationCompleted?.Invoke(succeeded, message, failures);

    public string GetName() => "PipeLayoutRequest";
}
