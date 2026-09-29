namespace DHBIMWATER.Core.Piping;

/// <summary>부재 하나의 접합 구성 — 소켓 단부 수와 플랜지 단부 수.</summary>
public readonly record struct PipeJointEnds(int Socket, int Flange)
{
    public int Total => Socket + Flange;
    public bool IsEmpty => Socket <= 0 && Flange <= 0;
}

/// <summary>
/// 부재 하나의 접합 구성. <c>DH_접합구성</c> 한 칸에 <c>2S1F</c>(소켓 2 + 플랜지 1) 형태로 담는다.
/// <para><b>표기가 산출서 부재명 접두와 같다(2026-09-15 사용자 결정).</b> 처음에는 <c>KP:2+플랜지:1</c>처럼
/// 소켓의 세부 종류(용접·KP·이탈방지압륜)까지 실었는데, 사용자가 속성창에서 손으로 고치기에는 그 포맷이 어렵다.
/// 소켓의 세부 종류는 <b>관종 + 압륜</b>이 정하며 개소 매개변수 넷(<c>DH_용접합수</c>·<c>DH_KP접합수</c>…)이
/// 이미 그 구분을 담고 있으므로, 이 칸은 <b>형상(소켓/플랜지)과 단부 수</b>만 적는다.</para>
/// <para><b>왜 부재당 한 종류로 못 적는가</b> — <c>1S1F</c> 단관은 한쪽이 소켓, 반대쪽이 플랜지다.
/// T형관은 단부가 셋(<c>2S1F</c>), 십자관은 넷이라 스칼라 하나로는 담기지 않는다.</para>
/// <para><b>읽기는 너그럽다</b> — 순서가 바뀐 <c>1F2S</c>, 소문자, 공백, 그리고 구형 <c>KP:2+플랜지:1</c>
/// (2026-09-15 이전 기록)도 같은 값으로 읽는다. 이미 찍힌 모델을 깨지 않기 위해서다.</para>
/// </summary>
public static class PipeJointComposition
{
    /// <summary>형상 표기의 플랜지 문자. 나머지는 전부 소켓(<see cref="SocketMark"/>)이다.</summary>
    public const char FlangeMark = 'F';
    public const char SocketMark = 'S';

    /// <summary>구형 표기의 종류 이름(2026-09-15 이전 기록). 구형을 읽을 때만 쓴다 — 플랜지면 F, 나머지는 S.
    /// 이 어휘 밖의 조각은 버린다 — `2X` 같은 오타를 소켓 1로 지어내지 않기 위해서다.</summary>
    private const string LegacyFlangeKind = "플랜지";
    private static readonly string[] LegacyKinds = [LegacyFlangeKind, "용접", "KP", "이탈방지압륜"];
    private const char LegacyEntrySeparator = '+';
    private const char LegacyCountSeparator = ':';

    /// <summary><c>2S1F</c>. 소켓을 먼저 적고(산출서 표기 순서), 0인 쪽은 생략한다 — <c>3F</c>·<c>2S</c>.
    /// 둘 다 0이면 빈 문자열이다.</summary>
    public static string Encode(PipeJointEnds ends)
        => (ends.Socket > 0 ? $"{ends.Socket}{SocketMark}" : string.Empty)
           + (ends.Flange > 0 ? $"{ends.Flange}{FlangeMark}" : string.Empty);

    public static string Encode(int socket, int flange) => Encode(new PipeJointEnds(socket, flange));

    /// <summary><see cref="Encode(PipeJointEnds)"/>의 역함수. 해석하지 못하면 (0, 0)이다.
    /// <para>형상 표기는 <c>숫자+문자</c> 쌍의 나열이며 같은 문자가 여러 번 나오면 더한다. 숫자를 생략한
    /// <c>SF</c>는 각 1로 읽는다 — 사용자가 개수 없이 적는 것이 자연스럽기 때문이다.
    /// 형상 표기로 읽히지 않으면 구형 <c>종류:개수+종류:개수</c>(개수 생략 가능)로 읽는다.</para></summary>
    public static PipeJointEnds Decode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return default;
        var value = text.Trim();
        return TryDecodeShape(value, out var ends) ? ends : DecodeLegacy(value);
    }

    /// <summary><c>2S1F</c>·<c>1F2S</c>·<c>SF</c>. 숫자 뒤에 S·F 외의 문자가 오거나 숫자로 끝나면 표기가 아니다.</summary>
    private static bool TryDecodeShape(string value, out PipeJointEnds ends)
    {
        ends = default;
        var socket = 0;
        var flange = 0;
        var index = 0;
        while (index < value.Length)
        {
            if (char.IsWhiteSpace(value[index])) { index++; continue; }
            var start = index;
            while (index < value.Length && char.IsDigit(value[index])) index++;
            var count = index == start ? 1 : int.Parse(value[start..index]);
            if (index >= value.Length) return false;
            var mark = char.ToUpperInvariant(value[index++]);
            if (mark == SocketMark) socket += count;
            else if (mark == FlangeMark) flange += count;
            else return false;
        }
        ends = new PipeJointEnds(socket, flange);
        return true;
    }

    /// <summary>구형 <c>KP:2+플랜지:1</c>. 개수를 생략한 조각은 1로, 종류가 비었거나 개수가 0 이하인 조각은 버린다.</summary>
    private static PipeJointEnds DecodeLegacy(string text)
    {
        var socket = 0;
        var flange = 0;
        foreach (var part in text.Split(LegacyEntrySeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var index = part.IndexOf(LegacyCountSeparator);
            var kind = (index < 0 ? part : part[..index]).Trim();
            if (kind.Length == 0) continue;
            var count = 1;
            if (index >= 0 && (!int.TryParse(part[(index + 1)..].Trim(), out count) || count <= 0)) continue;
            if (!LegacyKinds.Contains(kind, StringComparer.Ordinal)) continue;
            if (string.Equals(kind, LegacyFlangeKind, StringComparison.Ordinal)) flange += count; else socket += count;
        }
        return new PipeJointEnds(socket, flange);
    }

    /// <summary>접합 개소 합. 부재 단부 수와 맞는지 검증하는 쪽이 쓴다.</summary>
    public static int TotalCount(string? composition) => Decode(composition).Total;
}
