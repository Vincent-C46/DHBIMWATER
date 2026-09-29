using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

public class PipeFlowOrientationTests
{
    [Fact]
    public void Build_LShapedNetwork_OrientsBothEdgesForward()
    {
        var first = Edge((0, 0), (1000, 0));
        var second = Edge((1000, 0), (1000, 1000));

        var orientation = PipeFlowOrientation.Build(Network(first, second));

        Assert.True(orientation.IsForward(first.Id));
        Assert.True(orientation.IsForward(second.Id));
    }

    [Fact]
    public void Build_ReversedSecondEdge_OrientsItReversed()
    {
        var first = Edge((0, 0), (1000, 0));
        var reversedSecond = Edge((1000, 1000), (1000, 0));

        var orientation = PipeFlowOrientation.Build(Network(first, reversedSecond));

        Assert.True(orientation.IsForward(first.Id));
        Assert.False(orientation.IsForward(reversedSecond.Id));
    }

    [Fact]
    public void Build_DisconnectedComponents_StartsNextComponentAtEarliestRemainingEdge()
    {
        var first = Edge((0, 0), (1000, 0));
        var second = Edge((2000, 0), (3000, 0));
        var reversedThird = Edge((4000, 0), (3000, 0));

        var orientation = PipeFlowOrientation.Build(Network(first, second, reversedThird));

        Assert.True(orientation.IsForward(first.Id));
        Assert.True(orientation.IsForward(second.Id));
        Assert.False(orientation.IsForward(reversedThird.Id));
    }

    [Fact]
    public void Build_CyclicNetwork_OrientsEveryEdgeWithoutLooping()
    {
        var first = Edge((0, 0), (1000, 0));
        var second = Edge((1000, 0), (1000, 1000));
        var third = Edge((1000, 1000), (0, 0));

        var orientation = PipeFlowOrientation.Build(Network(first, second, third));

        Assert.True(orientation.IsForward(first.Id));
        Assert.True(orientation.IsForward(second.Id));
        Assert.False(orientation.IsForward(third.Id));
    }

    private static PipeEdgeDefinition Edge((double X, double Y) start, (double X, double Y) end)
        => new(Guid.NewGuid(), new Point2D(start.X, start.Y), new Point2D(end.X, end.Y), 0, PipeEdge.DefaultDiameterMm);

    private static PipeNetworkDefinition Network(params PipeEdgeDefinition[] edges)
        => new([], edges, [], 0, 100, PipeOutputMode.PipeAccessorySegment, new Point2D(0, 0));
}
