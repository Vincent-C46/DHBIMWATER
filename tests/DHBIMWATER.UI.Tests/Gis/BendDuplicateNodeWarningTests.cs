using System.Reflection;
using DHBIMWATER.Application.Gis;
using DHBIMWATER.Application.UseCases.Gis;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Gis;
using Xunit;

namespace DHBIMWATER.UI.Tests.Gis;

/// <summary>
/// docs/149. 같은 측점의 곡관 지정 2개가 스냅 허용오차 안에서 한 절점으로 합쳐질 때의 경고를 검증한다.
/// 배치 개수는 종전과 같아야 한다(경고만 늘린다).
/// </summary>
public sealed class BendDuplicateNodeWarningTests
{
    [Fact]
    public void MergedNode_WithTwoStandardAngles_WarnsAndKeepsLastAngle()
    {
        var alignment = MergedAlignment("11.25˚", "45˚");
        var result = Evaluate(alignment);

        var warning = Assert.Single(result.Warnings, x => x.Contains("한 절점으로 합쳐졌습니다"));
        Assert.Contains("11.25˚", warning);
        Assert.Contains("45˚", warning);
        Assert.Contains("측점 0+371.80", warning);

        // 채택 규칙(마지막 지정각이 이긴다)은 바뀌지 않는다.
        Assert.Equal(45d, Assert.Single(result.Kept).StandardAngleDeg);
    }

    [Fact]
    public void MergedNode_WithSameAngleTwice_DoesNotWarn()
    {
        var result = Evaluate(MergedAlignment("45˚", "45˚"));

        Assert.DoesNotContain(result.Warnings, x => x.Contains("한 절점으로 합쳐졌습니다"));
    }

    [Fact]
    public void MergedNode_PlacesOnlyOneBend_AndDuplicateWarningNamesBendKind()
    {
        var alignment = MergedAlignment("11.25˚", "45˚");
        var graph = PipeNetworkBuilder.Build([alignment]);
        var nodes = PipeNetworkClassifier.Classify(graph);
        var authority = BendAuthorityPolicy.Evaluate([alignment], graph, nodes, BendSettings.Default, PipeNetworkBuilder.DefaultSnapToleranceM);

        var plan = BendTrimPlanner.Plan([alignment], graph, authority.Kept, PipeNetworkBuilder.DefaultSnapToleranceM);

        // 0.5mm 간격으로 곡관 2개를 넣을 수 없으므로 1개만 배치한다(동작 불변).
        Assert.Single(plan.Placements);
        var warning = Assert.Single(plan.Warnings, x => x.Contains("곡관을 1개만 배치했습니다"));
        Assert.Contains("곡관유형", warning);
    }

    [Fact]
    public void CountDeclaredBends_CountsOnlyInteriorVerticesWithBendKind()
    {
        // 단부 정점(0, n-1)의 지정은 곡관 대상이 아니므로 세지 않는다.
        var alignment = MergedAlignment("11.25˚", "45˚", firstKind: "90˚", lastKind: "90˚");

        Assert.Equal(2, CountDeclaredBends([alignment]));
    }

    [Fact]
    public void CountDeclaredBends_IgnoresAlignmentsWithoutBendKindColumn()
    {
        var alignment = new PipeAlignment(
            [new Point3D(0, 0, 0), new Point3D(10, 0, 0), new Point3D(10, 10, 0)],
            PipeKindCatalog.Water1, 100, "test.shp", "1", new Dictionary<string, string>());

        Assert.Equal(0, CountDeclaredBends([alignment]));
    }

    private static int CountDeclaredBends(IReadOnlyList<PipeAlignment> alignments)
    {
        var method = typeof(ModelPipeAlignmentUseCase).GetMethod("CountDeclaredBends", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (int)method.Invoke(null, [alignments])!;
    }

    private static BendAuthorityResult Evaluate(PipeAlignment alignment)
    {
        var graph = PipeNetworkBuilder.Build([alignment]);
        var nodes = PipeNetworkClassifier.Classify(graph);
        return BendAuthorityPolicy.Evaluate([alignment], graph, nodes, BendSettings.Default, PipeNetworkBuilder.DefaultSnapToleranceM);
    }

    /// <summary>정점 1·2가 0.5mm 간격이라 기본 허용오차(10mm) 안에서 한 절점으로 병합되는 선형.</summary>
    private static PipeAlignment MergedAlignment(string firstBend, string secondBend, string? firstKind = null, string? lastKind = null) => new(
        [new Point3D(0, 0, 0), new Point3D(10, 0, 0), new Point3D(10.0005, 0, 0), new Point3D(10.0005, 10, 0)],
        PipeKindCatalog.Water1, 100, "test.xlsx", "1", new Dictionary<string, string>(),
        VertexBendKinds: [firstKind, firstBend, secondBend, lastKind],
        VertexStationTexts: ["0+360.00", "0+371.80", "0+371.80", "0+381.80"]);
}
