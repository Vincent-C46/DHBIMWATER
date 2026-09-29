namespace DHBIMWATER.Core.Gis;

/// <summary>
/// 같은 선형의 인접 곡관끼리 다리(차감) 구간이 겹치는지 판정한다(docs/217 § B).
/// 겹침은 엑셀 곡관유형·좌표 오류로 보고 배치는 그대로 두되 <see cref="BendPlacement.HasLegOverlap"/>로 적색 표시 대상에 올린다.
/// </summary>
public static class BendLegOverlapDetector
{
    private const double ToleranceM = 1e-6;
    private const double MmToM = 0.001;

    /// <returns>
    /// Placements: 겹친 곡관의 <see cref="BendPlacement.HasLegOverlap"/>를 켠 목록(입력 순서 유지. 이미 켜진 값은 유지).
    /// Pairs: 겹친 인접 쌍(상류, 하류) — 경고 문구용.
    /// </returns>
    public static (IReadOnlyList<BendPlacement> Placements, IReadOnlyList<(BendPlacement Upstream, BendPlacement Downstream)> Pairs) Mark(
        IReadOnlyList<BendPlacement> placements, IReadOnlyList<PipeAlignment> alignments)
    {
        var overlapped = new HashSet<BendPlacement>(ReferenceEqualityComparer.Instance);
        var pairs = new List<(BendPlacement Upstream, BendPlacement Downstream)>();
        foreach (var group in placements.GroupBy(x => x.AlignmentIndex))
        {
            var profile = new AlignmentStationProfile(alignments[group.Key]);
            var ordered = group.OrderBy(x => x.VertexIndex).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                var upstream = ordered[i - 1];
                var downstream = ordered[i];
                // 3D 선형거리 기준 — 샘플러 차감 구간(BuildExclusions)과 같은 축이다.
                var upstreamEnd = profile.DistanceAtVertex(upstream.VertexIndex) + upstream.DownstreamLegMm * MmToM;
                var downstreamStart = profile.DistanceAtVertex(downstream.VertexIndex) - downstream.UpstreamLegMm * MmToM;
                if (upstreamEnd <= downstreamStart + ToleranceM) continue;
                pairs.Add((upstream, downstream));
                overlapped.Add(upstream);
                overlapped.Add(downstream);
            }
        }

        if (overlapped.Count == 0) return (placements, pairs);
        var marked = placements.Select(x => overlapped.Contains(x) ? x with { HasLegOverlap = true } : x).ToList();
        return (marked, pairs);
    }
}
