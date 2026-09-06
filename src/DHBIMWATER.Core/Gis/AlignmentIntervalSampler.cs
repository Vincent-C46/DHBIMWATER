using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Core.Gis;

public sealed record AlignmentSamplePoint(Point3D Position, Vector3D Tangent, double DistanceFromStart);
public sealed record AlignmentSampleSegment(Point3D Start, Point3D End, double DistanceFromStart, bool IsDatumStep = false);

/// <summary>
/// 한 정점에서 직관을 비워야 하는 길이. 정점에 곡관·이형관이 들어가면 그 몸통 자리만큼 직관이 물러난다.
/// 좌표와 같은 단위(m)다. 양쪽이 다른 것은 B형 곡관처럼 한쪽에만 직관부가 더 붙기 때문이다.
/// </summary>
/// <param name="Before">정점 직전(진행 방향 상류) 쪽으로 비울 길이.</param>
/// <param name="After">정점 직후(진행 방향 하류) 쪽으로 비울 길이.</param>
public readonly record struct VertexTrim(double Before, double After)
{
    public static VertexTrim None => new(0d, 0d);
    public bool IsEmpty => Before <= 0d && After <= 0d;
}

/// <summary>폴리라인의 호 길이를 기준으로 균일 간격 지점을 샘플링한다. 좌표와 간격은 같은 단위를 사용한다.</summary>
public static class AlignmentIntervalSampler
{
    private const double Tolerance = 1e-9;
    /// <summary>직선 병합 허용각 0.5°의 사인값. 엑셀 mm 반올림 노이즈는 흡수하고 실제 곡관 각도는 보존한다.</summary>
    public const double StraightSinTolerance = 8.7265354983739E-03;

    public static IReadOnlyList<AlignmentSamplePoint> SamplePoints(IReadOnlyList<Point3D> vertices, double interval)
    {
        ValidateInterval(interval);
        var parts = BuildParts(vertices);
        if (parts.Count == 0) return Array.Empty<AlignmentSamplePoint>();

        var total = parts[^1].EndDistance;
        var result = new List<AlignmentSamplePoint>();
        for (var i = 0; ; i++)
        {
            var distance = i * interval;              // 누산 대신 인덱스 곱 — 장거리 선형에서 오차 누적 방지
            if (distance > total + Tolerance) break;
            var part = parts.First(x => distance <= x.EndDistance + 1e-9);
            var local = Math.Clamp((distance - part.StartDistance) / part.Length, 0d, 1d);
            result.Add(new AlignmentSamplePoint(
                new Point3D(part.Start.X + (part.End.X - part.Start.X) * local, part.Start.Y + (part.End.Y - part.Start.Y) * local, part.Start.Z + (part.End.Z - part.Start.Z) * local),
                new Vector3D((part.End.X - part.Start.X) / part.Length, (part.End.Y - part.Start.Y) / part.Length, (part.End.Z - part.Start.Z) / part.Length),
                Math.Min(distance, total)));
        }
        return result;
    }

    /// <summary>정점 차감 없이(연장 0) 분절한다. 기존 호출부 계약이다.</summary>
    public static IReadOnlyList<AlignmentSampleSegment> SampleSegments(IReadOnlyList<Point3D> vertices, double interval)
        => SampleSegments(vertices, interval, null);

    /// <summary>직선상 중간 측점은 넘기고 DN 변경점·굴곡점에서만 6m 기준을 다시 시작한다.</summary>
    public static IReadOnlyList<AlignmentSampleSegment> SampleSegments(PipeAlignment alignment, double interval,
        IReadOnlyList<VertexTrim>? trims = null, IReadOnlySet<int>? stepVertices = null)
    {
        ArgumentNullException.ThrowIfNull(alignment);
        var requiredBreaks = new HashSet<int>();
        for (var i = 1; i < alignment.Vertices.Count; i++)
            if (AlignmentDiameterProfile.ChangesAtVertex(alignment, i))
                requiredBreaks.Add(i);
        return SampleSegmentsCore(alignment.Vertices, interval, trims, requiredBreaks, stepVertices, preserveAllVertices: false);
    }

