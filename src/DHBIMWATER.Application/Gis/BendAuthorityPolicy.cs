using DHBIMWATER.Core.Gis;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Application.Gis;

public sealed record BendAuthorityResult(
    IReadOnlyList<BendResolution> All,
    IReadOnlyList<BendResolution> Kept,
    int SuppressedCount,
    IReadOnlyList<string> Warnings);

/// <summary>엑셀 곡관유형 열의 권위 범위를 자동 곡관 판정에 적용한다.</summary>
public static class BendAuthorityPolicy
{
    /// <summary>절점 병합으로 여러 정점이 한 절점에 모이므로, 경고에 측점을 찍으려면 출처 정점이 필요하다.</summary>
    private sealed record VertexSource(bool IsAuthoritative, string? Raw, int AlignmentIndex, int VertexIndex);

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

        var conflicts = new List<(int NodeId, List<(double Angle, int AlignmentIndex, int VertexIndex)> Picks)>();

        foreach (var (nodeId, nodeSources) in sources)
        {
            // docs/149 §A: 병합된 절점에 표준각 지정이 여러 개 오면 마지막 것이 이긴다.
            // 채택 규칙은 그대로 두고, 무엇이 버려졌는지만 경고로 남기기 위해 지정을 모아둔다.
            var picks = new List<(double Angle, int AlignmentIndex, int VertexIndex)>();

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
                // 표준각은 관종마다 다르다(강관은 5⅝° 포함, docs/211 § D-4). 정점이 속한 선형의 등급으로 관종을 본다.
                var material = PipeKindCatalog.MaterialOf(alignments[source.AlignmentIndex].PipeKind) ?? PipeMaterial.DuctileIron;
                if (BendingRuleCatalog.For(material).StandardAngles.Any(x => Math.Abs(x - angle) <= 1e-6))
                {
                    forced[nodeId] = angle;
                    picks.Add((angle, source.AlignmentIndex, source.VertexIndex));
                    continue;
                }

                // 비표준 지정값은 지시서 검증 #10에 따라 자동판정을 유지한다.
                automaticFallback.Add(nodeId);
                nonStandardCount++;
            }

            // 같은 각도가 중복된 경우는 결과가 같으므로 경고하지 않는다.
            if (picks.Select(x => x.Angle).Distinct().Count() > 1) conflicts.Add((nodeId, picks));
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

        warnings.AddRange(BuildConflictWarnings(alignments, conflicts));
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
                list.Add(new VertexSource(alignment.VertexBendKinds is not null, raw, a, v));
            }
        }
        return result;
    }

    /// <summary>
    /// docs/149 §A. 스냅 허용오차 안에서 한 절점으로 합쳐진 정점들이 서로 다른 곡관유형을 지정했을 때,
    /// 어떤 지정이 버려졌는지 절점마다 1건씩 알린다. 채택 결과(마지막 지정각)는 바꾸지 않는다.
    /// </summary>
    private static IEnumerable<string> BuildConflictWarnings(
        IReadOnlyList<PipeAlignment> alignments,
        IReadOnlyList<(int NodeId, List<(double Angle, int AlignmentIndex, int VertexIndex)> Picks)> conflicts)
    {
        foreach (var conflict in conflicts.OrderBy(x => x.Picks[0].AlignmentIndex).ThenBy(x => x.Picks[0].VertexIndex))
        {
            var labels = string.Join(", ", conflict.Picks
                .Select(x => AlignmentVertexLabel.Format(alignments[x.AlignmentIndex], x.VertexIndex)));
            var angles = string.Join(", ", conflict.Picks.Select(x => $"{x.Angle:0.##}˚"));
            var applied = $"{conflict.Picks[^1].Angle:0.##}˚";
            yield return $"{labels}: 좌표가 스냅 허용오차 안이라 한 절점으로 합쳐졌습니다. "
                         + $"지정된 곡관유형 {angles} 중 {applied} 하나만 적용합니다.";
        }
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
