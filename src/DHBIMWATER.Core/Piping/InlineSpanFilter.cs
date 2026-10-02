namespace DHBIMWATER.Core.Piping;

/// <summary>엣지 위 인라인 부속 구간 하나. 한 부속(<paramref name="OwnerId"/>)이 구간을 여럿 가질 수 있다(바이패스 세트의 A 두 개 등).</summary>
/// <param name="Priority">겹침·짧은 틈에서 누구를 남길지. 큰 쪽이 남는다(<see cref="InlineSpanFilter"/>의 상수).</param>
public readonly record struct InlineSpanCandidate(Guid OwnerId, OccupiedSpan Span, int Priority);

/// <param name="Kept">관 분할(<see cref="PipeSegmentPlan.Build"/>)에 넘길 남은 구간.</param>
/// <param name="Dropped">건너뛴 부속과 사유.</param>
public sealed record InlineSpanFilterResult(IReadOnlyList<OccupiedSpan> Kept, IReadOnlyDictionary<Guid, string> Dropped);

/// <summary>인라인 부속의 <b>위치 오류</b>를 그 부속만 건너뛰는 것으로 바꾼다(docs/251 § B).
/// <para>종전에는 배관 밖으로 나감·겹침·짧은 틈이 <see cref="PipeSegmentPlan.Build"/> 오류가 되어 모델 생성 전체가
/// 롤백됐다(PL-301). 관 자체는 부속을 빼면 항상 놓을 수 있으므로, 문제 부속만 빼고 빈 자리는 관이 채운다.</para>
/// <para>판정 문턱(허용오차·최소 조각)은 <see cref="PipeSegmentPlan"/>과 같게 둔다 — 여기서 통과한 구간이 관 분할에서
/// 다시 오류가 나면 안 된다.</para></summary>
public static class InlineSpanFilter
{
    public const int JointPriority = 0;
    public const int ValvePriority = 1;
    public const int BypassSetPriority = 2;
    private const double Tolerance = 1e-6;

    public static InlineSpanFilterResult Filter(double edgeLengthMm, IEnumerable<InlineSpanCandidate> candidates,
        double minSegmentMm = PipeSegmentPlan.MinSegmentMm)
    {
        var items = candidates
            .Select(x => x with { Span = x.Span.StartMm <= x.Span.EndMm ? x.Span : new OccupiedSpan(x.Span.EndMm, x.Span.StartMm) })
            .ToList();
        var dropped = new Dictionary<Guid, string>();

        // 1) 배관 밖. 다른 부속과 무관한 판정이라 먼저 한 번에 거른다.
        foreach (var item in items)
        {
            if (dropped.ContainsKey(item.OwnerId)) continue;
            var span = item.Span;
            if (span.StartMm >= -Tolerance && span.EndMm <= edgeLengthMm + Tolerance) continue;
            var widthMm = span.EndMm - span.StartMm;
            dropped[item.OwnerId] = widthMm > edgeLengthMm + Tolerance
                ? FormattableString.Invariant($"부속 폭 {widthMm:N0}mm가 배관 {edgeLengthMm:N0}mm보다 깁니다.")
                : FormattableString.Invariant(
                    $"부속이 배관 밖으로 {Math.Max(-span.StartMm, span.EndMm - edgeLengthMm):N0}mm 나갑니다(배관 {edgeLengthMm:N0}mm). 절점 부속 몸통과 겹친 자리입니다.");
        }

        // 2)·3) 겹침과 짧은 틈. 하나를 빼면 틈이 새로 생기므로 바뀌는 것이 없을 때까지 앞에서부터 다시 본다.
        while (true)
        {
            var kept = items.Where(x => !dropped.ContainsKey(x.OwnerId)).OrderBy(x => x.Span.StartMm).ToList();
            if (!TryFindConflict(kept, edgeLengthMm, minSegmentMm, out var ownerId, out var reason)) break;
            dropped[ownerId] = reason;
        }

        return new InlineSpanFilterResult(
            items.Where(x => !dropped.ContainsKey(x.OwnerId)).Select(x => x.Span).ToList(),
            dropped);
    }

    private static bool TryFindConflict(IReadOnlyList<InlineSpanCandidate> ordered, double edgeLengthMm, double minSegmentMm,
        out Guid ownerId, out string reason)
    {
        // 겹침을 먼저 본다 — 겹친 상태의 "틈"은 의미가 없다.
        for (var i = 1; i < ordered.Count; i++)
            for (var j = 0; j < i; j++)
            {
                var a = ordered[j];
                var b = ordered[i];
                if (a.OwnerId == b.OwnerId) continue;
                var overlapMm = a.Span.EndMm - b.Span.StartMm;
                if (overlapMm <= Tolerance) continue;
                ownerId = Loser(a, b).OwnerId;
                reason = FormattableString.Invariant($"다른 부속과 {Math.Min(overlapMm, b.Span.EndMm - b.Span.StartMm):N0}mm 겹칩니다.");
                return true;
            }

        // 짧은 틈. 엣지 끝(0 / 길이)도 경계로 본다 — 그 틈에는 맞닿은 부속 하나뿐이다.
        InlineSpanCandidate? previous = null;
        var cursor = 0.0;
        foreach (var item in ordered)
        {
            var gap = item.Span.StartMm - cursor;
            var sameOwner = previous is { } p && p.OwnerId == item.OwnerId;
            if (!sameOwner && IsShort(gap, minSegmentMm))
            {
                ownerId = previous is { } prev ? Loser(prev, item).OwnerId : item.OwnerId;
                reason = ShortGapReason(gap, minSegmentMm);
                return true;
            }
            if (item.Span.EndMm >= cursor) { cursor = item.Span.EndMm; previous = item; }
        }
        if (previous is { } last && IsShort(edgeLengthMm - cursor, minSegmentMm))
        {
            ownerId = last.OwnerId;
            reason = ShortGapReason(edgeLengthMm - cursor, minSegmentMm);
            return true;
        }

        ownerId = Guid.Empty;
        reason = string.Empty;
        return false;
    }

    /// <summary>우선순위가 낮은 쪽, 같으면 축 뒤쪽(<paramref name="later"/>)을 뺀다.</summary>
    private static InlineSpanCandidate Loser(InlineSpanCandidate earlier, InlineSpanCandidate later)
        => earlier.Priority < later.Priority ? earlier : later;

    private static bool IsShort(double gapMm, double minSegmentMm) => gapMm > Tolerance && gapMm < minSegmentMm;

    private static string ShortGapReason(double gapMm, double minSegmentMm)
        => FormattableString.Invariant(
            $"옆 부속·절점과의 틈이 {gapMm:N0}mm라 관을 넣을 수 없습니다(최소 {minSegmentMm:N0}mm).");
}
