using DHBIMWATER.Application.Gis;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Gis;
using Xunit;

namespace DHBIMWATER.UI.Tests.Gis;

public sealed class BendAuthorityPolicyTests
{
    [Fact]
    public void AuthoritativeBlank_SuppressesAutomaticBendWithoutWarning()
    {
        var result = Evaluate(Alignment([null, null, null]));

        Assert.Empty(result.Kept);
        Assert.Equal(1, result.SuppressedCount);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void AutomaticMode_PreservesAutomaticBend()
    {
        var result = Evaluate(Alignment(null));

        Assert.Single(result.Kept);
        Assert.Equal(0, result.SuppressedCount);
    }

    [Fact]
    public void StandardAngle_ForcesBend()
    {
        var result = Evaluate(Alignment([null, "90˚", null]));

        Assert.Single(result.Kept);
        Assert.Equal(90, result.Kept.Single().StandardAngleDeg);
    }

    [Fact]
    public void NonStandardAngle_FallsBackToAutomaticAndWarns()
    {
        var result = Evaluate(Alignment([null, "30˚", null]));

        Assert.Single(result.Kept);
        // 경고는 89bcd0d에서 정점별 장문 → 건수 집계형("비표준 지정각 자동판정 N건")으로 압축됐다(docs/135 § 2).
        Assert.Contains(result.Warnings, x => x.Contains("비표준") && x.Contains("자동판정"));
    }

    [Fact]
    public void SharedNonAuthoritativeAlignment_PreventsSuppression()
    {
        var authoritative = Alignment([null, null, null]);
        var automatic = Alignment(null);
        var graph = PipeNetworkBuilder.Build([authoritative]);
        var nodes = PipeNetworkClassifier.Classify(graph);

        var result = BendAuthorityPolicy.Evaluate([authoritative, automatic], graph, nodes, BendSettings.Default, PipeNetworkBuilder.DefaultSnapToleranceM);

        Assert.Single(result.Kept);
        Assert.Equal(0, result.SuppressedCount);
    }

    private static BendAuthorityResult Evaluate(PipeAlignment alignment)
    {
        var graph = PipeNetworkBuilder.Build([alignment]);
        var nodes = PipeNetworkClassifier.Classify(graph);
        return BendAuthorityPolicy.Evaluate([alignment], graph, nodes, BendSettings.Default, PipeNetworkBuilder.DefaultSnapToleranceM);
    }

    private static PipeAlignment Alignment(IReadOnlyList<string?>? bendKinds) => new(
        [new Point3D(0, 0, 0), new Point3D(10, 0, 0), new Point3D(10, 10, 0)],
        PipeKindCatalog.Water1, 100, "test.xlsx", "1", new Dictionary<string, string>(),
        VertexBendKinds: bendKinds,
        VertexStationTexts: ["0+000", "0+010", "0+020"]);
}
