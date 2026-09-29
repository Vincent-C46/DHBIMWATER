using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DHBIMWATER.Application.UseCases.Gis;
using DHBIMWATER.Revit.DependencyInjection;
using DHBIMWATER.UI.ViewModels.Modeling;
using DHBIMWATER.UI.Views.Modeling;
using System.Windows.Threading;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Revit.Commands;

[Transaction(TransactionMode.Manual)]
public class PipeAlignmentModelingCommand : ModelessCommandBase
{
    protected override Result ExecuteInternal(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        // 생성자는 Revit API 조회를 포함하므로 반드시 Revit 메인 스레드에서 해석한다.
        var viewModel = ServiceContainer.GetService<PipeAlignmentModelingViewModel>();
        var handler = new PipeAlignmentModelingRequestHandler(ServiceContainer.GetService<ModelPipeAlignmentUseCase>())
        {
            // 창을 연 문서에서만 배치한다(docs/237 § B). 콤보·진단의 Revit 읽기는 RevitDispatcher가 같은 확인을 한다.
            OwnerDocument = DocumentGuard.Capture(commandData.Application),
        };
        var externalEvent = ExternalEvent.Create(handler);
        var revitHandle = commandData.Application.MainWindowHandle;
        PipeAlignmentModelingView? view = null;
        // 이 창이 쓸 서비스(싱글턴 RevitDispatcher 포함)가 든 컨테이너를 떼어 창 수명에 묶는다(docs/236 § A).
        // 창이 열린 동안 다른 명령이 ServiceContainer.Build로 공용 슬롯을 갈아도 이 창의 디스패처는 폐기되지 않는다.
        var ownedServices = ServiceContainer.Detach();

        viewModel.CreateModelAction = request =>
        {
            // 새 요청마다 지난 취소 플래그를 내린다(docs/236 § B).
            handler.ResetCancel();
            handler.PendingRequest = request;
            handler.Request.Make(PipeAlignmentModelingRequestId.CreateModel);
            var status = externalEvent.Raise();
            if (status == ExternalEventRequest.Accepted) return;

            handler.PendingRequest = null;
            viewModel.ApplyFailure(RejectedMessage(status));
        };

        // 배치 중 [배치 취소] — 핸들러의 토큰을 세운다(docs/209 § A-4).
        viewModel.CancelModelingAction = () => handler.Cancel();

        viewModel.FinalizeModelAction = () =>
        {
            handler.ResetCancel();
            handler.Request.Make(PipeAlignmentModelingRequestId.FinalizeModel);
            var status = externalEvent.Raise();
            if (status == ExternalEventRequest.Accepted) return;
            viewModel.ApplyFailure(RejectedMessage(status));
        };

        handler.ProgressReported = progress => view?.Dispatcher.InvokeAsync(() => viewModel.ApplyProgress(progress));
        handler.Completed = result => view?.Dispatcher.InvokeAsync(() => viewModel.ApplyResult(result));
        handler.PreviewRetained = result => view?.Dispatcher.InvokeAsync(() => viewModel.ApplyPreviewRetained(result));
        handler.PreviewReady = result => view?.Dispatcher.InvokeAsync(() => viewModel.ApplyPreviewReady(result));
        handler.Failed = error => view?.Dispatcher.InvokeAsync(() => viewModel.ApplyFailure(error));

        var uiThread = new Thread(() =>
        {
            try
            {
                view = new PipeAlignmentModelingView(viewModel);
                view.Closed += (_, _) =>
                {
                    externalEvent.Dispose();
                    // 공용 슬롯(ServiceContainer.Dispose)이 아니라 이 창이 떼어 온 컨테이너만 폐기한다(docs/236 § A) —
                    // 종전에는 그 사이 다른 명령이 만든 컨테이너를 대신 폐기했다.
                    ownedServices?.Dispose();
                    view.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                };
                AttachModelessWindow(view, revitHandle);
                view.Show();
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                if (view is not null) ModelessWindowGate.Close(view);
                externalEvent.Dispose();
                ownedServices?.Dispose();
                // TaskDialog는 Revit 메인 스레드 전용이므로 전용 STA 스레드에서 호출하지 않는다.
                System.Windows.MessageBox.Show($"관로 모델링 창을 여는 중 오류가 발생했습니다.\n{ex.Message}", "DHBIMWATER");
            }
        });

        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        return Result.Succeeded;
    }

    /// <summary>ExternalEvent.Raise()가 Denied/TimedOut일 때의 안내(docs/205 § C-2).</summary>
    private static string RejectedMessage(ExternalEventRequest status)
        => $"[{ErrorCodes.ExternalEventRejected}] Revit이 요청을 받지 않았습니다({status}). 열려 있는 대화상자나 명령을 끝낸 뒤 다시 시도하세요.";
}
