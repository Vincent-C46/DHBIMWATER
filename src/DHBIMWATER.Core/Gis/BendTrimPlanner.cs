using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Core.Gis;

/// <param name="AlignmentIndex">입력 <see cref="PipeAlignment"/> 목록에서의 위치.</param>
/// <param name="VertexIndex">그 폴리선 안에서 곡관이 들어가는 정점 번호.</param>
/// <param name="Points">P1(상류 관 끝) / P2(호 중점) / P3(하류 관 끝). 좌표는 원본 GIS 좌표(m)다.</param>
/// <param name="UpstreamLegMm">상류 쪽 차감량 t(mm).</param>
/// <param name="DownstreamLegMm">하류 쪽 차감량 t+s(mm). A형은 t와 같다.</param>
/// <param name="WallThicknessMm">e — 곡관 벽 두께(mm). 카탈로그에 없으면 0.</param>
/// <param name="RotXYDeg">P1~P5 각 점의 rot_XY(도). 인덱스는 <see cref="Points"/>의 Start/ArcStart/ArcMid/ArcEnd/End 순서와 같다.</param>
/// <param name="RotXZDeg">P1~P5 각 점의 rot_XZ(도). 인덱스는 <paramref name="RotXYDeg"/>와 같다.</param>
public sealed record BendPlacement(
    int NodeId,
    int AlignmentIndex,
    int VertexIndex,
    double AngleDeg,
    double DiameterMm,
    string PipeKind,
    BendArcPoints Points,
    double UpstreamLegMm,
    double DownstreamLegMm,
    double WallThicknessMm,
    bool IsAcceptable,
    IReadOnlyList<double> RotXYDeg,
    IReadOnlyList<double> RotXZDeg,
    double DeflectionDeg = 0d,
    double EffectiveAllowableDeg = 0d,
    double ResidualDeg = 0d,
    double CenterlineRadiusMm = 0d,
    double LayingLengthMm = 0d,
    string JointType = "",
    JointApplicationMode ApplicationMode = JointApplicationMode.SingleJoint,
    BendForm Form = BendForm.BType,
    double WeightKg = 0d);

/// <param name="Trims">인덱스 = 폴리선 정점 인덱스. 차감이 없는 정점은 <see cref="VertexTrim.None"/>다.</param>
public sealed record AlignmentBendPlan(int AlignmentIndex, IReadOnlyList<VertexTrim> Trims);

/// <param name="Plans">인덱스 = 입력 <see cref="PipeAlignment"/> 인덱스.</param>
/// <param name="Placements">곡관이 실제로 들어가는 자리. 배치 단계가 소비한다.</param>
/// <param name="Warnings">기하 계산이 안 돼 자리를 비운 절점 안내(예: 편각이 0에 가까워 이등분선이 정의되지 않는 경우).</param>
public sealed record BendTrimPlan(IReadOnlyList<AlignmentBendPlan> Plans, IReadOnlyList<BendPlacement> Placements, IReadOnlyList<string> Warnings);

/// <summary>
/// 절점 곡관 판정 결과를 <b>폴리선 정점</b>에 되돌려, 정점별 직관 차감량과 곡관 배치점을 만든다.
/// Revit 의존 없는 순수 계산이다.
/// </summary>
/// <remarks>
/// 그래프는 여러 파일의 정점을 스냅 병합한 결과라 <see cref="NetworkEdge"/>에 원본 정점 인덱스가 없다.
/// 그래서 좌표로 역조회한다(B-1 폴리선 유지 방식). 기존 샘플러·배치 로직을 그대로 두기 위한 선택이다.
///
/// 비대칭 차감 규칙(사용자 결정 2026-08-07): B형 곡관의 긴 쪽 다리(t+s)는 <b>폴리선 진행 방향 기준 하류쪽</b>에 붙는다.
/// 어느 쪽이 하류인지는 절점 편각만으로 알 수 없고 정점 순서가 있어야 정해지므로 <see cref="BendResolver"/>가 아니라
/// 여기서 결정한다.
/// </remarks>
public static class BendTrimPlanner
{
    private const double MmToCoordinate = 0.001;

