using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Core.Gis;

/// <summary>곡관 한 개의 다리 길이(mm). 상류 = 짧은 다리(배치 길이), 하류 = 긴 다리(<see cref="BendResolution.LongLegLengthMm"/>).</summary>
public sealed record CompoundBendLegs(double UpstreamMm, double DownstreamMm);

/// <param name="SplitCount">나눈 곡관 쌍 수.</param>
/// <param name="MaxDownstreamTurnDeg">하류 직관이 다음 측점까지 틀어진 최대 각(도). 평면+종단 쌍에서 생긴다.</param>
public sealed record CompoundBendSplitResult(IReadOnlyList<PipeAlignment> Alignments, int SplitCount,
    double MaxDownstreamTurnDeg, IReadOnlyList<string> Warnings);

/// <summary>
/// 붙은 곡관 쌍을 다리 길이 합 간격의 두 정점으로 나눈다 — 곡관 2개를 단관 없이 바로 잇기 위해서다(2026-10-27 사용자 결정).
/// <para>대상: ① 한 칸에 각도 2개(`22.5˚, 11.25˚` — 적힌 순서 = 흐름 순서) ② 이웃한 지정 정점 간격이 앞 곡관 하류 다리 + 뒤 곡관 상류 다리보다 짧음.
/// 평면 꺾임점과 종단 꺾임점이 같은 측점에 찍힌 경우다.</para>
/// <para>상류 직선은 유지한다. 두 곡관이 서로 다른 평면이면 두 직선을 다 지킬 수 없어, 하류 직관이 다음 측점까지 각도를 틀어 잇는다(사용자 결정 A).
/// 지정각 합이 실제 편각보다 작으면 같은 평면에 두고 둘째 곡관이 남은 편각을 맡는다 — 허용 초과 표시는 기존 판정이 한다.</para>
/// </summary>
public static class CompoundBendSplitter
{
    private const double Epsilon = 1e-9;
    private const int MaxIterations = 50;
    private const double ConvergenceRad = 1e-10;
    /// <summary>쌍 앞·뒤에서 건너뛸 수 있는 일반 측점 수(한쪽).</summary>
    private const int MaxSkippedPlainVertices = 2;
    /// <summary>하류 직관 꺾임이 이 값을 넘으면 측점별로 알린다 — 강관 무곡관 문턱(<see cref="SteelBendRule.NoBendThresholdDeg"/>)과 같다.</summary>
    private const double TurnWarningDeg = SteelBendRule.NoBendThresholdDeg;
    private static readonly char[] AngleSeparators = [',', '，', '/', '+'];

    /// <param name="legs">(선형, 정점, 각도) → 다리 길이. 치수를 모르면 null — 그 쌍은 나누지 않는다.</param>
    public static CompoundBendSplitResult Split(IReadOnlyList<PipeAlignment> alignments,
        Func<PipeAlignment, int, double, CompoundBendLegs?> legs)
    {
        var result = new List<PipeAlignment>(alignments.Count);
        var labels = new List<string>();
        var skipped = new List<string>();
        var turned = new List<string>();
        var splitCount = 0;
        var maxTurn = 0d;
        foreach (var alignment in alignments)
        {
            var (split, count, turn) = SplitOne(alignment, legs, labels, skipped, turned);
            result.Add(split);
            splitCount += count;
            maxTurn = Math.Max(maxTurn, turn);
        }

        var warnings = new List<string>();
        if (splitCount > 0)
            warnings.Add($"붙은 곡관 쌍 {splitCount}곳을 다리 길이만큼 벌린 연속 곡관으로 나눴습니다(예: {string.Join(", ", labels.Take(5))}). "
                         + $"상류 직선은 그대로이고, 하류 직관은 다음 측점까지 최대 {maxTurn:0.##}° 틀어 이었습니다.");
        if (turned.Count > 0)
            warnings.Add($"연속 곡관 하류 직관이 {TurnWarningDeg:0.#}°를 넘게 틀어진 곳 {turned.Count}곳 — 지정각과 좌표 편각이 다릅니다. 곡관유형·좌표를 확인하세요(예: {string.Join(", ", turned.Take(5))}).");
        if (skipped.Count > 0)
            warnings.Add($"붙은 곡관 쌍 {skipped.Count}곳은 나누지 못해 그대로 둡니다(예: {string.Join(", ", skipped.Take(5))}).");
        return new CompoundBendSplitResult(result, splitCount, maxTurn, warnings);
    }

