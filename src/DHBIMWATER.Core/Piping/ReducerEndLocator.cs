using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Core.Piping;

/// <summary>이경관 DirectShape 형상의 원호 하나 — 중심(mm)과 반지름(mm).</summary>
public sealed record ReducerArc(Point3D Center, double RadiusMm);

/// <summary>이경관 양 끝 중심. 대경 쪽이 <paramref name="Large"/>다.</summary>
public sealed record ReducerEnds(Point3D Large, Point3D Small);

/// <summary>
/// 이경관 DirectShape는 배치점·커넥터가 없어 끝단을 형상에서 찾는다(docs/305 § 3-3).
/// 단면이 전부 원이라 원호 중심을 묶으면 끝단 2곳과 원통↔원뿔 이음 중심만 남는다. 이음은 양 끝 사이라 가장 먼 두 중심이 끝단이다.
/// </summary>
public static class ReducerEndLocator
{
    public const double DefaultToleranceMm = 1.0;

    /// <summary>끝단을 못 정하면 null — 중심이 2곳 미만이거나 두 끝 반지름이 같아 대·소경을 가를 수 없을 때다.</summary>
    public static ReducerEnds? Locate(IEnumerable<ReducerArc> arcs, double toleranceMm = DefaultToleranceMm)
    {
        // 안·밖 원과 반원 조각이 같은 중심을 공유한다 — 묶고 가장 큰 반지름만 남긴다.
        var clusters = new List<ReducerArc>();
        foreach (var arc in arcs)
        {
            var index = clusters.FindIndex(x => x.Center.DistanceTo(arc.Center) <= toleranceMm);
            if (index < 0) clusters.Add(arc);
            else if (arc.RadiusMm > clusters[index].RadiusMm) clusters[index] = clusters[index] with { RadiusMm = arc.RadiusMm };
        }
        if (clusters.Count < 2) return null;

        var (a, b, distance) = (clusters[0], clusters[1], -1d);
        for (var i = 0; i < clusters.Count; i++)
        for (var j = i + 1; j < clusters.Count; j++)
        {
            var d = clusters[i].Center.DistanceTo(clusters[j].Center);
            if (d > distance) (a, b, distance) = (clusters[i], clusters[j], d);
        }

        if (Math.Abs(a.RadiusMm - b.RadiusMm) <= toleranceMm) return null;
        return a.RadiusMm > b.RadiusMm ? new ReducerEnds(a.Center, b.Center) : new ReducerEnds(b.Center, a.Center);
    }
}
