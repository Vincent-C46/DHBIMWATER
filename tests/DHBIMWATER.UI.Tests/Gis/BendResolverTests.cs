using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.Core.Piping;
using Xunit;
// NodeKind가 Core.Gis와 Core.Piping 양쪽에 있다. 이 테스트는 관로(Gis) 노드 종류를 쓴다.
using NodeKind = DHBIMWATER.Core.Gis.NodeKind;

namespace DHBIMWATER.UI.Tests.Gis;

public class BendResolverTests
{
    private static BendSettings Settings(double allowableDeg, BendForm form = BendForm.BType,
        string activeJointType = JointTypeCatalog.KpMechanical, BendConnection connection = BendConnection.Socket,
        params BendFittingEntry[] fittings) => new(
        StraightPipeSpecTable.Default,
        new JointDeflectionTable(new[] { new JointDeflectionSpec(activeJointType, 100, allowableDeg) }),
        new BendFittingCatalog(fittings), activeJointType, connection, form);

    private static NodeClassification Bend(double deflectionDeg, double diameterMm = 100, string pipeKind = PipeKindCatalog.Water1) =>
        new(1, new Point3D(0, 0, 0), NodeKind.Bend, 2, deflectionDeg, diameterMm, diameterMm, pipeKind);

    [Theory]
    [InlineData(45, 5, 45)]
    [InlineData(43, 5, 45)]
    [InlineData(16, 5, 11.25)]
    public void Deflection_within_allowable_selects_standard_angle(double theta, double allowable, double expected)
    {
        var result = BendResolver.Resolve(Bend(theta), Settings(allowable));
        Assert.Equal(BendResolutionKind.Standard, result.Kind);
        Assert.Equal(expected, result.StandardAngleDeg);
        Assert.True(result.IsAcceptable);
    }

    [Fact]
    public void Deflection_inside_joint_allowable_needs_no_bend()
    {
        var result = BendResolver.Resolve(Bend(3), Settings(5));
        Assert.Equal(BendResolutionKind.None, result.Kind);
        Assert.True(result.IsAcceptable);
    }

    [Fact]
    public void Flanged_mode_limits_candidates_and_uses_flanged_dimensions()
    {
        var flanged = new BendFittingEntry(100, 45, BendForm.BType, 140, 230, Connection: BendConnection.Flanged);
        var result = BendResolver.Resolve(Bend(22.5), Settings(3, form: BendForm.AType, connection: BendConnection.Flanged, fittings: flanged));

        Assert.Equal(BendResolutionKind.Unresolved, result.Kind);
        Assert.Equal(45, result.StandardAngleDeg);
        Assert.Equal(140, result.LayingLengthMm);
        Assert.Equal(230, result.CenterlineRadiusMm);
        Assert.Equal(0, result.EffectiveAllowableDeg);
        Assert.Equal(0, result.ExtraLegLengthMm);
        Assert.Equal(BendForm.AType, result.Form);
    }

    [Fact]
    public void Flanged_b_form_uses_200mm_extra_leg()
    {
        var flanged = new BendFittingEntry(100, 45, BendForm.BType, 140, 230, Connection: BendConnection.Flanged);
        var result = BendResolver.Resolve(Bend(45), Settings(3, form: BendForm.BType, connection: BendConnection.Flanged, fittings: flanged));

        Assert.Equal(BendForm.BType, result.Form);
        Assert.Equal(200d, result.ExtraLegLengthMm);
    }

    [Theory]
    [InlineData(BendForm.AType, JointApplicationMode.BothJoints, 0d)]
    [InlineData(BendForm.BType, JointApplicationMode.SingleJoint, 200d)]
    public void Socket_form_controls_application_mode_spigot_and_handbook_weight(
        BendForm form, JointApplicationMode expectedMode, double expectedExtraLegMm)
    {
        var fitting = new BendFittingEntry(100, 45, BendForm.BType, 90, 100, WeightKpMechanicalKg: 999d);
        var settings = Settings(1, form: form, fittings: fitting);

        var result = BendResolver.Resolve(Bend(45), settings);

        Assert.Equal(expectedMode, settings.ApplicationMode);
        Assert.Equal(expectedExtraLegMm, result.ExtraLegLengthMm);
        Assert.Equal(form, result.Form);
    }

