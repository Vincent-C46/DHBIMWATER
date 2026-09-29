using DHBIMWATER.Application.Gis;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Gis;
using Xunit;

namespace DHBIMWATER.UI.Tests.Gis;

/// <summary>
/// docs/217. 곡관 다리 끝(P1·P5)은 폴리선을 3D 호길이로 따라간 점이어야 직관 시작/끝과 일치하고,
/// XY 투영 측점이 기록 측점과 같아진다. 인접 곡관 다리 겹침은 표시 대상으로만 올린다.
/// </summary>
public sealed class BendLegOnAlignmentTests
{
    private const double N0 = 543340d;
    private const double E0 = 220450d;

    [Fact]
    public void KinkInsideLeg_BendEndsMatchStraightEnds()
    {
        var alignment = Dosu11Around2413();
        var plan = Plan(alignment);
        var bendVertex = Array.IndexOf(alignment.VertexStationTexts!.ToArray(), "2+413.69");
        var placement = Assert.Single(plan.Placements, x => x.VertexIndex == bendVertex);

        var segments = AlignmentIntervalSampler.SampleSegments(alignment, 6d, plan.Plans[0].Trims);
        var profile = new AlignmentStationProfile(alignment);
        var nodeDistance = profile.DistanceAtVertex(bendVertex);
        var before = segments.Last(x => x.DistanceFromStart < nodeDistance);
        var after = segments.First(x => x.DistanceFromStart > nodeDistance);

        // 종전(첫 이웃 정점 방향 직선 연장)은 여기서 약 37mm 벌어졌다.
        Assert.True(placement.Points.Start.DistanceTo(before.End) < 1e-6, $"P1 gap {placement.Points.Start.DistanceTo(before.End)}");
        Assert.True(placement.Points.End.DistanceTo(after.Start) < 1e-6, $"P5 gap {placement.Points.End.DistanceTo(after.Start)}");
    }

    [Fact]
    public void KinkInsideLeg_ProjectedStationsMatchRecordedStations()
    {
        var alignment = Dosu11Around2413();
        var plan = Plan(alignment);
        var bendVertex = Array.IndexOf(alignment.VertexStationTexts!.ToArray(), "2+413.69");
        var placement = Assert.Single(plan.Placements, x => x.VertexIndex == bendVertex);
        var profile = new AlignmentStationProfile(alignment);
        var nodeDistance = profile.DistanceAtVertex(bendVertex);

        var recordedStart = profile.StationAt(nodeDistance - placement.UpstreamLegMm / 1000d);
        var recordedEnd = profile.StationAt(nodeDistance + placement.DownstreamLegMm / 1000d);
        Assert.InRange(ProjectedStation(alignment, placement.Points.Start).Station - recordedStart, -0.001, 0.001);
        Assert.InRange(ProjectedStation(alignment, placement.Points.End).Station - recordedEnd, -0.001, 0.001);

        // P2·P4는 평면상 선형에서 1mm 안이다(P3만 설계상 벗어난다).
        Assert.True(ProjectedStation(alignment, placement.Points.ArcStart).LateralM < 0.001);
        Assert.True(ProjectedStation(alignment, placement.Points.ArcEnd).LateralM < 0.001);
    }

    [Fact]
    public void NoKinkInsideLeg_PointsUnchangedFromVertexDirection()
    {
        var alignment = new PipeAlignment(
            [new Point3D(0, 0, 0), new Point3D(20, 0, 1), new Point3D(30, 10, 2), new Point3D(40, 20, 3)],
            PipeKindCatalog.Water1, 300, "test.xlsx", "1", new Dictionary<string, string>(),
            VertexBendKinds: [null, "45˚", null, null]);
        var plan = Plan(alignment);
        var placement = Assert.Single(plan.Placements);
        var v = alignment.Vertices[1];
        var upstream = AlignmentVertexDirection.SkippingNear(alignment.Vertices, 1, -1, PipeNetworkBuilder.DefaultSnapToleranceM)!;
        var downstream = AlignmentVertexDirection.SkippingNear(alignment.Vertices, 1, +1, PipeNetworkBuilder.DefaultSnapToleranceM)!;
        var deflection = 180d - Math.Acos(Math.Clamp(upstream.X * downstream.X + upstream.Y * downstream.Y + upstream.Z * downstream.Z, -1d, 1d)) * 180d / Math.PI;
        var expected = BendArcGeometry.ComputeForAlignment(v, upstream, downstream, placement.AngleDeg, deflection,
            placement.CenterlineRadiusMm, placement.UpstreamLegMm, placement.DownstreamLegMm);

        Assert.True(expected.Start.DistanceTo(placement.Points.Start) < 1e-6);
        Assert.True(expected.ArcStart.DistanceTo(placement.Points.ArcStart) < 1e-6);
        Assert.True(expected.ArcMid.DistanceTo(placement.Points.ArcMid) < 1e-6);
        Assert.True(expected.ArcEnd.DistanceTo(placement.Points.ArcEnd) < 1e-6);
        Assert.True(expected.End.DistanceTo(placement.Points.End) < 1e-6);
        Assert.False(placement.HasLegOverlap);
    }

