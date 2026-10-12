namespace DHBIMWATER.Core.Piping;

public sealed record JunctionConnector(double Dx, double Dy, double Dz, long? PartnerId);
public sealed record JunctionNode(long Id, IReadOnlyList<JunctionConnector> Connectors);
public sealed record VerticalSlot(long ElementId, int Order);
public sealed record JunctionVerticalChainResult(IReadOnlyList<VerticalSlot> Slots, IReadOnlyList<string> Warnings,
    IReadOnlyList<long> Orphans);

public static class JunctionVerticalChain
{
    public const double VerticalCosine = 0.9;

    public static JunctionVerticalChainResult Resolve(string junctionId, IReadOnlyList<JunctionNode> nodes)
    {
        var byId = nodes.ToDictionary(x => x.Id);
        var slots = new List<VerticalSlot>();
        var warnings = new List<string>();
        var visited = new HashSet<long>();

        foreach (var root in nodes.Where(x => !IsVertical(x)))
        foreach (var outlet in root.Connectors.Where(IsVertical))
        {
            var previousId = root.Id;
            var nextId = outlet.PartnerId;
            var order = 0;
            while (nextId is { } id && byId.TryGetValue(id, out var node) && IsVertical(node) && visited.Add(id))
            {
                slots.Add(new VerticalSlot(id, ++order));
                if (node.Connectors.Count >= 3)
                {
                    warnings.Add($"격점 {junctionId}: {order}번째 수직 부재에서 사슬이 갈라져 오프셋을 중단했습니다");
                    break;
                }

                var onward = node.Connectors.Where(x => x.PartnerId != previousId).ToList();
                if (onward.Count == 0) break;
                var next = onward[0];
                if (next.PartnerId is null) break;
                previousId = id;
                nextId = next.PartnerId;
            }
        }

        var connected = new HashSet<long>();
        var pending = new Queue<long>(nodes.Where(x => !IsVertical(x)).Select(x => x.Id));
        while (pending.Count > 0)
        {
            var id = pending.Dequeue();
            if (!connected.Add(id) || !byId.TryGetValue(id, out var node)) continue;
            foreach (var partner in node.Connectors.Select(x => x.PartnerId))
                if (partner is { } next && byId.ContainsKey(next) && !connected.Contains(next)) pending.Enqueue(next);
        }
        var orphans = nodes.Where(IsVertical).Select(x => x.Id).Where(x => !connected.Contains(x)).ToList();
        if (orphans.Count > 0)
            warnings.Add($"격점 {junctionId}: 뿌리와 이어지지 않은 수직 부재 {orphans.Count}개 — 오프셋을 주지 않습니다");
        return new JunctionVerticalChainResult(slots, warnings, orphans);
    }

    private static bool IsVertical(JunctionNode node)
        => node.Connectors.Count > 0 && node.Connectors.All(IsVertical);

    private static bool IsVertical(JunctionConnector connector)
        => Math.Abs(connector.Dz) >= VerticalCosine;
}
