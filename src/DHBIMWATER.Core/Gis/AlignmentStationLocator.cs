using DHBIMWATER.Shared.Errors;
namespace DHBIMWATER.Core.Gis;

/// <summary>엑셀 선형 1행. 측점(m)·공유좌표(m)·관 Z(m)(docs/300 § 3-1). 지표 EL은 docs/304부터 밸브실 목록 시트에서 받는다.</summary>
public sealed record AlignmentProfileRow(double StationM, double X, double Y, double Z);

/// <summary>측점 위 점. Dir은 측점 증가 방향 평면 단위벡터.</summary>
public sealed record AlignmentStationPoint(double StationM, double X, double Y, double Z, double DirX, double DirY, int SegmentIndex);

/// <summary>측점 → 선형 위 점(docs/300 § 3-1). 행 사이는 측점 비율 선형 보간, 범위 밖 외삽 안 함.</summary>
public static class AlignmentStationLocator
{
    private const double DegenerateEpsilon = 1e-9;

    public static AlignmentStationPoint Locate(IReadOnlyList<AlignmentProfileRow> rows, double stationM)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (!double.IsFinite(stationM))
            throw new DhbimwaterException(ErrorCodes.VR_PlacementStationInvalid, "측점 값이 올바르지 않습니다.");
        if (rows.Count < 2)
            throw new DhbimwaterException(ErrorCodes.VR_PlacementProfileInvalid, $"선형 유효 행이 {rows.Count}개입니다. 2개 이상 필요합니다.");
        for (var i = 1; i < rows.Count; i++)
        {
            if (rows[i].StationM < rows[i - 1].StationM)
                throw new DhbimwaterException(ErrorCodes.VR_PlacementProfileInvalid, $"측점 역행: {rows[i - 1].StationM:0.###} 다음 {rows[i].StationM:0.###}");
        }

        var first = rows[0].StationM;
        var last = rows[^1].StationM;
        if (stationM < first - DegenerateEpsilon || stationM > last + DegenerateEpsilon)
            throw new DhbimwaterException(ErrorCodes.VR_PlacementStationOutOfRange, $"측점 {stationM:0.###}m가 선형 범위({first:0.###}~{last:0.###}m) 밖입니다.");

        // 정점과 같으면 다음 구간, 마지막 정점이면 마지막 구간.
        var segment = rows.Count - 2;
        for (var i = 0; i < rows.Count - 1; i++)
        {
            if (stationM < rows[i + 1].StationM - DegenerateEpsilon) { segment = i; break; }
        }

        var a = rows[segment];
        var b = rows[segment + 1];
        var span = b.StationM - a.StationM;
        // 같은 측점 두 행이면 보간 대신 앞 행 값.
        var t = span > DegenerateEpsilon ? Math.Clamp((stationM - a.StationM) / span, 0d, 1d) : 0d;
        var (dirX, dirY) = FindDirection(rows, segment);
        return new AlignmentStationPoint(
            stationM,
            Lerp(a.X, b.X, t),
            Lerp(a.Y, b.Y, t),
            Lerp(a.Z, b.Z, t),
            dirX,
            dirY,
            segment);
    }

    /// <summary>해당 구간 평면 방향. 길이 0이면 다음 → 이전 구간 순으로 찾는다.</summary>
    private static (double X, double Y) FindDirection(IReadOnlyList<AlignmentProfileRow> rows, int segment)
    {
        for (var i = segment; i < rows.Count - 1; i++)
        {
            if (TryDirection(rows[i], rows[i + 1], out var dir)) return dir;
        }
        for (var i = segment - 1; i >= 0; i--)
        {
            if (TryDirection(rows[i], rows[i + 1], out var dir)) return dir;
        }
        throw new DhbimwaterException(ErrorCodes.VR_PlacementProfileInvalid, "선형 평면 길이가 0이라 관 방향을 정할 수 없습니다.");
    }

    private static bool TryDirection(AlignmentProfileRow a, AlignmentProfileRow b, out (double X, double Y) direction)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        direction = length > DegenerateEpsilon ? (dx / length, dy / length) : default;
        return length > DegenerateEpsilon;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
