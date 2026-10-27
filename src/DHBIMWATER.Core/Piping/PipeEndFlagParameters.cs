namespace DHBIMWATER.Core.Piping;

/// <summary>
/// 가변 관 패밀리 단부 가시성 Yes/No 이름·값 규칙(docs/315).
/// 좌측 = P1(선형 시점), 우측 = 끝점(종점).
/// <para>값 null = 안 씀. 주철관 곡관 소켓은 유형(`소켓_A형`·`소켓_B형`)이 정함 — 인스턴스로 덮지 않음.</para>
/// </summary>
public static class PipeEndFlagParameters
{
    public const string LeftSocket = "좌측_소켓";
    public const string LeftFlange = "좌측_플랜지";
    public const string RightSocket = "우측_소켓";
    public const string RightFlange = "우측_플랜지";

    /// <summary>
    /// 부재·관종·접합 → 단부 플래그 값. 관종 미상은 주철관 규칙.
    /// <para>플랜지 = 양쪽 플랜지만 켬. 주철관 소켓 직관 = 좌측 소켓만 켬. 강관 Socket(용접) = 전부 끔.</para>
    /// </summary>
    public static (bool? LeftSocket, bool? RightSocket, bool LeftFlange, bool RightFlange) Resolve(
        bool isBend, PipeMaterial? material, BendConnection connection)
    {
        var flanged = connection == BendConnection.Flanged;
        var isSteel = material == PipeMaterial.Steel;
        // 주철관 곡관 소켓은 유형 매개변수 — 건드리지 않음.
        if (isBend && !isSteel) return (null, null, flanged, flanged);
        var leftSocket = !flanged && !isSteel && !isBend;
        return (leftSocket, false, flanged, flanged);
    }
}
