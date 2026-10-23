using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Structures;

namespace DHBIMWATER.Application.UseCases.AutoGenerator;

/// <summary>밸브실 제원표 편집본을 문서(DataStorage)에 저장한다(docs/312). 선례: <see cref="SaveConcreteSettingsUseCase"/>.
/// <para>밸브실 창은 모델리스라 ExternalEvent로 호출한다.</para></summary>
public sealed class SaveValveRoomDimensionCatalogUseCase
{
    private readonly ITransactionContext _transaction;
    private readonly IValveRoomDimensionCatalogRepo _repo;

    public SaveValveRoomDimensionCatalogUseCase(ITransactionContext transaction, IValveRoomDimensionCatalogRepo repo)
    { _transaction = transaction; _repo = repo; }

    public void Execute(ValveRoomDimensionTable table)
    {
        using (_transaction)
        {
            try
            {
                _transaction.Begin("밸브실 제원표 저장");
                _repo.Save(table);
                _transaction.Commit();
            }
            catch { _transaction.Rollback(); throw; }
        }
    }
}
