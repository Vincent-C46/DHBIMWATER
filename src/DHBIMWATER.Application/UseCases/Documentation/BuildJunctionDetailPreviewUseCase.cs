using DHBIMWATER.Application.DTOs.Documentation;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Application.UseCases.Documentation;

public sealed class BuildJunctionDetailPreviewUseCase
{
    private readonly IJunctionDetailRepo _junctions;
    private readonly IPipeMaterialDraftingRepo _drafting;
    private readonly ITransactionContext _transaction;
    private readonly IJunctionSymbolShapeRepo _shapes;

    public BuildJunctionDetailPreviewUseCase(IJunctionDetailRepo junctions, IPipeMaterialDraftingRepo drafting,
        ITransactionContext transaction, IJunctionSymbolShapeRepo shapes)
    {
        _junctions = junctions;
        _drafting = drafting;
        _transaction = transaction;
        _shapes = shapes;
    }

    public JunctionDetailPreview Execute()
    {
        var groups = _junctions.GetJunctions();
        var parts = _drafting.GetParts();
        var previews = groups.OrderBy(x => x.JunctionId, StringComparer.Ordinal)
            .Select(group =>
            {
                var chain = JunctionVerticalChain.Resolve(group.JunctionId, group.Nodes);
                var order = chain.Slots.ToDictionary(x => x.ElementId, x => x.Order);
                var items = _junctions.GetPreviewItems(group)
                    .Select(x => x with { Order = order.TryGetValue(x.ElementId, out var value) ? value : null })
                    .Where(x => x.Order is not null || chain.Orphans.Contains(x.ElementId))
                    .Select(x => chain.Orphans.Contains(x.ElementId) ? x with { Order = null, Connected = false } : x)
                    .OrderBy(x => x.Order ?? int.MaxValue).ThenBy(x => x.ItemNumber).ToList();
                return new JunctionDetailPreviewJunction(group.JunctionId, _junctions.GetRoomType(),
                    group.Nodes.Count, items, PipeMaterialTable.Build(parts, group.JunctionId),
                    chain.Warnings.Concat(group.Warnings ?? []).ToList());
            }).ToList();
        var diagnostics = new List<JunctionSymbolShapeDiagnostic>();
        var elementNames = previews.SelectMany(x => x.VerticalItems).ToDictionary(x => x.ElementId, x => x.Name);
        var elementIds = elementNames.Keys.ToList();
        if (elementIds.Count > 0)
        {
            try
            {
                using (_transaction)
                {
                    try
                    {
                        _transaction.Begin("기호선 읽기");
                        var result = _shapes.Read(elementIds);
                        diagnostics.AddRange(result.Diagnostics);
                        previews = previews.Select(preview => preview with
                        {
                            VerticalItems = preview.VerticalItems.Select(item =>
                                item with { Shape = result.Shapes.GetValueOrDefault(item.ElementId) }).ToList()
                        }).ToList();
                    }
                    finally
                    {
                        _transaction.Rollback();
                    }
                }
            }
            catch (DhbimwaterException ex) when (ex.Code == ErrorCodes.NoActiveDocument) { throw; }
            catch (Exception ex)
            {
                diagnostics.AddRange(elementNames.Select(pair => new JunctionSymbolShapeDiagnostic(
                    pair.Key, pair.Value, 0, 0, 0, 0, "확인 안 함", $"기호선 읽기 실패: {ex.Message}")));
            }
        }
        return new JunctionDetailPreview(previews, PipeMaterialTable.Build(parts, ""), _junctions.GetTagTypes(),
            parts.Count, diagnostics);
    }
}
