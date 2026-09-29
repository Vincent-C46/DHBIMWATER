using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Core.Gis;

/// <summary>직결 직관 두 축의 공통 절단면 법선.</summary>
public static class StraightJointCutPlane
{
    private const double DegenerateEpsilon = 1e-12;
    private const double DeflectionEpsilonDeg = 1e-9;
    // 150도 초과 마이터는 길이 폭주. 축 직각 절단으로 전환.
    public const double MaxMiterDeflectionDeg = 150d;

    public static Vector3D Bisect(Vector3D firstDirection, Vector3D secondDirection)
    {
        ArgumentNullException.ThrowIfNull(firstDirection);
        ArgumentNullException.ThrowIfNull(secondDirection);
        if (firstDirection.Length <= DegenerateEpsilon || secondDirection.Length <= DegenerateEpsilon)
            throw new DhbimwaterException(ErrorCodes.PA_Internal, "방향벡터 길이는 0보다 커야 합니다.");

        var first = firstDirection.Normalize();
        var second = secondDirection.Normalize();
        var sum = new Vector3D(first.X + second.X, first.Y + second.Y, first.Z + second.Z);
        return sum.Length > DegenerateEpsilon ? sum.Normalize() : first;
    }

    public static bool ExceedsMiterLimit(Vector3D firstDirection, Vector3D secondDirection, double limitDeg)
    {
        var (first, second) = NormalizePair(firstDirection, secondDirection);
        if (limitDeg is < 0d or > 180d)
            throw new DhbimwaterException(ErrorCodes.PA_Internal, "임계각은 0~180도 범위여야 합니다.");

        var dot = Math.Clamp(Dot(first, second), -1d, 1d);
        var deflectionDeg = Math.Acos(dot) * 180d / Math.PI;
        return deflectionDeg - limitDeg > DeflectionEpsilonDeg;
    }

    public static double CutSlope(Vector3D axisDirection, Vector3D planeNormal, double maxSlope)
    {
        var (axis, normal) = NormalizePair(axisDirection, planeNormal);
        if (!double.IsFinite(maxSlope) || maxSlope < 0d)
            throw new DhbimwaterException(ErrorCodes.PA_Internal, "절단 기울기 상한은 0 이상의 유한값이어야 합니다.");

        var dot = Math.Clamp(Math.Abs(Dot(axis, normal)), 1e-6, 1d);
        var slope = Math.Sqrt(Math.Max(0d, 1d - dot * dot)) / dot;
        return Math.Min(slope, maxSlope);
    }

    private static (Vector3D First, Vector3D Second) NormalizePair(Vector3D firstDirection, Vector3D secondDirection)
    {
        ArgumentNullException.ThrowIfNull(firstDirection);
        ArgumentNullException.ThrowIfNull(secondDirection);
        if (firstDirection.Length <= DegenerateEpsilon || secondDirection.Length <= DegenerateEpsilon)
            throw new DhbimwaterException(ErrorCodes.PA_Internal, "방향벡터 길이는 0보다 커야 합니다.");
        return (firstDirection.Normalize(), secondDirection.Normalize());
    }

    private static double Dot(Vector3D first, Vector3D second)
        => first.X * second.X + first.Y * second.Y + first.Z * second.Z;
}
