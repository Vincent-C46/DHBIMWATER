namespace DHBIMWATER.Core.Piping;

/// <summary>바이패스 세트 한 개의 입력(docs/228 § A). 본관 위 인라인 부속(<see cref="InlineFitting"/>)에 실린다.
/// 본관 밸브는 세트가 아니다(docs/230) — 세트 부속의 <see cref="InlineFitting.FamilyTypeName"/>은 빈 문자열이고,
/// A–A 사이 밸브는 사용자가 일반 밸브로 놓는다.</summary>
/// <param name="BypassValveFamilyTypeName">B–B 사이 바이패스 밸브("패밀리명 : 유형명").</param>
/// <param name="BypassDiameterMm">바이패스 관경(mm). 밸브부관 A 분기·B·바이패스 밸브·채움관이 이 값을 쓴다.</param>
/// <param name="Side">+1 = 엣지 진행 방향(시작→끝)의 왼쪽(반시계 90°), -1 = 오른쪽. 바이패스가 이쪽으로 빠진다.</param>
public sealed record BypassSetSpec(string BypassValveFamilyTypeName, double BypassDiameterMm, int Side)
{
    /// <summary>0이나 이상값은 왼쪽으로 본다. 부호만 뜻이 있다.</summary>
    public int NormalizedSide => Side < 0 ? -1 : 1;
}

/// <summary>바이패스 세트 규칙 모음(docs/228).
/// <para>세트 = 밸브부관 A 2개 + 밸브부관 B 2개 + 바이패스 밸브(B–B 사이) + 바이패스 쪽 틈 채움 단관(docs/230).
/// A–A 사이 본관은 세트가 아니다 — 관으로 잇고, 밸브는 사용자가 일반 밸브로 놓는다.
/// 치수는 패밀리가 호칭지름으로 정하므로 캔버스는 개략 모양만 그리고, 실제 자리는 Revit 배치가 커넥터를 재서 정한다.</para></summary>
public static class BypassSet
{
    /// <summary>캔버스 인라인 부속의 TypeKey. 밸브("배관 밸브류")·이음관과 가르는 표식이다.</summary>
    public const string TypeKey = "바이패스 세트";

    /// <summary>캔버스 개략 표시용 — 세트 중심에서 A 중심까지(mm). 실제 값이 아니다(Revit 실측이 정한다).</summary>
    public static double EstimatedHalfSpanMm(double mainDiameterMm, double bypassDiameterMm)
        => Math.Max(500, mainDiameterMm * 0.9 + bypassDiameterMm);

    /// <summary>캔버스 개략 표시용 — 본관 중심선에서 바이패스 관 중심선까지(mm).</summary>
    public static double EstimatedOffsetMm(double mainDiameterMm, double bypassDiameterMm)
        => Math.Max(400, mainDiameterMm * 0.5 + bypassDiameterMm * 2);
}
