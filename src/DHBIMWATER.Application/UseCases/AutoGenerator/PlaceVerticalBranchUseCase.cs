using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Parameters;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Application.UseCases.AutoGenerator;

/// <summary>수직분기 배치(docs/301 § C). 트랜잭션 1개 — 실패하면 쌓던 부품을 모두 되돌린다.</summary>
public sealed class PlaceVerticalBranchUseCase
{
    private readonly ITransactionContext _transaction;
    private readonly IVerticalBranchCommandRepo _repository;
    private readonly ISharedParameterRepository _sharedParameterRepo;

    public PlaceVerticalBranchUseCase(ITransactionContext transaction, IVerticalBranchCommandRepo repository,
        ISharedParameterRepository sharedParameterRepo)
    {
        _transaction = transaction;
        _repository = repository;
        _sharedParameterRepo = sharedParameterRepo;
    }

    public VerticalBranchResult Execute(VerticalBranchRequest request)
    {
        var errors = VerticalBranchRules.Validate(request.Parts);
        if (errors.Count > 0) throw new DhbimwaterException(ErrorCodes.PL_VerticalBranchInvalid, string.Join(Environment.NewLine, errors));

        using (_transaction)
        {
            try
            {
                _transaction.Begin("Add Vertical Branch");
                // 명칭·규격 매개변수는 배치 전에 바인딩돼 있어야 함(docs/131 § A와 같은 이유).
                _sharedParameterRepo.EnsureParameters(ValvePipingParameters.Definitions);
                var result = _repository.Place(request);
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
