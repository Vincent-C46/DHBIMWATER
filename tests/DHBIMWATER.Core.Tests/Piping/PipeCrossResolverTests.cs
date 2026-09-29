using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>직교 십자 판정(docs/175 § B-2)과 십자 규격·부재명 표기(§ B-3).</summary>
public sealed class PipeCrossResolverTests
{
    [Fact]
    public void Resolve_OrthogonalCross_ReturnsFourLegs()
    {
        var (node, edges) = Cross(P(1, 0), P(-1, 0), P(0, 1), P(0, -1));

        var order = PipeCrossResolver.Resolve(node, edges);

        Assert.NotNull(order);
        var ids = new[] { order!.Main1EdgeId, order.Main2EdgeId, order.Branch1EdgeId, order.Branch2EdgeId };
        Assert.Equal(4, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Contains(edges, e => e.Id == id));
    }

    [Fact]
    public void Resolve_WithinTolerance_Succeeds()
    {
        // 분기 하나가 89° — 허용 2° 안이다.
        var (node, edges) = Cross(P(1, 0), P(-1, 0), Dir(89), P(0, -1));
        Assert.NotNull(PipeCrossResolver.Resolve(node, edges));
    }

    [Fact]
    public void Resolve_NonOrthogonalBranch_ReturnsNull()
    {
        var (node, edges) = Cross(P(1, 0), P(-1, 0), Dir(80), P(0, -1));
        Assert.Null(PipeCrossResolver.Resolve(node, edges));
    }

    [Fact]
    public void Resolve_ThreeLegs_ReturnsNull()
    {
        var node = new PipeNodeDefinition(Guid.NewGuid(), P(0, 0), NodeKind.Tee);
        var edges = new[] { Edge(P(0, 0), P(1000, 0)), Edge(P(0, 0), P(-1000, 0)), Edge(P(0, 0), P(0, 1000)) };
        Assert.Null(PipeCrossResolver.Resolve(node, edges));
    }

    [Fact]
    public void SpecText_CrossKeepsLegacyNotationAndFourEnds()
    {
        // 규격 접두는 ValvePipingParameters.DiameterText의 `D`다. 주관에만 붙는 구 표기를 유지한다 —
        // T형은 docs/203 § C로 `D300xD300`으로 갈라져 나갔고, 십자는 현행 표기 그대로다.
        Assert.Equal("D300x300", PipeMaterialSpecText.Cross(300, 300));
        Assert.NotEqual(PipeMaterialSpecText.Tee(300, 300), PipeMaterialSpecText.Cross(300, 300));
        Assert.Equal(4, PipeMaterialSpecText.EndCountFor(PipeMaterialSpecText.CrossName));
        Assert.Equal(3, PipeMaterialSpecText.EndCountFor(PipeMaterialSpecText.TeeName));
        Assert.Equal("4F 십자", PipeMaterialSpecText.WithJointMark(PipeMaterialSpecText.CrossName, DHBIMWATER.Core.Gis.BendConnection.Flanged));
        Assert.Equal(4, PipeMaterialSpecText.EndCountFor("4F 십자"));
    }

    private static Point2D P(double x, double y) => new(x, y);

    private static Point2D Dir(double degrees)
        => new(Math.Cos(degrees * Math.PI / 180), Math.Sin(degrees * Math.PI / 180));

    private static PipeEdgeDefinition Edge(Point2D start, Point2D end)
        => new(Guid.NewGuid(), start, end, 0, 100);

    private static (PipeNodeDefinition Node, IReadOnlyList<PipeEdgeDefinition> Edges) Cross(params Point2D[] directions)
    {
        var node = new PipeNodeDefinition(Guid.NewGuid(), P(0, 0), NodeKind.Cross);
        var edges = directions.Select(d => Edge(P(0, 0), P(d.X * 1000, d.Y * 1000))).ToList();
        return (node, edges);
    }
}