    /// <param name="snapTolerance">정점↔절점 역조회 허용오차. 좌표와 같은 단위(m)이며 그래프를 만들 때와 같은 값을 넘긴다.</param>
    public static BendTrimPlan Plan(
        IReadOnlyList<PipeAlignment> alignments,
        PipeNetworkGraph graph,
        IReadOnlyList<BendResolution> resolutions,
        double snapTolerance,
        IReadOnlyList<IReadOnlySet<int>>? stepVertices = null)
    {
        if (snapTolerance <= 0) throw new ArgumentOutOfRangeException(nameof(snapTolerance), "스냅 허용오차는 0보다 커야 합니다.");

        // 허용 초과(Unresolved)도 최근접 곡관을 배치한다. 치수 미입력만 배치하지 않는다.
        var placeable = resolutions
            .Where(x => x.Kind != BendResolutionKind.None && x.HasFittingSize && x.LayingLengthMm > 0)
            .ToDictionary(x => x.NodeId);
        var lookup = new AlignmentVertexNodeLookup(graph.Nodes, snapTolerance);
        var representatives = new Dictionary<int, (int AlignmentIndex, int VertexIndex)>();
        for (var a = 0; a < alignments.Count; a++)
        {
            var vertices = alignments[a].Vertices;
            for (var v = 1; v < vertices.Count - 1; v++)
            {
                var nodeId = lookup.Find(vertices[v]);
                if (nodeId is null || !placeable.ContainsKey(nodeId.Value)) continue;

                if (!representatives.TryGetValue(nodeId.Value, out var current))
                {
                    representatives[nodeId.Value] = (a, v);
                    continue;
                }

                if (!HasBendKind(alignments[current.AlignmentIndex], current.VertexIndex)
                    && HasBendKind(alignments[a], v))
                    representatives[nodeId.Value] = (a, v);
            }
        }

        var plans = new List<AlignmentBendPlan>(alignments.Count);
        var placements = new List<BendPlacement>();
        var warnings = new List<string>();
        var skippedDuplicates = new List<(int AlignmentIndex, int VertexIndex)>();

        for (var a = 0; a < alignments.Count; a++)
        {
            var vertices = alignments[a].Vertices;
            var trims = new VertexTrim[vertices.Count];
            Array.Fill(trims, VertexTrim.None);

            // 단부 정점(0, n-1)은 편각이 정의되지 않아 곡관 대상이 아니다.
            for (var v = 1; v < vertices.Count - 1; v++)
            {
                if (stepVertices?.ElementAtOrDefault(a)?.Contains(v) == true || stepVertices?.ElementAtOrDefault(a)?.Contains(v + 1) == true) continue;
                var nodeId = lookup.Find(vertices[v]);
                if (nodeId is null || !placeable.TryGetValue(nodeId.Value, out var bend)) continue;
                if (representatives[nodeId.Value] != (a, v))
                {
                    skippedDuplicates.Add((a, v));
                    continue;
                }

                var upstream = AlignmentVertexDirection.SkippingNear(vertices, v, -1, snapTolerance);
                var downstream = AlignmentVertexDirection.SkippingNear(vertices, v, +1, snapTolerance);
                if (upstream is null || downstream is null)
                {
                    warnings.Add($"{Path.GetFileName(alignments[a].SourceFile)} 레코드 {alignments[a].RecordNumber} {AlignmentVertexLabel.Format(alignments[a], v)}: 스냅 허용오차 밖의 이웃 정점을 찾지 못해 곡관 자리를 비웠습니다.");
                    continue;
                }

                var shortLeg = bend.LayingLengthMm;
                var longLeg = bend.LongLegLengthMm;
                var upstreamDiameter = AlignmentDiameterProfile.DiameterAtVertex(alignments[a], v - 1);
                var downstreamDiameter = AlignmentDiameterProfile.DiameterAtVertex(alignments[a], v);
                var bendDiameter = Math.Max(upstreamDiameter, downstreamDiameter);
                if (Math.Abs(upstreamDiameter - downstreamDiameter) > 1e-6)
                    warnings.Add($"{Path.GetFileName(alignments[a].SourceFile)} 레코드 {alignments[a].RecordNumber} {AlignmentVertexLabel.Format(alignments[a], v)}: 곡관 자리에서 DN이 {upstreamDiameter:0.##}→{downstreamDiameter:0.##}으로 바뀝니다. 이경곡관 미지원이라 DN{bendDiameter:0.##}으로 배치합니다.");

                var polylineDeflectionDeg = 180d - AngleDegrees(upstream, downstream);
                BendArcPoints points;
                try
                {
                    points = BendArcGeometry.ComputeForAlignment(
                        vertices[v], upstream, downstream,
                        bend.StandardAngleDeg, polylineDeflectionDeg,
                        bend.CenterlineRadiusMm, shortLeg, longLeg);
                }
                catch (ArgumentException)
                {
                    // 편각이 0에 가까워 이등분선이 정의되지 않는 극단치(예: 허용굴곡을 0에 가깝게 설정한 경우).
                    // 곡관 자리를 비우지 못하므로 직관을 절점까지 그대로 붙인다 — 치수 미입력 절점과 같은 처리다.
                    warnings.Add($"절점 {nodeId.Value}(정점 {v}): 편각이 너무 작아 곡관 기하를 계산할 수 없어 자리를 비웠습니다.");
                    continue;
                }

                trims[v] = new VertexTrim(shortLeg * MmToCoordinate, longLeg * MmToCoordinate);

                // P1~P5 각 점의 접선 방향에서 rot_XY_n/rot_XZ_n(도)을 구한다. P1·P2/P4·P5는 직선 구간이라 같은 값이다.
                var tangents = BendOrientation.Tangents(upstream, downstream, polylineDeflectionDeg);
                var rotXy = new double[tangents.Count];
                var rotXz = new double[tangents.Count];
                for (var i = 0; i < tangents.Count; i++) (rotXy[i], rotXz[i]) = BendOrientation.Compute(tangents[i]);

                placements.Add(new BendPlacement(
                    nodeId.Value, a, v, bend.StandardAngleDeg,
                    bendDiameter, alignments[a].PipeKind,
                    points, shortLeg, longLeg,
                    bend.WallThicknessMm, bend.IsAcceptable, rotXy, rotXz,
                    polylineDeflectionDeg, bend.EffectiveAllowableDeg, bend.ResidualDeg,
                    bend.CenterlineRadiusMm, bend.LayingLengthMm, bend.JointType, bend.ApplicationMode,
                    bend.Form, bend.WeightKg));
            }

            plans.Add(new AlignmentBendPlan(a, trims));
        }

        warnings.AddRange(BuildDuplicateWarnings(alignments, skippedDuplicates));
        return new BendTrimPlan(plans, placements, warnings);
    }

