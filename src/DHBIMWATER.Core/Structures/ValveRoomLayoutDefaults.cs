namespace DHBIMWATER.Core.Structures;

/// <summary>밸브실 창 자동 치수(mm). 보 균등분배·중간벽 정중앙·중간슬래브 H1=H2. pinned = 사용자가 고친 값이라 그대로 둠.</summary>
public static class ValveRoomLayoutDefaults
{
    /// <summary>
    /// 보 n개를 내부 span에 균등분배. 보 중심 = offset + spacing×i.
    /// 둘 다 자동이면 offset = spacing = span/(n+1). offset만 고정이면 남은 구간 균등, spacing만 고정이면 양쪽 여유 같게.
    /// </summary>
    public static (double Offset, double Spacing) EqualBeamLayout(int count, double span, double offset, bool offsetPinned, double spacing, bool spacingPinned)
    {
        if (count <= 0 || (offsetPinned && spacingPinned)) return (offset, spacing);
        // 보 1개면 spacing 안 씀 — 고정 offset 그대로, spacing 값 유지.
        if (offsetPinned) return (offset, count > 1 ? (span - 2 * offset) / (count - 1) : spacing);
        if (spacingPinned) return ((span - spacing * (count - 1)) / 2, spacing);
        var equal = span / (count + 1);
        return (equal, equal);
    }

    /// <summary>중간벽 정중앙 Lw. Lw = 좌측 외벽 내측 → 중간벽 좌측면 안목거리.</summary>
    public static double CenteredIntermediateWallOffset(double innerLength, double wallThickness) => (innerLength - wallThickness) / 2;

    /// <summary>중간슬래브 H1·H2. 합 = 내부 높이 − 중간슬래브 두께. 하나만 고정이면 나머지가 차이를 흡수.</summary>
    public static (double Floor1, double Floor2) SplitFloorHeights(double innerHeight, double slabThickness,
        double floor1, bool floor1Pinned, double floor2, bool floor2Pinned)
    {
        var total = innerHeight - slabThickness;
        if (floor1Pinned && floor2Pinned) return (floor1, floor2);
        if (floor1Pinned) return (floor1, total - floor1);
        if (floor2Pinned) return (total - floor2, floor2);
        return (total / 2, total / 2);
    }
}
