namespace DHBIMWATER.Shared.Errors;

/// <summary>예외를 "[코드] 메시지" 한 줄 규약으로 바꾼다(docs/205 § A-4).
/// <para>Revit 쪽 <c>ErrorPresenter.Format</c>과 같은 규칙을 UI 계층(상태줄·창 안 문구)에서도 쓰려고 Shared에 둔다(docs/235 § B, docs/240 § A).
/// <see cref="DhbimwaterException"/>은 자기 코드로, 그 외(Revit·.NET)는 <see cref="ErrorCodes.Unclassified"/>로 표시한다.</para></summary>
public static class ErrorText
{
    public static string Format(Exception ex)
        => ex is DhbimwaterException dh ? dh.Display : $"[{ErrorCodes.Unclassified}] {ex.Message}";
}
