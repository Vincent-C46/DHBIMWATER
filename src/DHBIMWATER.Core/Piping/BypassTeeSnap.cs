namespace DHBIMWATER.Core.Piping;

/// <summary>바이패스 세트 하나가 본관에서 차지하는 두 밸브부관 A 구간(엣지 배치 축, mm).
/// 구간 양 끝이 A 주관 커넥터다. <paramref name="First"/> = 엣지 시작 쪽 A, <paramref name="Second"/> = 끝 쪽 A.</summary>
public readonly record struct BypassTeePair(OccupiedSpan First, OccupiedSpan Second);

/// <summary>본관 밸브를 세트 A 안쪽 커넥터에 밀착하는 규칙(docs/257 § A).
/// <para>캔버스의 세트 그림은 개략이라 사용자가 A 위치를 알 수 없다 — 두 A 사이에 찍은 밸브는 거리와 무관하게
/// 가까운 A의 안쪽 커넥터에 붙인다(2026-10-01 사용자 결정 (가)). 반대쪽 틈은 관 분할이 단관으로 채운다.</para>
/// <para>A 바깥쪽은 여기서 다루지 않는다 — A 구간을 <see cref="InlineJointSnap"/>의 밸브 스팬 후보로 넘겨 허용 거리 규칙을 따른다.</para></summary>
public static class BypassTeeSnap
{
    /// <returns>중심이 어떤 세트의 두 A 중심 사이면 가까운 A 안쪽 커넥터에 붙인 구간. 아니면 null.
    /// 밸브가 안쪽 틈보다 넓어 반대쪽 A와 겹치는 것은 판정하지 않는다 — <see cref="InlineSpanFilter"/>가 그 밸브를 건너뛴다.</returns>
    public static OccupiedSpan? InsideSet(double centerMm, double widthMm, IEnumerable<BypassTeePair> sets)
    {
        foreach (var set in sets)
        {
            var firstCenter = (set.First.StartMm + set.First.EndMm) / 2;
            var secondCenter = (set.Second.StartMm + set.Second.EndMm) / 2;
            if (centerMm <= firstCenter || centerMm >= secondCenter) continue;
            // 동률이면 시작 쪽 A에 붙인다.
            return centerMm - set.First.EndMm <= set.Second.StartMm - centerMm
                ? new OccupiedSpan(set.First.EndMm, set.First.EndMm + widthMm)
                : new OccupiedSpan(set.Second.StartMm - widthMm, set.Second.StartMm);
        }
        return null;
    }
}