    private static bool HasBendKind(PipeAlignment alignment, int vertexIndex)
        => alignment.VertexBendKinds is { } kinds
           && vertexIndex < kinds.Count
           && !string.IsNullOrWhiteSpace(kinds[vertexIndex]);

    private static IEnumerable<string> BuildDuplicateWarnings(
        IReadOnlyList<PipeAlignment> alignments,
        IReadOnlyList<(int AlignmentIndex, int VertexIndex)> skipped)
    {
        foreach (var group in skipped.GroupBy(x => x.AlignmentIndex))
        {
            var alignment = alignments[group.Key];
            var examples = string.Join(", ", group.Take(5).Select(x => AlignmentVertexLabel.Format(alignment, x.VertexIndex)));
            var suffix = group.Count() > 5 ? $", 외 {group.Count() - 5}건" : string.Empty;
            yield return $"{Path.GetFileName(alignment.SourceFile)} 레코드 {alignment.RecordNumber}: 좌표가 스냅 허용오차 안으로 겹친 정점 {group.Count()}곳은 곡관을 1개만 배치했습니다 — {examples}{suffix}.";
        }
    }

    private static double AngleDegrees(Vector3D first, Vector3D second)
    {
        var dot = first.X * second.X + first.Y * second.Y + first.Z * second.Z;
        return Math.Acos(Math.Clamp(dot, -1d, 1d)) * 180d / Math.PI;
    }

}
