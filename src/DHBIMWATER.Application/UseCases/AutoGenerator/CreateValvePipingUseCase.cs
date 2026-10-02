using DHBIMWATER.Application.Gis;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Parameters;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Application.UseCases.AutoGenerator;

public sealed class CreateValvePipingUseCase
{
    private readonly ITransactionContext _transaction;
    private readonly IReadOnlyDictionary<PipeOutputMode, IPipeCommandRepo> _repositories;
    private readonly ISharedParameterRepository _sharedParameterRepo;
    private readonly BendSettingsProvider _bendSettings;

    public CreateValvePipingUseCase(ITransactionContext transaction, IEnumerable<IPipeCommandRepo> repositories,
        ISharedParameterRepository sharedParameterRepo, BendSettingsProvider bendSettings)
    {
        _transaction = transaction;
        _repositories = repositories.ToDictionary(x => x.OutputMode);
        _sharedParameterRepo = sharedParameterRepo;
        _bendSettings = bendSettings;
    }

    public PipeCreationResult Execute(PipeNetworkDefinition network)
    {
        if (network.Edges.Count == 0) throw new DhbimwaterException(ErrorCodes.PL_NoSegments, "생성할 배관 세그먼트가 없습니다.");
        if (!_repositories.TryGetValue(network.OutputMode, out var repository))
            throw new DhbimwaterException(ErrorCodes.PL_Internal, $"{network.OutputMode} 배관 출력 구현이 등록되지 않았습니다.");

        using (_transaction)
        {
            try
            {
                _transaction.Begin("Create Valve Room Piping");
                // 배관재료표용 정보 매개변수는 인스턴스를 배치하기 전에 바인딩되어 있어야 한다(docs/131 § A).
                if (network.OutputMode == PipeOutputMode.PipeAccessorySegment)
                    _sharedParameterRepo.EnsureParameters(ValvePipingParameters.Definitions);
                // 무게(`DH_무게`) 조회용 직관·곡관 규격표 — 규격표 창 편집값(프로젝트 저장본)을 실어 보낸다(docs/260 § D).
                var result = repository.CreateNetwork(network.CatalogSettings is null
                    ? network with { CatalogSettings = _bendSettings.Load().Settings }
                    : network);
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
