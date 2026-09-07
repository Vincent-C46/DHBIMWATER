using DHBIMWATER.Application.Interfaces;

namespace DHBIMWATER.Application.UseCases.Documentation;

/// <summary>
/// 격점상세도 배관재료표 일람표를 격점당 1개씩 만든다(docs/132).
/// <para>사용자 입력이 없다 — 열·정렬·항목화·이름이 전부 규약으로 고정되고,
/// 대상 격점은 모델의 <c>DH_격점번호</c>에서 읽는다.</para>
/// </summary>
public sealed class CreatePipeMaterialScheduleUseCase
{
    /// <summary>일람표 이름 규약. 프로젝트 탐색기에서 한데 모이도록 접두어를 고정한다.</summary>
    public const string NamePrefix = "배관재료표";
    /// <summary>격점번호가 비어 있는 부재를 담는 일람표 이름(docs/132 § 0 확정).</summary>
    public const string UnassignedName = NamePrefix + "_미지정";

    private readonly ITransactionContext _transaction;
    private readonly IPipeMaterialScheduleRepo _repo;

    public CreatePipeMaterialScheduleUseCase(ITransactionContext transaction, IPipeMaterialScheduleRepo repo)
    {
        _transaction = transaction;
        _repo = repo;
    }

    public PipeMaterialScheduleResult Execute()
    {
        // 스캔은 읽기 전용이라 트랜잭션 밖에서 먼저 한다. 만들 것이 없으면 트랜잭션을 아예 열지 않는다.
        var scan = _repo.Scan();
        var targets = BuildTargets(scan);
        if (targets.Count == 0) return new PipeMaterialScheduleResult([], [], scan);

        var created = new List<string>();
        var skipped = new List<string>();

        using (_transaction)
        {
            try
            {
                _transaction.Begin("배관재료표 일람표 생성");
                foreach (var (scheduleName, junctionId) in targets)
                {
                    // 같은 이름이 이미 있으면 덮어쓰지 않는다. 사용자가 열 폭·서식을 손봤을 수 있다.
                    if (_repo.ScheduleExists(scheduleName)) { skipped.Add(scheduleName); continue; }
                    _repo.CreateSchedule(scheduleName, junctionId);
                    created.Add(scheduleName);
                }
                _transaction.Commit();
                return new PipeMaterialScheduleResult(created, skipped, scan);
            }
            catch
            {
                _transaction.Rollback();
                throw;
            }
        }
    }

    /// <summary>격점번호 오름차순으로 일람표 대상을 만든다.
    /// 격점번호가 비어 있는 부재가 하나라도 있으면 필터 없는 "미지정" 표를 맨 뒤에 붙인다.</summary>
    private static List<(string ScheduleName, string JunctionId)> BuildTargets(PipeMaterialScheduleScan scan)
    {
        var targets = scan.JunctionIds
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(x => ($"{NamePrefix}_{x}", x))
            .ToList();
        if (scan.UnassignedCount > 0) targets.Add((UnassignedName, ""));
        return targets;
    }
}
