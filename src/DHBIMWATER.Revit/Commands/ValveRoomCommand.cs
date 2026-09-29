using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.UseCases.AutoGenerator;
using DHBIMWATER.Revit.DependencyInjection;
using DHBIMWATER.UI.ViewModels.Modeling;
using DHBIMWATER.UI.Views.Modeling;
using System.Windows.Interop;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Revit.Commands;

[Transaction(TransactionMode.Manual)]
public class ValveRoomCommand : CommandBase
{
    protected override Result ExecuteInternal(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var view = ServiceContainer.GetService<ValveRoomView>();
        var viewModel = (ValveRoomViewModel)view.DataContext;
        var handler = new ValveRoomRequestHandler(ServiceContainer.GetService<CreateValveRoomUseCase>());
        // 창을 연 문서에서만 생성한다(docs/237 § B).
        handler.OwnerDocument = DocumentGuard.Capture(commandData.Application);
        var externalEvent = ExternalEvent.Create(handler);
        new WindowInteropHelper(view).Owner = commandData.Application.MainWindowHandle;
        // 드레인 피트 위치 피킹은 화면에 연결된 적이 없어 제거했다(docs/238 § F). 위치는 코너 선택으로만 정한다.

        // 콤보 변경 시 매개변수 조회(EditFamily)를 API 컨텍스트에서 돌린다(docs/238 § E). 창 수명 동안만 쓰고 Dispose하지 않는다 —
        // Closed(WPF 디스패처)에서 ExternalEvent를 Dispose하면 Revit이 죽는다(docs/157 § A).
        var actionHandler = new RevitActionHandler("ValveRoom");
        var actionEvent = ExternalEvent.Create(actionHandler);
        viewModel.RunInRevitContext = action =>
        {
            // 모델리스 창은 Revit 메인 스레드에서 만들어지므로 Invoke는 인라인 실행된다(아래 Refresh와 같은 전제).
            actionHandler.Enqueue(() => view.Dispatcher.Invoke(action));
            actionEvent.Raise();
        };
        viewModel.CreateModelAction = request =>
        {
            handler.PendingRequest = request;
            handler.Request.Make(ValveRoomRequestId.CreateModel);
            var status = externalEvent.Raise();
            // Pending = 이미 올려 둔 이벤트가 아직 안 돈 것 — 곧 실행되므로 거부가 아니다(docs/235 § A). 슬롯은 마지막 요청이 이긴다.
            if (status is not (ExternalEventRequest.Denied or ExternalEventRequest.TimedOut)) return;
            handler.PendingRequest = null;
            handler.Request.Take();
            TaskDialog.Show("밸브실", RejectedMessage(status));
        };

        // 창이 떠 있는 동안 패밀리가 로드·편집되면 콤보 목록을 자동 갱신한다(docs/154 § C, docs/157 § B).
        // 구독 자체는 App.OnStartup이 세션당 1회 건다. 여기서는 "지금 열린 창"만 등록한다.
        // 모델리스 창은 Revit 메인 스레드에서 만들어지므로 Invoke는 인라인 실행된다 — ExternalEvent의 API 컨텍스트를 그대로 쓴다.
        ValveRoomRefreshSubscription.Attach(
            commandData.Application, view, () => view.Dispatcher.Invoke(viewModel.ReloadTypeNames));
        // Closed는 WPF 디스패처에서 돈다 — Revit API 컨텍스트가 아니므로 여기서 Revit을 부르면 Revit이 죽는다(docs/157 § A).
        view.Closed += (_, _) => ValveRoomRefreshSubscription.Detach(view);

        view.Show();
        // 최초 표시에도 창을 활성화한다 — WPF 포커스/IME 초기화의 전제(docs/182 § 2-2).
        view.Activate();
        return Result.Succeeded;
    }

    /// <summary>ExternalEvent.Raise()가 Denied/TimedOut일 때의 안내(docs/205 § C-1).</summary>
    private static string RejectedMessage(ExternalEventRequest status)
        => $"[{ErrorCodes.ExternalEventRejected}] Revit이 요청을 받지 않았습니다({status}). 열려 있는 대화상자나 명령을 끝낸 뒤 다시 시도하세요.";
}