    [Fact]
    public void Unacceptable_bend_keeps_nearest_fitting_size_for_placement()
    {
        var fitting = new BendFittingEntry(100, 11.25, BendForm.AType, 130, 210);
        var result = BendResolver.Resolve(Bend(16), Settings(3, fittings: fitting));
        Assert.Equal(BendResolutionKind.Unresolved, result.Kind);
        Assert.False(result.IsAcceptable);
        Assert.True(result.HasFittingSize);
        Assert.Equal(130, result.LayingLengthMm);
        Assert.Equal(210, result.CenterlineRadiusMm);
    }

    [Fact]
    public void Missing_joint_setting_marks_even_exact_angle_unacceptable()
    {
        // 허용굴곡 미설정 상태를 빈 표로 명시한다.
        // (2026-08-18 이전에는 JointDeflectionTable.Default가 빈 표라 그것을 썼지만, 이제 핸드북 값이 들어 있다.)
        var settings = new BendSettings(StraightPipeSpecTable.Default, new JointDeflectionTable(Array.Empty<JointDeflectionSpec>()),
            new BendFittingCatalog(new[] { new BendFittingEntry(100, 45, BendForm.AType, 130, 210) }),
            JointTypeCatalog.KpMechanical);
        var result = BendResolver.Resolve(Bend(45), settings);
        Assert.Equal(BendResolutionKind.Unresolved, result.Kind);
        Assert.False(result.IsAcceptable);
        Assert.Equal(0, result.EffectiveAllowableDeg);
    }

    [Fact]
    public void Both_joints_add_two_individually_looked_up_allowances()
    {
        var table = new JointDeflectionTable(new[] { new JointDeflectionSpec(JointTypeCatalog.KpMechanical, 100, 3) });
        Assert.Equal(3, table.EffectiveAllowableFor(JointTypeCatalog.KpMechanical, 100, JointApplicationMode.SingleJoint));
        Assert.Equal(6, table.EffectiveAllowableFor(JointTypeCatalog.KpMechanical, 100, JointApplicationMode.BothJoints));
    }

    [Fact]
    public void All_three_spec_tables_use_exact_dn_match()
    {
        var straight = new StraightPipeSpecTable(new[]
        {
            new StraightPipeSpec(PipeKindCatalog.Water1, 300, 322.8, 10),
            new StraightPipeSpec(PipeKindCatalog.Water1, 800, 842, 14)
        });
        var fittings = new BendFittingCatalog(new[]
        {
            new BendFittingEntry(300, 45, BendForm.AType, 100, 200),
            new BendFittingEntry(800, 45, BendForm.AType, 200, 400)
        });
        var joints = new JointDeflectionTable(new[]
        {
            new JointDeflectionSpec(JointTypeCatalog.KpMechanical, 300, 4),
            new JointDeflectionSpec(JointTypeCatalog.KpMechanical, 800, 3)
        });
        Assert.Null(straight.Find(PipeKindCatalog.Water1, 600));
        Assert.Null(fittings.Find(600, 45));
        Assert.Null(joints.AllowableFor(JointTypeCatalog.KpMechanical, 600));
    }

    [Fact]
    public void Pipe_kind_does_not_affect_fitting_or_joint_lookup()
    {
        var settings = Settings(5, fittings: new BendFittingEntry(100, 45, BendForm.AType, 130, 210));
        var water = BendResolver.Resolve(Bend(45, pipeKind: PipeKindCatalog.Water1), settings);
        var sewer = BendResolver.Resolve(Bend(45, pipeKind: PipeKindCatalog.Sewer3), settings);
        Assert.Equal(water.LayingLengthMm, sewer.LayingLengthMm);
        Assert.Equal(water.EffectiveAllowableDeg, sewer.EffectiveAllowableDeg);
    }

    [Fact]
    public void ResolveAll_skips_nodes_that_are_not_bends()
    {
        var nodes = new List<NodeClassification>
        {
            Bend(45),
            new(2, new Point3D(0, 0, 0), NodeKind.Tee, 3, 0, 100, 100, ""),
            new(3, new Point3D(0, 0, 0), NodeKind.EndPoint, 1, 0, 100, 100, "")
        };
        Assert.Single(BendResolver.ResolveAll(nodes, Settings(5)));
    }

    [Fact]
    public void Laying_length_shorter_than_tangent_length_is_inconsistent()
    {
        var result = BendResolver.Resolve(Bend(90), Settings(5, fittings: new BendFittingEntry(100, 90, BendForm.AType, 100, 210)));
        Assert.False(result.IsSizeConsistent);
    }

