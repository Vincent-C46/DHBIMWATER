using System.Reflection;
using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Gis;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Application.UseCases.Gis;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Gis;
using Xunit;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.UI.Tests.Gis;

public class DatumPlacementIntegrationTests
{
    private static readonly StraightPipeSpecTable Specs = new(new[]
    {
        new StraightPipeSpec(PipeKindCatalog.Water1, 300, 326, 8.8),
        new StraightPipeSpec(PipeKindCatalog.Water1, 500, 532, 11)
    });

    [Theory]
    [InlineData(PipeAlignmentOutputMode.DirectShape, ZDatum.Centerline)]
    [InlineData(PipeAlignmentOutputMode.DirectShape, ZDatum.Invert)]
    [InlineData(PipeAlignmentOutputMode.Adaptive, ZDatum.Centerline)]
    [InlineData(PipeAlignmentOutputMode.Adaptive, ZDatum.Invert)]
    public void Mixed_bend_keeps_plan_vertices_and_bend_orientation(PipeAlignmentOutputMode mode, ZDatum datum)
    {
        var input = Alignment(new(0, 0, 0), new(1, 0, 0), new(1, 1, -1)) with
        {
            VertexBendKinds = new string?[] { null, "90˚", null }
        };
        var capture = new Capture(input);
        var result = capture.Execute(datum, mode);

        Assert.Equal(input.Vertices, capture.Alignments!.Single().Vertices);
        Assert.Equal(datum, capture.Origin!.ZDatum);
        Assert.All(capture.Steps!, Assert.Empty);
        var shift = datum == ZDatum.Centerline ? 0 : 0.255;
        var node = Shift(capture.Alignments.Single().Vertices[1], capture.Origin.GetZOffsetM(input));
        Assert.Equal(new Point3D(1, 0, shift), node);
        Assert.True(capture.Bends is { Count: 1 }, string.Join(Environment.NewLine, result.Warnings));
        var bend = Assert.Single(capture.Bends!);
        Assert.Equal(90, bend.DeflectionDeg, 7);
        Assert.Equal(shift, bend.ZOffsetM, 8);
        if (mode == PipeAlignmentOutputMode.Adaptive)
        {
            Assert.Equal(0, bend.RotXYDeg[0], 7);
            Assert.Equal(0, bend.RotXZDeg[0], 7);
            Assert.Equal(90, bend.RotXYDeg[4], 7);
            Assert.Equal(45, bend.RotXZDeg[4], 7);
        }
    }

    [Fact]
    public void Sloped_straight_is_translated_without_changing_length()
    {
        var input = Alignment(new(0, 0, 0), new(8, 0, -6));
        var capture = new Capture(input);
        capture.Execute(ZDatum.Invert);
        var actual = capture.Alignments!.Single();
        var shift = capture.Origin!.GetZOffsetM(actual);
        Assert.Equal(0.255, shift, 8);
        Assert.Equal(input.Vertices, actual.Vertices);
        Assert.Equal(10, Shift(actual.Vertices[0], shift).DistanceTo(Shift(actual.Vertices[1], shift)), 8);
    }