    /// <summary>
    /// 정점별 차감(<paramref name="trims"/>)을 반영해 분절한다. 차감 구간은 곡관·이형관 몸통 자리이므로
    /// 직관을 만들지 않고, 남은 직관은 절점이 아니라 <b>몸통 끝에서부터</b> 다시 interval로 끊는다.
    /// </summary>
    /// <param name="trims">
    /// 인덱스 = <paramref name="vertices"/> 인덱스. null이거나 길이가 다르면 차감 없이 동작한다.
    /// </param>
    public static IReadOnlyList<AlignmentSampleSegment> SampleSegments(IReadOnlyList<Point3D> vertices, double interval,
        IReadOnlyList<VertexTrim>? trims, IReadOnlySet<int>? stepVertices = null)
        => SampleSegmentsCore(vertices, interval, trims, null, stepVertices, preserveAllVertices: true);

    private static IReadOnlyList<AlignmentSampleSegment> SampleSegmentsCore(IReadOnlyList<Point3D> vertices, double interval, IReadOnlyList<VertexTrim>? trims,
        IReadOnlySet<int>? requiredBreaks, IReadOnlySet<int>? stepVertices, bool preserveAllVertices)
    {
        ValidateInterval(interval);
        var parts = BuildParts(vertices);
        if (parts.Count == 0) return Array.Empty<AlignmentSampleSegment>();

        var total = parts[^1].EndDistance;
        var excluded = BuildExclusions(vertices, trims, total);
        var datumSteps = BuildDatumSteps(vertices, stepVertices);

        // 경계: 굴곡점·필수점·차감 끝. 같은 직선 측점은 6m 흐름 유지.
        var anchors = new List<double> { 0d, total };
        if (preserveAllVertices)
        {
            foreach (var part in parts) anchors.Add(part.EndDistance);
        }
        else
        {
            for (var i = 0; i < parts.Count - 1; i++)
                if (ChangesDirection(parts[i], parts[i + 1])) anchors.Add(parts[i].EndDistance);
            AddRequiredBreaks(vertices, requiredBreaks, anchors);
        }
        foreach (var range in excluded) { anchors.Add(range.Start); anchors.Add(range.End); }
        foreach (var range in datumSteps) { anchors.Add(range.Start); anchors.Add(range.End); }

        anchors.Sort();
        var boundaries = new List<double>(anchors.Count);
        foreach (var d in anchors)
        {
            var clamped = Math.Clamp(d, 0d, total);
            if (boundaries.Count == 0 || clamped - boundaries[^1] > Tolerance) boundaries.Add(clamped);
        }
        if (boundaries.Count < 2) return Array.Empty<AlignmentSampleSegment>();

        // 앵커 사이를 그 앵커 기준으로 다시 interval 분할한다(차감이 없으면 앵커=절점이라 기존 동작과 같다).
        var split = new List<double>(boundaries);
        for (var i = 1; i < boundaries.Count; i++)
        {
            var length = boundaries[i] - boundaries[i - 1];
            for (var k = 1; k * interval < length; k++) split.Add(boundaries[i - 1] + k * interval);
        }

        split.Sort();
        var final = new List<double>(split.Count);
        foreach (var d in split)
            // 절점 거리와 interval 배수가 겹칠 때 길이 0 세그먼트가 생기지 않도록 tolerance 기준으로 중복 제거한다.
            if (final.Count == 0 || d - final[^1] > Tolerance) final.Add(d);

        var result = new List<AlignmentSampleSegment>(final.Count - 1);
        for (var i = 1; i < final.Count; i++)
        {
            // 차감 구간 안에 든 조각은 곡관 몸통 자리라 직관을 만들지 않는다.
            var midpoint = (final[i - 1] + final[i]) * 0.5;
            if (IsInRange(excluded, midpoint)) continue;
            result.Add(new AlignmentSampleSegment(PointAt(parts, final[i - 1]), PointAt(parts, final[i]), final[i - 1],
                IsInRange(datumSteps, midpoint)));
        }
        return result;
    }