    /// <summary>`22.5˚, 11.25˚` → [22.5, 11.25]. 한 조각이라도 각도가 아니면 빈 목록.</summary>
    public static IReadOnlyList<(double Angle, string Text)> ParseAngles(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        var parts = raw.Split(AngleSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var angles = new List<(double, string)>(parts.Length);
        foreach (var part in parts)
        {
            if (!BendKindParser.TryParseAngle(part, out var angle) || angle <= 0d) return [];
            angles.Add((angle, part));
        }
        return angles;
    }

    private sealed record Group(int First, int Last, double Angle1, string Text1, double Angle2, string Text2);

    private sealed record Replacement(Point3D V1, Point3D V2, double OffsetFromFirstM, double PairLengthM, double TurnDeg);

    private static (PipeAlignment Alignment, int Count, double MaxTurnDeg) SplitOne(PipeAlignment alignment,
        Func<PipeAlignment, int, double, CompoundBendLegs?> legs, List<string> labels, List<string> skipped, List<string> turned)
    {
        if (alignment.VertexBendKinds is not { } kinds || alignment.Vertices.Count < 3) return (alignment, 0, 0d);
        var vertices = alignment.Vertices;
        var groups = FindGroups(alignment, kinds, legs);
        if (groups.Count == 0) return (alignment, 0, 0d);

        var replacements = new Dictionary<int, (Group Group, Replacement Value)>();
        var removed = new HashSet<int>();
        var maxTurn = 0d;
        foreach (var (group, pairLengthM) in groups)
        {
            var label = AlignmentVertexLabel.Format(alignment, group.First);
            Replacement? value = null;
            foreach (var (prev, next) in Neighbors(kinds, vertices.Count, group))
            {
                value = Solve(vertices[prev], vertices[group.First], vertices[group.Last], vertices[next], group.Angle1, group.Angle2, pairLengthM);
                if (value is null) continue;
                // 쌍 자리를 덮은 일반 측점(곡관유형 없음)은 지운다 — 직관 중간 정점이라 선형이 바뀌지 않는다.
                for (var i = prev + 1; i < group.First; i++) removed.Add(i);
                for (var i = group.Last + 1; i < next; i++) removed.Add(i);
                break;
            }
            if (value is null)
            {
                skipped.Add(label);
                continue;
            }
            replacements[group.First] = (group, value);
            labels.Add(label);
            if (value.TurnDeg > TurnWarningDeg) turned.Add($"{label} {value.TurnDeg:0.#}°");
            maxTurn = Math.Max(maxTurn, value.TurnDeg);
        }
        if (replacements.Count == 0) return (alignment, 0, 0d);
        return (Rebuild(alignment, replacements, removed), replacements.Count, maxTurn);
    }

    /// <summary>앞·뒤 기준 정점 후보. 바로 이웃이 너무 가까우면 곡관유형 없는 일반 측점을 최대 <see cref="MaxSkippedPlainVertices"/>개까지 건너뛴다.</summary>
    private static IEnumerable<(int Prev, int Next)> Neighbors(IReadOnlyList<string?> kinds, int vertexCount, Group group)
    {
        var prevs = new List<int> { group.First - 1 };
        for (var i = group.First - 1; i > 0 && prevs.Count <= MaxSkippedPlainVertices && IsPlain(kinds, i); i--) prevs.Add(i - 1);
        var nexts = new List<int> { group.Last + 1 };
        for (var i = group.Last + 1; i < vertexCount - 1 && nexts.Count <= MaxSkippedPlainVertices && IsPlain(kinds, i); i++) nexts.Add(i + 1);
        foreach (var skip in Enumerable.Range(0, prevs.Count + nexts.Count - 1))
            for (var p = 0; p <= skip; p++)
                if (p < prevs.Count && skip - p < nexts.Count) yield return (prevs[p], nexts[skip - p]);
    }

    private static bool IsPlain(IReadOnlyList<string?> kinds, int index)
        => index >= kinds.Count || string.IsNullOrWhiteSpace(kinds[index]);

    /// <summary>앞에서부터 짝을 짓는다. 한 정점은 한 쌍에만 든다 — 셋 이상 붙은 경우 남은 하나는 그대로 둔다.</summary>
    private static List<(Group Group, double PairLengthM)> FindGroups(PipeAlignment alignment, IReadOnlyList<string?> kinds,
        Func<PipeAlignment, int, double, CompoundBendLegs?> legs)
    {
        var vertices = alignment.Vertices;
        var groups = new List<(Group, double)>();
        var count = Math.Min(kinds.Count, vertices.Count);
        for (var v = 1; v < vertices.Count - 1 && v < count; v++)
        {
            var angles = ParseAngles(kinds[v]);
            if (angles.Count == 2)
            {
                if (PairLength(alignment, v, v, angles[0].Angle, angles[1].Angle, legs) is { } length)
                    groups.Add((new Group(v, v, angles[0].Angle, angles[0].Text, angles[1].Angle, angles[1].Text), length));
                continue;
            }
            if (angles.Count != 1 || v + 1 >= vertices.Count - 1 || v + 1 >= count) continue;
            var next = ParseAngles(kinds[v + 1]);
            if (next.Count != 1) continue;
            if (PairLength(alignment, v, v + 1, angles[0].Angle, next[0].Angle, legs) is not { } required) continue;
            if (vertices[v].DistanceTo(vertices[v + 1]) >= required - Epsilon) continue;
            groups.Add((new Group(v, v + 1, angles[0].Angle, angles[0].Text, next[0].Angle, next[0].Text), required));
            v++;
        }
        return groups;
    }

    /// <summary>앞 곡관 하류 다리 + 뒤 곡관 상류 다리(m). 둘 사이에 단관이 없다.</summary>
    private static double? PairLength(PipeAlignment alignment, int first, int last, double angle1, double angle2,
        Func<PipeAlignment, int, double, CompoundBendLegs?> legs)
    {
        if (legs(alignment, first, angle1) is not { } a || legs(alignment, last, angle2) is not { } b) return null;
        var length = (a.DownstreamMm + b.UpstreamMm) / 1000d;
        return length > Epsilon ? length : null;
    }

    /// <summary>
    /// P(앞 정점) → V1 → V2 → Q(뒤 정점). V1은 상류 직선(P→first) 위, |V1V2| = 쌍 길이, ∠(상류, V1V2) = θ1.
    /// 하류 방향 c = (Q − V2)이고 ∠(V1V2, c) = θ2가 되도록 c를 고정점 반복으로 맞춘다.
    /// </summary>
    private static Replacement? Solve(Point3D p, Point3D first, Point3D last, Point3D q, double angle1, double angle2, double pairLengthM)
    {
        var upstreamLength = p.DistanceTo(first);
        if (upstreamLength <= Epsilon || q.DistanceTo(last) <= Epsilon) return null;
        var a = Unit(Sub(first, p));
        var c0 = Unit(Sub(q, last));
        var delta = AngleRad(a, c0);
        if (delta <= 1e-6 || delta >= Math.PI - 1e-6) return null;
        var t1 = Rad(angle1);
        var t2 = Rad(angle2);
        // 두 정점 쌍은 상류·하류 직선의 교점(가장 가까운 점)을 꺾임점으로 본다. 한 정점이면 그 정점이다.
        var (cornerUp, cornerDown) = ClosestPoints(first, a, last, c0);
        first = cornerUp;
        last = cornerDown;
        upstreamLength = p.DistanceTo(first);

        // 지정각 합이 실제 편각보다 작으면 공간 해가 없다 — 같은 평면에 두고 둘째 곡관이 남은 편각(δ − θ1)을 맡는다.
        var planar = t1 + t2 <= delta + 1e-9;
        if (planar)
        {
            var t2Eff = delta - t1;
            if (t2Eff <= 1e-9) return null;
            // 삼각형 V1-V-V2: V1 내각 θ1, V2 내각 δ − θ1, V 외각 δ. 두 직선을 그대로 지킨다.
            var s1 = pairLengthM * Math.Sin(t2Eff) / Math.Sin(delta);
            var s2 = pairLengthM * Math.Sin(t1) / Math.Sin(delta);
            if (s1 >= upstreamLength - Epsilon || s2 >= q.DistanceTo(last) - Epsilon) return null;
            var planarV1 = Add(first, Scale(a, -s1));
            var planarV2 = Add(last, Scale(c0, s2));
            return new Replacement(planarV1, planarV2, s1, pairLengthM, 0d);
        }

        // 공간 쌍: V1 위치는 같은 평면이었을 때의 비율(θ1+θ2 기준)로 정한다 — 같은 평면이면 위 해와 같아진다.
        var offset = pairLengthM * Math.Sin(t2) / Math.Sin(t1 + t2);
        if (offset >= upstreamLength - Epsilon) return null;
        var v1 = Add(first, Scale(a, -offset));
        var c = c0;
        Vector3D? m = null;
        for (var i = 0; i < MaxIterations; i++)
        {
            m = Middle(a, c, t1, t2);
            if (m is null) return null;
            var v2Try = Add(v1, Scale(m, pairLengthM));
            var toQ = Sub(q, v2Try);
            if (toQ.Length <= pairLengthM * 0.5) return null;
            var next = Unit(toQ);
            var moved = AngleRad(next, c);
            c = next;
            if (moved < ConvergenceRad) break;
        }
        if (m is null) return null;
        var v2 = Add(v1, Scale(m, pairLengthM));
        return new Replacement(v1, v2, offset, pairLengthM, Deg(AngleRad(c, c0)));
    }

    /// <summary>직선 (p1 + a·t)와 (p2 + c·u)에서 서로 가장 가까운 두 점. 평행이면 원래 두 점.</summary>
    private static (Point3D OnFirst, Point3D OnSecond) ClosestPoints(Point3D p1, Vector3D a, Point3D p2, Vector3D c)
    {
        var w0 = Sub(p1, p2);
        var b = Dot(a, c);
        var denom = 1d - b * b;
        if (denom < 1e-12) return (p1, p2);
        var d = Dot(a, w0);
        var e = Dot(c, w0);
        var t = (b * e - d) / denom;
        var u = (e - b * d) / denom;
        return (Add(p1, Scale(a, t)), Add(p2, Scale(c, u)));
    }

    /// <summary>∠(a, m) = θ1, ∠(m, c) = θ2인 단위벡터 m. 해가 둘이면 두 곡관 평면이 수평·수직에 가까운 쪽(평면 꺾임 + 종단 꺾임)을 고른다.</summary>
    private static Vector3D? Middle(Vector3D a, Vector3D c, double t1, double t2)
    {
        var cosDelta = Dot(a, c);
        var det = 1d - cosDelta * cosDelta;
        if (det < 1e-12) return null;
        var alpha = (Math.Cos(t1) - cosDelta * Math.Cos(t2)) / det;
        var beta = (Math.Cos(t2) - cosDelta * Math.Cos(t1)) / det;
        var w = Add(Scale(a, alpha), Scale(c, beta));
        var gammaSquared = 1d - Dot(w, w);
        if (gammaSquared < -1e-9) return null;
        var gamma = Math.Sqrt(Math.Max(0d, gammaSquared));
        var n = Unit(Cross(a, c));
        var plus = Unit(Add(w, Scale(n, gamma)));
        var minus = Unit(Add(w, Scale(n, -gamma)));
        return AxisScore(a, plus, c) <= AxisScore(a, minus, c) ? plus : minus;
    }

    /// <summary>곡관 평면 법선의 연직 성분이 0(종단 꺾임) 또는 1(평면 꺾임)에서 먼 정도의 합. 작을수록 좋다.</summary>
    private static double AxisScore(Vector3D a, Vector3D m, Vector3D c)
        => PlaneScore(Cross(a, m)) + PlaneScore(Cross(m, c));

    private static double PlaneScore(Vector3D normal)
    {
        if (normal.Length <= Epsilon) return 0d;
        var z = Math.Abs(normal.Normalize().Z);
        return Math.Min(z, 1d - z);
    }

    /// <summary>쌍 정점(first..last)을 V1·V2 둘로 바꾼다. 정점별 값은 V1 = first, V2 = last 값을 따른다.</summary>
    private static PipeAlignment Rebuild(PipeAlignment source, IReadOnlyDictionary<int, (Group Group, Replacement Value)> replacements,
        IReadOnlySet<int> removed)
    {
        var vertices = new List<Point3D>();
        var map = new List<int>();
        var kinds = new List<string?>();
        var stations = source.VertexStationM is null ? null : new List<double?>();
        for (var v = 0; v < source.Vertices.Count; v++)
        {
            if (removed.Contains(v)) continue;
            if (replacements.TryGetValue(v, out var item))
            {
                var (group, value) = item;
                vertices.Add(value.V1);
                vertices.Add(value.V2);
                map.Add(group.First);
                map.Add(group.Last);
                kinds.Add(group.Text1);
                kinds.Add(group.Text2);
                if (stations is not null)
                {
                    var origin = ValueAt(source.VertexStationM, group.First);
                    stations.Add(origin - value.OffsetFromFirstM);
                    stations.Add(origin - value.OffsetFromFirstM + value.PairLengthM);
                }
                v = group.Last;
                continue;
            }
            vertices.Add(source.Vertices[v]);
            map.Add(v);
            kinds.Add(ValueAt(source.VertexBendKinds, v));
            stations?.Add(ValueAt(source.VertexStationM, v));
        }

        return source with
        {
            Vertices = vertices,
            VertexBendKinds = kinds,
            VertexDiameterMm = Remap(source.VertexDiameterMm, map),
            VertexDiameterRawValues = Remap(source.VertexDiameterRawValues, map),
            VertexStationTexts = Remap(source.VertexStationTexts, map),
            VertexStationM = stations
        };
    }

    private static List<T?>? Remap<T>(IReadOnlyList<T?>? values, IReadOnlyList<int> map)
        => values is null ? null : map.Select(i => ValueAt(values, i)).ToList();

    private static T? ValueAt<T>(IReadOnlyList<T?>? values, int index)
        => values is not null && index >= 0 && index < values.Count ? values[index] : default;

    private static Vector3D Sub(Point3D a, Point3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static Point3D Add(Point3D p, Vector3D v) => new(p.X + v.X, p.Y + v.Y, p.Z + v.Z);
    private static Vector3D Add(Vector3D a, Vector3D b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    private static Vector3D Scale(Vector3D v, double s) => new(v.X * s, v.Y * s, v.Z * s);
    private static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static Vector3D Cross(Vector3D a, Vector3D b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    private static Vector3D Unit(Vector3D v) => v.Normalize();
    private static double AngleRad(Vector3D a, Vector3D b) => Math.Acos(Math.Clamp(Dot(a, b), -1d, 1d));
    private static double Rad(double deg) => deg * Math.PI / 180d;
    private static double Deg(double rad) => rad * 180d / Math.PI;
}
