using DHBIMWATER.Application.DTOs.Documentation;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Application.UseCases.Documentation;

public sealed class BuildJunctionDetailPreviewUseCase
{
    private readonly IJunctionDetailRepo _junctions;
    private readonly IPipeMaterialDraftingRepo _drafting;

    public BuildJunctionDetailPreviewUseCase(IJunctionDetailRepo junctions, IPipeMaterialDraftingRepo drafting)
    {
        _junctions = junctions;
        _drafting = drafting;
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
                    .Where(x => x.Order is not null)
                    .Select(x => x.Connected ? x : x with { Order = null })
                    .OrderBy(x => x.Order ?? int.MaxValue).ThenBy(x => x.ItemNumber).ToList();
                return new JunctionDetailPreviewJunction(group.JunctionId, _junctions.GetRoomType(),
                    group.Nodes.Count, items, PipeMaterialTable.Build(parts, group.JunctionId), chain.Warnings);
            }).ToList();
        return new JunctionDetailPreview(previews, PipeMaterialTable.Build(parts, ""), _junctions.GetTagTypes());
    }
}
