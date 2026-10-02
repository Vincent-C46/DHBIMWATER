namespace DHBIMWATER.Core.Piping;

/// <summary>이음관 밀착 규칙(docs/175 § D-2, docs/250). 거리는 모두 엣지 배치 축(시작 앵커부터, mm) 기준이다.
/// <para>캔버스는 실척이 아니라 사용자가 밸브·절점 부속 옆에 정확히 붙여 찍을 수 없다. 그래서 이음관 구간이
/// 밸브 스팬 또는 엣지 끝의 절점 부속 커넥터와 겹치거나 허용 거리 안이면 그 경계에 폭만큼 이어 붙인다 —
/// 그 사이에 단관이 끼거나 10mm 미만 조각 오류로 생성이 중단되지 않게 한다.</para>
/// <para>절점 쪽은 그 끝에 절점 부속의 <b>실측 커넥터 앵커</b>가 있을 때만 후보다. 축 0 / <c>edgeLengthMm</c>이
/// 바로 그 커넥터 위치이므로, 이어 붙이면 이음관 커넥터가 절점 부속 커넥터와 일치해 연결된다.
/// 앵커가 없는 끝(끝점, 건너뛴 절점 부속)은 비워 둔 자리라 붙이지 않는다.</para>
/// <para>후보가 여럿이면 틈(절대값)이 가장 작은 것, 동률이면 절점 부속이 우선이다. 이음관끼리의 밀착은 하지 않는다(범위 밖).</para></summary>
public static class InlineJointSnap
{
    /// <returns>밀착한 구간. 후보가 없으면 null — 그때는 찍은 자리를 그대로 쓴다.</returns>
    public static OccupiedSpan? Resolve(
        double centerMm, double widthMm,
        IEnumerable<OccupiedSpan> valveSpans,
        double edgeLengthMm, bool startAnchored, bool endAnchored,
        double toleranceMm)
    {
        OccupiedSpan? best = null;
        var bestGap = double.MaxValue;

        // 절점 부속 커넥터. 겹침(음수 틈)은 하한 없이 밀착한다 — 캔버스 절점 중심과 커넥터 사이에 부속 레그 길이만큼
        // 차이가 있어, "T 바로 옆"을 찍으면 대개 부속 몸통 안쪽(축 0 앞)으로 떨어진다(docs/250 § 0).
        // 절점을 밸브보다 먼저 보고 밸브는 더 작을 때만 바꾸므로 동률이면 절점이 남는다.
        if (startAnchored)
            Consider((centerMm - widthMm / 2) - 0, new OccupiedSpan(0, widthMm));
        if (endAnchored)
            Consider(edgeLengthMm - (centerMm + widthMm / 2), new OccupiedSpan(edgeLengthMm - widthMm, edgeLengthMm));

        foreach (var valve in valveSpans)
        {
            // 이음관이 밸브 앞(시작쪽)에 오는 경우와 뒤(끝쪽)에 오는 경우의 틈. 음수면 겹친 것이다.
            var gapBefore = valve.StartMm - (centerMm + widthMm / 2);
            var gapAfter = (centerMm - widthMm / 2) - valve.EndMm;
            // 중심이 밸브 스팬 안이면 가까운 쪽 경계로 보낸다.
            var beforeSide = centerMm <= (valve.StartMm + valve.EndMm) / 2;
            var gap = beforeSide ? gapBefore : gapAfter;
            if (gap < -widthMm) continue;
            Consider(gap, beforeSide
                ? new OccupiedSpan(valve.StartMm - widthMm, valve.StartMm)
                : new OccupiedSpan(valve.EndMm, valve.EndMm + widthMm));
        }
        return best;

        void Consider(double gap, OccupiedSpan span)
        {
            if (gap > toleranceMm) return;
            var distance = Math.Abs(gap);
            if (distance >= bestGap) return;
            bestGap = distance;
            best = span;
        }
    }
}
