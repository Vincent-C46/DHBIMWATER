using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

public sealed class JunctionPreviewLayoutTests
{
    // 위치 단위 mm, 축척 1/50 → 도면 mm = 위치 / 50
    private static JunctionPreviewItem Item(long id, int? order, long setId, double x = 0, double y = 0,
        bool vertical = true, bool connected = true) => new(id, (int)id, "단관", "DN100",
        JunctionSymbolKind.Pipe, order, new(1, 0, 0), new(0, 1, 0), new(0, 0, 1), new(0, 1, 0), 0, connected,
        new(1, 0, 0), null, new ShapePoint(x, y), vertical, setId);

    private static readonly JunctionPreviewItem Tee = Item(1, null, 0, vertical: false);

    [Theory]
    [InlineData(JunctionExpandAxis.Y, 8, 0, 8)]
    [InlineData(JunctionExpandAxis.Y, -8, 0, -8)]
    [InlineData(JunctionExpandAxis.X, 8, 8, 0)]
    [InlineData(JunctionExpandAxis.X, -6.5, -6.5, 0)]
    public void Build_SignedStepSelectsDirection(JunctionExpandAxis axis, double step, double x, double y)
    {
        var scene = JunctionPreviewLayout.Build([Tee, Item(2, 1, 2)], [new(1, 2)], [new(2, axis, step)], 50);
        var placement = scene.Placements.Single(p => p.Item.ElementId == 2);
        Assert.Equal(x, placement.X, 6);
        Assert.Equal(y, placement.Y, 6);
    }

    [Fact]
    public void Build_UsesPlanPositionAndScale()
    {
        var scene = JunctionPreviewLayout.Build([Item(1, null, 0, 1000, -500, vertical: false)], [], [], 50);
        Assert.Equal((20d, -10d), (scene.Placements[0].X, scene.Placements[0].Y));
    }

    [Fact]
    public void Build_EachSetUsesOwnAxis_AndNoUpperLimit()
    {
        var scene = JunctionPreviewLayout.Build(
            [Tee, Item(2, 1, 2), Item(3, 2, 2), Item(4, 1, 4, 2000), Item(5, null, 0, 2000, vertical: false)],
            [new(1, 2), new(2, 3), new(5, 4)],
            [new(2, JunctionExpandAxis.Y, 8), new(4, JunctionExpandAxis.X, 100)], 50);
        var byId = scene.Placements.ToDictionary(x => x.Item.ElementId);
        Assert.Equal((0d, 16d), (byId[3].X, byId[3].Y));
        Assert.Equal((140d, 0d), (byId[4].X, byId[4].Y));
        var dimension = Assert.Single(scene.Dimensions);
        Assert.Equal(JunctionExpandAxis.Y, dimension.Axis);
        // 주관 1-5 없음(링크 없음), 세트 중심선 2개
        Assert.Equal(2, scene.CenterLines.Count);
    }

    [Fact]
    public void Build_ZeroStepOrCollapsed_KeepsPartsOnRoot()
    {
        var zero = JunctionPreviewLayout.Build([Tee, Item(2, 1, 2)], [new(1, 2)], [new(2, JunctionExpandAxis.Y, 0)], 50);
        Assert.Equal(0, zero.Placements[1].Y);
        Assert.Empty(zero.Warnings);
        var collapsed = JunctionPreviewLayout.Build([Tee, Item(2, 1, 2)], [new(1, 2)], [new(2, JunctionExpandAxis.Y, 8)], 50, false);
        Assert.Equal(0, collapsed.Placements[1].Y);
    }

    [Fact]
    public void Build_TagFollowsRevitRule()
    {
        var scene = JunctionPreviewLayout.Build([Tee, Item(2, 1, 2), Item(3, 1, 3)], [new(1, 2), new(1, 3)],
            [new(2, JunctionExpandAxis.Y, 8), new(3, JunctionExpandAxis.X, 8)], 50);
        var byId = scene.Placements.ToDictionary(x => x.Item.ElementId);
        Assert.Equal((0d, -4.5), (byId[1].TagX - byId[1].X, byId[1].TagY - byId[1].Y));
        Assert.Equal((-4.5, 0d), (byId[2].TagX - byId[2].X, byId[2].TagY - byId[2].Y));
        Assert.Equal((0d, -4.5), (byId[3].TagX - byId[3].X, byId[3].TagY - byId[3].Y));
    }

    [Fact]
    public void Build_WarnsWhenSetsOverlap_ButNotWithOwnRoot()
    {
        var scene = JunctionPreviewLayout.Build([Tee, Item(2, 1, 2), Item(3, 1, 3, 400)],
            [new(1, 2), new(1, 3)], [new(2, JunctionExpandAxis.X, 8), new(3, JunctionExpandAxis.Y, 0)], 50);
        var warning = Assert.Single(scene.Warnings);
        Assert.Contains("세트 1", warning);
        Assert.Contains("세트 2", warning);
    }

    [Fact]
    public void Build_WarnsWhenNumberCirclesOverlap()
    {
        // 수평 부재 번호 원(아래 4.5) 자리로 세트가 내려옴 → 세트 부재 번호 원(왼쪽 4.5)과 겹침
        var scene = JunctionPreviewLayout.Build([Item(1, null, 0, -225, vertical: false), Item(2, 1, 2)],
            [new(1, 2)], [new(2, JunctionExpandAxis.Y, -4.5)], 50);
        Assert.Contains(scene.Warnings, x => x.Contains("번호 원"));
    }

    [Fact]
    public void Build_OrphanStaysPut()
    {
        var scene = JunctionPreviewLayout.Build([Tee, Item(2, null, 0, connected: false)], [], [], 50);
        Assert.Equal((0d, 0d), (scene.Placements[1].X, scene.Placements[1].Y));
        Assert.Empty(scene.Warnings);
    }
}
