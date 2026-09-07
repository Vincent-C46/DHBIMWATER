namespace DHBIMWATER.Application.Interfaces;

/// <summary>모델에 이미 기록된 배관재료표용 값(docs/131 § B)을 훑은 결과.
/// 일람표를 몇 개 만들지는 전적으로 이 스캔 결과가 정한다 — 사용자 입력이 없다(docs/132 § 0).</summary>
/// <param name="JunctionIds">비어 있지 않은 <c>DH_격점번호</c> 값 목록(중복 없음).</param>
/// <param name="UnassignedCount">격점번호가 비어 있는 배관 밸브류 인스턴스 수.</param>
/// <param name="TotalPartCount">DHBIMWATER가 만든 배관 밸브류 인스턴스 총수.</param>
public sealed record PipeMaterialScheduleScan(
    IReadOnlyList<string> JunctionIds, int UnassignedCount, int TotalPartCount);

/// <summary>일람표 생성 결과. 이미 같은 이름이 있으면 만들지 않고 <see cref="Skipped"/>로 보고한다.</summary>
public sealed record PipeMaterialScheduleResult(
    IReadOnlyList<string> Created, IReadOnlyList<string> Skipped, PipeMaterialScheduleScan Scan);

/// <summary>격점상세도 배관재료표 일람표(ViewSchedule) 조회·생성 경계(docs/132).</summary>
public interface IPipeMaterialScheduleRepo
{
    /// <summary>배관 밸브류 인스턴스에 기록된 격점번호와 부재 수를 훑는다. 트랜잭션이 필요 없는 읽기 전용 경로다.</summary>
    PipeMaterialScheduleScan Scan();

    /// <summary>이름이 같은 일람표가 이미 있는지.</summary>
    bool ScheduleExists(string scheduleName);

    /// <summary>배관재료표 일람표 1개를 만든다.
    /// <paramref name="junctionId"/>가 비어 있으면 격점 필터 없이 전체 배관 밸브류를 담는다.</summary>
    void CreateSchedule(string scheduleName, string junctionId);
}
