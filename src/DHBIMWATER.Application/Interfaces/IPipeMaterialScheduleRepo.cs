using DHBIMWATER.Core.Piping;

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
    IReadOnlyList<string> Created, IReadOnlyList<string> Skipped, PipeMaterialScheduleScan Scan)
{
    /// <summary>내용을 지우고 다시 그린 뷰 이름(docs/173). 드래프팅뷰 전용 —
    /// 일람표는 갱신 경로가 없으므로 항상 비어 있다.
    /// <para>위치 매개변수가 아니라 init 속성인 이유: 일람표 UseCase의 기존 호출부를 건드리지 않기 위해서다.</para></summary>
    public IReadOnlyList<string> Updated { get; init; } = [];
}

/// <summary>격점상세도 배관재료표 일람표(ViewSchedule) 조회·생성 경계(docs/132).</summary>
public interface IPipeMaterialScheduleRepo
{
    /// <summary>배치된 배관 밸브류 부재에서 재료표 원자료를 읽는다. 읽기 전용이라 트랜잭션이 필요 없다(docs/208 § D-1).
    /// 격점 스캔은 <c>PipeMaterialScan.Of(parts)</c>가 만든다 — 일람표·도면이 같은 규칙을 쓴다.</summary>
    IReadOnlyList<PipeMaterialPart> GetParts();

    /// <summary>템플릿이 아닌 모든 뷰 이름(일람표 포함). Revit 뷰 이름은 종류를 가리지 않고 전체에서 유일해야 하므로
    /// 격점마다 전체를 훑는 대신 한 번 받아 둔다(docs/208 § D-3).</summary>
    IReadOnlySet<string> GetExistingViewNames();

    /// <summary>배관재료표 일람표 1개를 만든다.
    /// <paramref name="junctionId"/>가 비어 있으면 격점 필터 없이 전체 배관 밸브류를 담는다.</summary>
    void CreateSchedule(string scheduleName, string junctionId);
}
