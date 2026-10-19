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
                var slots = chain.Slots.Select((x, i) => (Slot: x, Index: i)).ToDictionary(x => x.Slot.ElementId);
                var vertical = group.Nodes.Where(x => x.Connectors.Count > 0 &&
                        x.Connectors.All(c => Math.Abs(c.Dz) >= JunctionVerticalChain.VerticalCosine))
                    .Select(x => x.Id).ToHashSet();
                // 캔버스가 실제 평면을 그리도록 수평 부재(T관·단관)까지 전부 넣음. 세트 순서 = 사슬 발견 순서
                var items = _junctions.GetPreviewItems(group)
                    .Select(x => slots.TryGetValue(x.ElementId, out var slot)
                        ? x with { Order = slot.Slot.Order, SetId = slot.Slot.SetId, IsVertical = true, Connected = true }
                        : x with { Order = null, SetId = 0, IsVertical = vertical.Contains(x.ElementId),
                            Connected = !chain.Orphans.Contains(x.ElementId) })
                    .OrderBy(x => slots.TryGetValue(x.ElementId, out var slot) ? slot.Index : -1)
                    .ThenBy(x => x.ItemNumber).ToList();
                var links = group.Nodes.SelectMany(node => node.Connectors
                        .Where(c => c.PartnerId is { } partner && partner != node.Id)
                        .Select(c => new JunctionPreviewLink(Math.Min(node.Id, c.PartnerId!.Value), Math.Max(node.Id, c.PartnerId!.Value))))
                    .Distinct().ToList();
                var room = _junctions.GetValveRoom(group);
                return new JunctionDetailPreviewJunction(group.JunctionId, _junctions.GetRoomType(),
                    group.Nodes.Count, items, PipeMaterialTable.Build(parts, group.JunctionId),
                    chain.Warnings.Concat(group.Warnings ?? []).Concat(room.Problem is { } problem ? [problem] : [])
                        .ToList(), links, room.Room);
            }).ToList();
        var diagnostics = new List<JunctionSymbolShapeDiagnostic>();
        var elementNames = previews.SelectMany(x => x.Items).ToDictionary(x => x.ElementId, x => x.Name);
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
                            Items = preview.Items.Select(item =>
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
