using Autodesk.Revit.UI;

namespace DHBIMWATER.Revit.Commands;

/// <summary>모델리스 창이 Revit API 컨텍스트에서 실행할 동작을 큐로 받는 범용 핸들러(docs/238 § E).
/// <para>첫 사용처: 밸브실 드레인 피트 Void 콤보 변경 → 매개변수 조회(EditFamily). WPF 디스패처에서 부르면 API 컨텍스트 밖이다.</para>
/// <para>생성용 핸들러와 슬롯을 나누는 이유는 <see cref="ValveRoomRefreshRequestHandler"/>와 같다 — 대기 중인 생성 요청을 덮지 않게.</para>
/// <para>실패는 로그만 남긴다 — 조회 실패로 창이 죽으면 안 된다.</para></summary>
internal sealed class RevitActionHandler : IExternalEventHandler
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _actions = new();
    private readonly string _feature;

    public RevitActionHandler(string feature) => _feature = feature;

    public void Enqueue(Action action) => _actions.Enqueue(action);

    /// <summary>Raise 거부(Denied/TimedOut) 시 큐 비움 — 다음 Raise 때 묵은 요청이 돌지 않게(docs/295 § A).</summary>
    public void Clear() => _actions.Clear();

    public void Execute(UIApplication app)
    {
        while (_actions.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception ex) { ErrorPresenter.Log(_feature, ex); }
        }
    }

    public string GetName() => $"{_feature}Action";
}
