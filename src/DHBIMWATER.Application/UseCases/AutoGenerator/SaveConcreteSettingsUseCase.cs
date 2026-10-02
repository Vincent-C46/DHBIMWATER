using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Structures;

namespace DHBIMWATER.Application.UseCases.AutoGenerator;

/// <summary>공용 콘크리트 강도 설정을 문서(DataStorage)에 저장한다(docs/249 § B). 선례: <c>SavePipeLayoutFamilySettingsUseCase</c>.
/// <para>밸브실 창은 모델리스라 Revit API 컨텍스트 밖이다 — 그 경로에서는 ExternalEvent로 호출한다(docs/249 § E).</para></summary>
public sealed class SaveConcreteSettingsUseCase
{
    private readonly ITransactionContext _transaction;
    private readonly IConcreteSettingsRepo _repo;

    public SaveConcreteSettingsUseCase(ITransactionContext transaction, IConcreteSettingsRepo repo)
    { _transaction = transaction; _repo = repo; }

    public void Execute(ConcreteSettings settings)
    {
        using (_transaction)
        {
            try
            {
                _transaction.Begin("콘크리트 강도 설정 저장");
                _repo.Save(settings);
                _transaction.Commit();
            }
            catch { _transaction.Rollback(); throw; }
        }
    }
}
