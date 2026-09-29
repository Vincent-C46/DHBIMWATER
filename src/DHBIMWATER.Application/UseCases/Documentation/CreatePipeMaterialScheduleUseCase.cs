using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Parameters;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Shared.Errors;

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
    private readonly ISharedParameterRepository _sharedParameterRepo;

    public CreatePipeMaterialScheduleUseCase(ITransactionContext transaction, IPipeMaterialScheduleRepo repo,
        ISharedParameterRepository sharedParameterRepo)
    {
        _transaction = transaction;
        _repo = repo;
        _sharedParameterRepo = sharedParameterRepo;
    }

    public PipeMaterialScheduleResult Execute()
    {
        // 스캔은 읽기 전용이라 트랜잭션 밖에서 먼저 한다. 만들 것이 없으면 트랜잭션을 아예 열지 않는다.
        var parts = _repo.GetParts();
        var scan = PipeMaterialScan.Of(parts);
        var targets = BuildTargets(scan);
        if (targets.Count == 0) return new PipeMaterialScheduleResult([], [], scan);

        var created = new List<string>();
        var skipped = new List<string>();

        using (_transaction)
        {
            try
            {
                _transaction.Begin("배관재료표 일람표 생성");
                // 재기록을 건너뛴 옛 문서는 DH_비고·DH_부재번호 바인딩이 없어 열을 못 찾았다(docs/208 § 0-1).
                // EnsureParameters는 멱등이라 이미 바인딩된 문서에서는 아무 일도 하지 않는다(docs/131 § A).
                _sharedParameterRepo.EnsureParameters(ValvePipingParameters.Definitions);
                // 뷰 이름 존재 확인은 격점마다 전체 뷰를 훑지 않고 한 번 받아 둔다(docs/208 § D-3).
                var existing = new HashSet<string>(_repo.GetExistingViewNames(), StringComparer.Ordinal);
                foreach (var (scheduleName, junctionId) in targets)
                {
                    // 같은 이름이 이미 있으면 덮어쓰지 않는다. 사용자가 열 폭·서식을 손봤을 수 있다.
                    if (existing.Contains(scheduleName)) { skipped.Add(scheduleName); continue; }
                    // 격점 하나가 실패해도(열 누락·이름 충돌 등) 나머지 격점은 살린다(docs/208 § C).
                    // Repo가 SubTransaction으로 그 격점의 변경만 되돌린다 — 바깥 Transaction은 여기서 관리한다.
                    try
                    {
                        _repo.CreateSchedule(scheduleName, junctionId);
                        created.Add(scheduleName);
                        existing.Add(scheduleName);
                    }
                    catch (DhbimwaterException ex) when (ex.Code == ErrorCodes.NoActiveDocument) { throw; }
                    catch (Exception ex)
                    {
                        skipped.Add($"{scheduleName} — [{ErrorCodes.MT_ScheduleCreateFailed}] {ex.Message}");
                    }
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
            .Select(x => (PipeMaterialViewName.For(NamePrefix, x), x))
            .ToList();
        if (scan.UnassignedCount > 0) targets.Add((UnassignedName, ""));
        return targets;
    }
}
