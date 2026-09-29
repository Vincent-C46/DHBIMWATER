using Autodesk.Revit.UI;

namespace DHBIMWATER.Revit.Commands;

/// <summary>밸브실 창의 유형 목록 갱신 전용 핸들러(docs/154 § C-1).
/// <para>생성·피킹용 <see cref="ValveRoomRequestHandler"/>와 요청 슬롯을 공유하면, 생성 직후 우리 트랜잭션이
/// 일으킨 DocumentChanged가 대기 중인 CreateModel 요청을 덮어써 밸브실이 만들어지지 않는다.
/// 그래서 이벤트와 핸들러를 따로 둔다.</para></summary>
internal sealed class ValveRoomRefreshRequestHandler : IExternalEventHandler
{
    public Action? Refresh { get; set; }

    public void Execute(UIApplication uiapp)
    {
        // 목록 갱신 실패로 창이 죽지 않게 삼킨다. 갱신은 다음 패밀리 로드 때 다시 시도된다.
        try { Refresh?.Invoke(); }
        catch (Exception ex) { ErrorPresenter.Log("ValveRoom.Refresh", ex); }
    }

    public string GetName() => "ValveRoomRefreshRequest";
}
