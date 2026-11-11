using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Application.UseCases.AutoGenerator;

/// <summary>타이밸브실 관경별 최소 폭·화살표 위치(docs/323). 로드된 패밀리를 실측하고 <b>항상 롤백</b>한다 — 모델에 아무것도 안 남김.
/// <para>밸브실 창·PipeLayout 창 둘 다 모델리스라 ExternalEvent 안에서 부른다.</para></summary>
public sealed class MeasureTieValveRoomUseCase
{
    private readonly ITransactionContext _transaction;
    private readonly ITieValveRoomProbeRepo _repo;

    public MeasureTieValveRoomUseCase(ITransactionContext transaction, ITieValveRoomProbeRepo repo)
    { _transaction = transaction; _repo = repo; }

    public TieValveRoomLayout Execute(double mainDiameterMm,
        PipeMaterial material = PipeMaterial.DuctileIron, SteelFittingClass steelClass = SteelFittingClass.F12)
    {
        using (_transaction)
        {
            _transaction.Begin("타이밸브실 최소 폭 실측");
            // 프로브만 놓고 지우는 측정 — 성공해도 되돌림
            try { return TieValveRoomSizing.Compute(_repo.Measure(mainDiameterMm, material, steelClass)); }
            finally { _transaction.Rollback(); }
        }
    }
}
