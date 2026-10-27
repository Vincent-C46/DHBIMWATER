namespace DHBIMWATER.Core.Piping;

public sealed record PipeEndComposition(int Socket, int Flange, bool HasEndFlags, bool AllOff,
    IReadOnlyList<string> ConflictSides);

/// <summary>
/// 패밀리의 Yes/No 매개변수에서 접합 단부 수와 곡관 형식을 읽는다.
/// <para><b>패밀리마다 분기하지 않는다.</b> `DH_플랜지_직관`은 `좌측_플랜지`·`우측_플랜지`로,
/// `DH_소켓_곡관`은 `소켓곡관(A형)`↔`소켓곡관(B형)`로 형상·형식을 제어하는데, 이름만 다를 뿐 축은 둘뿐이다 —
/// <b>단부 형상 · 형식</b>. 축별 규칙 하나로 읽으면 패밀리가 늘어도 코드가 늘지 않는다.</para>
/// <para>소켓의 세부 종류(`KP접합`↔`이탈방지압륜`)는 접합 구성에 싣지 않는다(2026-09-15 사용자 결정,
/// 개소 매개변수 넷이 관종+압륜으로 그 구분을 담는다.</para>
/// <para><b>모르면 null이다.</b> 단부 플래그도 없고 이름으로도 못 정하면 값을 지어내지 않고
/// 호출부의 폴백에 맡긴다.</para>
/// </summary>
public static class PipeJointFlagReader
{
    private const string SocketWord = "소켓";
    private const string FlangeWord = "플랜지";

    /// <summary>좌·우 단부 가시성을 판독한다. 같은 단부에서 소켓과 플랜지가 모두 켜지면 플랜지로 계산한다.</summary>
    public static PipeEndComposition? ComposeByEnd(IReadOnlyDictionary<string, bool> flags, PipeMaterial? material, int endCount)
    {
        var leftSocket = false;
        var leftFlange = false;
        var rightSocket = false;
        var rightFlange = false;
        var hasEndFlags = false;

        foreach (var (name, value) in flags)
        {
            if (ParenLabel(name) is not null) continue;
            if (name.StartsWith(PipeEndFlagParameters.LeftSocket, StringComparison.Ordinal))
            {
                leftSocket = value;
                hasEndFlags = true;
            }
            else if (name.StartsWith(PipeEndFlagParameters.LeftFlange, StringComparison.Ordinal))
            {
                leftFlange = value;
                hasEndFlags = true;
            }
            else if (name.StartsWith(PipeEndFlagParameters.RightSocket, StringComparison.Ordinal))
            {
                rightSocket = value;
                hasEndFlags = true;
            }
            else if (name.StartsWith(PipeEndFlagParameters.RightFlange, StringComparison.Ordinal))
            {
                rightFlange = value;
                hasEndFlags = true;
            }
        }

        if (!hasEndFlags) return null;

        var allOff = !leftSocket && !leftFlange && !rightSocket && !rightFlange;
        var conflicts = new List<string>(2);
        var socket = 0;
        var flange = 0;
        CountSide("좌측", leftSocket, leftFlange);
        CountSide("우측", rightSocket, rightFlange);

        if (allOff)
        {
            return new PipeEndComposition(0, 0, true, true, conflicts);
        }
        if (material == PipeMaterial.Steel && flange > 0)
        {
            socket = Math.Max(0, endCount - flange);
        }

        return new PipeEndComposition(socket, flange, true, allOff, conflicts);

        void CountSide(string side, bool hasSocket, bool hasFlange)
        {
            if (hasSocket && hasFlange)
            {
                flange++;
                conflicts.Add(side);
            }
            else if (hasFlange) flange++;
            else if (hasSocket) socket++;
        }
    }

    /// <summary>
    /// 켜져 있는 단부 플래그를 센다. 정하지 못하면 null.
    /// </summary>
    /// <param name="flags">인스턴스·유형의 Yes/No 매개변수 전체(이름 → 값).</param>
    /// <param name="familyName">단부 플래그가 하나도 없는 패밀리의 2순위 근거. `DH_플랜지_플랜지소켓관`처럼
    /// 구성이 패밀리에 고정된 경우가 있다.</param>
    /// <param name="endCount">부재의 단부 수(<see cref="PipeMaterialSpecText.EndCountFor"/>). 2순위에서만 쓴다.</param>
    public static (int Socket, int Flange)? Compose(IReadOnlyDictionary<string, bool> flags, string? familyName, int endCount)
    {
        var (socket, flange, hasSocketFlags, hasFlangeFlags) = CountEnds(flags);
        // 플래그가 있는데 전부 꺼진 것과, 플래그 자체가 없는 것은 다르다. 앞은 접합이 없는 상태이고
        // 뒤는 패밀리가 구성을 고정으로 갖는 경우다 — 뒤에서만 이름으로 유도한다.
        if (!hasSocketFlags && !hasFlangeFlags) (socket, flange) = FromFamilyName(familyName, endCount);
        // 한 종류에만 플래그가 있는 패밀리(`DH_소켓_플랜지T형관`: 소켓 단부 2개만 플래그, 분기 플랜지는 고정)는
        // 플래그가 없는 종류를 이름으로 유도해 남은 단부를 채운다 — 위 "플래그 없으면 이름" 규칙의 종류별 적용이다.
        // 그 종류의 플래그가 있는데 꺼져 있으면 채우지 않는다(사용자가 끈 단부를 지어내면 안 된다).
        else if (!hasFlangeFlags && NameHas(familyName, FlangeWord)) flange = Math.Max(0, endCount - socket);
        else if (!hasSocketFlags && NameHas(familyName, SocketWord)) socket = Math.Max(0, endCount - flange);
        if (socket == 0 && flange == 0) return null;
        return (socket, flange);
    }

