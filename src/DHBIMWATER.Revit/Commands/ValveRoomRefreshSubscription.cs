using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using DHBIMWATER.Application.UseCases.AutoGenerator;

namespace DHBIMWATER.Revit.Commands;

/// <summary>밸브실 창의 유형 목록 자동 갱신용 <c>DocumentChanged</c> 구독 지점(docs/157 § C-1).
/// <para>구독 자체는 <see cref="App"/>이 세션당 1회만 걸고(`OnStartup`/`OnShutdown`), 여기서는
/// "지금 열려 있는 밸브실 창"만 슬롯에 담는다.</para>
/// <para><b>창을 닫을 때 Revit API를 부르면 안 된다.</b> WPF <c>Window.Closed</c>는 디스패처에서 돌며
/// Revit API 컨텍스트가 아니라, 거기서 <c>DocumentChanged -=</c>나 <c>ExternalEvent.Dispose()</c>를
/// 부르면 Revit이 강제 종료된다(docs/157 § A). 그래서 <see cref="Detach"/>는 필드 대입만 한다.</para></summary>
internal static class ValveRoomRefreshSubscription
{
    private static readonly ValveRoomRefreshRequestHandler _handler = new();

    // 세션 수명. 한 번 만들면 Dispose하지 않는다 — 창마다 만들고 닫을 때 Dispose하던 것이 크래시 경로였다(docs/157 § A-3).
    private static ExternalEvent? _refreshEvent;

    private static UIApplication? _uiApp;
    private static object? _owner;

    /// <summary>열려 있는 밸브실 창을 갱신 대상으로 등록한다.
    /// <para><see cref="ExternalEvent.Create"/>를 부르므로 <b>반드시 Revit API 컨텍스트</b>(명령 Execute)에서 호출한다.</para></summary>
    /// <param name="owner">창 식별 토큰. <see cref="Detach"/>에 같은 인스턴스를 넘긴다.</param>
    /// <param name="refresh">이미 UI 디스패처로 마샬링된 갱신 동작.</param>
    public static void Attach(UIApplication uiApp, object owner, Action refresh)
    {
        _refreshEvent ??= ExternalEvent.Create(_handler);
        _uiApp = uiApp;
        _owner = owner;
        _handler.Refresh = refresh;
    }

    /// <summary>창이 닫혔음을 알린다. <b>Revit API를 부르지 않는다</b> — WPF <c>Closed</c>에서 호출되기 때문이다.</summary>
    public static void Detach(object owner)
    {
        // 창을 닫자마자 다시 연 경우, 옛 창의 Closed가 새로 등록된 슬롯을 지우면 안 된다.
        if (!ReferenceEquals(_owner, owner)) return;
        _owner = null;
        _uiApp = null;
        _handler.Refresh = null;
    }

    /// <summary><see cref="App"/>이 세션 내내 물고 있는 <c>DocumentChanged</c> 핸들러(docs/157 § C-2).</summary>
    public static void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        var uiApp = _uiApp;
        var refreshEvent = _refreshEvent;
        // 밸브실 창이 닫혀 있으면 아무 일도 하지 않는다.
        if (_owner is null || uiApp is null || refreshEvent is null) return;

        // 이 콜백에서는 문서를 열 수 없다(갱신이 EditFamily를 부른다). 예약만 하고 실제 갱신은 ExternalEvent에서 한다.
        if (HasFamilyChange(uiApp, e)) refreshEvent.Raise();
    }

    /// <summary>활성 문서에서 패밀리나 패밀리 유형이 추가·수정됐는지. 벽·바닥만 바뀐 트랜잭션에서 EditFamily가 다시 도는 것을 막는다.</summary>
    private static bool HasFamilyChange(UIApplication uiApp, DocumentChangedEventArgs e)
    {
        var doc = e.GetDocument();
        // Repo가 활성 문서만 읽으므로(Func<Document?>) 다른 프로젝트·링크 문서의 변경은 무시한다.
        if (doc == null || !doc.Equals(uiApp.ActiveUIDocument?.Document)) return false;
        // 우리 트랜잭션은 Activate·Duplicate로 FamilySymbol을 건드리므로 생성마다 창이 EditFamily를 다시 돌렸다(docs/207 § 0-4).
        if (e.GetTransactionNames().Any(name => string.Equals(name, CreateValveRoomUseCase.TransactionName, StringComparison.Ordinal))) return false;
        // 전체 요소를 GetElement로 되짚지 않고 클래스 필터로 Revit에게 맡긴다.
        var filter = new ElementMulticlassFilter(new List<Type> { typeof(Family), typeof(FamilySymbol) });
        return e.GetAddedElementIds(filter).Count > 0 || e.GetModifiedElementIds(filter).Count > 0;
    }
}
