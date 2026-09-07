using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Application.UseCases.Documentation;

/// <summary>
/// 배관재료표를 드래프팅뷰에 그려 격점당 1개씩 만든다(docs/133).
/// <para>일람표(<see cref="CreatePipeMaterialScheduleUseCase"/>)와 트랜잭션을 나눠 둔다 —
/// 드래프팅뷰 그리기가 실패해도 이미 만든 일람표까지 롤백되면 안 된다.</para>
/// </summary>
public sealed class CreatePipeMaterialDraftingUseCase
{
    /// <summary>드래프팅뷰 이름 규약. Revit 뷰 이름은 일람표를 포함해 전체에서 유일해야 하므로
    /// 일람표 이름(<see cref="CreatePipeMaterialScheduleUseCase.NamePrefix"/>)과 반드시 달라야 한다.</summary>
    public const string NamePrefix = "배관재료표_도면";
    public const string UnassignedName = NamePrefix + "_미지정";

    private readonly ITransactionContext _transaction;
    private readonly IPipeMaterialDraftingRepo _repo;

    public CreatePipeMaterialDraftingUseCase(ITransactionContext transaction, IPipeMaterialDraftingRepo repo)
    {
        _transaction = transaction;
        _repo = repo;
    }

    public PipeMaterialScheduleResult Execute()
    {
        // 읽기는 트랜잭션 밖에서 먼저 한다. 만들 것이 없으면 트랜잭션을 아예 열지 않는다.
        var parts = _repo.GetParts();
        var scan = new PipeMaterialScheduleScan(
            PipeMaterialTable.JunctionIds(parts), PipeMaterialTable.UnassignedCount(parts), parts.Count);

        var targets = BuildTargets(scan);
        if (targets.Count == 0) return new PipeMaterialScheduleResult([], [], scan);

        var created = new List<string>();
        var skipped = new List<string>();

        using (_transaction)
        {
            try
            {
                _transaction.Begin("배관재료표 도면 생성");
                foreach (var (viewName, title, junctionId) in targets)
                {
                    // 같은 이름이 있으면 덮어쓰지 않는다. 사용자가 표를 손봤을 수 있다.
                    if (_repo.ViewExists(viewName)) { skipped.Add(viewName); continue; }
                    var rows = PipeMaterialTable.Build(parts, junctionId);
                    if (rows.Count == 0) continue;   // 대상 부재가 없으면 빈 표를 만들지 않는다.
                    _repo.CreateTableView(viewName, title, rows);
                    created.Add(viewName);
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

    private static List<(string ViewName, string Title, string JunctionId)> BuildTargets(PipeMaterialScheduleScan scan)
    {
        var targets = scan.JunctionIds
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(x => ($"{NamePrefix}_{x}", $"배관재료표 (격점 {x})", x))
            .ToList();
        if (scan.UnassignedCount > 0)
            targets.Add((UnassignedName, "배관재료표 (격점번호 미지정)", ""));
        return targets;
    }
}
