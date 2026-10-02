namespace DHBIMWATER.Core.Piping;

/// <summary>
/// 밸브실 부재 하나의 접합 방식을 정한다(docs/170 § A).
/// <para><b>왜 소켓/플랜지 두 값인가</b> — 산출서가 요구하는 축이 그것뿐이다. 소켓일 때 용접이냐 KP냐
/// 이탈방지압륜이냐는 <b>관종 + 압륜 플래그</b>가 정하며, 그 표는 계획관로에 이미 있다
/// (<see cref="PipeAlignmentParameters.JointKindFor"/>). 그래서 새 enum을 만들지 않고
/// <see cref="BendConnection"/>을 재사용한다 — 두 경로가 같은 어휘를 쓰면 표가 갈릴 여지가 없다.</para>
/// <para><b>우선순위</b>는 패밀리 파라미터 → 부재명 표기 → 폴백이며, 위에서 정해지면 아래는 보지 않는다.
/// <see cref="ParameterNames"/>가 비어 있으면 아래 둘만 동작해 docs/170 이전과 같은 결과가 나온다.</para>
/// </summary>
public static class PipeJointResolution
{
    /// <summary>접합 방식을 담고 있는 패밀리 파라미터 이름. 인스턴스를 먼저 보고 없으면 유형에서 찾는다.
    /// <para><b>docs/170 § B 조사 결과로 채운다.</b> 비어 있는 동안에는 순위 2·3만 동작하며,
    /// 그것이 docs/170 이전의 동작과 같다 — 조사가 끝나기 전에 구현을 막지 않으려는 의도다.</para></summary>
    public static IReadOnlyList<string> ParameterNames { get; } = [];

    /// <summary>값 어디엔가 들어 있으면 그 접합으로 보는 낱말. 두 글자 이상만 담는다 —
    /// 한 글자는 <see cref="ExactSocketTokens"/>에서 정확히 일치할 때만 본다.</summary>
    private static readonly string[] SocketWords = ["소켓", "Socket", "메커니컬", "타이튼", "용접", "Weld", "KP"];
    private static readonly string[] FlangeWords = ["플랜지", "Flange"];

    /// <summary>값 전체가 이것과 같을 때만 접합으로 읽는 짧은 표기.
    /// <para>포함 판정으로 돌리면 <c>Stainless</c>가 소켓(S)으로, <c>FLEX</c>가 플랜지(FL)로 잡힌다.</para></summary>
    private static readonly string[] ExactSocketTokens = ["S", "SO"];
    private static readonly string[] ExactFlangeTokens = ["F", "FL"];

    /// <summary>패밀리 파라미터 값 → 접합 방식. <b>해석하지 못하면 null</b>이다.
    /// <para>여기서 기본값을 지어내면 산출서가 조용히 틀린다 —
    /// <see cref="PipeAlignmentParameters.MaterialFromCode"/>가 null을 돌려주는 것과 같은 태도다.</para>
    /// <para>소켓 낱말과 플랜지 낱말이 <b>함께</b> 잡히는 값도 null이다. `1S1F`처럼 두 접합이 섞인 표기를
    /// 한쪽으로 몰면 그게 어느 쪽이든 절반은 틀린다 — 모르는 것은 모른다고 두고 폴백에 맡긴다.</para></summary>
    public static BendConnection? ParseConnection(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();

        var socket = ExactSocketTokens.Any(x => string.Equals(text, x, StringComparison.OrdinalIgnoreCase))
                     || SocketWords.Any(x => text.Contains(x, StringComparison.OrdinalIgnoreCase));
        var flange = ExactFlangeTokens.Any(x => string.Equals(text, x, StringComparison.OrdinalIgnoreCase))
                     || FlangeWords.Any(x => text.Contains(x, StringComparison.OrdinalIgnoreCase));

        if (socket == flange) return null;   // 둘 다 잡혔거나 하나도 못 잡았다
        return socket ? BendConnection.Socket : BendConnection.Flanged;
    }

    /// <param name="familyParameterValue"><see cref="ParameterNames"/>에서 읽어 온 값. 없으면 null.</param>
    /// <param name="partName">이 부재에 <b>이미 찍혀 있는</b> <c>DH_부재명칭</c>.
    /// 새로 만들 이름이 아니라 기존 값이어야 한다 — 방금 붙인 표기를 다시 읽으면 순환이다(docs/170 § C-⚠2).</param>
    /// <param name="fallback">위 둘로 못 정했을 때 쓸 값. 신규 배치는 창에서 고른 값, 재기록은 플랜지다.</param>
    public static BendConnection Resolve(string? familyParameterValue, string? partName, BendConnection fallback)
        => ParseConnection(familyParameterValue)
           ?? (HasSocketMark(partName) ? BendConnection.Socket : (BendConnection?)null)
           ?? fallback;

    /// <summary>부재명 앞머리의 접합 표기(`1S`·`2F`·`1S1F`)에 소켓(S)이 있는지.
    /// <para>표기는 숫자+문자 쌍이 이어지는 형태이며 이름 맨 앞에만 온다. 본문의 `S`(예: `STS 단관`)를
    /// 잘못 집지 않도록 <b>앞머리 토큰만</b> 본다. docs/166 §11-2에서 옮겨 온 규칙이며 동작은 그대로다.</para></summary>
    public static bool HasSocketMark(string? partName)
    {
        if (string.IsNullOrWhiteSpace(partName)) return false;
        var socket = false;
        var index = 0;
        while (index < partName.Length && char.IsDigit(partName[index]))
        {
            while (index < partName.Length && char.IsDigit(partName[index])) index++;
            if (index >= partName.Length) return false;
            var mark = char.ToUpperInvariant(partName[index]);
            if (mark == 'S') socket = true;
            else if (mark != 'F') return false;   // 숫자 뒤가 S도 F도 아니면 접합 표기가 아니다
            index++;
        }
        return socket;
    }
}
