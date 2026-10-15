using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

public sealed class JunctionPreviewLayoutTests
{
    // 위치·간격 단위 실제 mm, 축척 1/50 → 도면 mm = 값 / 50
    private static JunctionPreviewItem Item(long id, int? order, long setId, double x = 0, double y = 0,
        bool vertical = true, bool connected = true) => new(id, (int)id, "단관", "DN100",
        JunctionSymbolKind.Pipe, order, new(1, 0, 0), new(0, 1, 0), new(0, 0, 1), new(0, 1, 0), 0, connected,
        new(1, 0, 0), null, new ShapePoint(x, y), vertical, setId);

    private static readonly JunctionPreviewItem Tee = Item(1, null, 0, vertical: false);

    [Theory]
    [InlineData(JunctionExpandAxis.Y, 400, 0, 8)]
    [InlineData(JunctionExpandAxis.Y, -400, 0, -8)]
    [InlineData(JunctionExpandAxis.X, 400, 8, 0)]
    [InlineData(JunctionExpandAxis.X, -325, -6.5, 0)]
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
            [new(2, JunctionExpandAxis.Y, 400), new(4, JunctionExpandAxis.X, 5000)], 50);
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
        var collapsed = JunctionPreviewLayout.Build([Tee, Item(2, 1, 2)], [new(1, 2)], [new(2, JunctionExpandAxis.Y, 400)], 50, false);
        Assert.Equal(0, collapsed.Placements[1].Y);
    }

    // 머리 = 대체 기호 반폭 2.2 + 간격 3 + 원 반지름 1.25 = 6.45, 지시선 끝 = 부재 위치 → 직선(docs/296)
    [Theory]
    [InlineData(JunctionTagSide.Right, 6.45, 0)]
    [InlineData(JunctionTagSide.Top, 0, 6.45)]
    [InlineData(JunctionTagSide.Left, -6.45, 0)]
    [InlineData(JunctionTagSide.Bottom, 0, -6.45)]
    public void Build_FixedTagSide_PlacesHeadOutsideSymbolOnStraightLeader(JunctionTagSide side, double dx, double dy)
    {
        var scene = JunctionPreviewLayout.Build([Item(1, null, 0, 1000, 500, vertical: false)], [], [], 50, true, side);
        var p = scene.Placements[0];
        Assert.Equal((20d, 10d), (p.LeaderX, p.LeaderY));
        Assert.Equal(20 + dx, p.TagX, 6);
        Assert.Equal(10 + dy, p.TagY, 6);
    }

    [Fact]
    public void Build_AutoTagSide_AvoidsNeighbourSymbol()
    {
        // 격점 중심이 왼쪽 → 1번 바깥쪽 = 우측. 우측엔 2번 기호 있음 → 다음 후보 상단
        var scene = JunctionPreviewLayout.Build(
            [Item(1, null, 0, 0, 0, vertical: false), Item(2, null, 0, 325, 0, vertical: false), Item(3, null, 0, -1500, 0, vertical: false)],
            [], [], 50);
        var first = scene.Placements.Single(x => x.Item.ElementId == 1);
        Assert.Equal((0d, 6.45), (first.TagX, Math.Round(first.TagY, 6)));
        Assert.DoesNotContain(scene.Warnings, x => x.Contains("번호 원"));
        Assert.All(scene.Placements, x => Assert.True(x.TagX == x.LeaderX || x.TagY == x.LeaderY));
    }

    [Fact]
    public void Build_WarnsWhenSetsOverlap_ButNotWithOwnRoot()
    {
        var scene = JunctionPreviewLayout.Build([Tee, Item(2, 1, 2), Item(3, 1, 3, 400)],
            [new(1, 2), new(1, 3)], [new(2, JunctionExpandAxis.X, 400), new(3, JunctionExpandAxis.Y, 0)], 50);
        var warning = Assert.Single(scene.Warnings);
        Assert.Contains("세트 1", warning);
        Assert.Contains("세트 2", warning);
    }

    [Fact]
    public void Build_WarnsWhenNumberCirclesOverlap()
    {
        // 일괄 방향은 피하지 않음 → 같은 자리 두 부재 번호 원 겹침 경고. 자동은 피해서 경고 없음(docs/296)
        JunctionPreviewItem[] items = [Item(1, null, 0, vertical: false), Item(2, null, 0, vertical: false)];
        var fixedSide = JunctionPreviewLayout.Build(items, [], [], 50, true, JunctionTagSide.Right);
        Assert.Contains(fixedSide.Warnings, x => x.Contains("번호 원"));
        var auto = JunctionPreviewLayout.Build(items, [], [], 50);
        Assert.DoesNotContain(auto.Warnings, x => x.Contains("번호 원"));
    }

    [Fact]
    public void Build_OrphanStaysPut()
    {
        var scene = JunctionPreviewLayout.Build([Tee, Item(2, null, 0, connected: false)], [], [], 50);
        Assert.Equal((0d, 0d), (scene.Placements[1].X, scene.Placements[1].Y));
        Assert.Empty(scene.Warnings);
    }
}
