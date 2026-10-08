namespace DHBIMWATER.Core.Piping;

public sealed record JunctionConnector(double Dx, double Dy, double Dz, long? PartnerId);
public sealed record JunctionNode(long Id, IReadOnlyList<JunctionConnector> Connectors);
public sealed record VerticalSlot(long ElementId, int Order);
public sealed record JunctionVerticalChainResult(IReadOnlyList<VerticalSlot> Slots, IReadOnlyList<string> Warnings);

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
                if (next.PartnerId is null)
                {
                    warnings.Add($"격점 {junctionId}: 연결이 끊겨 {order}번째 부재 뒤는 오프셋을 주지 않습니다");
                    break;
                }
                previousId = id;
                nextId = next.PartnerId;
            }
        }

        return new JunctionVerticalChainResult(slots, warnings);
    }

    private static bool IsVertical(JunctionNode node)
        => node.Connectors.Count > 0 && node.Connectors.All(IsVertical);

    private static bool IsVertical(JunctionConnector connector)
        => Math.Abs(connector.Dz) >= VerticalCosine;
}
