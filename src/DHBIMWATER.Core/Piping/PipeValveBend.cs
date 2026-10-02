using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Core.Piping;

/// <summary>절점에 놓을 부속 종류(docs/225 § A, docs/227 § A).
/// <para>절점 분류(<see cref="NodeKind"/>)는 그래프 모양만으로 정해지지만, 곡관과 밸브부관 B, T형과 밸브부관 A는
/// 모양이 같아 사용자가 절점마다 고른다. 그래서 NodeKind에 값을 더하지 않고 따로 둔다.</para></summary>
public enum PipeNodeFittingKind
{
    /// <summary>기본값. 절점 종류별 전역 패밀리(곡관·T형 등)를 놓는다.</summary>
    Bend,
    /// <summary>밸브부관 B — 바이패스용 90° 밸브부관(배관 부속류, 2포트, 비대칭). 꺾인 절점 전용. 밸브 쪽 레그를 따로 정한다.</summary>
    ValveBend,
    /// <summary>밸브부관 A — 본관에서 바이패스를 떼는 T자 밸브부관(배관 부속류, 4포트: 주관 2·수평분기·수직분기). T 절점 전용(docs/227).</summary>
    ValveTee
}

/// <summary>밸브부관 A·B 규칙 모음(docs/225, docs/227). 캔버스·배치 경계·역가져오기가 같은 값을 보게 여기 한곳에 둔다.</summary>
public static class PipeValveBend
{
    /// <summary>밸브부관 패밀리를 가리는 패밀리명 키워드. 이음관(<see cref="InlineJoint.FamilyKeyword"/>)과 같은 방식이다
    /// (2026-09-28 사용자 결정). A·B는 커넥터 수(4/2)로 가른다.</summary>
    public const string FamilyKeyword = "밸브부관";

    /// <summary>캔버스 라벨·배치 역할명.</summary>
    public const string PartNameA = "밸브부관 A";
    public const string PartNameB = "밸브부관 B";

    /// <summary>역가져오기 판정용 커넥터 수(docs/227 § 0-5·6). A는 수직분기 커넥터가 가시성과 무관하게 남아 4개다.</summary>
    public const int ValveTeeConnectorCount = 4;
    public const int ValveBendConnectorCount = 2;

    /// <summary>밸브부관 A로 볼 수 있는 커넥터 수인지 — 수직분기 없는 3포트도 받는다(2026-09-29).
    /// 배치 경계(<c>MeasureTee</c>)는 수평 커넥터 3개만 검사하므로 3포트 A도 모델이 만들어진다. 역가져오기·재기록이 4개만 받으면
    /// 그렇게 만든 A가 T형으로 돌아와 바이패스 세트가 풀린다. 수평 3개 여부는 호출부가 따로 본다.</summary>
    public static bool IsValveTeeConnectorCount(int count) => count is 3 or ValveTeeConnectorCount;

    /// <summary>B가 지원하는 꺾임각. 90° 곡관형만 다룬다(docs/225 § 0-1).</summary>
    public const double AngleDeg = 90;

    /// <summary>꺾임각 허용오차(도). 배치 경계의 레그 정렬 허용치(<c>LegAlignmentToleranceDeg</c>)와 같다 —
    /// 이보다 벌어지면 고정각 패밀리 커넥터가 레그와 맞지 않아 어차피 배치에서 건너뛴다.</summary>
    public const double AngleToleranceDeg = 1.0;

    /// <summary>사용자 지정 방향이 레그와 같은 쪽이라고 볼 최소 내적(≈ 45°). 절점을 조금 끌어 레그가 틀어져도 유지된다.</summary>
    private const double PreferredDirectionMinDot = 0.7;

    /// <summary>"패밀리명 : 유형명" 또는 패밀리명만 받아도 된다 — 이름 어디든 키워드가 있으면 밸브부관이다.</summary>
    public static bool IsValveBendFamily(string? familyTypeName)
        => !string.IsNullOrWhiteSpace(familyTypeName)
           && familyTypeName.Contains(FamilyKeyword, StringComparison.Ordinal);

    /// <summary>꺾임각이 90° ± 허용오차인지. 레그가 2개가 아니면 false.</summary>
    public static bool HasSupportedAngle(PipeNodeDefinition node, IReadOnlyList<PipeEdgeDefinition> edges)
        => PipeNodeAngle.Deflection(node, edges) is { } deflection && Math.Abs(deflection - AngleDeg) <= AngleToleranceDeg;

