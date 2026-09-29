using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Core.Gis;

/// <param name="DiameterStepVertexIndices">선형별 기준면 단차로 갈라진 정점 쌍의 하류 쪽 출력 정점 인덱스 집합이다. 인덱스 v는 (v-1, v) 사이가 관축 직각 단차 조각임을 뜻한다.</param>
public sealed record DatumOffsetResult(IReadOnlyList<PipeAlignment> Alignments, IReadOnlyList<string> Warnings,
    IReadOnlyList<IReadOnlySet<int>> DiameterStepVertexIndices);

public static class AlignmentDatumOffsetter
{
    public const double SkewResidualToleranceMm = 1d;
    private const double Epsilon = 1e-9;

    /// <summary>OffsetMm 값을 연직 이동이 아닌 관축 직각 거리로 써 중심선 폴리선을 만든다.</summary>
    public static DatumOffsetResult ToCenterline(IReadOnlyList<PipeAlignment> alignments, ZDatum datum,
        StraightPipeSpecTable specs, double reducerBodyLengthMm = 250d)
    {
        ArgumentNullException.ThrowIfNull(alignments);
        ArgumentNullException.ThrowIfNull(specs);
        if (datum == ZDatum.Centerline)
            return new DatumOffsetResult(alignments, Array.Empty<string>(),
                alignments.Select(_ => (IReadOnlySet<int>)new HashSet<int>()).ToList());

        var warnings = new OffsetWarnings();
        var result = alignments.Select(alignment => Offset(alignment, datum, specs, reducerBodyLengthMm, warnings)).ToList();
        return new DatumOffsetResult(result.Select(x => x.Alignment).ToList(), warnings.Build(),
            result.Select(x => x.StepVertexIndices).ToList());
    }

    private static (PipeAlignment Alignment, IReadOnlySet<int> StepVertexIndices) Offset(PipeAlignment alignment, ZDatum datum, StraightPipeSpecTable specs,
        double reducerBodyLengthMm, OffsetWarnings warnings)
    {
        var vertices = alignment.Vertices;
        if (vertices.Count == 0) return (alignment, new HashSet<int>());
        var output = new RebuiltVertices(alignment);
        var stepVertexIndices = new HashSet<int>();
        for (var v = 0; v < vertices.Count; v++)
        {
            var upstream = v > 0 ? AlignmentVertexDirection.SkippingNear(vertices, v, -1, Epsilon) : null;
            var downstream = v + 1 < vertices.Count ? AlignmentVertexDirection.SkippingNear(vertices, v, +1, Epsilon) : null;
            var upstreamDiameter = v > 0 ? AlignmentDiameterProfile.DiameterAtVertex(alignment, v - 1) : AlignmentDiameterProfile.DiameterAtVertex(alignment, v);
            var downstreamDiameter = AlignmentDiameterProfile.DiameterAtVertex(alignment, v);
            var diameterChanged = v > 0 && v + 1 < vertices.Count && AlignmentDiameterProfile.ChangesAtVertex(alignment, v);
            var isBend = upstream is not null && downstream is not null && !AreCollinear(upstream, downstream);

            if (diameterChanged && !isBend)
            {
                var up = OffsetEnd(vertices[v], upstream, OffsetMm(alignment, datum, specs, upstreamDiameter, warnings), warnings);
                var down = OffsetEnd(vertices[v], downstream, OffsetMm(alignment, datum, specs, downstreamDiameter, warnings), warnings);
                if (up is null || down is null)
                {
                    output.Add(vertices[v], v);
                    continue;
                }
                output.Add(up, v - 1, v);
                output.Add(down, v);
                stepVertexIndices.Add(output.Count - 1);
                if (reducerBodyLengthMm <= 0d || up.DistanceTo(down) > reducerBodyLengthMm / 1000d)
                    warnings.ShortReducerGaps++;
                continue;
            }

            var upstreamOffsetMm = OffsetMm(alignment, datum, specs, upstreamDiameter, warnings);
            var downstreamOffsetMm = OffsetMm(alignment, datum, specs, downstreamDiameter, warnings);
            var point = OffsetVertex(vertices[v], upstream, downstream, upstreamOffsetMm, downstreamOffsetMm, warnings);
            output.Add(point ?? vertices[v], v);
        }
        return (output.Build(), stepVertexIndices);
    }