    [Fact]
    public void Arc_midpoint_lies_on_the_centerline_circle()
    {
        var dirA = new Vector3D(-1, 0, 0);
        var dirB = new Vector3D(Math.Cos(Math.PI / 4), Math.Sin(Math.PI / 4), 0);
        var origin = new Point3D(0, 0, 0);
        var arc = BendArcGeometry.Compute(origin, dirA, dirB, 45, 210, 130);
        Assert.Equal(0.130, arc.Start.DistanceTo(origin), 12);
        Assert.Equal(0.130, arc.End.DistanceTo(origin), 12);
        var bisector = new Vector3D(dirA.X + dirB.X, dirA.Y + dirB.Y, dirA.Z + dirB.Z).Normalize();
        var centerDistance = 0.210 / Math.Cos(22.5 * Math.PI / 180);
        var center = new Point3D(bisector.X * centerDistance, bisector.Y * centerDistance, bisector.Z * centerDistance);
        Assert.Equal(0.210, center.DistanceTo(arc.ArcMid), 12);
        Assert.Equal(17.30, arc.ExternalMm, 2);
    }

    [Fact]
    public void Arc_uses_measured_deflection_external_distance_when_fitting_angle_differs()
    {
        const double actualAngleDeg = 40d;
        const double fittingAngleDeg = 45d;
        const double radiusMm = 210d;
        var actualAngleRad = actualAngleDeg * Math.PI / 180d;
        var dirA = new Vector3D(-1, 0, 0);
        var dirB = new Vector3D(Math.Cos(actualAngleRad), Math.Sin(actualAngleRad), 0);
        var origin = new Point3D(0, 0, 0);

        var arc = BendArcGeometry.ComputeForAlignment(
            origin, dirA, dirB, fittingAngleDeg, actualAngleDeg, radiusMm, 130, 130);

        var tangentMm = BendResolver.TangentLength(radiusMm, actualAngleDeg);
        var expectedExternalMm = BendArcGeometry.ExternalDistance(radiusMm, actualAngleDeg);
        Assert.Equal(tangentMm / 1000d, arc.ArcStart.DistanceTo(origin), 12);
        Assert.Equal(tangentMm / 1000d, arc.ArcEnd.DistanceTo(origin), 12);
        Assert.Equal(expectedExternalMm, arc.ExternalMm, 10);
        Assert.Equal(expectedExternalMm / 1000d, arc.ArcMid.DistanceTo(origin), 12);
    }

    [Theory]
    [InlineData(11.25, 185d, 65d)]   // DN200 11¼° — |P4-P5| ≈ 246.8mm
    [InlineData(22.5, 155d, 80d)]    // DN150 22½°
    [InlineData(45d, 300d, 175d)]    // DN300 45°
    [InlineData(90d, 390d, 470d)]    // DN400 90°
    public void B_type_long_leg_exceeds_short_leg_by_exactly_the_spigot_length(
        double angleDeg, double radiusMm, double layingMm)
    {
        const double spigotMm = 200d;
        var angleRad = angleDeg * Math.PI / 180d;
        var dirA = new Vector3D(-1, 0, 0);
        var dirB = new Vector3D(Math.Cos(angleRad), Math.Sin(angleRad), 0);
        var origin = new Point3D(0, 0, 0);

        var arc = BendArcGeometry.ComputeForAlignment(
            origin, dirA, dirB, angleDeg, angleDeg, radiusMm, layingMm, layingMm + spigotMm);

        // P2/P4는 관 끝(t)이 아니라 호의 접점(T)이다 — 사용자 확정 2026-08-25.
        var tangentMm = BendResolver.TangentLength(radiusMm, angleDeg);
        Assert.Equal(tangentMm / 1000d, arc.ArcStart.DistanceTo(origin), 12);
        Assert.Equal(tangentMm / 1000d, arc.ArcEnd.DistanceTo(origin), 12);

        // s는 두 점 사이 거리가 아니라 짧은 다리 대비 긴 다리의 증분이다. T가 상쇄된다.
        var shortLegM = arc.Start.DistanceTo(arc.ArcStart);
        var longLegM = arc.End.DistanceTo(arc.ArcEnd);
        Assert.Equal((layingMm - tangentMm) / 1000d, shortLegM, 12);
        Assert.Equal(spigotMm / 1000d, longLegM - shortLegM, 12);
    }

