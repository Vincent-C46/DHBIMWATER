using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Core.Gis;

public sealed record ReducerPlacement(
    int AlignmentIndex, int VertexIndex, Point3D UpstreamEnd, Point3D DownstreamEnd,
    double UpstreamDiameterMm, double DownstreamDiameterMm, string PipeKind,
    double BodyLengthMm, ReducerAlignment Alignment, double StaStartM, double StaEndM, Vector3D RunDirection);

public sealed record ReducerTrimPlan(
    IReadOnlyList<IReadOnlyList<VertexTrim>> Trims,
    IReadOnlyList<ReducerPlacement> Placements,
    IReadOnlyList<string> Warnings);

public static class ReducerTrimPlanner
{
    public static ReducerTrimPlan Plan(IReadOnlyList<PipeAlignment> alignments, IReducerSpecSource specSource,
        BendTrimPlan? bendPlan, double snapTolerance, IReadOnlyList<IReadOnlySet<int>>? stepVertices = null)
    {
        ArgumentNullException.ThrowIfNull(alignments);
        ArgumentNullException.ThrowIfNull(specSource);
        if (snapTolerance <= 0d) throw new DhbimwaterException(ErrorCodes.PA_InvalidSnapTolerance, "스냅 허용오차는 0보다 커야 합니다.");

        var plans = new List<IReadOnlyList<VertexTrim>>(alignments.Count);
        var placements = new List<ReducerPlacement>();
        var missingLengthCount = 0;
        var directionFailureCount = 0;
        var shortStepCount = 0;
        double? shortStepBodyLengthMm = null;
        var shortSpacingCount = 0;
        double? shortSpacingBodyLengthMm = null;
        // 곡관과 같은 절점에서 DN이 바뀌면 곡관(큰 DN)만 두고 이경관은 생략한다 — 현행 동작 유지, 경고만 낸다(docs/215 § E).
        var bendOverlapCount = 0;

        for (var a = 0; a < alignments.Count; a++)
        {
            var alignment = alignments[a];
            var vertices = alignment.Vertices;
            var trims = new VertexTrim[vertices.Count];
            Array.Fill(trims, VertexTrim.None);
            var stations = new AlignmentStationProfile(alignment);
            var bendTrims = bendPlan?.Plans.FirstOrDefault(x => x.AlignmentIndex == a)?.Trims;
            var steps = stepVertices?.ElementAtOrDefault(a);

            for (var v = 1; v < vertices.Count - 1; v++)
            {
                if (!AlignmentDiameterProfile.ChangesAtVertex(alignment, v)) continue;
                var upstreamVertex = steps?.Contains(v) == true ? v - 1 : v;
                if (bendTrims is not null && ((v < bendTrims.Count && !bendTrims[v].IsEmpty)
                    || (upstreamVertex < bendTrims.Count && !bendTrims[upstreamVertex].IsEmpty)))
                {
                    bendOverlapCount++;
                    continue;
                }

                var upstreamDiameter = AlignmentDiameterProfile.DiameterAtVertex(alignment, v - 1);
                var downstreamDiameter = AlignmentDiameterProfile.DiameterAtVertex(alignment, v);
                var spec = specSource.Find(alignment.PipeKind, Math.Max(upstreamDiameter, downstreamDiameter), Math.Min(upstreamDiameter, downstreamDiameter));
                if (spec is null || spec.BodyLengthMm <= 0d) { missingLengthCount++; continue; }

                var upstream = AlignmentVertexDirection.SkippingNear(vertices, upstreamVertex, -1, snapTolerance);
                var downstream = AlignmentVertexDirection.SkippingNear(vertices, v, +1, snapTolerance);
                if (upstream is null || downstream is null) { directionFailureCount++; continue; }

                var halfLengthM = spec.BodyLengthMm / 2000d;
                var upstreamDistance = stations.DistanceAtVertex(upstreamVertex);
                var downstreamDistance = stations.DistanceAtVertex(v);
                var stepM = downstreamDistance - upstreamDistance;
                var reachM = halfLengthM - stepM / 2d;
                if (reachM <= 0d)
                {
                    shortStepCount++;
                    shortStepBodyLengthMm ??= spec.BodyLengthMm;
                    continue;
                }
                trims[v] = new VertexTrim(halfLengthM + stepM / 2d, reachM);
                var midpointDistance = (upstreamDistance + downstreamDistance) / 2d;
                placements.Add(new ReducerPlacement(a, v,
                    Move(vertices[upstreamVertex], upstream, reachM), Move(vertices[v], downstream, reachM),
                    upstreamDiameter, downstreamDiameter, alignment.PipeKind, spec.BodyLengthMm, spec.Alignment,
                    // 시·종점 위치(3D 거리)를 평면 측점으로 환산한다(docs/215 § B-2).
                    stations.StationAt(midpointDistance - halfLengthM),
                    stations.StationAt(midpointDistance + halfLengthM), downstream));
                if (vertices[upstreamVertex - 1].DistanceTo(vertices[upstreamVertex]) < reachM
                    || vertices[v].DistanceTo(vertices[v + 1]) < reachM)
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
        if (shortStepCount > 0)
            warnings.Add($"이경관 몸통(S={shortStepBodyLengthMm:0.##}mm)이 기준면 단차보다 짧아 DN 변경 지점 {shortStepCount}곳에 이경관을 배치하지 못했습니다.");
        if (shortSpacingCount > 0)
            warnings.Add($"이경관 몸통(S={shortSpacingBodyLengthMm:0.##}mm)이 인접 정점 간격보다 길어 직관이 생기지 않는 구간이 {shortSpacingCount}곳 있습니다.");
        if (bendOverlapCount > 0)
            warnings.Add($"곡관과 같은 절점에서 DN이 바뀌어 이경관을 생략한 곳 {bendOverlapCount}곳 — 곡관은 큰 DN으로 배치했습니다.");
        return new ReducerTrimPlan(plans, placements, warnings);
    }

    private static Point3D Move(Point3D point, Vector3D direction, double distance)
        => new(point.X + direction.X * distance, point.Y + direction.Y * distance, point.Z + direction.Z * distance);
}
