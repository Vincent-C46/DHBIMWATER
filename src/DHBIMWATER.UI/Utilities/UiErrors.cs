using DHBIMWATER.Infrastructure.Logging;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.UI.Utilities;

/// <summary>UI 계층(ViewModel)에서 잡은 예외를 사용자 문구와 로그로 남긴다(docs/240 § A).
/// <para>종전에는 창 안 문구·경고창이 <c>ex.Message</c>만 보여 [PA-xxx]/[PL-xxx] 코드가 빠지고 로그도 남지 않았다 —
/// 오류코드표(docs/00)의 "최종 표시 = `[코드] 메시지`" 규약이 Revit 핸들러 경로에서만 지켜졌다.</para>
/// <para>Revit 쪽 <c>ErrorPresenter</c>와 같은 규칙(<see cref="ErrorText.Format"/>)과 같은 로그 파일을 쓴다.</para></summary>
public static class UiErrors
{
    /// <summary>로그를 남기고 `[코드] 메시지`를 돌려준다. 로그 실패는 삼킨다 — 오류 표시가 로그 때문에 막히면 안 된다.</summary>
    public static string Report(string feature, Exception ex)
    {
        var text = ErrorText.Format(ex);
        try { LogManager.Logger.Error($"{feature} {text}", ex); }
        catch { /* 로그 파일 잠김 등 — 표시가 우선이다 */ }
        return text;
    }
}
