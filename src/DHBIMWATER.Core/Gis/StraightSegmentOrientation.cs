using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Core.Gis;

/// <summary>
/// 2점 가변 직관의 시작/끝 회전 방향을 정한다. 같은 선형의 인접 세그먼트와 맞닿으면 두 진행방향의
/// 이등분 방향을 쓴다. 떨어진 경계는 세그먼트 고유 방향을 쓴다.
/// </summary>
public static class StraightSegmentOrientation
{
    private const double JoinToleranceM = 1e-6;

    public static (Vector3D Start, Vector3D End, Vector3D Own) Resolve(
        Point3D segmentStart, Point3D segmentEnd,
        Point3D? previousEnd, Vector3D? previousOwnDirection,
        Point3D? nextStart, Vector3D? nextOwnDirection)
    {
        var own = new Vector3D(
            segmentEnd.X - segmentStart.X,
            segmentEnd.Y - segmentStart.Y,
            segmentEnd.Z - segmentStart.Z);
        var joinsPrevious = previousEnd is { } end && previousOwnDirection is not null
            && end.DistanceTo(segmentStart) <= JoinToleranceM;
        var joinsNext = nextStart is { } start && nextOwnDirection is not null
            && segmentEnd.DistanceTo(start) <= JoinToleranceM;
        var startDirection = joinsPrevious
            ? StraightJointCutPlane.Bisect(previousOwnDirection!, own)
            : own;
        var endDirection = joinsNext
            ? StraightJointCutPlane.Bisect(own, nextOwnDirection!)
            : own;
        return (startDirection, endDirection, own);
    }
}
