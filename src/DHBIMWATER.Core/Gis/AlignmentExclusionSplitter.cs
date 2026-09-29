using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Core.Gis;

/// <summary>관을 두지 않을 측점 구간(밸브실 등 구체 자리, docs/219). 측점은 파일·레코드마다 따로라 레코드번호로 대상을 고른다.</summary>
/// <param name="RecordNumber">대상 레코드. 같은 파일(엑셀은 시트) 안에서만 유일하다.</param>
public sealed record AlignmentExclusionRange(string RecordNumber, double StartStationM, double EndStationM, string? Name = null);

/// <param name="Parts">제외 구간 경계로 잘린 선형 조각(진행 방향 순서).</param>
/// <param name="AppliedRanges">실제로 적용한 구간(측점 범위 밖 제외).</param>
/// <param name="ExcludedLengthM">관을 두지 않는 3D 길이 합계(겹침 합친 뒤).</param>
/// <param name="SkippedBendKindCount">구간 안에 들어 사라진 곡관유형 지정 정점 수.</param>
/// <param name="IgnoredRanges">선형 측점 범위 밖이거나 길이가 0이라 무시한 구간.</param>
public sealed record AlignmentExclusionSplitResult(
    IReadOnlyList<PipeAlignment> Parts,
    IReadOnlyList<AlignmentExclusionRange> AppliedRanges,
    double ExcludedLengthM,
    int SkippedBendKindCount,
    IReadOnlyList<AlignmentExclusionRange> IgnoredRanges);

/// <summary>
/// 선형을 제외 구간 경계에서 잘라 여러 조각으로 만든다(docs/219 § B).
/// <para>잘린 조각은 독립 선형이므로 직관 분절은 조각 시작(= 끝 Sta.)에서 다시 interval로 시작하고,
/// 구간 안 정점(곡관 자리)은 조각에 들어가지 않아 곡관도 생기지 않는다. 배치 Repo·플래너는 그대로 쓴다.</para>
/// <para>조각의 <see cref="PipeAlignment.VertexStationM"/>에는 부모 측점을 모든 정점에 기록한다 — 조각의 측점이 0부터
/// 다시 시작하지 않고 원래 측점(측점 없는 SHP는 부모 평면 누적거리)을 유지하게 하기 위해서다.</para>
/// </summary>
public static class AlignmentExclusionSplitter
{
    private const double VertexTolerance = 1e-6;
    /// <summary>이보다 짧은 조각은 버린다 — 구간이 선형 시·종점에 붙은 경우의 잔여.</summary>
    public const double MinPartLengthM = 1e-3;