    [Theory]
    [InlineData(ZDatum.Centerline, false)]
    [InlineData(ZDatum.Invert, false)]
    [InlineData(ZDatum.Invert, true)]
    [InlineData(ZDatum.Crown, false)]
    [InlineData(ZDatum.Crown, true)]
    [InlineData(ZDatum.OutsideBottom, false)]
    [InlineData(ZDatum.OutsideTop, true)]
    public void Reducer_ends_meet_each_trimmed_pipe_and_preserve_selected_datum(ZDatum datum, bool reverse)
    {
        var up = reverse ? 500d : 300d;
        var down = reverse ? 300d : 500d;
        var input = Alignment(new(0, 0, 0), new(10, 0, 0), new(20, 0, 0)) with
        {
            DiameterMm = up, VertexDiameterMm = new double?[] { up, down, down }
        };
        var capture = new Capture(input);
        capture.Execute(datum);

        Assert.Equal(3, capture.Alignments!.Single().Vertices.Count);
        Assert.All(capture.Steps!, Assert.Empty);
        var reducer = Assert.Single(capture.Reducers!);
        Assert.Equal(up, reducer.Placement.UpstreamDiameterMm);
        Assert.Equal(down, reducer.Placement.DownstreamDiameterMm);
        var profiles = reducer.EndProfiles();
        Assert.Equal(ExpectedShift(profiles.Upstream, datum), reducer.UpstreamZOffsetM, 8);
        Assert.Equal(ExpectedShift(profiles.Downstream, datum), reducer.DownstreamZOffsetM, 8);
        Assert.Equal(9.875, reducer.Placement.UpstreamEnd.X, 8);
        Assert.Equal(10.125, reducer.Placement.DownstreamEnd.X, 8);
        var samples = AlignmentIntervalSampler.SampleSegments(capture.Alignments.Single(), 6, capture.Trims!.Single());
        var diameterProfile = new AlignmentDiameterProfile(capture.Alignments.Single());
        var ends = samples.Select(segment => new
        {
            Start = Shift(segment.Start, capture.Origin!.GetZOffsetM(input.PipeKind, diameterProfile.Find(segment.DistanceFromStart))),
            End = Shift(segment.End, capture.Origin!.GetZOffsetM(input.PipeKind, diameterProfile.Find(segment.DistanceFromStart)))
        }).ToList();
        Assert.Contains(ends, x => x.End.DistanceTo(Shift(reducer.Placement.UpstreamEnd, reducer.UpstreamZOffsetM)) < 1e-8);
        Assert.Contains(ends, x => x.Start.DistanceTo(Shift(reducer.Placement.DownstreamEnd, reducer.DownstreamZOffsetM)) < 1e-8);
        Assert.DoesNotContain(samples, x => x.IsDatumStep);
    }

    [Theory]
    [InlineData(ZDatum.Invert, 0.01, false)]
    [InlineData(ZDatum.Invert, 0.75, true)]
    [InlineData(ZDatum.Centerline, 0.75, false)]
    [InlineData(ZDatum.Crown, 0.75, true)]
    public void Slope_warning_reports_only_errors_over_ten_mm(ZDatum datum, double slope, bool warned)
    {
        var capture = new Capture(Alignment(new(0, 0, 0), new(4, 0, -4 * slope)));
        var result = capture.Execute(datum);
        var warnings = result.Warnings.Where(x => x.StartsWith("경사가 급해")).ToList();
        if (!warned) Assert.Empty(warnings);
        else
        {
            var warning = Assert.Single(warnings);
            Assert.Contains("구간 1곳", warning);
            Assert.Contains(63.75.ToString("0.#"), warning);
            Assert.Contains(PipeAlignmentParameters.Label(datum), warning);
        }
    }

    [Fact]
    public void Slope_warning_uses_segment_dn_and_skips_zero_length_and_vertical_segments()
    {
        var input = Alignment(new(0, 0, 0), new(0, 0, 0), new(0, 0, 1), new(4, 0, -2)) with
        {
            DiameterMm = 300, VertexDiameterMm = new double?[] { 300, 300, 500, 500 }
        };
        var request = new PipeAlignmentModelingRequest { Files = Array.Empty<AlignmentSourceFile>(), ZDatum = ZDatum.Invert };
        var method = typeof(ModelPipeAlignmentUseCase).GetMethod("GetSlopeDatumWarnings", BindingFlags.NonPublic | BindingFlags.Static)!;
        var warnings = (IReadOnlyList<string>)method.Invoke(null, new object[] { new[] { input }, request, Specs })!;
        var warning = Assert.Single(warnings);
        Assert.Contains("구간 1곳", warning);
        Assert.Contains(63.75.ToString("0.#"), warning);
    }

    private static double ExpectedShift(ReducerEndProfile profile, ZDatum datum)
    {
        var outer = profile.OuterDiameterMm / 2000;
        var inner = outer - profile.WallThicknessMm / 1000;
        return datum switch
        {
            ZDatum.Invert => inner, ZDatum.Crown => -inner,
            ZDatum.OutsideBottom => outer, ZDatum.OutsideTop => -outer, _ => 0
        };
    }

    private static Point3D Shift(Point3D point, double z) => new(point.X, point.Y, point.Z + z);
    private static PipeAlignment Alignment(params Point3D[] vertices) => new(vertices,
        PipeKindCatalog.Water1, 500, "synthetic.shp", "1", new Dictionary<string, string>());

