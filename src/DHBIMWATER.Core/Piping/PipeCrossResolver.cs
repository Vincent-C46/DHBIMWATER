using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Core.Piping;

/// <summary>십자 절점의 레그 순서. 십자는 90° 대칭이라 배치에서는 주관/분기 구분이 의미가 없다 —
/// 레그 0이 속한 쌍을 Main으로 부를 뿐이며, 재료표 규격의 주관 직경만 여기서 온다(docs/175 § B-2).</summary>
public sealed record PipeCrossLegOrder(Guid Main1EdgeId, Guid Main2EdgeId, Guid Branch1EdgeId, Guid Branch2EdgeId);

/// <summary>차수 4 절점이 직교 십자인지 판별한다(docs/175 § B-2).
/// 두 쌍이 각각 공선(180±tol)이고 서로 90±tol이어야 한다. 규약·허용오차는 <see cref="PipeTeeResolver"/>와 같다.</summary>
public static class PipeCrossResolver
{
    public const double DefaultAngleToleranceDeg = PipeTeeResolver.DefaultAngleToleranceDeg;

    public static PipeCrossLegOrder? Resolve(
        PipeNodeDefinition node,
        IReadOnlyList<PipeEdgeDefinition> edges,
        double angleToleranceDeg = DefaultAngleToleranceDeg)
    {
        var legs = edges
            .Where(x => PipeTeeResolver.IsAt(x.Start, node.Position) || PipeTeeResolver.IsAt(x.End, node.Position))
            .Select(x => (Edge: x, Direction: PipeTeeResolver.DirectionAwayFrom(x, node.Position)))
            .Where(x => x.Direction is not null)
            .Select(x => (x.Edge, Direction: x.Direction!.Value))
            .ToList();
        if (legs.Count != 4) return null;

        // 레그 0의 반대편(180°)이 주관 짝이다.
        var opposite = -1;
        for (var i = 1; i < 4; i++)
        {
            if (Math.Abs(180 - PipeTeeResolver.AngleDeg(legs[0].Direction, legs[i].Direction)) > angleToleranceDeg) continue;
            opposite = i;
            break;
        }
        if (opposite < 0) return null;

        var branches = Enumerable.Range(1, 3).Where(x => x != opposite).ToList();
        var b1 = legs[branches[0]].Direction;
        var b2 = legs[branches[1]].Direction;
        if (Math.Abs(180 - PipeTeeResolver.AngleDeg(b1, b2)) > angleToleranceDeg) return null;
        if (Math.Abs(90 - PipeTeeResolver.AngleDeg(legs[0].Direction, b1)) > angleToleranceDeg) return null;
        if (Math.Abs(90 - PipeTeeResolver.AngleDeg(legs[0].Direction, b2)) > angleToleranceDeg) return null;

        return new PipeCrossLegOrder(
            legs[0].Edge.Id,
            legs[opposite].Edge.Id,
            legs[branches[0]].Edge.Id,
            legs[branches[1]].Edge.Id);
    }
}