    [Theory]
    [InlineData(BendForm.BType, 200d)]
    [InlineData(BendForm.AType, 0d)]
    public void Placement_carries_the_spigot_increment_from_catalog_to_arc_points(
        BendForm form, double expectedSpigotMm)
    {
        const double angleDeg = 45d;
        const double radiusMm = 300d;
        const double layingMm = 175d;
        var angleRad = angleDeg * Math.PI / 180d;
        var vertices = new[]
        {
            new Point3D(0, 0, 0),
            new Point3D(10, 0, 0),
            new Point3D(10 + 10 * Math.Cos(angleRad), 10 * Math.Sin(angleRad), 0)
        };
        var alignments = new[]
        {
            new PipeAlignment(vertices, PipeKindCatalog.Water1, 100, "test.shp", "1", new Dictionary<string, string>())
        };
        const double snapTolerance = 0.001;
        var graph = PipeNetworkBuilder.Build(alignments, snapTolerance);
        var nodes = PipeNetworkClassifier.Classify(graph);
        var resolutions = BendResolver.ResolveAll(
            nodes,
            Settings(10, form: form, fittings: new BendFittingEntry(100, angleDeg, form, layingMm, radiusMm)));

        var resolution = Assert.Single(resolutions);
        Assert.Equal(layingMm, resolution.LayingLengthMm, 8);
        Assert.Equal(layingMm + expectedSpigotMm, resolution.LongLegLengthMm, 8);

        var placement = Assert.Single(BendTrimPlanner.Plan(alignments, graph, resolutions, snapTolerance).Placements);
        Assert.Equal(layingMm, placement.UpstreamLegMm, 8);
        Assert.Equal(layingMm + expectedSpigotMm, placement.DownstreamLegMm, 8);

        // 좌표까지 반영됐는지 — 긴 다리(하류)가 짧은 다리보다 정확히 s만큼 길다.
        var shortLegM = placement.Points.Start.DistanceTo(placement.Points.ArcStart);
        var longLegM = placement.Points.End.DistanceTo(placement.Points.ArcEnd);
        Assert.Equal(expectedSpigotMm / 1000d, longLegM - shortLegM, 12);
    }

    [Fact]
    public void Trim_planner_uses_measured_deflection_for_arc_and_midpoint_orientation()
    {
        const double actualAngleDeg = 40d;
        const double fittingAngleDeg = 45d;
        const double radiusMm = 210d;
        var actualAngleRad = actualAngleDeg * Math.PI / 180d;
        var vertices = new[]
        {
            new Point3D(0, 0, 0),
            new Point3D(10, 0, 0),
            new Point3D(10 + 10 * Math.Cos(actualAngleRad), 10 * Math.Sin(actualAngleRad), 0)
        };
        var alignments = new[]
        {
            new PipeAlignment(vertices, PipeKindCatalog.Water1, 100, "test.shp", "1", new Dictionary<string, string>())
        };
        const double snapTolerance = 0.001;
        var graph = PipeNetworkBuilder.Build(alignments, snapTolerance);
        var nodes = PipeNetworkClassifier.Classify(graph);
        var resolutions = BendResolver.ResolveAll(
            nodes,
            Settings(10, fittings: new BendFittingEntry(100, fittingAngleDeg, BendForm.AType, 130, radiusMm)));

        var placement = Assert.Single(BendTrimPlanner.Plan(alignments, graph, resolutions, snapTolerance).Placements);

        Assert.Equal(actualAngleDeg, placement.DeflectionDeg, 8);
        Assert.Equal(BendArcGeometry.ExternalDistance(radiusMm, actualAngleDeg), placement.Points.ExternalMm, 8);
        Assert.Equal(actualAngleDeg / 2d, placement.RotXYDeg[2], 8);
    }

    [Fact]
    public void Trim_planner_places_one_bend_for_vertices_merged_into_the_same_node()
    {
        var (alignments, graph, resolutions, snapTolerance) = BuildMergedBendScenario();

        var plan = BendTrimPlanner.Plan(alignments, graph, resolutions, snapTolerance);

        var placement = Assert.Single(plan.Placements);
        Assert.Equal(1, placement.VertexIndex);
        Assert.NotEqual(VertexTrim.None, plan.Plans[0].Trims[1]);
        Assert.Equal(VertexTrim.None, plan.Plans[0].Trims[2]);
        Assert.Contains("겹친 정점 1곳", Assert.Single(plan.Warnings));
    }

    [Fact]
    public void Trim_planner_prefers_merged_vertex_with_a_bend_kind()
    {
        var bendKinds = new string?[] { null, null, "45°", null };
        var (alignments, graph, resolutions, snapTolerance) = BuildMergedBendScenario(bendKinds);

        var plan = BendTrimPlanner.Plan(alignments, graph, resolutions, snapTolerance);

        var placement = Assert.Single(plan.Placements);
        Assert.Equal(2, placement.VertexIndex);
        Assert.Equal(VertexTrim.None, plan.Plans[0].Trims[1]);
        Assert.NotEqual(VertexTrim.None, plan.Plans[0].Trims[2]);
    }

