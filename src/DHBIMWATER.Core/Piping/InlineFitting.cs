namespace DHBIMWATER.Core.Piping;

/// <summary>엣지 내부에 순서대로 배치되는 부속품이다.</summary>
/// <param name="TypeKey">부속 종류. 밸브는 "배관 밸브류", 이음관은 <see cref="InlineJoint.TypeKey"/>다.
/// 이음관은 docs/175 § C부터 밸브의 속성이 아니라 <b>독립 부속</b>이며, 이 키로만 구분한다.</param>
/// <param name="FamilyTypeName">배치할 FamilySymbol("패밀리명 : 타입명"). 사용자가 아직 선택하지 않았으면 빈 문자열.</param>
/// <param name="Bypass">바이패스 세트일 때만 값이 있다(docs/228 § A). 이때 <paramref name="FamilyTypeName"/>은 본관 밸브다.</param>
public sealed record InlineFitting(Guid Id, string TypeKey, string FamilyTypeName, double T, int Order, BypassSetSpec? Bypass = null)
{
    public InlineFitting(string typeKey, string familyTypeName, double t, int order)
        : this(Guid.NewGuid(), typeKey, familyTypeName, Math.Clamp(t, 0, 1), order) { }

    /// <summary>이음관인지. 배치 경계·캔버스 표식·재료표가 이 하나를 본다(docs/175 § C-1).</summary>
    public bool IsJoint => string.Equals(TypeKey, InlineJoint.TypeKey, StringComparison.Ordinal);

    /// <summary>바이패스 세트인지(docs/228). TypeKey와 입력이 함께 있어야 한다.</summary>
    public bool IsBypassSet => Bypass is not null && string.Equals(TypeKey, BypassSet.TypeKey, StringComparison.Ordinal);
}