    private static double OffsetMm(PipeAlignment alignment, ZDatum datum, StraightPipeSpecTable specs,
        double diameterMm, OffsetWarnings warnings)
    {
        var spec = specs.Find(alignment.PipeKind, diameterMm);
        // 외경 폴백은 같은 관종 안에서만(docs/211 § B-3). 알 수 없는 등급은 주철관으로 본다.
        var material = PipeKindCatalog.MaterialOf(alignment.PipeKind) ?? PipeMaterial.DuctileIron;
        if (spec is null && specs.FindOuterDiameterByDiameter(diameterMm, material) is { } fallback)
            spec = new StraightPipeSpec(alignment.PipeKind, diameterMm, fallback.OuterDiameterMm, 0d);
        if (spec is null) warnings.MissingSpecs.Add((alignment.PipeKind, diameterMm));
        return PipeElevationDatum.OffsetMm(datum, spec, diameterMm) / 1000d;
    }

    private static Point3D? OffsetVertex(Point3D vertex, Vector3D? upstream, Vector3D? downstream,
        double upstreamOffsetM, double downstreamOffsetM, OffsetWarnings warnings)
    {
        if (upstream is null && downstream is null) return null;
        if (upstream is null) return OffsetEnd(vertex, downstream, downstreamOffsetM, warnings);
        if (downstream is null) return OffsetEnd(vertex, upstream, upstreamOffsetM, warnings);
        var firstNormal = UpNormal(upstream);
        var secondNormal = UpNormal(downstream);
        if (firstNormal is null || secondNormal is null) { warnings.VerticalVertices++; return null; }
        var first = Move(vertex, firstNormal, upstreamOffsetM);
        var second = Move(vertex, secondNormal, downstreamOffsetM);
        if (AreCollinear(upstream, downstream)) return Midpoint(first, second);
        var (a, b, residualM) = ClosestPoints(first, upstream, second, downstream);
        if (residualM > SkewResidualToleranceMm / 1000d)
        {
            warnings.SkewCount++;
            warnings.MaxSkewResidualM = Math.Max(warnings.MaxSkewResidualM, residualM);
        }
        return Midpoint(a, b);
    }

    private static Point3D? OffsetEnd(Point3D vertex, Vector3D? direction, double offsetM, OffsetWarnings warnings)
    {
        if (direction is null) return null;
        var normal = UpNormal(direction);
        if (normal is null) { warnings.VerticalVertices++; return null; }
        return Move(vertex, normal, offsetM);
    }

    private static Vector3D? UpNormal(Vector3D direction)
    {
        var normal = new Vector3D(-direction.X * direction.Z, -direction.Y * direction.Z, 1d - direction.Z * direction.Z);
        return normal.Length <= Epsilon ? null : normal.Normalize();
    }

    private static bool AreCollinear(Vector3D first, Vector3D second)
    {
        first = new Vector3D(-first.X, -first.Y, -first.Z);
        var cross = new Vector3D(
            first.Y * second.Z - first.Z * second.Y,
            first.Z * second.X - first.X * second.Z,
            first.X * second.Y - first.Y * second.X);
        return Dot(first, second) > 0d && cross.Length <= AlignmentIntervalSampler.StraightSinTolerance;
    }

    private static (Point3D First, Point3D Second, double ResidualM) ClosestPoints(Point3D firstPoint, Vector3D firstDirection,
        Point3D secondPoint, Vector3D secondDirection)
    {
        var delta = Difference(firstPoint, secondPoint);
        var dot = Dot(firstDirection, secondDirection);
        var divisor = 1d - dot * dot;
        if (Math.Abs(divisor) <= Epsilon) return (firstPoint, secondPoint, firstPoint.DistanceTo(secondPoint));
        var firstDistance = (dot * Dot(secondDirection, delta) - Dot(firstDirection, delta)) / divisor;
        var secondDistance = (Dot(secondDirection, delta) - dot * Dot(firstDirection, delta)) / divisor;
        var first = Move(firstPoint, firstDirection, firstDistance);
        var second = Move(secondPoint, secondDirection, secondDistance);
        return (first, second, first.DistanceTo(second));
    }

