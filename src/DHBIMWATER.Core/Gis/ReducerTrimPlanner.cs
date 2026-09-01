using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Core.Gis;

public sealed record ReducerPlacement(
    int AlignmentIndex, int VertexIndex, Point3D UpstreamEnd, Point3D DownstreamEnd,
    double UpstreamDiameterMm, double DownstreamDiameterMm, string PipeKind,
    double BodyLengthMm, ReducerAlignment Alignment, double StaStartM, double StaEndM);

public sealed record ReducerTrimPlan(
    IReadOnlyList<IReadOnlyList<VertexTrim>> Trims,
    IReadOnlyList<ReducerPlacement> Placements,
    IReadOnlyList<string> Warnings);

public static class ReducerTrimPlanner
{
    public static ReducerTrimPlan Plan(IReadOnlyList<PipeAlignment> alignments, IReducerSpecSource specSource,
        BendTrimPlan? bendPlan, double snapTolerance)
    {
        ArgumentNullException.ThrowIfNull(alignments);
        ArgumentNullException.ThrowIfNull(specSource);
        if (snapTolerance <= 0d) throw new ArgumentOutOfRangeException(nameof(snapTolerance));

        var plans = new List<IReadOnlyList<VertexTrim>>(alignments.Count);
        var placements = new List<ReducerPlacement>();
        var missingLengthCount = 0;
        var directionFailureCount = 0;
        var shortSpacingCount = 0;
        double? shortSpacingBodyLengthMm = null;

        for (var a = 0; a < alignments.Count; a++)
        {
            var alignment = alignments[a];
            var vertices = alignment.Vertices;
            var trims = new VertexTrim[vertices.Count];
            Array.Fill(trims, VertexTrim.None);
            var stations = new AlignmentStationProfile(alignment);
            var bendTrims = bendPlan?.Plans.FirstOrDefault(x => x.AlignmentIndex == a)?.Trims;

            for (var v = 1; v < vertices.Count - 1; v++)
            {
                if (!AlignmentDiameterProfile.ChangesAtVertex(alignment, v)) continue;
                if (bendTrims is not null && v < bendTrims.Count && !bendTrims[v].IsEmpty) continue;

                var upstreamDiameter = AlignmentDiameterProfile.DiameterAtVertex(alignment, v - 1);
                var downstreamDiameter = AlignmentDiameterProfile.DiameterAtVertex(alignment, v);
                var spec = specSource.Find(alignment.PipeKind, Math.Max(upstreamDiameter, downstreamDiameter), Math.Min(upstreamDiameter, downstreamDiameter));
                if (spec is null || spec.BodyLengthMm <= 0d) { missingLengthCount++; continue; }

                var upstream = AlignmentVertexDirection.SkippingNear(vertices, v, -1, snapTolerance);
                var downstream = AlignmentVertexDirection.SkippingNear(vertices, v, +1, snapTolerance);
                if (upstream is null || downstream is null) { directionFailureCount++; continue; }

                var halfLengthM = spec.BodyLengthMm / 2000d;
                trims[v] = new VertexTrim(halfLengthM, halfLengthM);
                var point = vertices[v];
                placements.Add(new ReducerPlacement(a, v,
                    Move(point, upstream, halfLengthM), Move(point, downstream, halfLengthM),
                    upstreamDiameter, downstreamDiameter, alignment.PipeKind, spec.BodyLengthMm, spec.Alignment,
                    stations.StationAt(stations.DistanceAtVertex(v)) - halfLengthM,
                    stations.StationAt(stations.DistanceAtVertex(v)) + halfLengthM));
                if (vertices[v - 1].DistanceTo(point) < halfLengthM || point.DistanceTo(vertices[v + 1]) < halfLengthM)
                {
                    shortSpacingCount++;
                    shortSpacingBodyLengthMm ??= spec.BodyLengthMm;
                }
            }
            plans.Add(trims);
        }

        var warnings = new List<string>();
        if (missingLengthCount > 0)
            warnings.Add($"이경관 길이가 0이어서 DN 변경 지점 {missingLengthCount}곳에 이경관을 배치하지 못했습니다. 모델링 탭의 [이경관 길이]를 확인하세요.");
        if (directionFailureCount > 0)
            warnings.Add($"인접 방향을 찾지 못해 DN 변경 지점 {directionFailureCount}곳에 이경관을 배치하지 못했습니다.");
        if (shortSpacingCount > 0)
            warnings.Add($"이경관 몸통(S={shortSpacingBodyLengthMm:0.##}mm)이 인접 정점 간격보다 길어 직관이 생기지 않는 구간이 {shortSpacingCount}곳 있습니다.");
        return new ReducerTrimPlan(plans, placements, warnings);
    }

    private static Point3D Move(Point3D point, Vector3D direction, double distance)
        => new(point.X + direction.X * distance, point.Y + direction.Y * distance, point.Z + direction.Z * distance);
}
