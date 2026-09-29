using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Gis;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Gis;
using Xunit;

namespace DHBIMWATER.UI.Tests.Gis;

/// <summary>docs/219 § C — Loader가 제외 구간이 걸린 레코드만 잘라 넘긴다.</summary>
public sealed class AlignmentSourceLoaderExclusionTests
{
    [Fact]
    public void Load_SplitsOnlyTargetRecordAndWarns()
    {
        var reader = new StubReader(Feature("1"), Feature("2"));
        var file = new AlignmentSourceFile("sample.shp", PipeKindCatalog.Water1, null, null,
            ExclusionRanges: [new AlignmentExclusionRange("2", 30, 45, "밸브실 #1")]);

        var result = new AlignmentSourceLoader([reader]).Load([file]);

        Assert.Equal(3, result.Alignments.Count);
        Assert.Equal("1", result.Alignments[0].RecordNumber);
        Assert.Equal(new Point3D(100, 0, 0), result.Alignments[0].Vertices[^1]);
        Assert.Equal(new Point3D(30, 0, 0), result.Alignments[1].Vertices[^1]);
        Assert.Equal(new Point3D(45, 0, 0), result.Alignments[2].Vertices[0]);
        Assert.Contains(result.Warnings, x => x.Contains("배치 제외 구간 1곳 적용", StringComparison.Ordinal) && x.Contains("밸브실 #1", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, x => x.Contains("곡관유형 지정 정점 1곳", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_AppliesExclusionAfterReversal()
    {
        // 반전하면 진행 방향으로 측점이 100→0으로 줄어든다. 측점 35에서 자르고(x=35) 측점 20(x=20)부터 다시 시작한다.
        var reader = new StubReader(Feature("1"));
        var file = new AlignmentSourceFile("sample.shp", PipeKindCatalog.Water1, null, null, ReversedRecordNumbers: ["1"],
            ExclusionRanges: [new AlignmentExclusionRange("1", 20, 35)]);

        var result = new AlignmentSourceLoader([reader]).Load([file]);

        Assert.Equal(2, result.Alignments.Count);
        Assert.Equal(new Point3D(35, 0, 0), result.Alignments[0].Vertices[^1]);
        Assert.Equal(new Point3D(20, 0, 0), result.Alignments[1].Vertices[0]);
    }

    [Fact]
    public void Load_WarnsIgnoredRangeOutsideStations()
    {
        var reader = new StubReader(Feature("1"));
        var file = new AlignmentSourceFile("sample.shp", PipeKindCatalog.Water1, null, null,
            ExclusionRanges: [new AlignmentExclusionRange("1", 150, 160)]);

        var result = new AlignmentSourceLoader([reader]).Load([file]);

        Assert.Single(result.Alignments);
        Assert.Contains(result.Warnings, x => x.Contains("선형 측점 범위 밖", StringComparison.Ordinal));
    }

    private static PipeAlignment Feature(string recordNumber) => new(
        [new Point3D(0, 0, 0), new Point3D(40, 0, 0), new Point3D(100, 0, 0)],
        string.Empty, 0, "sample.shp", recordNumber, new Dictionary<string, string>(),
        VertexBendKinds: [null, "22.5", null], VertexStationM: [0d, 40d, 100d]);

    private sealed class StubReader(params PipeAlignment[] features) : IAlignmentSourceReader
    {
        public bool CanRead(string filePath) => true;
        public ShapefileReadResult Read(string filePath) => new(
            features, Array.Empty<ShapefileFieldInfo>(), ShapefileExtent.Empty, null, null, "UTF-8",
            features.Length, features.Sum(x => x.Vertices.Count), features.Length,
            Array.Empty<string>(), features[0].Attributes);
    }
}
