using System.Reflection;
using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.UseCases.Gis;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Gis;
using Xunit;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.UI.Tests.Gis;

public class ModelPipeAlignmentUseCaseTests
{
    [Fact]
    public void Infeasible_bend_is_removed_trim_is_released_and_warning_names_node()
    {
        var result = RemoveInfeasibleBends(CreatePlan(infeasibleFirst: true));

        var placement = Assert.Single(result.Plan.Placements);
        Assert.Equal(102, placement.NodeId);
        Assert.True(result.Plan.Plans.Single().Trims[1].IsEmpty);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("절점 101", warning);
    }

    [Fact]
    public void All_placeable_bends_keep_original_plan_without_warning()
    {
        var plan = CreatePlan(infeasibleFirst: false);
        var result = RemoveInfeasibleBends(plan);

        Assert.Same(plan, result.Plan);
        Assert.Empty(result.Warnings);
    }

    private static (BendTrimPlan Plan, IReadOnlyList<string> Warnings) RemoveInfeasibleBends(BendTrimPlan plan)
    {
        var method = typeof(ModelPipeAlignmentUseCase).GetMethod("RemoveInfeasibleBends", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = method.Invoke(null, new object[] { plan, Alignments(), Origin() });
        return ((BendTrimPlan Plan, IReadOnlyList<string> Warnings))result!;
    }

    private static BendTrimPlan CreatePlan(bool infeasibleFirst)
    {
        var firstLeg = infeasibleFirst ? 50d : 300d;
        return new BendTrimPlan(
            new[] { new AlignmentBendPlan(0, new[] { VertexTrim.None, new VertexTrim(0.05d, 0.05d), new VertexTrim(0.05d, 0.05d), VertexTrim.None }) },
            new[] { Placement(101, 1, firstLeg), Placement(102, 2, 300d) }, Array.Empty<string>());
    }

    private static BendPlacement Placement(int nodeId, int vertexIndex, double legMm) => new(
        nodeId, 0, vertexIndex, 45d, 300d, PipeKindCatalog.Water1,
        new BendArcPoints(new Point3D(0, 0, 0), new Point3D(0, 0, 0), new Point3D(0, 0, 0), new Point3D(0, 0, 0), new Point3D(0, 0, 0), 0d, 0d),
        legMm, legMm, 0d, true, Array.Empty<double>(), Array.Empty<double>(), DeflectionDeg: 90d, CenterlineRadiusMm: 200d);

    private static IReadOnlyList<PipeAlignment> Alignments() => new[]
    {
        new PipeAlignment(new[] { new Point3D(0, 0, 0), new Point3D(1, 0, 0), new Point3D(2, 0, 0), new Point3D(3, 0, 0) },
            PipeKindCatalog.Water1, 300d, "test.shp", "1", new Dictionary<string, string>(),
            VertexStationTexts: new[] { "0+000.000", "0+001.000", "0+002.000", "0+003.000" })
    };

    private static AlignmentPlacementOrigin Origin() => new(0d, 0d, ZDatum.Centerline,
        new StraightPipeSpecTable(new[] { new StraightPipeSpec(PipeKindCatalog.Water1, 300d, 300d, 0d) }));
}
