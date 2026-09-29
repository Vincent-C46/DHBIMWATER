using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Parameters;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Application.UseCases.Documentation;

/// <summary>
/// 재료표 값이 비어 있는 기존 배관 부재에 명칭·규격·번호를 다시 기록한다(docs/136).
/// <para>사용자 입력이 없다 — 값은 전부 인스턴스 기하에서 복원한다.
/// 격점번호는 쓰지 않으므로 재기록된 부재는 <c>배관재료표_미지정</c>으로 모인다.</para>
/// </summary>
public sealed class BackfillPipeMaterialParametersUseCase
{
    private readonly ITransactionContext _transaction;
    private readonly IPipeMaterialBackfillRepo _repo;
    private readonly ISharedParameterRepository _sharedParameterRepo;

    public BackfillPipeMaterialParametersUseCase(ITransactionContext transaction,
        IPipeMaterialBackfillRepo repo, ISharedParameterRepository sharedParameterRepo)
    {
        _transaction = transaction;
        _repo = repo;
        _sharedParameterRepo = sharedParameterRepo;
    }

    /// <summary>재기록 후보를 센다. 읽기 전용이므로 트랜잭션을 열지 않는다.</summary>
    public PipeMaterialBackfillScan ScanCandidates() => _repo.ScanCandidates();

    public PipeMaterialBackfillResult Execute()
    {
        using (_transaction)
        {
            try
            {
                _transaction.Begin("배관재료표 값 재기록");
                // 오래된 문서는 배관 이음쇠에 바인딩 자체가 없다. 값을 쓰기 전에 반드시 먼저 해야 한다(docs/131 § A).
                _sharedParameterRepo.EnsureParameters(ValvePipingParameters.Definitions);
                var result = _repo.Backfill();
                _transaction.Commit();
                return result;
            }
            catch
            {
                _transaction.Rollback();
                throw;
            }
        }
    }
}
