using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DHBIMWATER.Shared.Errors;
using System.Windows;

namespace DHBIMWATER.Revit.Commands;

/// <summary>모델리스 모델링 창의 요청을 API 컨텍스트에서 실행(docs/320 § B). 선례: <see cref="ValveRoomCommand"/>.
/// <para>ExternalEvent는 창 수명 동안 쓰고 Dispose 안 함 — Closed(WPF 디스패처)에서 Dispose하면 Revit 죽음(docs/157 § A).</para></summary>
internal sealed class ModelessRevitRunner
{
    private readonly UIApplication _app;
    private readonly Document? _owner;
    private readonly string _feature;
    private readonly string _title;
    private readonly Window _view;
    private readonly RevitActionHandler _handler;
    private readonly ExternalEvent _event;
    private bool _createPending;

    public ModelessRevitRunner(UIApplication app, string feature, string title, Window view)
    {
        _app = app;
        _owner = DocumentGuard.Capture(app);
        _feature = feature;
        _title = title;
        _view = view;
        _handler = new RevitActionHandler(feature);
        _event = ExternalEvent.Create(_handler);
    }

    /// <summary>설정창 읽기·저장용. 실패는 실행 본문이 처리.</summary>
    public void Run(Action action) => Raise(action, null);

    /// <summary>모델 생성용. 대기 중 재요청 무시 — 핸들러가 큐라서 연타가 2회 생성됨. 창 연 문서에서만 실행.</summary>
    public void RunCreate(Action action)
    {
        if (_createPending) return;
        _createPending = true;
        Raise(() =>
        {
            try
            {
                DocumentGuard.EnsureOwner(_owner, _app);
                action();
            }
            catch (Exception ex) { ErrorPresenter.Show($"{_title} 생성 오류", _feature, ex); }
            finally { _createPending = false; }
        }, () => _createPending = false);
    }

    private void Raise(Action action, Action? onRejected)
    {
        // 모델리스 창은 Revit 메인 스레드에서 만들어짐 — Invoke 인라인 실행(ValveRoomCommand와 같은 전제)
        _handler.Enqueue(() => _view.Dispatcher.Invoke(action));
        var status = _event.Raise();
        // Pending = 이미 올린 이벤트가 곧 돎. 거부 아님(docs/235 § A)
        if (status is not (ExternalEventRequest.Denied or ExternalEventRequest.TimedOut)) return;
        _handler.Clear();
        onRejected?.Invoke();
        TaskDialog.Show(_title, $"[{ErrorCodes.ExternalEventRejected}] Revit이 요청을 받지 않았습니다({status}). 열려 있는 대화상자나 명령을 끝낸 뒤 다시 시도하세요.");
    }
}
