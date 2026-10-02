using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.Application.DTOs.Gis;
using Xunit;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.UI.Tests.Gis;

public class ReducerTrimPlannerTests
{
    [Fact]
    public void Diameter_change_creates_symmetric_trim_and_placement()
    {
        var plan = Plan(250d);

        var placement = Assert.Single(plan.Placements);
        Assert.Equal(new VertexTrim(0.125d, 0.125d), plan.Trims[0][1]);
        Assert.Equal(VertexTrim.None, plan.Trims[0][0]);
        Assert.Equal(VertexTrim.None, plan.Trims[0][2]);
        Assert.Equal(300d, placement.UpstreamDiameterMm);
        Assert.Equal(500d, placement.DownstreamDiameterMm);
    }

    [Fact]
    public void Zero_length_skips_placement_with_one_warning()
    {
        var plan = Plan(0d);

        Assert.Empty(plan.Placements);
        Assert.All(plan.Trims.Single(), x => Assert.Equal(VertexTrim.None, x));
        Assert.Single(plan.Warnings);
        Assert.Contains("이경관 길이가 0", plan.Warnings.Single());
    }

    [Fact]
    public void Bend_trim_at_same_vertex_skips_reducer_with_warning()
    {
        var bends = new BendTrimPlan(
            new[] { new AlignmentBendPlan(0, new[] { VertexTrim.None, new VertexTrim(0.1d, 0.1d), VertexTrim.None }) },
            Array.Empty<BendPlacement>(), Array.Empty<string>());

        var plan = Plan(250d, bends);

        Assert.Empty(plan.Placements);
        // 배치 동작은 그대로(이경관 생략), 경고만 추가됐다(docs/215 § E).
        Assert.Contains("이경관을 생략한 곳 1곳", Assert.Single(plan.Warnings));
        Assert.All(plan.Trims.Single(), x => Assert.Equal(VertexTrim.None, x));
    }

    [Fact]
    public void Expansion_keeps_progress_direction_diameters_and_lookup_order()
    {
        var plan = Plan(250d, vertexDiameters: new double?[] { 500d, 300d, 300d });

        var placement = Assert.Single(plan.Placements);
        Assert.Equal(500d, placement.UpstreamDiameterMm);
        Assert.Equal(300d, placement.DownstreamDiameterMm);
    }

    [Fact]
    public void Placement_station_uses_vertex_station_plus_minus_half_body_length()
    {
        var plan = Plan(250d);

        var placement = Assert.Single(plan.Placements);
        Assert.Equal(134.435d, placement.StaStartM, 6);
        Assert.Equal(134.685d, placement.StaEndM, 6);
    }

    [Fact]
    public void Datum_step_uses_each_run_direction_and_asymmetric_trim()
    {
        var plan = Plan(250d, steps: new[] { (IReadOnlySet<int>)new HashSet<int> { 2 } }, alignment: StepAlignment());

        var placement = Assert.Single(plan.Placements);
        Assert.Equal(new Point3D(9.925d, 0, 0), placement.UpstreamEnd);
        Assert.Equal(new Point3D(10.075d, 0, 0.1d), placement.DownstreamEnd);
        Assert.Equal(new Vector3D(1, 0, 0), placement.RunDirection);
        Assert.Equal(0.175d, plan.Trims[0][2].Before, 12);
        Assert.Equal(0.075d, plan.Trims[0][2].After, 12);
        Assert.Equal(VertexTrim.None, plan.Trims[0][1]);
    }

    [Fact]
    public void Datum_step_longer_than_body_skips_placement_with_warning()
    {
        var plan = Plan(50d, steps: new[] { (IReadOnlySet<int>)new HashSet<int> { 2 } }, alignment: StepAlignment());

        Assert.Empty(plan.Placements);
        Assert.Single(plan.Warnings);
        Assert.Contains("기준면 단차보다 짧아", plan.Warnings.Single());
    }

