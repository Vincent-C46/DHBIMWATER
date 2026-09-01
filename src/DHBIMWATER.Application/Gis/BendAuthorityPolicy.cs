using DHBIMWATER.Core.Gis;

namespace DHBIMWATER.Application.Gis;

public sealed record BendAuthorityResult(
    IReadOnlyList<BendResolution> All,
    IReadOnlyList<BendResolution> Kept,
    int SuppressedCount,
    IReadOnlyList<string> Warnings);

/// <summary>엑셀 곡관유형 열의 권위 범위를 자동 곡관 판정에 적용한다.</summary>
public static class BendAuthorityPolicy
{
    private sealed record VertexSource(bool IsAuthoritative, string? Raw);

    public static BendAuthorityResult Evaluate(
        IReadOnlyList<PipeAlignment> alignments,
        PipeNetworkGraph graph,
        IReadOnlyList<NodeClassification> nodes,
        BendSettings settings,
        double snapTolerance)
    {
        var lookup = new AlignmentVertexNodeLookup(graph.Nodes, snapTolerance);
        var sources = BuildSources(alignments, lookup);
        var forced = new Dictionary<int, double>();
        var automaticFallback = new HashSet<int>();
        var warnings = new List<string>();
        var unreadableCount = 0;
        var nonStandardCount = 0;

        foreach (var (nodeId, nodeSources) in sources)
        {
            foreach (var source in nodeSources.Where(x => x.IsAuthoritative && !string.IsNullOrWhiteSpace(x.Raw)))
            {
                // 각도로 못 읽는 표기(예: "45°곡관", "MJ45")는 지정이 없는 것과 다르다.
                // 곡관을 지우면 사용자가 넣은 값이 조용히 사라지므로, 비표준 지정각과 같이 자동판정으로 넘긴다.
                if (!BendKindParser.TryParseAngle(source.Raw, out var angle))
                {
                    automaticFallback.Add(nodeId);
                    unreadableCount++;
                    continue;
                }
                if (JointDeflectionRule.StandardAngles.Any(x => Math.Abs(x - angle) <= 1e-6))
                {
                    forced[nodeId] = angle;
                    continue;
                }

                // 비표준 지정값은 지시서 검증 #10에 따라 자동판정을 유지한다.
                automaticFallback.Add(nodeId);
                nonStandardCount++;
            }
        }

        var all = BendResolver.ResolveAll(nodes, settings, forced);
        var authoritativeNodes = sources
            .Where(x => x.Value.Any(s => s.IsAuthoritative) && x.Value.All(s => s.IsAuthoritative))
            .Select(x => x.Key)
            .ToHashSet();

        // 비권위 선형이 같은 절점에 붙으면 엑셀 한 장이 다른 소스의 곡관을 지우지 못한다.
        var kept = all.Where(x => !authoritativeNodes.Contains(x.NodeId)
                                  || forced.ContainsKey(x.NodeId)
                                  || automaticFallback.Contains(x.NodeId)).ToList();
        var dropped = all.Where(x => authoritativeNodes.Contains(x.NodeId)
                                     && !forced.ContainsKey(x.NodeId)
                                     && !automaticFallback.Contains(x.NodeId)
                                     && x.Kind != BendResolutionKind.None).ToList();

        if (nonStandardCount > 0) warnings.Add($"비표준 지정각 자동판정 {nonStandardCount}건");
        warnings.AddRange(BuildUnreadableWarnings(unreadableCount));
        warnings.AddRange(BuildForcedMismatchWarnings(all, forced));
        return new BendAuthorityResult(all, kept, dropped.Count, warnings);
    }

    private static Dictionary<int, List<VertexSource>> BuildSources(
        IReadOnlyList<PipeAlignment> alignments,
        AlignmentVertexNodeLookup lookup)
    {
        var result = new Dictionary<int, List<VertexSource>>();
        for (var a = 0; a < alignments.Count; a++)
        {
            var alignment = alignments[a];
            for (var v = 0; v < alignment.Vertices.Count; v++)
            {
                if (lookup.Find(alignment.Vertices[v]) is not { } nodeId) continue;
                if (!result.TryGetValue(nodeId, out var list)) result[nodeId] = list = [];
                var raw = v > 0 && v < alignment.Vertices.Count - 1
                          && alignment.VertexBendKinds is { } kinds && v < kinds.Count
                    ? kinds[v]
                    : null;
                list.Add(new VertexSource(alignment.VertexBendKinds is not null, raw));
            }
        }
        return result;
    }

    private static IEnumerable<string> BuildUnreadableWarnings(int unreadableCount)
    {
        if (unreadableCount > 0) yield return $"곡관유형 각도 해석 실패 {unreadableCount}건 (자동판정)";
    }

    private static IEnumerable<string> BuildForcedMismatchWarnings(
        IReadOnlyList<BendResolution> resolutions,
        IReadOnlyDictionary<int, double> forced)
    {
        var count = 0;
        foreach (var item in forced)
        {
            var resolution = resolutions.FirstOrDefault(x => x.NodeId == item.Key);
            if (resolution is null || Math.Abs(item.Value - resolution.DeflectionDeg) <= resolution.EffectiveAllowableDeg + 1e-9) continue;
            count++;
        }
        if (count > 0) yield return $"허용 범위 초과 곡관 {count}건";
    }
}