    /// <summary>정점 차감을 거리 구간으로 바꾸고 겹치는 것끼리 합친다. 인접 절점 간섭 시 구간이 겹칠 수 있다.</summary>
    private static List<(double Start, double End)> BuildExclusions(IReadOnlyList<Point3D> vertices, IReadOnlyList<VertexTrim>? trims, double total)
    {
        var merged = new List<(double Start, double End)>();
        if (trims is null || trims.Count != vertices.Count) return merged;

        var raw = new List<(double Start, double End)>();
        var distance = 0d;
        for (var i = 0; i < vertices.Count; i++)
        {
            if (i > 0) distance += vertices[i - 1].DistanceTo(vertices[i]);
            var trim = trims[i];
            if (trim.IsEmpty) continue;
            var start = Math.Clamp(distance - Math.Max(trim.Before, 0d), 0d, total);
            var end = Math.Clamp(distance + Math.Max(trim.After, 0d), 0d, total);
            if (end - start > Tolerance) raw.Add((start, end));
        }

        foreach (var range in raw.OrderBy(x => x.Start))
        {
            if (merged.Count > 0 && range.Start <= merged[^1].End + Tolerance)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, range.End));
            else merged.Add(range);
        }
        return merged;
    }

    private static List<(double Start, double End)> BuildDatumSteps(IReadOnlyList<Point3D> vertices, IReadOnlySet<int>? stepVertices)
    {
        var ranges = new List<(double Start, double End)>();
        if (stepVertices is null || stepVertices.Count == 0) return ranges;

        var distance = 0d;
        for (var i = 1; i < vertices.Count; i++)
        {
            var start = distance;
            distance += vertices[i - 1].DistanceTo(vertices[i]);
            if (stepVertices.Contains(i) && distance - start > Tolerance)
                ranges.Add((start, distance));
        }
        return ranges;
    }

    private static bool IsInRange(IReadOnlyList<(double Start, double End)> ranges, double distance)
    {
        foreach (var range in ranges)
            if (distance > range.Start - Tolerance && distance < range.End + Tolerance) return true;
        return false;
    }

    private static Point3D PointAt(IReadOnlyList<Part> parts, double distance)
    {
        var part = parts.First(x => distance <= x.EndDistance + 1e-9);
        var local = Math.Clamp((distance - part.StartDistance) / part.Length, 0d, 1d);
        return new Point3D(part.Start.X + (part.End.X - part.Start.X) * local, part.Start.Y + (part.End.Y - part.Start.Y) * local, part.Start.Z + (part.End.Z - part.Start.Z) * local);
    }

    private static bool ChangesDirection(Part first, Part second)
    {
        var ax = (first.End.X - first.Start.X) / first.Length;
        var ay = (first.End.Y - first.Start.Y) / first.Length;
        var az = (first.End.Z - first.Start.Z) / first.Length;
        var bx = (second.End.X - second.Start.X) / second.Length;
        var by = (second.End.Y - second.Start.Y) / second.Length;
        var bz = (second.End.Z - second.Start.Z) / second.Length;
        var dot = ax * bx + ay * by + az * bz;
        var cx = ay * bz - az * by;
        var cy = az * bx - ax * bz;
        var cz = ax * by - ay * bx;
        return dot <= 0d || Math.Sqrt(cx * cx + cy * cy + cz * cz) > StraightSinTolerance;
    }

    private static void AddRequiredBreaks(IReadOnlyList<Point3D> vertices, IReadOnlySet<int>? requiredBreaks, ICollection<double> anchors)
    {
        if (requiredBreaks is null || requiredBreaks.Count == 0) return;
        var distance = 0d;
        for (var i = 1; i < vertices.Count; i++)
        {
            distance += vertices[i - 1].DistanceTo(vertices[i]);
            if (requiredBreaks.Contains(i)) anchors.Add(distance);
        }
    }

    private static List<Part> BuildParts(IReadOnlyList<Point3D> vertices)
    {
        var parts = new List<Part>();
        var distance = 0d;
        for (var i = 1; i < vertices.Count; i++)
        {
            var length = vertices[i - 1].DistanceTo(vertices[i]);
            if (length <= 1e-9) continue;
            parts.Add(new Part(vertices[i - 1], vertices[i], distance, distance + length, length));
            distance += length;
        }
        return parts;
    }

    private static void ValidateInterval(double interval)
    {
        if (interval <= 0) throw new ArgumentOutOfRangeException(nameof(interval), "간격은 0보다 커야 합니다.");
    }

    private sealed record Part(Point3D Start, Point3D End, double StartDistance, double EndDistance, double Length);
}
