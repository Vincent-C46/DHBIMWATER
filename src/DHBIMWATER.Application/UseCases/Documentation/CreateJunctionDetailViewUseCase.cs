using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.DTOs.Documentation;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Application.UseCases.Documentation;

public sealed class CreateJunctionDetailViewUseCase
{
    public const string NamePrefix = "격점상세도";

    private readonly ITransactionContext _transaction;
    private readonly IJunctionDetailRepo _repo;

    public CreateJunctionDetailViewUseCase(ITransactionContext transaction, IJunctionDetailRepo repo)
    {
        _transaction = transaction;
        _repo = repo;
    }

    public JunctionDetailResult Execute(bool resetOnly = false)
        => Execute(new JunctionDetailViewOptions(true, JunctionDetailParameters.StepPaperMm,
            JunctionDetailParameters.ViewScale, true, 0), resetOnly);

    public JunctionDetailResult Execute(JunctionDetailViewOptions options, bool resetOnly = false)
    {
        var groups = _repo.GetJunctions();
        var created = new List<string>();
        var updated = new List<string>();
        var reset = new List<string>();
        var skipped = new List<string>();
        var warnings = new List<string>();
        if (groups.Count == 0) return new JunctionDetailResult(created, updated, reset, skipped, warnings);

        using (_transaction)
        {
            try
            {
                _transaction.Begin(resetOnly ? "격점 심볼 오프셋 초기화" : "격점 평면 상세도 생성·갱신");
                foreach (var group in groups)
                {
                    var viewName = PipeMaterialViewName.For(NamePrefix, group.JunctionId);
                    try
                    {
                        if (resetOnly)
                        {
                            warnings.AddRange(_repo.ResetOffsets(group));
                            reset.Add(viewName);
                            continue;
                        }
                        var chain = JunctionVerticalChain.Resolve(group.JunctionId, group.Nodes);
                        var disconnectedVertical = group.Nodes.Where(node => node.Connectors.Count > 0
                                && node.Connectors.All(connector => Math.Abs(connector.Dz) >= JunctionVerticalChain.VerticalCosine)
                                && node.Connectors.Any(connector => connector.PartnerId is null))
                            .Select(node => node.Id).ToHashSet();
                        var slots = options.ExpandVertical
                            ? chain.Slots.Where(slot => !disconnectedVertical.Contains(slot.ElementId)).ToList()
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
