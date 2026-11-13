using DHBIMWATER.Application.Interfaces;

namespace DHBIMWATER.Application.UseCases.AutoGenerator;

/// <summary>배수지 창 입력값을 문서(DataStorage)에 저장(docs/325 § E). 선례: <see cref="SavePumpingStationInputUseCase"/>.
/// <para>모델리스 창이라 ExternalEvent(API 컨텍스트)에서 호출.</para></summary>
public sealed class SaveReservoirInputUseCase
{
    private readonly ITransactionContext _transaction;
    private readonly IReservoirInputRepo _repo;

    public SaveReservoirInputUseCase(ITransactionContext transaction, IReservoirInputRepo repo)
    { _transaction = transaction; _repo = repo; }

    public void Execute(string json)
    {
        using (_transaction)
        {
            try
            {
                _transaction.Begin("배수지 입력값 저장");
                _repo.Save(json);
                _transaction.Commit();
            }
            catch { _transaction.Rollback(); throw; }
        }
    }
}