    /// <summary>
    /// 밸브부관 B의 밸브 쪽 레그(엣지 Id)를 정한다(docs/225 § A-1, docs/227 § A).
    /// <list type="number">
    /// <item><paramref name="preferredDirection"/>(사용자가 뒤집기로 지정한 방향)이 있으면 그 방향에 가장 가까운 레그.</item>
    /// <item>없으면 밸브(이음관 제외)가 올라탄 레그가 정확히 하나일 때 그 레그.</item>
    /// <item>그래도 없으면 한쪽 레그만 밸브부관 A의 분기로 이어질 때 <b>반대쪽</b> 레그 — B의 밸브 반대쪽은 A와 붙는다.</item>
    /// <item>그 밖에는 null — 검증 오류로 사용자에게 지정을 요구한다. 방향을 추측해 놓으면 비대칭 부속이 거꾸로 선다.</item>
    /// </list>
    /// <paramref name="nodes"/>는 3번 규칙에만 쓴다. 절점 종류(A 여부)가 확정된 정의여야 한다.
    /// </summary>
    public static Guid? ResolveValveSideEdge(
        PipeNodeDefinition node, IReadOnlyList<PipeEdgeDefinition> edges,
        IReadOnlyList<FittingPlacementDefinition> fittings, Point2D? preferredDirection,
        IReadOnlyList<PipeNodeDefinition>? nodes = null)
    {
        var legs = PipeNodeAngle.Legs(node, edges);
        if (legs.Count != 2) return null;

        if (preferredDirection is { } preferred)
        {
            var best = legs.Select(x => (Leg: x, Dot: x.DirectionX * preferred.X + x.DirectionY * preferred.Y))
                .OrderByDescending(x => x.Dot).First();
            if (best.Dot >= PreferredDirectionMinDot) return best.Leg.Edge.Id;
        }

        var valveLegs = legs.Where(leg => fittings.Any(f => f.EdgeId == leg.Edge.Id && !f.IsJoint)).ToList();
        if (valveLegs.Count == 1) return valveLegs[0].Edge.Id;

        if (nodes is null) return null;
        var towardValveTee = legs.Where(leg => TraceToValveTeeBranch(node, leg.Edge, nodes, edges) is not null).ToList();
        return towardValveTee.Count == 1 ? legs.First(x => x.Edge.Id != towardValveTee[0].Edge.Id).Edge.Id : null;
    }

    /// <summary>밸브부관 B의 주관 구경(docs/227 § A). 밸브 반대쪽 레그를 따라가 만난 밸브부관 A의 주관 직경이다.
    /// 밸브 쪽을 모르거나 A의 분기에 닿지 않으면 null — 검증 오류로 막는다(§ 0-8).</summary>
    public static double? ResolveMainDiameter(
        PipeNodeDefinition node, IReadOnlyList<PipeNodeDefinition> nodes, IReadOnlyList<PipeEdgeDefinition> edges)
    {
        if (node.ValveSideEdgeId is not { } valveSide) return null;
        var legs = PipeNodeAngle.Legs(node, edges);
        if (legs.Count != 2) return null;
        var opposite = legs.FirstOrDefault(x => x.Edge.Id != valveSide);
        if (opposite.Edge is null) return null;
        if (TraceToValveTeeBranch(node, opposite.Edge, nodes, edges) is not { } valveTee) return null;
        var order = PipeTeeResolver.Resolve(valveTee, edges);
        if (order is null) return null;
        return edges.Where(x => x.Id == order.MainEdge1Id || x.Id == order.MainEdge2Id).Select(x => x.DiameterMm).DefaultIfEmpty().Max();
    }

    /// <summary><paramref name="start"/>에서 <paramref name="edge"/>를 타고 나가, 일직선 통과점(Inline)을 지나쳐 처음 만나는 절점이
    /// 밸브부관 A이고 그 <b>분기 레그로 도착</b>했으면 그 A를 돌려준다. 주관 쪽으로 도착했으면 바이패스가 아니므로 null.</summary>
    private static PipeNodeDefinition? TraceToValveTeeBranch(
        PipeNodeDefinition start, PipeEdgeDefinition edge, IReadOnlyList<PipeNodeDefinition> nodes, IReadOnlyList<PipeEdgeDefinition> edges)
    {
        var current = start;
        var via = edge;
        // 순환 배관에서 끝나지 않는 것을 막는다. 엣지 수보다 많이 걸을 일은 없다.
        for (var step = 0; step <= edges.Count; step++)
        {
            var farPoint = via.Start.DistanceTo(current.Position) <= 0.01 ? via.End : via.Start;
            var far = nodes.FirstOrDefault(x => x.Position.DistanceTo(farPoint) <= 0.01);
            if (far is null) return null;
            if (far.IsValveTee)
            {
                var order = PipeTeeResolver.Resolve(far, edges);
                return order is not null && order.BranchEdgeId == via.Id ? far : null;
            }
            if (far.NodeKind != NodeKind.Inline) return null;
            var next = PipeNodeAngle.Legs(far, edges).FirstOrDefault(x => x.Edge.Id != via.Id);
            if (next.Edge is null) return null;
            current = far;
            via = next.Edge;
        }
        return null;
    }
}
