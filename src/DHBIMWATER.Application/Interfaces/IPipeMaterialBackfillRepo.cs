namespace DHBIMWATER.Application.Interfaces;

/// <summary>재기록 대상 스캔 결과(docs/136 § 3-6.1).</summary>
/// <param name="CandidateCount">DH_Addin이 비어 있는 배관 밸브류·이음쇠 인스턴스 수.</param>
/// <param name="FromSelection">Revit 선택 요소로 범위를 좁혔는지. 좁혔으면 안내 문구가 달라진다.</param>
public sealed record PipeMaterialBackfillScan(int CandidateCount, bool FromSelection);

/// <summary>재기록 결과.</summary>
/// <param name="WrittenCount">재료표 값을 기록한 부재 수.</param>
/// <param name="SkippedCount">커넥터로 종류를 판정할 수 없어 건너뛴 부재 수.</param>
/// <param name="Warnings">건너뛴 사유·쓰기 실패 요약. 모델 생성 자체를 막지는 않는다.</param>
public sealed record PipeMaterialBackfillResult(
    int WrittenCount, int SkippedCount, IReadOnlyList<string> Warnings);

/// <summary>
/// 재료표 매개변수가 비어 있는 기존 배관 부재에 값을 다시 기록하는 경계(docs/136).
/// <para>docs/133 이전에 배치된 배관 이음쇠는 공유 매개변수 바인딩 자체가 없어 <c>DH_Addin</c>이 비어 있고,
/// 바인딩을 나중에 보강해도 값은 소급되지 않는다. 그 부재를 재모델링 없이 재료표에 넣기 위한 경로다.</para>
/// </summary>
public interface IPipeMaterialBackfillRepo
{
    /// <summary>재기록 후보를 센다. 읽기 전용이라 트랜잭션이 필요 없다.</summary>
    PipeMaterialBackfillScan ScanCandidates();

    /// <summary>후보에 재료표 값을 기록한다. 트랜잭션은 UseCase가 연다(CLAUDE.md).</summary>
    PipeMaterialBackfillResult Backfill();
}
