using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>타이밸브실 최소 폭(docs/323).</summary>
public sealed class TieValveRoomSizingTests
{
    // T 분기 레그 488, d 630, A 반폭 275, 바이패스 빠짐 600, 바이패스 DN100, 버터플라이 78
    private static TieValveRoomProbe Probe(double setOffset = 630, double butterfly = 78)
        => new(300, 488, setOffset, 275, 600, 100, butterfly);

    [Fact]
    public void Compute_SumsLinkAndWallClearance()
    {
        var layout = TieValveRoomSizing.Compute(Probe());

        Assert.Equal(2 * 488 + 2 * (630 + 275), layout.TeeToTeeMm, 6);   // 2786
        Assert.Equal(600 + 50 + 300, layout.WallClearanceMm, 6);        // 950
        Assert.Equal(4700, layout.MinWidthMm, 6);                       // 2786 + 1900 = 4686 → 100 올림
    }

    [Fact]
    public void Compute_RoundsUpTo100()
    {
        // d 600 → T–T 2726 + 1900 = 4626 → 4700 (50 단위였으면 4650)
        Assert.Equal(4700, TieValveRoomSizing.Compute(Probe(setOffset: 600)).MinWidthMm, 6);
    }

    [Fact]
    public void Compute_ButterfliesWiderThanSetGap_PushOffsetOut()
    {
        // d 300 < A 반폭 275 + 버터플라이 78 → d' 353
        var layout = TieValveRoomSizing.Compute(Probe(setOffset: 300));

        Assert.Equal(2 * 488 + 2 * (353 + 275), layout.TeeToTeeMm, 6);
    }

    [Fact]
    public void Compute_WidthIsMultipleOfRoundStep()
        => Assert.Equal(0, TieValveRoomSizing.Compute(Probe(setOffset: 641.3)).MinWidthMm % TieValveRoomSizing.RoundStepMm, 6);

    [Fact]
    public void TieWithWallClearance_PlacesMainsAtClearanceFromWalls()
    {
        var outline = Rect(4000, 5000);

        var template = ValveRoomPipingTemplateCatalog.TieWithWallClearance(outline, 950)!;

        Assert.Equal(950d / 5000, template.Ports.Single(x => x.Label == "IN2").OffsetRatio, 9);
        Assert.Equal(1 - 950d / 5000, template.Ports.Single(x => x.Label == "IN").OffsetRatio, 9);
        Assert.Equal(3, template.BypassSets!.Count);
    }

    [Theory]
    [InlineData(2500)]   // c ≥ 폭 절반
    [InlineData(0)]
    public void TieWithWallClearance_ImpossibleClearance_ReturnsNull(double clearance)
        => Assert.Null(ValveRoomPipingTemplateCatalog.TieWithWallClearance(Rect(4000, 5000), clearance));

    /// <summary>원점 직사각형 외곽(내측면 기준, 벽 두께 200).</summary>
    private static ValveRoomOutline Rect(double w, double h)
    {
        const double t = 200;
        var bl = new Point2D(0, 0); var br = new Point2D(w, 0); var tr = new Point2D(w, h); var tl = new Point2D(0, h);
        return new ValveRoomOutline(
        [
            new(1, bl, br, new Point2D(0, -t), new Point2D(w, -t), t),
            new(2, br, tr, new Point2D(w + t, 0), new Point2D(w + t, h), t),
            new(3, tr, tl, new Point2D(w, h + t), new Point2D(0, h + t), t),
            new(4, tl, bl, new Point2D(-t, h), new Point2D(-t, 0), t),
        ]);
    }
}