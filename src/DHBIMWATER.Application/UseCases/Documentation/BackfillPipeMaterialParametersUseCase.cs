using DHBIMWATER.Application.Gis;
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
    private readonly BendSettingsProvider _bendSettings;

    public BackfillPipeMaterialParametersUseCase(ITransactionContext transaction,
        IPipeMaterialBackfillRepo repo, ISharedParameterRepository sharedParameterRepo, BendSettingsProvider bendSettings)
    {
        _transaction = transaction;
        _repo = repo;
        _sharedParameterRepo = sharedParameterRepo;
        _bendSettings = bendSettings;
    }

    /// <summary>재기록 후보를 센다. 읽기 전용이므로 트랜잭션을 열지 않는다.</summary>
    public PipeMaterialBackfillScan ScanCandidates() => _repo.ScanCandidates();

    /// <summary>격점상세도 자동 경로. 값 빈 배관 부재만 채움, 개수 재산정 없음. 후보 없으면 트랜잭션 안 열고 null.</summary>
    public PipeMaterialBackfillResult? ExecuteUntagged()
    {
        if (_repo.ScanCandidates().CandidateCount == 0) return null;
        using (_transaction)
        {
            try
            {
                _transaction.Begin("배관재료표 값 자동 기록");
                // 바인딩 없는 옛 문서 대비. 값 쓰기 전 필수(docs/131 § A).
                _sharedParameterRepo.EnsureParameters(ValvePipingParameters.Definitions);
                var result = _repo.BackfillUntagged(_bendSettings.Load().Settings);
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

    /// <summary>부재 정보 갱신 창 기본 모드. 값 빈 배관 부재 + 값 빈 계획관로 가변 패밀리만 채움.
    /// 손으로 고친 개수 덮는 문서 전체 재산정은 <see cref="Execute"/>.</summary>
    /// <param name="fallbackMaterialCode">재질로 관종 못 정한 부재에 일괄로 쓸 DH_관종 코드. null = 비움.</param>
    public PipeMaterialBackfillResult ExecuteEmpty(string? fallbackMaterialCode = null)
    {
        using (_transaction)
        {
            try
            {
                _transaction.Begin("배관 부재 정보 채움");
                EnsureBindings();
                var result = _repo.BackfillEmpty(_bendSettings.Load().Settings, fallbackMaterialCode);
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

    /// <summary>문서 전체 재산정 + 빈 DH_관종·수동 MEP 배관 채움(부재 정보 갱신 창 재산정 모드).</summary>
    public PipeMaterialBackfillResult Execute(string? fallbackMaterialCode)
    {
        using (_transaction)
        {
            try
            {
                _transaction.Begin("배관재료표 값 재기록");
                EnsureBindings();
                var result = _repo.Backfill(_bendSettings.Load().Settings, fallbackMaterialCode);
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

    // 배관 이음쇠·밸브 바인딩 필수. 가변 패밀리·MEP 배관은 후보 있을 때만 — 밸브실 문서에 DH_* 칸 안 늘림(docs/267 § 2)
    private void EnsureBindings()
    {
        _sharedParameterRepo.EnsureParameters(ValvePipingParameters.Definitions);
        var scan = _repo.ScanCandidates();
        if (scan.AdaptiveCandidateCount > 0)
            _sharedParameterRepo.EnsureParameters(DHBIMWATER.Core.Gis.PipeAlignmentParameters.Definitions);
        if (scan.MepPipeCandidateCount > 0)
            _sharedParameterRepo.EnsureParameters(DHBIMWATER.Core.Gis.PipeAlignmentParameters.MepPipeDefinitions);
    }

    // 격점상세도 옵션 경로(종전 동작 유지). 관종·MEP 배관 채움 없음
    public PipeMaterialBackfillResult Execute()
    {
        using (_transaction)
        {
            try
            {
                _transaction.Begin("배관재료표 값 재기록");
                // 오래된 문서는 배관 이음쇠에 바인딩 자체가 없다. 값을 쓰기 전에 반드시 먼저 해야 한다(docs/131 § A).
                _sharedParameterRepo.EnsureParameters(ValvePipingParameters.Definitions);
                // 직접 배치한 계획관로 가변 패밀리(일반 모델)를 채울 때만 계획관로 매개변수를 묶는다(docs/267 § 2) —
                // 밸브실 문서의 일반 모델 전체에 DH_* 칸을 늘리지 않으려는 것이다.
                if (_repo.ScanCandidates().AdaptiveCandidateCount > 0)
                    _sharedParameterRepo.EnsureParameters(DHBIMWATER.Core.Gis.PipeAlignmentParameters.Definitions);
                // 무게 조회용 규격표(docs/260 § E) — 신규 배치와 같은 프로젝트 저장값을 쓴다.
                var result = _repo.Backfill(_bendSettings.Load().Settings);
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
