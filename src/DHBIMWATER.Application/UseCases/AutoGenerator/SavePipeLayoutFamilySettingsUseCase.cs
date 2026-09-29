using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Application.UseCases.AutoGenerator;

/// <summary>PipeLayout 패밀리 설정을 프로젝트(DataStorage)에 저장한다(docs/175 § E-2). 선례: <c>SaveBendSettingsUseCase</c>.
/// <para>PipeLayout 창은 모델리스라 Revit API 컨텍스트 밖이다 — 반드시 ExternalEvent 핸들러에서 호출한다(docs/175 § E-6).</para></summary>
public sealed class SavePipeLayoutFamilySettingsUseCase
{
    private readonly ITransactionContext _transaction;
    private readonly IPipeLayoutFamilySettingsRepo _repo;

    public SavePipeLayoutFamilySettingsUseCase(ITransactionContext transaction, IPipeLayoutFamilySettingsRepo repo)
    { _transaction = transaction; _repo = repo; }

    public void Execute(PipeLayoutFamilySettings settings)
    {
        using (_transaction)
        {
            try
            {
                _transaction.Begin("PipeLayout 패밀리 설정 저장");
                _repo.Save(settings);
                _transaction.Commit();
            }
            catch { _transaction.Rollback(); throw; }
        }
    }
}
