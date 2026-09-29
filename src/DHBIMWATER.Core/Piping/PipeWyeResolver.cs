using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Core.Piping;

/// <summary>Y형 절점의 레그 순서와 분기각(docs/187 § 1-3).</summary>
/// <param name="MainEdge1Id">분기와 <b>예각</b>을 이루는 주관 레그. Y형은 T형과 달리 비대칭이라
/// 주관 두 레그의 역할이 다르며, 회전·커넥터 매칭·재료표가 이 약속을 함께 본다.</param>
/// <param name="MainEdge2Id">나머지 주관 레그(분기와 둔각).</param>
/// <param name="BranchEdgeId">분기 레그.</param>
/// <param name="BranchAngleDeg"><paramref name="MainEdge1Id"/> 레그와 분기 레그가 이루는 각(30~60).
/// 패밀리 각도 파라미터 구동값이자 재료표 규격 문자열의 각도다.</param>
public sealed record PipeWyeLegOrder(Guid MainEdge1Id, Guid MainEdge2Id, Guid BranchEdgeId, double BranchAngleDeg);

/// <summary>차수 3 절점이 경사 분기(Y형)인지 판별한다(docs/187 § 1).
/// 주관 2레그가 일직선(180±tol)이고 나머지 레그가 <see cref="BranchAngleMinDeg"/>~<see cref="BranchAngleMaxDeg"/>여야 한다.
/// 규약·허용오차·방향 계산은 <see cref="PipeTeeResolver"/>와 같은 것을 쓴다.</summary>
public static class PipeWyeResolver
{
    public const double DefaultAngleToleranceDeg = PipeTeeResolver.DefaultAngleToleranceDeg;

    /// <summary>Y형으로 보는 분기각 하한(도). 이보다 예리하면 판정하지 않는다.</summary>
    public const double BranchAngleMinDeg = 30.0;

    /// <summary>Y형으로 보는 분기각 상한(도). 직교(90°)는 T형이므로 여기 들어오지 않는다.</summary>
    public const double BranchAngleMaxDeg = 60.0;

    public static PipeWyeLegOrder? Resolve(
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
        if (legs.Count != 3) return null;

        // 가장 벌어진 두 레그가 주관 후보다 — T형 판정과 같은 방식(PipeTeeResolver.Resolve).
        var pairs = new[] { (A: 0, B: 1, Branch: 2), (A: 0, B: 2, Branch: 1), (A: 1, B: 2, Branch: 0) };
        var main = pairs.Select(x => (Pair: x, Angle: PipeTeeResolver.AngleDeg(legs[x.A].Direction, legs[x.B].Direction)))
            .OrderByDescending(x => x.Angle).First();
        if (Math.Abs(180 - main.Angle) > angleToleranceDeg) return null;

        var branchDirection = legs[main.Pair.Branch].Direction;
        var angleToA = PipeTeeResolver.AngleDeg(legs[main.Pair.A].Direction, branchDirection);
        var angleToB = PipeTeeResolver.AngleDeg(legs[main.Pair.B].Direction, branchDirection);

        // 예각 쪽이 MainEdge1이다. 주관이 일직선이므로 두 각의 합은 180°이며, 작은 쪽이 분기각이 된다.
        var acuteIsA = angleToA <= angleToB;
        var branchAngleDeg = acuteIsA ? angleToA : angleToB;
        if (branchAngleDeg < BranchAngleMinDeg || branchAngleDeg > BranchAngleMaxDeg) return null;

        return new PipeWyeLegOrder(
            legs[acuteIsA ? main.Pair.A : main.Pair.B].Edge.Id,
            legs[acuteIsA ? main.Pair.B : main.Pair.A].Edge.Id,
            legs[main.Pair.Branch].Edge.Id,
            branchAngleDeg);
    }

    /// <summary>분기 레그와 주관 두 레그가 이루는 각 중 작은 쪽(도). 인자는 절점에서 <b>바깥을 향하는</b>
    /// 단위벡터끼리의 내적이다 — 역가져오기·재기록은 커넥터 방향만 갖고 있어 이 형태로 받는다(docs/187 § 5).</summary>
    public static double BranchAngleDegFromDots(double dotToMainA, double dotToMainB)
        => Math.Min(AngleDeg(dotToMainA), AngleDeg(dotToMainB));

    private static double AngleDeg(double dotOfUnitDirections)
        => Math.Acos(Math.Clamp(dotOfUnitDirections, -1, 1)) * 180 / Math.PI;

    /// <summary>분기각이 Y형 범위인지. 절점 분류(<c>PipeTopologyBuilder.ClassifyNodes</c>)와
    /// 역가져오기 판정이 같은 값을 보게 하려고 여기 한곳에 둔다.</summary>
    public static bool IsWyeBranchAngle(double branchAngleDeg)
        => branchAngleDeg >= BranchAngleMinDeg && branchAngleDeg <= BranchAngleMaxDeg;
}
