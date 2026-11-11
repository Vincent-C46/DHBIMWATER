using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DHBIMWATER.Revit.DependencyInjection;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.UseCases.AutoGenerator;
using DHBIMWATER.Application.UseCases.Gis;
using DHBIMWATER.Application.Interfaces.Piping;
using DHBIMWATER.UI.Views.Modeling;
using System.Windows.Interop;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Revit.Commands;

[Transaction(TransactionMode.Manual)]
public class PipeLayoutCommand : CommandBase
{
    protected override Result ExecuteInternal(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var catalog = ServiceContainer.GetService<IPipeCatalogSource>();
        if (!catalog.TryEnsureAvailable(out var catalogError))
        {
            TaskDialog.Show("관로 규격표", catalogError);
            return Result.Cancelled;
        }
        if (!string.IsNullOrEmpty(catalogError)) TaskDialog.Show("관로 규격표", catalogError);
        var migration = ServiceContainer.GetService<MigratePipeTablesUseCase>().Execute();
        if (!string.IsNullOrEmpty(migration.Error))
            TaskDialog.Show("관로 규격표", $"기존 규격표를 가져오지 못했습니다: {migration.Error}");
        var view = ServiceContainer.GetService<PipeLayoutView>();
        var useCase = ServiceContainer.GetService<CreateValvePipingUseCase>();
        var outlinePick = ServiceContainer.GetService<IValveRoomOutlinePickRepo>();
        var pipingPick = ServiceContainer.GetService<IValveRoomPipingPickRepo>();
        var saveFamilySettings = ServiceContainer.GetService<SavePipeLayoutFamilySettingsUseCase>();
        var handler = new PipeLayoutRequestHandler(useCase, outlinePick, pipingPick, saveFamilySettings,
            ServiceContainer.GetService<IVerticalBranchPickRepo>(), ServiceContainer.GetService<PlaceVerticalBranchUseCase>())
        {
            // 창을 연 문서에서만 생성·피킹·저장한다(docs/237 § B).
            OwnerDocument = DocumentGuard.Capture(commandData.Application),
        };
        var externalEvent = ExternalEvent.Create(handler);
        var viewModel = (DHBIMWATER.UI.ViewModels.Modeling.PipeLayoutViewModel)view.DataContext;

        viewModel.CreateModelAction = network =>
        {
            handler.PendingNetwork = network;
            // 모델을 만든 값 그대로 프로젝트에 남긴다(docs/195 § D). [설정…] 창의 [저장]을 누르지 않아도 다음 세션에 돌아온다 —
            // 본창 §4로 옮긴 밸브·이음관 구경 파라미터에는 누를 버튼이 곁에 없기 때문이다. 스냅은 대상이 아니다(세션 한정).
            // 여기서 떠 두는 이유: 핸들러는 Revit 유휴 시점에 도는데 그때 창 값이 이미 바뀌어 있을 수 있다.
            handler.PendingNetworkFamilySettings = viewModel.ToFamilySettings();
            handler.Request.Make(PipeLayoutRequestId.CreateModel);
            var raiseResult = externalEvent.Raise();
            if (IsRejected(raiseResult))
            {
                handler.PendingNetwork = null;
                handler.PendingNetworkFamilySettings = null;
                viewModel.ApplyModelCreationResult(false, RejectedMessage(raiseResult), []);
            }
        };

        // 캔버스에서 고른 배관·부속을 Revit에서 선택하고 해당 뷰로 옮긴다(docs/179 § F-4).
        // 수량산출 애드인 QuantityCommand.cs:60-65와 같은 모양이다.
        viewModel.SelectInRevitAction = ids =>
        {
            handler.ElementIdsToSelect = ids;
            handler.Request.Make(PipeLayoutRequestId.SelectInRevit);
            var status = externalEvent.Raise();
            if (!IsRejected(status)) return;
            handler.ElementIdsToSelect = [];
            handler.Request.Clear();
            viewModel.ApplyRequestError(RejectedMessage(status));   // "생성 실패" 접두사·생성 상태 초기화 없이(docs/239 § C)
        };

        // 피킹 중에는 Revit 뷰를 봐야 하므로 창을 숨기고, 결과를 받은 뒤 UI 스레드에서 다시 띄운다.
        viewModel.PickOutlineAction = () =>
        {
            view.Hide();
            handler.Request.Make(PipeLayoutRequestId.PickOutline);
            var status = externalEvent.Raise();
            if (!IsRejected(status)) return;
            // 거부되면 OutlinePicked가 오지 않아 창이 숨겨진 채 남는다(docs/205 § 0-2).
            handler.Request.Clear();
            view.Show(); view.Activate();
            viewModel.ApplyRequestError(RejectedMessage(status));   // "생성 실패" 접두사·생성 상태 초기화 없이(docs/239 § C)
        };
        handler.OutlinePicked = outline => view.Dispatcher.Invoke(() =>
        {
            view.Show();
            view.Activate();
            viewModel.ApplyOutline(outline);
            if (outline is not null) handler.PendingLayoutSettings = viewModel.ToFamilySettings();
        });

        // 기존 배관 가져오기도 같은 왕복이다 — 피킹 동안 창을 숨기고, 결과를 UI 스레드에서 반영한다(docs/158 § G-3).
        viewModel.PickPipingAction = () =>
        {
            view.Hide();
            handler.Request.Make(PipeLayoutRequestId.PickPiping);
            var status = externalEvent.Raise();
            if (!IsRejected(status)) return;
            handler.Request.Clear();
            view.Show(); view.Activate();
            viewModel.ApplyRequestError(RejectedMessage(status));   // "생성 실패" 접두사·생성 상태 초기화 없이(docs/239 § C)
        };
        handler.PipingPicked = import => view.Dispatcher.Invoke(() =>
        {
            view.Show();
            view.Activate();
            viewModel.ApplyPickedPiping(import);
        });
        // 수직분기(docs/301) — 시작 부속 피킹은 기존 배관 가져오기와 같은 왕복. 입면 창은 피킹 결과를 받은 뒤 UI 스레드에서 연다.
        viewModel.PickVerticalBranchAction = () =>
        {
            view.Hide();
            handler.Request.Make(PipeLayoutRequestId.PickVerticalBranchStart);
            var status = externalEvent.Raise();
            if (!IsRejected(status)) return;
            handler.Request.Clear();
            view.Show(); view.Activate();
            viewModel.ApplyRequestError(RejectedMessage(status));
        };
        // 모달 입면 창을 이 콜백 안에서 바로 열면 ExternalEvent 실행 중에 막힘 — BeginInvoke로 핸들러가 끝난 뒤 연다.
        handler.VerticalBranchStartPicked = (start, error) => view.Dispatcher.BeginInvoke(() =>
        {
            view.Show();
            view.Activate();
            viewModel.ApplyVerticalBranchStart(start, error);
        });
        viewModel.PlaceVerticalBranchAction = request =>
        {
            handler.PendingVerticalBranch = request;
            handler.Request.Make(PipeLayoutRequestId.PlaceVerticalBranch);
            var status = externalEvent.Raise();
            if (!IsRejected(status)) return;
            handler.PendingVerticalBranch = null;
            handler.Request.Clear();
            viewModel.ApplyVerticalBranchResult(false, RejectedMessage(status));
        };
        handler.VerticalBranchPlaced = (succeeded, resultMessage) => view.Dispatcher.Invoke(() =>
            viewModel.ApplyVerticalBranchResult(succeeded, resultMessage));
        // 피킹이 예외로 끝나면 "취소" 대신 [코드] 문구를 상태줄에(docs/239 § C).
        handler.RequestFailed = error => view.Dispatcher.Invoke(() => viewModel.ApplyRequestError(error));
        handler.ModelCreationCompleted = (succeeded, resultMessage, failures, placedElementIds) => view.Dispatcher.Invoke(() =>
            viewModel.ApplyModelCreationResult(succeeded, resultMessage, failures, placedElementIds));
        // [Revit에서 보기] 실제 선택 수를 상태줄에 반영한다(docs/216 § B-2).
        handler.ElementsSelected = (requested, selected) => view.Dispatcher.Invoke(() =>
            viewModel.ApplySelectInRevitResult(requested, selected));

        // 타이밸브실 최소 폭 실측(docs/323) — 프로브 놓고 롤백하는 트랜잭션이라 API 컨텍스트 필요. 생성 핸들러 슬롯과 안 겹치게 별도 핸들러.
        // ExternalEvent는 창 수명 동안 쓰고 Dispose 안 함(ValveRoomCommand와 같은 이유, docs/157 § A).
        var actionHandler = new RevitActionHandler("PipeLayout");
        var actionEvent = ExternalEvent.Create(actionHandler);
        viewModel.RunInRevitContext = action =>
        {
            actionHandler.Enqueue(() => view.Dispatcher.Invoke(action));
            var status = actionEvent.Raise();
            if (!IsRejected(status)) return;
            actionHandler.Clear();
            viewModel.ApplyRequestError(RejectedMessage(status));
        };
        var measureTie = ServiceContainer.GetService<MeasureTieValveRoomUseCase>();
        viewModel.MeasureTieLayout = (diameterMm, material, steelClass) => measureTie.Execute(diameterMm, material, steelClass);

        // 패밀리 설정 저장은 DataStorage 쓰기(트랜잭션)라 API 컨텍스트가 필요하다 — 모델리스 창이므로 ExternalEvent로 보낸다(docs/175 § E-6).
        // 설정 창은 모달(ShowDialog)이라 요청은 창이 닫힌 뒤에야 실행된다 — ViewModel이 [저장]에서 창을 닫고, 결과는 아래 콜백으로 본창 상태줄에 찍는다.
        viewModel.SaveFamilySettingsAction = settings =>
        {
            handler.PendingFamilySettings = settings;
            handler.Request.Make(PipeLayoutRequestId.SaveFamilySettings);
            var raiseResult = externalEvent.Raise();
            if (IsRejected(raiseResult))
            {
                handler.PendingFamilySettings = null;
                viewModel.ApplyFamilySettingsSaveResult(false, RejectedMessage(raiseResult));
            }
        };
        handler.FamilySettingsSaved = (succeeded, resultMessage) => view.Dispatcher.Invoke(() =>
            viewModel.ApplyFamilySettingsSaveResult(succeeded, resultMessage));

        // 레이아웃 저장은 [닫기] 버튼이 아니라 Closing에 건다 — 타이틀바 X(TitleBar.xaml.cs의 _parentWindow.Close())와
        // Alt+F4도 같은 저장을 거치게 한다(docs/235 § E). 피킹 중의 view.Hide()는 Closing이 아니다.
        // CloseAction은 PipeLayoutView 생성자가 넣은 Close 그대로 둔다.
        view.Closing += (_, _) =>
        {
            handler.PendingLayoutSettings = viewModel.ToFamilySettings();
            handler.Request.Make(PipeLayoutRequestId.SaveLayout);
            var status = externalEvent.Raise();
            if (IsRejected(status))
            {
                // 창은 어차피 닫힌다 — 거부된 요청만 거두고 로그로 남긴다(docs/205 § C-2, 204 § E-3).
                handler.PendingLayoutSettings = null;
                handler.Request.Clear();
                ErrorPresenter.Log("PipeLayout", new DhbimwaterException(ErrorCodes.ExternalEventRejected, RejectedMessage(status)));
            }
        };

        if (viewModel.SavedOutlineElementIds.Count > 0)
        {
            var restored = outlinePick.ResolveExistingObjects(viewModel.SavedOutlineElementIds, out var missingCount);
            viewModel.ApplyRestoredOutline(restored, missingCount);
        }

        new WindowInteropHelper(view).Owner = commandData.Application.MainWindowHandle;
        view.Show();
        return Result.Succeeded;
    }

    /// <summary>Denied/TimedOut만 거부다. Pending은 이미 올려 둔 이벤트가 아직 안 돈 것이고, 핸들러가 큐를 비울 때까지
    /// 돌므로(PipeLayoutRequestHandler.Execute) 방금 넣은 요청도 실행된다(docs/235 § A). RevitDispatcher와 같은 기준이다.</summary>
    private static bool IsRejected(ExternalEventRequest status)
        => status is ExternalEventRequest.Denied or ExternalEventRequest.TimedOut;

    /// <summary>ExternalEvent.Raise()가 Denied/TimedOut일 때의 안내(docs/205 § C-2).</summary>
    private static string RejectedMessage(ExternalEventRequest status)
        => $"[{ErrorCodes.ExternalEventRejected}] Revit이 요청을 받지 않았습니다({status}). 열려 있는 대화상자나 명령을 끝낸 뒤 다시 시도하세요.";
}
