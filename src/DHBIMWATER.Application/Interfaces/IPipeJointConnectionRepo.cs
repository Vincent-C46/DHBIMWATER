namespace DHBIMWATER.Application.Interfaces;

/// <summary>
/// 문서 전체 배관 부재의 단부 연결을 보고 플랜지 접합 · 유입부 절단을 기록하는 경계(docs/266 § 1).
/// <para>구현체는 모델을 읽고 수량 규칙의 결과를 쓴다. 트랜잭션은 UseCase가 연다(CLAUDE.md).</para>
/// </summary>
public interface IPipeJointConnectionRepo
{
    /// <summary>패스를 돌려 기록한다.</summary>
    /// <returns>남길 경고(짝 없는 플랜지 · 판정 불가 · 쓰기 실패). 없으면 빈 목록.</returns>
    IReadOnlyList<string> RecordConnectionJoints();
}
