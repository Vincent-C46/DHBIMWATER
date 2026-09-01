using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Gis;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Gis;
using Xunit;

namespace DHBIMWATER.UI.Tests.Gis;

public sealed class AlignmentSourceLoaderMappingTests
{
    [Fact]
    public void Load_ConfirmedMappingOverridesParsedDiameterAndKind()
    {
        var reader = new StubReader(Feature(new Dictionary<string, string> { ["DIA"] = "상수_D300", ["KIND"] = PipeKindCatalog.Water1 }));
        var file = new AlignmentSourceFile("sample.shp", PipeKindCatalog.Water1, "DIA", "KIND", DiameterMappings:
            new Dictionary<string, ResolvedPipeSpecKey> { [DiameterMappingKey.Of("상수_D300", PipeKindCatalog.Water1)] = new(450, PipeKindCatalog.Sewer2) });

        var result = new AlignmentSourceLoader(new[] { reader }).Load(new[] { file });

        Assert.Equal(450, result.Alignments[0].DiameterMm);
        Assert.Equal(PipeKindCatalog.Sewer2, result.Alignments[0].PipeKind);
    }

    [Fact]
    public void Load_WithoutMappingPreservesManualDiameterFallback()
    {
        var reader = new StubReader(Feature(new Dictionary<string, string> { ["DIA"] = "직경미상" }));
        var file = new AlignmentSourceFile("sample.shp", PipeKindCatalog.Water2, "DIA", null, ManualDiameterMm: 200);

        var result = new AlignmentSourceLoader(new[] { reader }).Load(new[] { file });

        Assert.Equal(200, result.Alignments[0].DiameterMm);
        Assert.Equal(PipeKindCatalog.Water2, result.Alignments[0].PipeKind);
    }

    [Fact]
    public void Load_MappedKindSurvivesWhenMappedDiameterIsUnspecified()
    {
        var reader = new StubReader(Feature(new Dictionary<string, string> { ["DIA"] = "D300" }));
        var file = new AlignmentSourceFile("sample.shp", PipeKindCatalog.Water1, "DIA", null, DiameterMappings:
            new Dictionary<string, ResolvedPipeSpecKey> { [DiameterMappingKey.Of("D300", null)] = new(null, PipeKindCatalog.Sewer2) });

        var result = new AlignmentSourceLoader(new[] { reader }).Load(new[] { file });

        Assert.Equal(300, result.Alignments[0].DiameterMm);
        Assert.Equal(PipeKindCatalog.Sewer2, result.Alignments[0].PipeKind);
    }

    [Fact]
    public void Load_CompositeMappingKeySeparatesSameDiameterByKindRawValue()
    {
        var reader = new StubReader(Feature(new Dictionary<string, string> { ["DIA"] = "D300", ["KIND"] = PipeKindCatalog.Water2 }));
        var file = new AlignmentSourceFile("sample.shp", string.Empty, "DIA", "KIND", DiameterMappings:
            new Dictionary<string, ResolvedPipeSpecKey> { [DiameterMappingKey.Of("D300", PipeKindCatalog.Water2)] = new(350, PipeKindCatalog.Water3) });

        var result = new AlignmentSourceLoader(new[] { reader }).Load(new[] { file });

        Assert.Equal(350, result.Alignments[0].DiameterMm);
        Assert.Equal(PipeKindCatalog.Water3, result.Alignments[0].PipeKind);
    }

    [Fact]
    public void Load_EmptyRawValueUsesEmptyValueMappingKey()
    {
        var reader = new StubReader(Feature(new Dictionary<string, string>()));
        var file = new AlignmentSourceFile("sample.shp", string.Empty, null, null, DiameterMappings:
            new Dictionary<string, ResolvedPipeSpecKey> { [DiameterMappingKey.Of(null, null)] = new(300, PipeKindCatalog.Water3) });

        var result = new AlignmentSourceLoader(new[] { reader }).Load(new[] { file });

        Assert.Equal(300, result.Alignments[0].DiameterMm);
        Assert.Equal(PipeKindCatalog.Water3, result.Alignments[0].PipeKind);
    }

    [Fact]
    public void Load_WhenUnknownKindWarningDisabled_UsesWater1WithoutWarning()
    {
        var reader = new StubReader(Feature(new Dictionary<string, string> { ["KIND"] = "상수" }));
        var file = new AlignmentSourceFile("sample.shp", string.Empty, null, "KIND");

        var result = new AlignmentSourceLoader(new[] { reader }).Load(new[] { file }, warnOnUnknownPipeKinds: false);

        Assert.Equal(PipeKindCatalog.Water1, result.Alignments[0].PipeKind);
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("고정 관종 목록"));
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("상수 1종관으로 배치"));
    }

    [Fact]
    public void Load_WhenUnknownKindWarningEnabled_UsesWater1AndWarns()
    {
        var reader = new StubReader(Feature(new Dictionary<string, string> { ["KIND"] = "상수" }));
        var file = new AlignmentSourceFile("sample.shp", string.Empty, null, "KIND");

        var result = new AlignmentSourceLoader(new[] { reader }).Load(new[] { file });

        Assert.Contains(result.Warnings, warning => warning.Contains("미등록 관종 상수"));
        Assert.Equal(PipeKindCatalog.Water1, result.Alignments[0].PipeKind);
        Assert.Contains(result.Warnings, warning => warning.Contains("등급 해석 실패 1건 → 상수 1종관 적용"));
    }

    [Fact]
    public void Load_ReversalReversesDnAndStationAfterForwardFill()
    {
        var feature = new PipeAlignment(
            [new Point3D(0, 0, 0), new Point3D(1, 0, 0), new Point3D(2, 0, 0), new Point3D(3, 0, 0)],
            string.Empty, 300, "sample.xlsx", "1", new Dictionary<string, string>(),
            VertexBendKinds: [null, "22.5˚", null, null],
            VertexDiameterMm: [300, 300, 500, 500],
            VertexDiameterRawValues: ["DN300", "DN300", "DN500", "DN500"],
            VertexStationTexts: ["0+000", "0+010", "0+020", "0+030"]);
        var file = new AlignmentSourceFile("sample.xlsx", PipeKindCatalog.Water1, null, null,
            ReversedRecordNumbers: ["1"]);

        var result = new AlignmentSourceLoader([new StubReader(feature)]).Load([file]);
        var reversed = result.Alignments.Single();

        Assert.Equal([500d, 300d, 300d, 300d], reversed.VertexDiameterMm);
        Assert.Equal(["DN500", "DN300", "DN300", "DN300"], reversed.VertexDiameterRawValues);
        Assert.Equal(["0+030", "0+020", "0+010", "0+000"], reversed.VertexStationTexts);
        Assert.Equal([null, null, "22.5˚", null], reversed.VertexBendKinds);
        Assert.Equal([500d, 300d, 300d], PipeNetworkBuilder.Build([reversed]).Edges.Select(x => x.DiameterMm));
    }

    private static PipeAlignment Feature(IReadOnlyDictionary<string, string> attributes) => new(
        new[] { new Point3D(0, 0, 0), new Point3D(1, 0, 0) }, string.Empty, 0, "sample.shp", "1", attributes);

    private sealed class StubReader(PipeAlignment feature) : IAlignmentSourceReader
    {
        public bool CanRead(string filePath) => true;
        public ShapefileReadResult Read(string filePath) => new(
            new[] { feature }, Array.Empty<ShapefileFieldInfo>(), ShapefileExtent.Empty, null, null, "UTF-8", 1, 2, 1,
            Array.Empty<string>(), feature.Attributes);
    }
}