    [Fact]
    public void AdjacentBendsWithOverlappingLegs_AreBothMarked()
    {
        // 정점 1·2가 0.2m 간격 — 두 45° 곡관의 다리가 서로 겹친다.
        var alignment = new PipeAlignment(
            [new Point3D(0, 0, 0), new Point3D(20, 0, 0), new Point3D(20.1414, 0.1414, 0), new Point3D(40.1414, 0.1414, 0)],
            PipeKindCatalog.Water1, 300, "test.xlsx", "1", new Dictionary<string, string>(),
            VertexBendKinds: [null, "45˚", "45˚", null]);
        var plan = Plan(alignment);
        Assert.Equal(2, plan.Placements.Count);

        var (placements, pairs) = BendLegOverlapDetector.Mark(plan.Placements, [alignment]);

        Assert.Single(pairs);
        Assert.All(placements, x => Assert.True(x.HasLegOverlap));
    }

    [Fact]
    public void DistantBends_AreNotMarked()
    {
        var alignment = new PipeAlignment(
            [new Point3D(0, 0, 0), new Point3D(20, 0, 0), new Point3D(34.1421, 14.1421, 0), new Point3D(54.1421, 14.1421, 0)],
            PipeKindCatalog.Water1, 300, "test.xlsx", "1", new Dictionary<string, string>(),
            VertexBendKinds: [null, "45˚", "45˚", null]);
        var plan = Plan(alignment);
        Assert.Equal(2, plan.Placements.Count);

        var (placements, pairs) = BendLegOverlapDetector.Mark(plan.Placements, [alignment]);

        Assert.Empty(pairs);
        Assert.All(placements, x => Assert.False(x.HasLegOverlap));
    }

    private static BendTrimPlan Plan(PipeAlignment alignment)
    {
        var graph = PipeNetworkBuilder.Build([alignment]);
        var nodes = PipeNetworkClassifier.Classify(graph);
        var authority = BendAuthorityPolicy.Evaluate([alignment], graph, nodes, BendSettings.Default, PipeNetworkBuilder.DefaultSnapToleranceM);
        return BendTrimPlanner.Plan([alignment], graph, authority.Kept, PipeNetworkBuilder.DefaultSnapToleranceM);
    }

    /// <summary>XY 평면에서 엑셀 폴리선에 가장 가까운 점의 측점(구간 안 평면거리 비율 보간)과 횡거리.</summary>
    private static (double Station, double LateralM) ProjectedStation(PipeAlignment alignment, Point3D point)
    {
        var best = (Station: double.NaN, LateralM: double.MaxValue);
        for (var i = 1; i < alignment.Vertices.Count; i++)
        {
            var a = alignment.Vertices[i - 1];
            var b = alignment.Vertices[i];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var length2 = dx * dx + dy * dy;
            if (length2 <= 1e-18) continue;
            var t = Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy) / length2, 0d, 1d);
            var lateral = Math.Sqrt(Math.Pow(a.X + dx * t - point.X, 2) + Math.Pow(a.Y + dy * t - point.Y, 2));
            if (lateral >= best.LateralM) continue;
            var s0 = alignment.VertexStationM![i - 1]!.Value;
            var s1 = alignment.VertexStationM![i]!.Value;
            best = (s0 + t * (s1 - s0), lateral);
        }
        return best;
    }

    /// <summary>'도수1-1' 시트 2+409.00 ~ 2+424.00(곡관 22.5° @ 2+413.69, 13.8mm 뒤 2+413.70에서 2.17° 꺾임).</summary>
    private static PipeAlignment Dosu11Around2413()
    {
        (string Text, double Station, double N, double E, double Z, string? Kind)[] rows =
        [
            ("2+409.00", 2409.00, 543350.5755, 220454.7531, 84.668, null),
            ("2+410.56", 2410.56, 543349.2336, 220455.5528, 84.890, null),
            ("2+410.60", 2410.60, 543349.2010, 220455.5722, 84.895, null),
            ("2+412.00", 2412.00, 543347.9984, 220456.2889, 85.151, null),
            ("2+412.86", 2412.86, 543347.2634, 220456.7268, 85.307, null),
            ("2+412.90", 2412.90, 543347.2253, 220456.7496, 85.315, null),
            ("2+413.69", 2413.69, 543346.5496, 220457.1522, 85.462, "22.5˚"),
            ("2+413.70", 2413.70, 543346.5418, 220457.1632, 85.465, null),
            ("2+415.00", 2415.00, 543345.7876, 220458.2221, 85.703, null),
            ("2+417.19", 2417.19, 543344.5155, 220460.0081, 86.104, null),
            ("2+417.20", 2417.20, 543344.5113, 220460.0140, 86.105, null),
            ("2+418.00", 2418.00, 543344.0472, 220460.6657, 86.252, null),
            ("2+421.00", 2421.00, 543342.3068, 220463.1092, 86.801, null),
            ("2+424.00", 2424.00, 543340.5664, 220465.5528, 87.350, null),
        ];
        return new PipeAlignment(
            rows.Select(x => new Point3D(x.E - E0, x.N - N0, x.Z)).ToList(),
            PipeKindCatalog.Water1, 300, "test.xlsx", "1", new Dictionary<string, string>(),
            VertexBendKinds: rows.Select(x => x.Kind).ToList(),
            VertexStationTexts: rows.Select(x => (string?)x.Text).ToList(),
            VertexStationM: rows.Select(x => (double?)x.Station).ToList());
    }
}
