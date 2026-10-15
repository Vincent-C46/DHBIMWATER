using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DHBIMWATER.Revit.DependencyInjection;
using DHBIMWATER.UI.ViewModels.Documentation;
using DHBIMWATER.UI.Views.Documentation;
using System.Windows.Interop;

namespace DHBIMWATER.Revit.Commands;

[Transaction(TransactionMode.Manual)]
public class JunctionDetailCommand : CommandBase
{
    private const string Feature = "JunctionDetail";

    protected override Result ExecuteInternal(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        // 모델리스(docs/295). VM 생성자의 미리보기 읽기는 지금(명령 = API 컨텍스트)에서 돈다
        var view = ServiceContainer.GetService<JunctionDetailView>();
        var viewModel = (JunctionDetailViewModel)view.DataContext;
        var uiApp = commandData.Application;
        // 창 연 문서에서만 실행(docs/237 § B)
        var owner = DocumentGuard.Capture(uiApp);

        // 실행·초기화·선택을 API 컨텍스트에서 돌림. 창 수명 동안 쓰고 Dispose 안 함 —
        // Closed(WPF 디스패처)에서 ExternalEvent Dispose하면 Revit 죽음(docs/157 § A)
        var actionHandler = new RevitActionHandler(Feature);
        var actionEvent = ExternalEvent.Create(actionHandler);
        bool Raise(Action action)
        {
            // 모델리스 창은 Revit 메인 스레드에서 만들어짐 → Invoke 인라인 실행(ValveRoomCommand와 같은 전제)
            actionHandler.Enqueue(() => view.Dispatcher.Invoke(action));
            var status = actionEvent.Raise();
            // Pending = 이미 올린 이벤트가 곧 돎 → 거부 아님(docs/235 § A)
            if (status is not (ExternalEventRequest.Denied or ExternalEventRequest.TimedOut)) return true;
            actionHandler.Clear();
            return false;
        }

        viewModel.RunInRevitContext = Raise;
        // 문서 확인은 VM 실행 본문 try 안에서 → 불일치 안내가 결과창에 뜨고 실행 중 상태도 풀림(docs/295 § B-3)
        viewModel.EnsureDocument = () => DocumentGuard.EnsureOwner(owner, uiApp);
        viewModel.SelectInRevit = ids => Raise(() => SelectAndShow(uiApp, owner, ids));

        new WindowInteropHelper(view).Owner = uiApp.MainWindowHandle;
        view.Show();
        // 최초 표시도 활성화 — WPF 포커스/IME 초기화 전제(docs/182 § 2-2)
        view.Activate();
        return Result.Succeeded;
    }

    /// <summary>세트 부재 Revit 선택 + 화면 이동(docs/295 § C). PipeLayoutRequestHandler SelectInRevit 이식.
    /// 문서 변경 아님 → 트랜잭션 없음. 실패는 로그만.</summary>
    private static void SelectAndShow(UIApplication uiApp, Document? owner, IReadOnlyList<long> ids)
    {
        try
        {
            var uiDoc = uiApp.ActiveUIDocument;
            if (uiDoc is null) return;
            DocumentGuard.EnsureOwner(owner, uiDoc.Document);
            // 창 연 뒤 모델에서 지운 부재 거름 — 안 거르면 Revit 예외
            var elementIds = ids.Select(id => new ElementId(id))
                .Where(id => uiDoc.Document.GetElement(id) != null)
                .ToList();
            if (elementIds.Count == 0) return;
            uiDoc.Selection.SetElementIds(elementIds);
            uiDoc.ShowElements(elementIds);
        }
        catch (Exception ex) { ErrorPresenter.Log(Feature, ex); }
    }
}
