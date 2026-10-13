using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Application.UseCases.Documentation;

/// <summary>
/// 배관재료표를 드래프팅뷰에 그려 격점당 1개씩 만든다(docs/133).
/// <para>일람표(<see cref="CreatePipeMaterialScheduleUseCase"/>)와 트랜잭션을 나눠 둔다 —
/// 드래프팅뷰 그리기가 실패해도 이미 만든 일람표까지 롤백되면 안 된다.</para>
/// <para>일람표는 필터 기반 live 뷰라 모델이 바뀌면 값이 저절로 따라오지만, 드래프팅뷰는 선·문자를 박아 넣은
/// 정적 도면이라 그렇지 않다. 그래서 여기에만 갱신 경로가 있다(docs/173).</para>
/// <para>docs/291: 갱신 여부는 사용자가 고르지 않는다. 표 서명이 저장값과 같으면 그대로, 다르면 애드인이 그린 요소만 다시 그린다.</para>
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

    /// <summary>이미 있어 갱신 대상이 되는 도면 뷰 이름. 확인 대화상자를 띄울지 판단하는 용도다(docs/173).
    /// <para>읽기 전용이라 트랜잭션을 열지 않는다. 결과를 보관하지 않고 <see cref="Execute"/>가 다시 읽는다 —
    /// 그 사이 모델이 바뀌어도 실제로 그리는 값은 항상 Execute 시점의 값이어야 하기 때문이다.</para></summary>
    // TODO: docs/291 이후 호출처 없음(확인 대화상자 제거). 테스트만 사용
    public IReadOnlyList<string> FindExistingViews()
    {
        var parts = _repo.GetParts();
        var existing = _repo.GetExistingViewNames();
        return BuildTargets(PipeMaterialScan.Of(parts))
            .Where(x => PipeMaterialTable.Build(parts, x.JunctionId).Count > 0)   // 빈 표는 대상이 아니다
            .Select(x => x.ViewName)
            .Where(existing.Contains)
            .ToList();
    }

    /// <summary>이름이 같은 도면 뷰가 있으면 서명 비교 → 같으면 <see cref="PipeMaterialScheduleResult.Unchanged"/>,
    /// 다르거나 비교 불가면 다시 그림(docs/291).</summary>
    public PipeMaterialScheduleResult Execute(bool includeTypeImage = true)
    {
        // 읽기는 트랜잭션 밖에서 먼저 한다. 만들 것이 없으면 트랜잭션을 아예 열지 않는다.
        var parts = _repo.GetParts();
        var scan = PipeMaterialScan.Of(parts);

        var targets = BuildTargets(scan);
        if (targets.Count == 0) return new PipeMaterialScheduleResult([], [], scan);

        var created = new List<string>();
        var updated = new List<string>();
        var unchanged = new List<string>();
        var skipped = new List<string>();

        using (_transaction)
        {
            try
            {
                _transaction.Begin("배관재료표 도면 생성·갱신");
                // 뷰 이름 존재 확인은 격점마다 전체 뷰를 훑지 않고 한 번 받아 둔다(docs/208 § D-3).
                var existing = new HashSet<string>(_repo.GetExistingViewNames(), StringComparer.Ordinal);
                foreach (var (viewName, title, junctionId) in targets)
                {
                    // rows를 먼저 만든다 — 대상 부재가 없으면 만들 것도 갱신할 것도 없다.
                    var rows = PipeMaterialTable.Build(parts, junctionId);
                    if (rows.Count == 0) continue;   // 빈 표는 만들지도, 기존 표를 지우지도 않는다.

                    // 격점 하나가 실패해도 나머지 격점은 살린다(docs/208 § C). Repo가 SubTransaction으로 그 격점만 되돌린다.
                    try
                    {
                        var signature = PipeMaterialTableSignature.Of(title, rows, includeTypeImage);
                        if (existing.Contains(viewName))
                        {
                            // 내용 같으면 손대지 않음(docs/291)
                            if (_repo.GetTableSignature(viewName) == signature) { unchanged.Add(viewName); continue; }
                            // 그 이름을 다른 종류의 뷰가 쓰고 있으면 지우지 않고 건너뛴다(docs/173 § B).
                            if (!_repo.RedrawTableView(viewName, title, rows, includeTypeImage, signature)) { skipped.Add(viewName); continue; }
                            updated.Add(viewName);
                            continue;
                        }

                        _repo.CreateTableView(viewName, title, rows, includeTypeImage, signature);
                        created.Add(viewName);
                        existing.Add(viewName);
                    }
                    catch (DhbimwaterException ex) when (ex.Code == ErrorCodes.NoActiveDocument) { throw; }
                    catch (Exception ex)
                    {
                        skipped.Add($"{viewName} — [{ErrorCodes.MT_DraftingCreateFailed}] {ex.Message}");
                    }
                }
                _transaction.Commit();
                return new PipeMaterialScheduleResult(created, skipped, scan) { Updated = updated, Unchanged = unchanged };
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
            .Select(x => (PipeMaterialViewName.For(NamePrefix, x), $"배관재료표 (격점 {x})", x))
            .ToList();
        if (scan.UnassignedCount > 0)
            targets.Add((UnassignedName, "배관재료표 (격점번호 미지정)", ""));
        return targets;
    }
}