    public static AlignmentExclusionSplitResult Split(PipeAlignment alignment, IReadOnlyList<AlignmentExclusionRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(alignment);
        ArgumentNullException.ThrowIfNull(ranges);
        if (ranges.Count == 0 || alignment.Vertices.Count < 2)
            return new AlignmentExclusionSplitResult(new[] { alignment }, Array.Empty<AlignmentExclusionRange>(), 0d, 0, ranges);

        var profile = new AlignmentStationProfile(alignment);
        var total = profile.TotalDistance;
        var applied = new List<AlignmentExclusionRange>();
        var ignored = new List<AlignmentExclusionRange>();
        var raw = new List<(double Start, double End)>();
        foreach (var range in ranges)
        {
            if (!double.IsFinite(range.StartStationM) || !double.IsFinite(range.EndStationM)
                || Math.Abs(range.EndStationM - range.StartStationM) <= VertexTolerance
                || !profile.TryGetDistanceAtStation(range.StartStationM, out var a)
                || !profile.TryGetDistanceAtStation(range.EndStationM, out var b))
            {
                ignored.Add(range);
                continue;
            }
            // 역방향(측점 감소) 선형은 끝 Sta.이 진행 방향 앞쪽이다.
            raw.Add((Math.Clamp(Math.Min(a, b), 0d, total), Math.Clamp(Math.Max(a, b), 0d, total)));
            applied.Add(range);
        }
        if (raw.Count == 0)
            return new AlignmentExclusionSplitResult(new[] { alignment }, applied, 0d, 0, ignored);

        var merged = new List<(double Start, double End)>();
        foreach (var range in raw.OrderBy(x => x.Start))
        {
            if (merged.Count > 0 && range.Start <= merged[^1].End + VertexTolerance)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, range.End));
            else merged.Add(range);
        }

        var distances = new double[alignment.Vertices.Count];
        for (var i = 1; i < distances.Length; i++) distances[i] = profile.DistanceAtVertex(i);

        var kept = new List<(double Start, double End)>();
        var cursor = 0d;
        foreach (var range in merged)
        {
            if (range.Start - cursor >= MinPartLengthM) kept.Add((cursor, range.Start));
            cursor = Math.Max(cursor, range.End);
        }
        if (total - cursor >= MinPartLengthM) kept.Add((cursor, total));

        var skippedBends = 0;
        if (alignment.VertexBendKinds is { } kinds)
            for (var v = 0; v < distances.Length && v < kinds.Count; v++)
                if (!string.IsNullOrWhiteSpace(kinds[v]) && merged.Any(r => distances[v] > r.Start + VertexTolerance && distances[v] < r.End - VertexTolerance))
                    skippedBends++;

        var parts = kept.Select(x => BuildPart(alignment, profile, distances, x.Start, x.End)).ToList();
        return new AlignmentExclusionSplitResult(parts, applied, merged.Sum(x => x.End - x.Start), skippedBends, ignored);
    }

    private static PipeAlignment BuildPart(PipeAlignment source, AlignmentStationProfile profile, double[] distances, double start, double end)
    {
        // (원본 정점 인덱스 또는 -1, 거리) — 절단점은 원본 정점과 1e-6 이내로 겹치면 그 정점을 쓴다.
        var entries = new List<(int Index, double Distance)>();
        var startIndex = VertexAt(distances, start);
        entries.Add((startIndex, start));
        for (var v = 0; v < distances.Length; v++)
            if (distances[v] > start + VertexTolerance && distances[v] < end - VertexTolerance) entries.Add((v, distances[v]));
        entries.Add((VertexAt(distances, end), end));

        var vertices = entries.Select(x => x.Index >= 0 ? source.Vertices[x.Index] : PointAt(source.Vertices, distances, x.Distance)).ToList();
        var stations = entries.Select(x => (double?)profile.StationAt(x.Distance)).ToList();
        return source with
        {
            Vertices = vertices,
            VertexStationM = stations,
            VertexBendKinds = source.VertexBendKinds is null ? null : entries.Select(x => OriginalOnly(source.VertexBendKinds, x.Index)).ToList(),
            VertexStationTexts = source.VertexStationTexts is null ? null : entries.Select(x => OriginalOnly(source.VertexStationTexts, x.Index)).ToList(),
            // DN은 "이 정점부터 시작하는 구간" 값이다(Loader 주석). 절단점은 그 점이 속한 원본 구간 값을 이어받는다.
            VertexDiameterMm = source.VertexDiameterMm is null ? null : SegmentValues(source.VertexDiameterMm, distances, entries),
            VertexDiameterRawValues = source.VertexDiameterRawValues is null ? null : SegmentValues(source.VertexDiameterRawValues, distances, entries)
        };
    }

    private static int VertexAt(double[] distances, double distance)
    {
        for (var v = 0; v < distances.Length; v++)
            if (Math.Abs(distances[v] - distance) <= VertexTolerance) return v;
        return -1;
    }

    /// <summary>거리가 속한 원본 구간의 시작 정점 인덱스. 원본 정점 위면 그 정점(마지막 정점이면 직전 구간).</summary>
    private static int SegmentStartIndex(double[] distances, double distance)
    {
        var index = 0;
        for (var v = 1; v < distances.Length - 1; v++)
        {
            if (distances[v] > distance + VertexTolerance) break;
            index = v;
        }
        return index;
    }

    private static T? OriginalOnly<T>(IReadOnlyList<T?> values, int index) where T : class
        => index >= 0 && index < values.Count ? values[index] : null;

    private static List<T> SegmentValues<T>(IReadOnlyList<T> values, double[] distances, List<(int Index, double Distance)> entries)
    {
        var result = new List<T>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var (index, distance) = entries[i];
            var isLast = i == entries.Count - 1;
            // 조각 끝점: 원본 마지막 정점이면 원본 값 그대로(기존 동작 보존), 절단점이면 그 앞 구간 값 — 조각 끝에서 DN이 바뀐 것으로 보이지 않게.
            // 그 밖: 원본 정점이면 그 값, 절단점은 속한 원본 구간 값.
            var sourceIndex = isLast
                ? index == distances.Length - 1 ? index : SegmentStartIndex(distances, distance - VertexTolerance * 2)
                : index >= 0 ? index : SegmentStartIndex(distances, distance);
            result.Add(sourceIndex < values.Count ? values[sourceIndex] : default!);
        }
        return result;
    }

    private static Point3D PointAt(IReadOnlyList<Point3D> vertices, double[] distances, double distance)
    {
        var i = SegmentStartIndex(distances, distance);
        var length = distances[i + 1] - distances[i];
        var t = length <= 1e-12 ? 0d : Math.Clamp((distance - distances[i]) / length, 0d, 1d);
        var a = vertices[i];
        var b = vertices[i + 1];
        return new Point3D(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
    }
}
