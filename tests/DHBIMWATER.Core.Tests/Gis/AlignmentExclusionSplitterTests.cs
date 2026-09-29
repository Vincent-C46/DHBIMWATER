using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Gis;
using Xunit;

namespace DHBIMWATER.Core.Tests.Gis;

/// <summary>docs/219 — 배치 제외 구간: 측점 역변환 · 선형 절단 · 절단 조각 분절 · 측점 표기.</summary>
public sealed class AlignmentExclusionSplitterTests
{
    [Fact]
    public void 측점이_없으면_평면_누적거리로_거리를_찾는다()
    {
        var profile = new AlignmentStationProfile(Line([0, 50, 100]));

        Assert.True(profile.TryGetDistanceAtStation(30, out var distance));
        Assert.Equal(30d, distance, 9);
        Assert.False(profile.TryGetDistanceAtStation(150, out _));
        Assert.Equal(0d, profile.MinStation);
        Assert.Equal(100d, profile.MaxStation);
    }

    [Fact]
    public void 엑셀_측점은_측점값으로_거리를_찾는다()
    {
        var profile = new AlignmentStationProfile(Line([0, 50, 100], stations: [1000d, 1050d, 1100d]));

        Assert.True(profile.TryGetDistanceAtStation(1025, out var distance));
        Assert.Equal(25d, distance, 9);
    }

    [Fact]
    public void 역방향_선형은_감소하는_측점으로_거리를_찾는다()
    {
        var profile = new AlignmentStationProfile(Line([0, 50, 100], stations: [200d, 150d, 100d]));

        Assert.True(profile.TryGetDistanceAtStation(175, out var distance));
        Assert.Equal(25d, distance, 9);
        Assert.Equal(100d, profile.MinStation);
        Assert.Equal(200d, profile.MaxStation);
    }

    [Fact]
    public void 구간_경계에서_잘리고_구간_안_정점은_사라진다()
    {
        var alignment = Line([0, 40, 100], bendKinds: [null, "22.5", null]);

        var result = AlignmentExclusionSplitter.Split(alignment, [Range(30, 45, "밸브실")]);

        Assert.Equal(2, result.Parts.Count);
        Assert.Equal([0d, 30d], result.Parts[0].Vertices.Select(v => v.X));
        Assert.Equal([45d, 100d], result.Parts[1].Vertices.Select(v => v.X));
        Assert.Equal(1, result.SkippedBendKindCount);
        Assert.Equal(15d, result.ExcludedLengthM, 9);
        // 조각 측점은 0부터 다시 시작하지 않고 원래 측점을 유지한다.
        Assert.Equal([45d, 100d], result.Parts[1].VertexStationM!.Select(x => x!.Value));
        Assert.Equal([null, null], result.Parts[0].VertexBendKinds!);
        // 레코드·원본 파일은 그대로다.
        Assert.All(result.Parts, part => Assert.Equal("7", part.RecordNumber));
    }

    [Fact]
    public void 구간_밖_정점과_DN_구간값을_이어받는다()
    {
        // DN은 "이 정점부터 시작하는 구간" 값: 0~20 300, 20~60 300, 60~100 400.
        var alignment = Line([0, 20, 60, 100], diameters: [300d, 300d, 400d, 400d]);

        var result = AlignmentExclusionSplitter.Split(alignment, [Range(50, 70)]);

        Assert.Equal([0d, 20d, 50d], result.Parts[0].Vertices.Select(v => v.X));
        Assert.Equal([300d, 300d, 300d], result.Parts[0].VertexDiameterMm!.Select(x => x!.Value));
        Assert.Equal([70d, 100d], result.Parts[1].Vertices.Select(v => v.X));
        Assert.Equal([400d, 400d], result.Parts[1].VertexDiameterMm!.Select(x => x!.Value));
    }

    [Fact]
    public void 선형_끝에_붙은_구간은_조각_하나만_남긴다()
    {
        var result = AlignmentExclusionSplitter.Split(Line([0, 100]), [Range(0, 10), Range(95, 100)]);

        var part = Assert.Single(result.Parts);
        Assert.Equal([10d, 95d], part.Vertices.Select(v => v.X));
    }

    [Fact]
    public void 측점_범위_밖_구간은_무시하고_원본을_돌려준다()
    {
        var alignment = Line([0, 100]);

        var result = AlignmentExclusionSplitter.Split(alignment, [Range(150, 160)]);

        Assert.Same(alignment, Assert.Single(result.Parts));
        Assert.Single(result.IgnoredRanges);
        Assert.Empty(result.AppliedRanges);
    }

    [Fact]
    public void 역방향_선형은_측점_구간을_진행방향으로_바꿔_자른다()
    {
        var alignment = Line([0, 100], stations: [100d, 0d]);

        var result = AlignmentExclusionSplitter.Split(alignment, [Range(40, 60)]);

        Assert.Equal([0d, 40d], result.Parts[0].Vertices.Select(v => v.X));
        Assert.Equal([100d, 60d], result.Parts[0].VertexStationM!.Select(x => x!.Value));
        Assert.Equal([60d, 100d], result.Parts[1].Vertices.Select(v => v.X));
        Assert.Equal([40d, 0d], result.Parts[1].VertexStationM!.Select(x => x!.Value));
    }

    [Fact]
    public void 시작_Sta에서_절단관으로_끝나고_끝_Sta부터_직관이_다시_시작한다()
    {
        var result = AlignmentExclusionSplitter.Split(Line([0, 100]), [Range(32, 45)]);

        var before = AlignmentIntervalSampler.SampleSegments(result.Parts[0], 6d);
        var after = AlignmentIntervalSampler.SampleSegments(result.Parts[1], 6d);

        Assert.Equal(32d, before[^1].End.X, 9);
        Assert.Equal(2d, before[^1].Start.DistanceTo(before[^1].End), 9);
        Assert.Equal(45d, after[0].Start.X, 9);
        Assert.Equal(51d, after[0].End.X, 9);
    }

    [Theory]
    [InlineData(352.5, "0+352.500")]
    [InlineData(1250, "1+250.000")]
    [InlineData(999.9996, "1+000.000")]
    [InlineData(0, "0+000.000")]
    public void 측점_표기(double station, string expected) => Assert.Equal(expected, StationTextFormatter.Format(station));

    [Theory]
    [InlineData("0+352.5", 352.5)]
    [InlineData("352.5", 352.5)]
    [InlineData("STA.0+352.5", 352.5)]
    [InlineData("sta 1+250", 1250d)]
    [InlineData(" 1,250.5 ", 1250.5)]
    public void 측점_입력_해석(string text, double expected) => Assert.Equal(expected, StationTextFormatter.Parse(text)!.Value, 9);

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0+")]
    public void 측점_입력_해석_실패(string text) => Assert.Null(StationTextFormatter.Parse(text));

    private static AlignmentExclusionRange Range(double start, double end, string? name = null) => new("7", start, end, name);

    private static PipeAlignment Line(double[] xs, double?[]? stations = null, string?[]? bendKinds = null, double?[]? diameters = null)
        => new(xs.Select(x => new Point3D(x, 0, 0)).ToList(), "상수1종관", 300, "a.xlsx", "7", new Dictionary<string, string>(),
            VertexBendKinds: bendKinds, VertexDiameterMm: diameters, VertexStationM: stations);
}
