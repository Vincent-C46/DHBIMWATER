using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Revit.Commands;

/// <summary>모델리스 창이 연 문서에서만 요청을 실행하게 한다(docs/237 § A).
/// <para>Repo는 <c>Func&lt;Document?&gt;</c>로 호출 시점의 활성 문서를 읽는다(CLAUDE.md). 창을 연 뒤 다른 문서를 활성화하면
/// 생성·저장·삭제가 그 문서에서 실행된다 — 요청 경계(ExternalEvent 핸들러·RevitDispatcher)에서 한 번 막는다.</para></summary>
internal static class DocumentGuard
{
    /// <summary>창을 연 문서. 창을 열 때 활성 문서가 없으면 null — 그 경우 각 기능이 이미 SYS-101로 막는다.</summary>
    public static Document? Capture(UIApplication app) => app.ActiveUIDocument?.Document;

    public static void EnsureOwner(Document? owner, UIApplication app) => EnsureOwner(owner, app.ActiveUIDocument?.Document);

    public static void EnsureOwner(Document? owner, Document? active)
    {
        if (owner is null) return;
        if (!owner.IsValidObject)
            throw new DhbimwaterException(ErrorCodes.ActiveDocumentChanged, "창을 연 문서가 닫혔습니다. 창을 닫고 다시 여세요.");
        if (active is null || !owner.Equals(active))
            throw new DhbimwaterException(ErrorCodes.ActiveDocumentChanged,
                $"창을 연 문서('{owner.Title}')가 활성 상태가 아닙니다. 그 문서로 돌아가 다시 시도하세요.");
    }
}