    [Theory]
    [InlineData(300d, 500d, 326d, 532d)]
    [InlineData(500d, 300d, 532d, 326d)]
    public void End_profiles_follow_progress_direction(double upstreamDiameter, double downstreamDiameter,
        double expectedUpstreamOuter, double expectedDownstreamOuter)
    {
        var placement = new ReducerPlacement(0, 1, new Point3D(0, 0, 0), new Point3D(1, 0, 0),
            upstreamDiameter, downstreamDiameter, PipeKindCatalog.Water1, 250d, ReducerAlignment.Eccentric, 0d, 0.25d, new Vector3D(1, 0, 0));
        var plan = new ReducerDirectShapePlacementPlan(placement, "test.xlsx", "1", 532d, 326d, 11d, 8.8d, 0d, 0d);

        var profiles = plan.EndProfiles();

        Assert.Equal(expectedUpstreamOuter, profiles.Upstream.OuterDiameterMm);
        Assert.Equal(expectedDownstreamOuter, profiles.Downstream.OuterDiameterMm);
    }

    [Fact]
    public void Catalog_source_uses_handbook_length_form_and_weight()
    {
        // 주철 소켓 편락관 300×200: L=250, B형은 소경 쪽 직관부 200 → 몸통 450, KP B형 38.0kg(docs/260 § G).
        var source = new CatalogReducerSpecSource(BendConnection.Socket, JointTypeCatalog.KpMechanical, ReducerForm.BType, 250d, ReducerAlignment.Eccentric);
        var plan = Plan(source, new double?[] { 300d, 200d, 200d });

        var placement = Assert.Single(plan.Placements);
        Assert.Equal(450d, placement.BodyLengthMm);
        Assert.Equal(200d, placement.SmallStraightMm);
        Assert.Equal(0d, placement.LargeStraightMm);
        Assert.Equal(38.0d, placement.WeightKg);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Catalog_source_falls_back_to_input_length_with_warning_and_no_weight()
    {
        // 500×300은 소켓 편락관 표에 없다(대경 500은 소경 350·400만) → 입력 길이 250, 무게 없음, 경고 1줄.
        var source = new CatalogReducerSpecSource(BendConnection.Socket, JointTypeCatalog.KpMechanical, ReducerForm.BType, 250d, ReducerAlignment.Eccentric);
        var plan = Plan(source, new double?[] { 300d, 500d, 500d });

        var placement = Assert.Single(plan.Placements);
        Assert.Equal(250d, placement.BodyLengthMm);
        Assert.Null(placement.WeightKg);
        Assert.Contains("카탈로그에 없는 대·소경 조합 1곳", Assert.Single(plan.Warnings));
    }

    private static ReducerTrimPlan Plan(IReducerSpecSource source, IReadOnlyList<double?> vertexDiameters)
    {
        var alignment = new PipeAlignment(
            new[] { new Point3D(0, 0, 0), new Point3D(10, 0, 0), new Point3D(20, 0, 0) },
            PipeKindCatalog.Water1, 300d, "test.xlsx", "도수1-3", new Dictionary<string, string>(),
            VertexDiameterMm: vertexDiameters,
            VertexStationM: new double?[] { 124.56d, 134.56d, 144.56d });
        return ReducerTrimPlanner.Plan(new[] { alignment }, source, null, 0.001d);
    }

    private static ReducerTrimPlan Plan(double bodyLengthMm, BendTrimPlan? bends = null, IReadOnlyList<double?>? vertexDiameters = null,
        IReadOnlyList<IReadOnlySet<int>>? steps = null, PipeAlignment? alignment = null)
    {
        alignment ??= new PipeAlignment(
            new[] { new Point3D(0, 0, 0), new Point3D(10, 0, 0), new Point3D(20, 0, 0) },
            PipeKindCatalog.Water1, 300d, "test.xlsx", "도수1-3", new Dictionary<string, string>(),
            VertexDiameterMm: vertexDiameters ?? new double?[] { 300d, 500d, 500d },
            VertexStationM: new double?[] { 124.56d, 134.56d, 144.56d });
        return ReducerTrimPlanner.Plan(new[] { alignment },
            new FixedReducerSpecSource(bodyLengthMm, ReducerAlignment.Eccentric), bends, 0.001d, steps);
    }

    private static PipeAlignment StepAlignment() => new(
        new[] { new Point3D(0, 0, 0), new Point3D(10, 0, 0), new Point3D(10, 0, 0.1d), new Point3D(20, 0, 0.1d) },
        PipeKindCatalog.Water1, 300d, "test.xlsx", "도수1-3", new Dictionary<string, string>(),
        VertexDiameterMm: new double?[] { 300d, 300d, 500d, 500d }, VertexStationM: new double?[] { 0d, 10d, 10.1d, 20.1d });
}
