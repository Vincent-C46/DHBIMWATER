using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Gis;
using Xunit;

namespace DHBIMWATER.Core.Tests.Gis;

public sealed class AlignmentDnAndBendParsingTests
{
    [Theory]
    [InlineData("DN300", 300)]
    [InlineData("DN 500", 500)]
    [InlineData("dn300", 300)]
    [InlineData("D300", 300)]
    [InlineData("300", 300)]
    [InlineData("300mm", 300)]
    [InlineData("상수_D300", 300)]
    [InlineData("300 PVC", 300)]
    public void ParseDiameter_RecognizesSupportedFormats(string raw, double expected)
    {
        var result = AlignmentAttributeParser.ParseDiameter(raw);

        Assert.True(result.Success);
        Assert.Equal(expected, result.DiameterMm);
    }

    [Fact]
    public void ParseDiameter_DoesNotTreatSerialNumberAsDiameter()
        => Assert.False(AlignmentAttributeParser.ParseDiameter("밸브실_2").Success);

    /// <summary>docs/212 § D-5·D-6 — 핸드북 분수 표기(5⅝·11¼·22½·"5 5/8")를 읽고, 표준각 라벨은 핸드북 어휘로 돌려준다.</summary>
    [Theory]
    [InlineData("5⅝", 5.625)]
    [InlineData("5⅝°", 5.625)]
    [InlineData("5 5/8", 5.625)]
    [InlineData("5-5/8", 5.625)]
    [InlineData("11¼", 11.25)]
    [InlineData("22½도", 22.5)]
    public void BendKindParser_ReadsHandbookFractionNotation(string raw, double expected)
    {
        Assert.True(BendKindParser.TryParseAngle(raw, out var angle));
        Assert.Equal(expected, angle, 6);
    }

    [Theory]
    [InlineData(5.625, "5⅝")]
    [InlineData(11.25, "11¼")]
    [InlineData(22.5, "22½")]
    [InlineData(45, "45")]
    [InlineData(90, "90")]
    public void BendKindParser_LabelUsesHandbookVocabulary(double angle, string expected)
        => Assert.Equal(expected, BendKindParser.Label(angle));

    [Theory]
    [InlineData("22.5˚")]
    [InlineData("22.5°")]
    [InlineData("22.5도")]
    [InlineData("22.5")]
    public void BendKindParser_RecognizesDegreeSuffixes(string raw)
    {
        Assert.True(BendKindParser.TryParseAngle(raw, out var angle));
        Assert.Equal(22.5, angle);
    }

    [Theory]
    [InlineData("T")]
    [InlineData("")]
    [InlineData(null)]
    public void BendKindParser_RejectsNonAngles(string? raw)
        => Assert.False(BendKindParser.TryParseAngle(raw, out _));

    [Fact]
    public void DiameterProfile_ChangesAtVertexBoundary()
    {
        var alignment = Alignment([300, 500, 500]);
        var profile = new AlignmentDiameterProfile(alignment);

        Assert.Equal(300, profile.Find(0));
        Assert.Equal(300, profile.Find(19.999));
        Assert.Equal(500, profile.Find(20));
        Assert.Equal(500, profile.Find(20.001));
        Assert.Equal(500, profile.Find(40));
    }

    [Fact]
    public void NetworkBuilder_UsesStartVertexDnAndClassifiesReducer()
    {
        var alignment = Alignment([300, 500, 500]);
        var graph = PipeNetworkBuilder.Build([alignment]);
        var nodes = PipeNetworkClassifier.Classify(graph);

        Assert.Equal([300d, 500d], graph.Edges.Select(x => x.DiameterMm));
        Assert.Equal(NodeKind.Reducer, nodes.Single(x => x.NodeId == 1).Kind);
    }

    private static PipeAlignment Alignment(IReadOnlyList<double?> diameters) => new(
        [new Point3D(0, 0, 0), new Point3D(20, 0, 0), new Point3D(40, 0, 0)],
        PipeKindCatalog.Water1, 100, "test.xlsx", "1", new Dictionary<string, string>(),
        VertexDiameterMm: diameters);
}
