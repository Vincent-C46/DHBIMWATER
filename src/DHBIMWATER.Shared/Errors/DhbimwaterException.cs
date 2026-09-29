namespace DHBIMWATER.Shared.Errors;

/// <summary>사용자에게 코드와 함께 보여줄 수 있는 애드인 오류(docs/205 § A).
/// <para>Revit·.NET 예외와 구분하는 것이 목적이다 — 핸들러는 이 타입이면 "[코드] 메시지"로,
/// 아니면 "[SYS-999] 원문"으로 보여준다(<c>ErrorPresenter</c>). 코드는 <see cref="ErrorCodes"/>에서만 고른다.</para>
/// <para><see cref="InvalidOperationException"/>을 상속하는 이유: 기존 코드의 <c>catch (InvalidOperationException)</c>과
/// 테스트의 <c>Assert.Throws&lt;InvalidOperationException&gt;</c>이 그대로 성립해야 한다(docs/205 § A-1).</para></summary>
public sealed class DhbimwaterException : InvalidOperationException
{
    public string Code { get; }

    public DhbimwaterException(string code, string message, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
    }

    /// <summary>"[PL-201] 메시지" — 대화상자 제목·상태줄·로그가 같은 모양을 쓴다.</summary>
    public string Display => $"[{Code}] {Message}";
}