    [Fact]
    public void Bend_orientation_follows_each_horizontal_control_point()
    {
        var tangents = BendOrientation.Tangents(
            new Vector3D(-1, 0, 0),
            new Vector3D(0, 1, 0),
            90);
        var rotations = tangents.Select(BendOrientation.Compute).ToList();

        var expected = new[] { 0d, 0d, 45d, 90d, 90d };
        for (var i = 0; i < expected.Length; i++) Assert.Equal(expected[i], rotations[i].RotXYDeg, 8);
        Assert.All(rotations, x => Assert.Equal(0d, x.RotXZDeg, 8));
    }

    [Fact]
    public void Bend_orientation_preserves_vertical_slope_at_straight_legs()
    {
        var rise = Math.Sqrt(0.5);
        var tangents = BendOrientation.Tangents(
            new Vector3D(-rise, 0, -rise),
            new Vector3D(0, rise, rise),
            90);
        var rotations = tangents.Select(BendOrientation.Compute).ToList();

        Assert.Equal(-45d, rotations[0].RotXZDeg, 8);
        Assert.Equal(-45d, rotations[1].RotXZDeg, 8);
        Assert.Equal(-45d, rotations[3].RotXZDeg, 8);
        Assert.Equal(-45d, rotations[4].RotXZDeg, 8);
    }

    [Fact]
    public void Rot_xz_uses_family_sign_convention_for_uphill_and_downhill_vectors()
    {
        var rise = Math.Sqrt(0.5);

        Assert.Equal(-45d, BendOrientation.Compute(new Vector3D(rise, 0, rise)).RotXZDeg, 8);
        Assert.Equal(45d, BendOrientation.Compute(new Vector3D(rise, 0, -rise)).RotXZDeg, 8);
    }

    [Fact]
    public void Apply_project_north_keeps_input_when_true_north_is_zero()
    {
        Assert.Equal(-180d, BendOrientation.ApplyProjectNorth(-180d, 0d));
    }

    [Theory]
    [InlineData(0d, 30d, 30d)]
    [InlineData(-90d, 30d, -60d)]
    public void Apply_project_north_adds_clockwise_true_north(double rotXyDeg, double trueNorthDeg, double expected)
    {
        Assert.Equal(expected, BendOrientation.ApplyProjectNorth(rotXyDeg, trueNorthDeg), 8);
    }

    [Fact]
    public void Apply_project_north_normalizes_to_minus_180_exclusive_and_180_inclusive()
    {
        Assert.Equal(-160d, BendOrientation.ApplyProjectNorth(170d, 30d), 8);
    }

    [Fact]
    public void Apply_project_north_supports_counterclockwise_true_north()
    {
        Assert.Equal(-30d, BendOrientation.ApplyProjectNorth(0d, -30d), 8);
    }

    private static double Dot(Vector3D a, Vector3D b)
        => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static (PipeAlignment[] Alignments, PipeNetworkGraph Graph, IReadOnlyList<BendResolution> Resolutions, double SnapTolerance)
        BuildMergedBendScenario(IReadOnlyList<string?>? bendKinds = null)
    {
        const double angleDeg = 45d;
        const double snapTolerance = 0.01d;
        const double closeDistance = 0.005d;
        var angleRad = angleDeg * Math.PI / 180d;
        var closeAngleRad = angleRad / 2d;
        var directionX = Math.Cos(angleRad);
        var directionY = Math.Sin(angleRad);
        var vertices = new[]
        {
            new Point3D(0, 0, 0),
            new Point3D(10, 0, 0),
            new Point3D(10 + closeDistance * Math.Cos(closeAngleRad), closeDistance * Math.Sin(closeAngleRad), 0),
            new Point3D(10 + 10 * directionX, 10 * directionY, 0)
        };
        var alignments = new[]
        {
            new PipeAlignment(vertices, PipeKindCatalog.Water1, 100, "test.xlsx", "1",
                new Dictionary<string, string>(), bendKinds)
        };
        var graph = PipeNetworkBuilder.Build(alignments, snapTolerance);
        var nodes = PipeNetworkClassifier.Classify(graph);
        var resolutions = BendResolver.ResolveAll(
            nodes,
            Settings(10, fittings: new BendFittingEntry(100, angleDeg, BendForm.AType, 130, 210)));

        return (alignments, graph, resolutions, snapTolerance);
    }
}
