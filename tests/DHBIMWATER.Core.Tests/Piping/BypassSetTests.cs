using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>바이패스 세트 — 본관 위 인라인 부속 1개로 담기는지(docs/228 § A).</summary>
public sealed class BypassSetTests
{
    private const string MainValve = "DH_버터플라이 밸브 : 1650A";
    private const string BypassValve = "DH_게이트 밸브 : 300A";

    [Fact]
    public void AddInlineFitting_WithBypass_IsSentToDefinition()
    {
        var network = MainPipe();
        var edge = network.Edges.Single();

        network.AddInlineFitting(edge.Id, BypassSet.TypeKey, MainValve, 0.5, bypass: new BypassSetSpec(BypassValve, 300, -1));

        var fitting = Assert.Single(network.Edges.Single().InlineFittings);
        Assert.True(fitting.IsBypassSet);
        var definition = Assert.Single(network.ToDefinition(1650, PipeOutputMode.PipeAccessorySegment, P(0, 0)).InlineFittings);
        Assert.True(definition.IsBypassSet);
        Assert.Equal(MainValve, definition.FamilyTypeName);
        Assert.Equal(new BypassSetSpec(BypassValve, 300, -1), definition.Bypass);
        Assert.Equal(2000, definition.Position.X, 3);
    }

    [Fact]
    public void PlainValve_IsNotBypassSet_AndCannotBeFlipped()
    {
        var network = MainPipe();
        var edge = network.Edges.Single();
        network.AddInlineFitting(edge.Id, "배관 밸브류", MainValve, 0.5);
        var fitting = network.Edges.Single().InlineFittings.Single();

        Assert.False(fitting.IsBypassSet);
        Assert.False(network.FlipBypassSide(edge.Id, fitting.Id));
    }

    [Fact]
    public void FlipBypassSide_NegatesSide()
    {
        var network = MainPipe();
        var edge = network.Edges.Single();
        network.AddInlineFitting(edge.Id, BypassSet.TypeKey, MainValve, 0.5, bypass: new BypassSetSpec(BypassValve, 300, 1));
        var fitting = network.Edges.Single().InlineFittings.Single();

        Assert.True(network.FlipBypassSide(edge.Id, fitting.Id));
        Assert.Equal(-1, network.Edges.Single().InlineFittings.Single().Bypass!.Side);
        Assert.True(network.FlipBypassSide(edge.Id, fitting.Id));
        Assert.Equal(1, network.Edges.Single().InlineFittings.Single().Bypass!.Side);
    }

    [Fact]
    public void SplitAndClone_KeepBypassSpec()
    {
        var network = MainPipe();
        var edge = network.Edges.Single();
        network.AddInlineFitting(edge.Id, BypassSet.TypeKey, MainValve, 0.5, bypass: new BypassSetSpec(BypassValve, 300, -1));

        network.SplitSegment(edge.Id, 0.25);
        var set = network.Edges.SelectMany(x => x.InlineFittings).Single();
        Assert.True(set.IsBypassSet);
        Assert.Equal(-1, set.Bypass!.Side);

        var clone = network.DeepClone().Edges.SelectMany(x => x.InlineFittings).Single();
        Assert.Equal(set, clone);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(-1, -1)]
    [InlineData(-5, -1)]
    public void NormalizedSide_OnlySign(int side, int expected)
        => Assert.Equal(expected, new BypassSetSpec(BypassValve, 300, side).NormalizedSide);

    /// <summary>본관 (0,0)→(4000,0) D1650.</summary>
    private static PipeNetwork MainPipe()
    {
        var network = new PipeNetwork();
        network.AddSegment(P(0, 0), P(4000, 0), 1650);
        return network;
    }

    private static Point2D P(double x, double y) => new(x, y);
}