    private sealed class Capture(PipeAlignment input)
    {
        public IReadOnlyList<PipeAlignment>? Alignments;
        public AlignmentPlacementOrigin? Origin;
        public IReadOnlyList<IReadOnlyList<VertexTrim>>? Trims;
        public IReadOnlyList<IReadOnlySet<int>>? Steps;
        public IReadOnlyList<AdaptiveBendPlacementPlan>? Bends;
        public IReadOnlyList<ReducerDirectShapePlacementPlan>? Reducers;

        public PipeAlignmentModelingResult Execute(ZDatum datum, PipeAlignmentOutputMode mode = PipeAlignmentOutputMode.DirectShape)
        {
            var reader = (IAlignmentSourceReader)Stub(typeof(IAlignmentSourceReader));
            var loader = new AlignmentSourceLoader(new[] { reader });
            var constructor = typeof(ModelPipeAlignmentUseCase).GetConstructors().Single();
            var dependencies = constructor.GetParameters().Select(p => p.ParameterType == typeof(AlignmentSourceLoader)
                ? (object)loader : p.ParameterType == typeof(BendSettingsProvider)
                    ? new BendSettingsProvider((IBendSettingsRepo)Stub(typeof(IBendSettingsRepo))) : Stub(p.ParameterType)).ToArray();
            var useCase = (ModelPipeAlignmentUseCase)constructor.Invoke(dependencies);
            return useCase.Execute(new PipeAlignmentModelingRequest
            {
                Files = new[] { new AlignmentSourceFile(input.SourceFile, input.PipeKind, null, null, ManualDiameterMm: input.DiameterMm) },
                ZDatum = datum, OutputMode = mode, ReferenceX = 0, ReferenceY = 0,
                StraightFamilyTypeName = "Straight : Test", BendFamilyTypeName = "Bend : Test",
                CurrentBendSettings = BendSettings.Default with { StraightPipes = Specs }
            });
        }

        private object Stub(Type type)
        {
            var proxy = (RepositoryProxy)DispatchProxy.Create(type, typeof(RepositoryProxy));
            proxy.Handle = (method, args) =>
            {
                switch (method.Name)
                {
                    case "CanRead": return true;
                    case "Read": return new ShapefileReadResult(new[] { input }, Array.Empty<ShapefileFieldInfo>(),
                        ShapefileExtent.Empty, null, null, "UTF-8", 1, input.Vertices.Count, input.Vertices.Count - 1, Array.Empty<string>(), null);
                    case "get_SuppressedWarnings": return Array.Empty<string>();
                    case "GetTrueNorthToProjectNorthClockwiseDegrees": return 0d;
                    case "GetFamilyTypeCategoryId": return null;
                    case "FindMissingSymbols": return Array.Empty<(string FamilyName, string TypeName)>();
                    case "RecordConnectionJoints": return Array.Empty<string>();   // 연결 기준 접합(docs/266 § 1) — 배치 검증과 무관
                    case "PlaceAlong":
                        Alignments = (IReadOnlyList<PipeAlignment>)args[0]!;
                        Origin = (AlignmentPlacementOrigin)args[3]!;
                        Trims = (IReadOnlyList<IReadOnlyList<VertexTrim>>?)args[5];
                        Steps = (IReadOnlyList<IReadOnlySet<int>>?)args[11];
                        return new AlignmentStraightPlacementResult(1, Array.Empty<string>());
                    case "Place" when args[0] is IReadOnlyList<ReducerDirectShapePlacementPlan> reducers:
                        Reducers = reducers;
                        return new ReducerPlacementResult(reducers.Count, Array.Empty<string>());
                    case "Place" when args[0] is IReadOnlyList<AdaptiveBendPlacementPlan> bends:
                        Bends = bends;
                        return new AdaptiveBendPlacementResult(bends.Count, Array.Empty<string>());
                    default:
                        if (method.ReturnType == typeof(void)) return null;
                        throw new InvalidOperationException($"Unexpected repository call: {type.Name}.{method.Name}");
                }
            };
            return proxy;
        }
    }

    public class RepositoryProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handle = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handle(targetMethod!, args!);
    }
}
