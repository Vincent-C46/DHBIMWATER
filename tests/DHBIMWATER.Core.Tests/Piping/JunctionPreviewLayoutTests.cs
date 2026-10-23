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

    // 머리 = 대체 기호 반폭 2.2 + 간격 8 + 원 반지름 1.25 = 11.45, 지시선 끝 = 부재 위치 → 직선(docs/296)
    [Theory]
    [InlineData(JunctionTagSide.Right, 11.45, 0)]
    [InlineData(JunctionTagSide.Top, 0, 11.45)]
    [InlineData(JunctionTagSide.Left, -11.45, 0)]
    [InlineData(JunctionTagSide.Bottom, 0, -11.45)]
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
        Assert.Equal((0d, 11.45), (first.TagX, Math.Round(first.TagY, 6)));
        Assert.DoesNotContain(scene.Warnings, x => x.Contains("번호 원"));
        Assert.All(scene.Placements, x => Assert.True(x.TagX == x.LeaderX || x.TagY == x.LeaderY));
    }

    // 같은 세트·같은 열 좌측 태그 = 가장 넓은 기호 기준 한 줄. 다른 열·다른 세트·주관은 따로(docs/298)
    [Fact]
    public void Build_AlignsTagHeadsInSameSetColumn()
    {
        static JunctionPreviewItem Wide(long id, long setId, double x, double y, double half) =>
            Item(id, setId == 0 ? null : 1, setId, x, y, vertical: setId != 0) with
            {
                Shape = new JunctionSymbolShape([[new ShapePoint(-half, -50), new ShapePoint(half, 50)]],
                    -half, -50, half, 50)
            };
        var scene = JunctionPreviewLayout.Build(
            [Wide(1, 2, 0, 0, 100), Wide(2, 2, 0, 1000, 300), Wide(3, 2, 20, 2000, 200), Wide(4, 2, 2000, 0, 100),
             Wide(5, 0, 0, -1000, 1500), Wide(6, 3, 0, 3000, 100)],
            [], [], 50, true, JunctionTagSide.Left);
        var byId = scene.Placements.ToDictionary(x => x.Item.ElementId);
        // 세트 2 열 1~3(X 0·0·0.4mm): 가장 넓은 2번 MinX −6 − 9.25
        foreach (var id in new long[] { 1, 2, 3 }) Assert.Equal(-15.25, byId[id].TagX, 6);
        Assert.Equal((0d, 20d, 40d), (byId[1].TagY, byId[2].TagY, byId[3].TagY));
        // 다른 열 4번: 자기 기호 기준 40 − 2 − 9.25
        Assert.Equal(28.75, byId[4].TagX, 6);
        // 주관 넓은 단관 5번은 정렬 안 함 → 세트 태그도 안 끌려감. 다른 세트 6번은 자기 기호 기준
        Assert.Equal(-39.25, byId[5].TagX, 6);
        Assert.Equal(-11.25, byId[6].TagX, 6);
    }

    // 주관 같은 행 위/아래 태그 = 가장 높은 기호 기준 한 줄. 좌/우 열은 정렬 안 함(docs/308)
    [Fact]
    public void Build_AlignsMainPipeTagRowTopBottom()
    {
        static JunctionPreviewItem Box(long id, double x, double halfWidth, double halfHeight) =>
            Item(id, null, 0, x, 0, vertical: false) with
            {
                Shape = new JunctionSymbolShape([[new ShapePoint(-halfWidth, -halfHeight), new ShapePoint(halfWidth, halfHeight)]],
                    -halfWidth, -halfHeight, halfWidth, halfHeight)
            };
        JunctionPreviewItem[] items = [Box(1, 0, 50, 50), Box(2, 1000, 100, 250), Box(3, 2000, 50, 50)];

        // 위: 가장 높은 2번 MaxY 5 + 9.25
        var top = JunctionPreviewLayout.Build(items, [], [], 50, true, JunctionTagSide.Top);
        Assert.All(top.Placements, p => Assert.Equal(14.25, p.TagY, 6));
        Assert.All(top.Placements, p => Assert.Equal(p.LeaderX, p.TagX));

        // 아래: 가장 낮은 2번 MinY −5 − 9.25
        var bottom = JunctionPreviewLayout.Build(items, [], [], 50, true, JunctionTagSide.Bottom);
        Assert.All(bottom.Placements, p => Assert.Equal(-14.25, p.TagY, 6));

        // 좌: 주관은 자기 기호 기준
        var left = JunctionPreviewLayout.Build(items, [], [], 50, true, JunctionTagSide.Left).Placements
            .ToDictionary(x => x.Item.ElementId);
        Assert.Equal(-10.25, left[1].TagX, 6);
        Assert.Equal(8.75, left[2].TagX, 6);
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

    // 세트 −Y 펼침 → 위쪽 삼각 모양 180° 회전(docs/306). 주관 부재 모양은 그대로
    [Fact]
    public void Build_RotatesSetShapeToExpandDirection_ButNotMainPipe()
    {
        var triangle = JunctionSymbolShape.FromPolylines([[new ShapePoint(-100, 0), new ShapePoint(0, 300), new ShapePoint(100, 0)]]);
        var scene = JunctionPreviewLayout.Build([Tee with { Shape = triangle }, Item(2, 1, 2) with { Shape = triangle }],
            [new(1, 2)], [new(2, JunctionExpandAxis.Y, -400)], 50);
        var member = scene.Placements.Single(x => x.Item.ElementId == 2);
        Assert.Equal(-300, member.Item.Shape!.MinY, 6);
        Assert.Equal(0, member.Item.Shape.MaxY, 6);
        Assert.Equal(-8 - 6, member.MinY, 6);
        Assert.Same(triangle, scene.Placements.Single(x => x.Item.ElementId == 1).Item.Shape);
    }

    [Fact]
    public void Build_OrphanStaysPut()
    {
        var scene = JunctionPreviewLayout.Build([Tee, Item(2, null, 0, connected: false)], [], [], 50);
        Assert.Equal((0d, 0d), (scene.Placements[1].X, scene.Placements[1].Y));
        Assert.Empty(scene.Warnings);
    }
}