    /// <summary>켜져 있는 형식 플래그의 괄호 라벨 — `소켓곡관(A형)` → `A형`. 없거나 둘 이상이면 null.</summary>
    public static string? Form(IReadOnlyDictionary<string, bool> flags)
    {
        string? found = null;
        foreach (var (name, value) in flags)
        {
            if (!value) continue;
            var label = ParenLabel(name);
            if (label is null) continue;
            if (found is not null) return null;   // 상반이어야 할 플래그가 둘 다 켜졌다
            found = label;
        }
        return found;
    }

    /// <summary>
    /// 단부 플래그를 센다. <b>켜진 플래그 하나가 단부 하나다</b> — `좌측_소켓`·`우측_소켓`이 둘 다 켜진
    /// 양쪽 소켓 직관도 그대로 2로 세어진다. 한쪽에만 소켓을 두는 지금 패밀리에 맞춰 1로 고정하지 않는 이유다.
    /// <para>형식 플래그(`소켓곡관(A형)`)는 괄호가 있어 단부 플래그와 갈린다 — 이름에 `소켓`이 들어 있어도
    /// 단부가 아니다.</para>
    /// </summary>
    private static (int Socket, int Flange, bool HasSocketFlags, bool HasFlangeFlags) CountEnds(IReadOnlyDictionary<string, bool> flags)
    {
        var socket = 0;
        var flange = 0;
        var hasSocketFlags = false;
        var hasFlangeFlags = false;
        foreach (var (name, value) in flags)
        {
            if (ParenLabel(name) is not null) continue;
            var isSocket = name.Contains(SocketWord, StringComparison.Ordinal);
            var isFlange = !isSocket && name.Contains(FlangeWord, StringComparison.Ordinal);
            if (!isSocket && !isFlange) continue;

            // 종류별로 "플래그가 있는지"를 따로 기억한다 — 한 종류만 플래그로 제어하는 패밀리를 가르기 위해서다(Compose 참고).
            if (isSocket) hasSocketFlags = true; else hasFlangeFlags = true;
            if (!value) continue;
            if (isSocket) socket++; else flange++;
        }
        return (socket, flange, hasSocketFlags, hasFlangeFlags);
    }

    private static bool NameHas(string? familyName, string word)
        => !string.IsNullOrWhiteSpace(familyName) && familyName.Contains(word, StringComparison.Ordinal);

    /// <summary>
    /// 단부 플래그가 없는 패밀리는 이름으로 유도한다 — `DH_플랜지_플랜지소켓관` → 소켓 1 + 플랜지 1.
    /// <para>낱말이 하나만 나오면 단부 전부가 그 형상이다(`소켓곡관` → 소켓 <paramref name="endCount"/>개).
    /// 둘 다 나오면 하나씩으로 본다 — 단부가 셋 이상인 부재에서는 합이 모자라므로, 그런 패밀리에는
    /// 단부 플래그를 두어야 한다.</para>
    /// </summary>
    private static (int Socket, int Flange) FromFamilyName(string? familyName, int endCount)
    {
        if (string.IsNullOrWhiteSpace(familyName) || endCount <= 0) return (0, 0);
        var socket = familyName.Contains(SocketWord, StringComparison.Ordinal);
        var flange = familyName.Contains(FlangeWord, StringComparison.Ordinal);
        return (socket, flange) switch
        {
            (true, true) => (1, 1),
            (true, false) => (endCount, 0),
            (false, true) => (0, endCount),
            _ => (0, 0)
        };
    }

    /// <summary>`소켓곡관(A형)` → `A형`. 괄호가 없거나 속이 비면 null.</summary>
    private static string? ParenLabel(string name)
    {
        var open = name.IndexOf('(');
        if (open < 0) return null;
        var close = name.IndexOf(')', open + 1);
        if (close <= open + 1) return null;
        return name[(open + 1)..close].Trim() is { Length: > 0 } label ? label : null;
    }
}
