namespace DHBIMWATER.Core.Piping;

/// <summary>이음관 관련 상수(docs/167, docs/175 § C).
/// <para>이음관은 직관과 같은 배관 이음쇠(<c>OST_PipeFitting</c>) 카테고리라 카테고리로는 직관과 구분되지 않는다.
/// 정방향(캔버스 콤보 필터)과 역방향(피킹 판정)이 <b>같은 이 상수</b>를 봐야 한다 —
/// 콤보에서 키워드가 없는 패밀리를 고를 수 있으면 역가져오기가 그것을 직관으로 오인한다.</para>
/// <para>docs/175 § C부터 이음관은 밸브의 속성이 아니라 밸브와 같은 <b>독립 인라인 부속</b>이다.
/// <see cref="TypeKey"/>가 밸브("배관 밸브류")와 갈라 주는 유일한 표식이다.</para></summary>
public static class InlineJoint
{
    /// <summary>이음관 배치 정의의 <c>TypeKey</c>. 캔버스 밸브의 "배관 밸브류"와 구분하는 값이다.</summary>
    public const string TypeKey = "이음관";

    /// <summary>이음관 패밀리를 가리는 패밀리명 키워드. 사용자가 정한 규칙이다(docs/167 § 1).</summary>
    public const string FamilyKeyword = "이음관";

    /// <summary>"패밀리명 : 유형명" 또는 패밀리명만 받아도 된다 — 이름 어디든 키워드가 있으면 이음관이다.</summary>
    public static bool IsJointFamily(string? familyTypeName)
        => !string.IsNullOrWhiteSpace(familyTypeName)
           && familyTypeName.Contains(FamilyKeyword, StringComparison.Ordinal);
}
