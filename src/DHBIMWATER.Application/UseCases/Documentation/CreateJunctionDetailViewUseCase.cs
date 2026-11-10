using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.DTOs.Documentation;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Application.UseCases.Documentation;

public sealed class CreateJunctionDetailViewUseCase
{
    // 2026-11-10 격점상세도 → 배관상세도. 옛 이름 뷰·시트는 못 찾아 새로 만듦(사용자 결정)
    public const string NamePrefix = "배관상세도";

    private readonly ITransactionContext _transaction;
    private readonly IJunctionDetailRepo _repo;

    public CreateJunctionDetailViewUseCase(ITransactionContext transaction, IJunctionDetailRepo repo)
    {
        _transaction = transaction;
        _repo = repo;
    }

    public JunctionDetailResult Execute(bool resetOnly = false)
        => Execute(new JunctionDetailViewOptions(true, JunctionDetailParameters.StepMm,
            JunctionDetailParameters.ViewScale, true, 0), resetOnly);

    public JunctionDetailResult Execute(JunctionDetailViewOptions options, bool resetOnly = false)
    {
        var groups = _repo.GetJunctions();
        var created = new List<string>();
        var updated = new List<string>();
        var reset = new List<string>();
        var skipped = new List<string>();
        var warnings = new List<string>();
        if (groups.Count == 0)
        {
            warnings.Add("배관 이음쇠·배관 밸브류 부재가 없어 평면 상세도를 만들지 않았습니다.");
            return new JunctionDetailResult(created, updated, reset, skipped, warnings);
        }

        using (_transaction)
        {
            try
            {
                _transaction.Begin(resetOnly ? "배관상세도 심볼 오프셋 초기화" : "배관상세도 생성·갱신");
                foreach (var group in groups)
                {
                    var viewName = string.IsNullOrWhiteSpace(group.JunctionId) ? "배관상세도_미지정"
                        : PipeMaterialViewName.For(NamePrefix, group.JunctionId);
                    if (group.Warnings is not null) warnings.AddRange(group.Warnings);
                    try
                    {
                        if (resetOnly)
                        {
                            warnings.AddRange(_repo.ResetOffsets(group));
                            reset.Add(viewName);
                            continue;
                        }
                        var chain = JunctionVerticalChain.Resolve(group.JunctionId, group.Nodes);
                        var slots = options.ExpandVertical
                            ? chain.Slots
                            : [];
                        var change = _repo.CreateOrUpdateView(group, slots, options);
                        warnings.AddRange(chain.Warnings);
                        warnings.AddRange(change.Warnings);
                        if (change.Created) created.Add(viewName);
                        else updated.Add(viewName);
                    }
                    catch (DhbimwaterException ex) when (ex.Code == ErrorCodes.NoActiveDocument) { throw; }
                    catch (Exception ex)
                    {
                        skipped.Add($"{viewName}: {ex.Message}");
                    }
                }
                _transaction.Commit();
            }
            catch
            {
                _transaction.Rollback();
                throw;
            }
        }
        return new JunctionDetailResult(created, updated, reset, skipped,
            warnings.Distinct(StringComparer.Ordinal).ToList());
    }
}