    private static double Dot(Vector3D first, Vector3D second) => first.X * second.X + first.Y * second.Y + first.Z * second.Z;
    private static Vector3D Difference(Point3D first, Point3D second) => new(first.X - second.X, first.Y - second.Y, first.Z - second.Z);
    private static Point3D Move(Point3D point, Vector3D direction, double distance) => new(point.X + direction.X * distance, point.Y + direction.Y * distance, point.Z + direction.Z * distance);
    private static Point3D Midpoint(Point3D first, Point3D second) => new((first.X + second.X) / 2d, (first.Y + second.Y) / 2d, (first.Z + second.Z) / 2d);

    private sealed class RebuiltVertices
    {
        private readonly PipeAlignment _source;
        private readonly List<Point3D> _vertices = new();
        private readonly List<string?>? _bendKinds;
        private readonly List<double?>? _diameters;
        private readonly List<string?>? _diameterRawValues;
        private readonly List<string?>? _stationTexts;
        private readonly List<double?>? _stations;

        public RebuiltVertices(PipeAlignment source)
        {
            _source = source;
            _bendKinds = source.VertexBendKinds is null ? null : new();
            _diameters = source.VertexDiameterMm is null ? null : new();
            _diameterRawValues = source.VertexDiameterRawValues is null ? null : new();
            _stationTexts = source.VertexStationTexts is null ? null : new();
            _stations = source.VertexStationM is null ? null : new();
        }

        public int Count => _vertices.Count;

        public void Add(Point3D point, int sourceIndex)
            => Add(point, sourceIndex, sourceIndex);

        public void Add(Point3D point, int diameterSourceIndex, int stationAndBendSourceIndex)
        {
            _vertices.Add(point);
            _bendKinds?.Add(ValueAt(_source.VertexBendKinds, stationAndBendSourceIndex));
            _diameters?.Add(ValueAt(_source.VertexDiameterMm, diameterSourceIndex));
            _diameterRawValues?.Add(ValueAt(_source.VertexDiameterRawValues, diameterSourceIndex));
            _stationTexts?.Add(ValueAt(_source.VertexStationTexts, stationAndBendSourceIndex));
            _stations?.Add(ValueAt(_source.VertexStationM, stationAndBendSourceIndex));
        }

        public PipeAlignment Build() => _source with
        {
            Vertices = _vertices,
            VertexBendKinds = _bendKinds,
            VertexDiameterMm = _diameters,
            VertexDiameterRawValues = _diameterRawValues,
            VertexStationTexts = _stationTexts,
            VertexStationM = _stations
        };

        private static T? ValueAt<T>(IReadOnlyList<T?>? values, int index) where T : struct
            => values is not null && index >= 0 && index < values.Count ? values[index] : null;
        private static string? ValueAt(IReadOnlyList<string?>? values, int index)
            => values is not null && index >= 0 && index < values.Count ? values[index] : null;
    }

    private sealed class OffsetWarnings
    {
        public HashSet<(string PipeKind, double DiameterMm)> MissingSpecs { get; } = new();
        public int VerticalVertices { get; set; }
        public int SkewCount { get; set; }
        public double MaxSkewResidualM { get; set; }
        public int ShortReducerGaps { get; set; }

        public IReadOnlyList<string> Build()
        {
            var warnings = new List<string>();
            if (MissingSpecs.Count > 0)
            {
                var values = string.Join(", ", MissingSpecs.OrderBy(x => x.PipeKind).ThenBy(x => x.DiameterMm).Select(x => $"{x.PipeKind}/DN{x.DiameterMm:0.##}"));
                warnings.Add($"직관 제원이 없어 관종/DN {MissingSpecs.Count}건은 호칭지름을 외경으로 간주해 기준면 보정을 적용했습니다: {values}");
            }
            if (VerticalVertices > 0) warnings.Add($"관이 연직에 가까워 기준면 보정을 적용하지 못한 정점 {VerticalVertices}곳이 있습니다.");
            if (SkewCount > 0) warnings.Add($"평면·종단이 동시에 꺾여 기준면 보정을 근사한 정점 {SkewCount}곳이 있습니다(최대 편차 {MaxSkewResidualM * 1000d:0.#}mm).");
            if (ShortReducerGaps > 0) warnings.Add($"이경관 길이가 짧아 DN 변경 지점 {ShortReducerGaps}곳에서 단차 구간이 직관으로 남았습니다.");
            return warnings;
        }
    }
}
