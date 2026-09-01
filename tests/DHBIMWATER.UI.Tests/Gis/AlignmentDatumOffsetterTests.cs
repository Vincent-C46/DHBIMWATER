using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Gis;
using Xunit;

namespace DHBIMWATER.UI.Tests.Gis;

public class AlignmentDatumOffsetterTests
{
    private static readonly StraightPipeSpecTable Specs = new(new[]
    {
        new StraightPipeSpec(PipeKindCatalog.Water1, 300d, 326d, 8.8d),
        new StraightPipeSpec(PipeKindCatalog.Water1, 500d, 532d, 11d)
    });

    [Fact]
    public void Centerline_returns_input_instance_without_warning()
    {
        var input = new[] { Alignment(new Point3D(0, 0, 0), new Point3D(1, 0, 0)) };

        var result = AlignmentDatumOffsetter.ToCenterline(input, ZDatum.Centerline, Specs);

        Assert.Same(input, result.Alignments);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Horizontal_invert_raises_every_vertex_by_inner_radius()
    {
        var input = Alignment(new Point3D(0, 0, 0), new Point3D(1, 0, 0));
        var radiusM = PipeElevationDatum.OffsetMm(ZDatum.Invert, Specs.Find(PipeKindCatalog.Water1, 500d), 500d) / 1000d;

        var result = AlignmentDatumOffsetter.ToCenterline(new[] { input }, ZDatum.Invert, Specs).Alignments.Single();

        Assert.All(result.Vertices, point => Assert.Equal(radiusM, point.Z, 12));
    }

    [Fact]
    public void Sloped_invert_line_has_r_over_cos_vertical_separation_at_same_plan_position()
    {
        var input = Alignment(new Point3D(0, 0, 0), new Point3D(0.8, 0, -0.6));
        var radiusM = PipeElevationDatum.OffsetMm(ZDatum.Invert, Specs.Find(PipeKindCatalog.Water1, 500d), 500d) / 1000d;

        var result = AlignmentDatumOffsetter.ToCenterline(new[] { input }, ZDatum.Invert, Specs).Alignments.Single();
        var transformedZAtOrigin = ZAtX(result.Vertices[0], result.Vertices[1], 0d);

        Assert.Equal(radiusM / 0.8d, transformedZAtOrigin, 4);
    }

    [Fact]
    public void Vertical_bend_keeps_deflection_angle()
    {
        var input = Alignment(new Point3D(0, 0, 0), new Point3D(1, 0, 0), new Point3D(2, 0, -1));
        var before = Deflection(input.Vertices);

        var result = AlignmentDatumOffsetter.ToCenterline(new[] { input }, ZDatum.Invert, Specs).Alignments.Single();

        Assert.Equal(before, Deflection(result.Vertices), 9);
    }

    [Fact]
    public void Plan_bend_raises_all_vertices_equally()
    {
        var input = Alignment(new Point3D(0, 0, 0), new Point3D(1, 0, 0), new Point3D(1, 1, 0));
        var radiusM = PipeElevationDatum.OffsetMm(ZDatum.Invert, Specs.Find(PipeKindCatalog.Water1, 500d), 500d) / 1000d;

        var result = AlignmentDatumOffsetter.ToCenterline(new[] { input }, ZDatum.Invert, Specs).Alignments.Single();

        Assert.All(result.Vertices, point => Assert.Equal(radiusM, point.Z, 12));
    }

    [Fact]
    public void Mixed_three_dimensional_bend_warns_and_uses_equal_distance_midpoint()
    {
        var input = Alignment(new Point3D(0, 0, 0), new Point3D(1, 0, 0), new Point3D(1, 1, -1));
        var radiusM = PipeElevationDatum.OffsetMm(ZDatum.Invert, Specs.Find(PipeKindCatalog.Water1, 500d), 500d) / 1000d;

        var result = AlignmentDatumOffsetter.ToCenterline(new[] { input }, ZDatum.Invert, Specs);
        var point = result.Alignments.Single().Vertices[1];
        var firstDistance = DistanceToLine(point, new Point3D(1, 0, radiusM), new Vector3D(-1, 0, 0));
        var secondDirection = new Vector3D(0, Math.Sqrt(0.5), -Math.Sqrt(0.5));
        var secondNormal = new Vector3D(0, 0.5, 0.5).Normalize();
        var secondDistance = DistanceToLine(point, Move(new Point3D(1, 0, 0), secondNormal, radiusM), secondDirection);

        Assert.Contains(result.Warnings, warning => warning.Contains("근사한 정점 1곳"));
        Assert.Equal(firstDistance, secondDistance, 9);
    }

    [Fact]
    public void Vertical_line_stays_put_and_warns_once()
    {
        var input = Alignment(new Point3D(0, 0, 0), new Point3D(0, 0, 1));

        var result = AlignmentDatumOffsetter.ToCenterline(new[] { input }, ZDatum.Invert, Specs);

        Assert.Equal(input.Vertices, result.Alignments.Single().Vertices);
        Assert.Single(result.Warnings);
        Assert.Contains("관이 연직", result.Warnings.Single());
    }

    [Fact]
    public void Crown_is_opposite_of_invert_and_outside_top_has_negative_sign()
    {
        var input = Alignment(new Point3D(0, 0, 0), new Point3D(1, 0, 0));
        var invert = AlignmentDatumOffsetter.ToCenterline(new[] { input }, ZDatum.Invert, Specs).Alignments.Single();
        var crown = AlignmentDatumOffsetter.ToCenterline(new[] { input }, ZDatum.Crown, Specs).Alignments.Single();
        var outsideTop = AlignmentDatumOffsetter.ToCenterline(new[] { input }, ZDatum.OutsideTop, Specs).Alignments.Single();

        Assert.Equal(-invert.Vertices[0].Z, crown.Vertices[0].Z, 12);
        Assert.True(outsideTop.Vertices[0].Z < 0d);
    }

    [Fact]
    public void Diameter_change_splits_vertex_and_rebuilds_every_parallel_array()
    {
        var input = new PipeAlignment(
            new[] { new Point3D(0, 0, 0), new Point3D(1, 0, 0), new Point3D(2, 0, 0) },
            PipeKindCatalog.Water1, 300d, "test.xlsx", "1", new Dictionary<string, string>(),
            new string?[] { null, "", "45도" }, new double?[] { 300d, 500d, 500d },
            new string?[] { "300", "500", "500" }, new string?[] { "0+000", "0+001", "0+002" },
            new double?[] { 0d, 1d, 2d });

        var result = AlignmentDatumOffsetter.ToCenterline(new[] { input }, ZDatum.Invert, Specs).Alignments.Single();

        Assert.Equal(4, result.Vertices.Count);
        Assert.Equal(4, result.VertexDiameterMm!.Count);
        Assert.Equal(4, result.VertexBendKinds!.Count);
        Assert.Equal(4, result.VertexDiameterRawValues!.Count);
        Assert.Equal(4, result.VertexStationTexts!.Count);
        Assert.Equal(4, result.VertexStationM!.Count);
        Assert.Equal(300d, result.VertexDiameterMm[1]);
        Assert.Equal(500d, result.VertexDiameterMm[2]);
    }

    private static PipeAlignment Alignment(params Point3D[] vertices) => new(vertices, PipeKindCatalog.Water1, 500d, "test.xlsx", "1", new Dictionary<string, string>());
    private static double ZAtX(Point3D first, Point3D second, double x) => first.Z + (second.Z - first.Z) * (x - first.X) / (second.X - first.X);
    private static double Deflection(IReadOnlyList<Point3D> vertices)
    {
        var first = Direction(vertices[0], vertices[1]);
        var second = Direction(vertices[1], vertices[2]);
        return Math.Acos(Math.Clamp(Dot(first, second), -1d, 1d)) * 180d / Math.PI;
    }
    private static double DistanceToLine(Point3D point, Point3D linePoint, Vector3D direction)
    {
        var delta = new Vector3D(point.X - linePoint.X, point.Y - linePoint.Y, point.Z - linePoint.Z);
        var projection = Dot(delta, direction);
        return new Vector3D(delta.X - direction.X * projection, delta.Y - direction.Y * projection, delta.Z - direction.Z * projection).Length;
    }
    private static Vector3D Direction(Point3D first, Point3D second) => new Vector3D(second.X - first.X, second.Y - first.Y, second.Z - first.Z).Normalize();
    private static Point3D Move(Point3D point, Vector3D direction, double distance) => new(point.X + direction.X * distance, point.Y + direction.Y * distance, point.Z + direction.Z * distance);
    private static double Dot(Vector3D first, Vector3D second) => first.X * second.X + first.Y * second.Y + first.Z * second.Z;
}
