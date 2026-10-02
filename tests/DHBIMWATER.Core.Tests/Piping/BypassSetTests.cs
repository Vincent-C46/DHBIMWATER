using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>바이패스 세트 — 본관 위 인라인 부속 1개로 담기는지(docs/228 § A).</summary>
public sealed class BypassSetTests
{
    private const string MainValve = "DH_버터플라이 밸브 : 1650A";
    private const string BypassValve = "DH_게이트 밸브 : 300A";
    private const string CheckValve = "DH_역지 밸브 : 300A";

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

    [Fact]
    public void TwoValveSpec_IsKept_AndSwapExchangesValves()
    {
        // docs/255 — V2·단관은 정의까지 그대로 가고, [순서 바꾸기]는 V1↔V2만 바꾼다(방향·단관 유지).
        var network = MainPipe();
        var edge = network.Edges.Single();
        network.AddInlineFitting(edge.Id, BypassSet.TypeKey, "", 0.5, bypass: new BypassSetSpec(BypassValve, 0, -1, CheckValve, 300));
        var fitting = network.Edges.Single().InlineFittings.Single();
        Assert.True(fitting.Bypass!.HasTwoValves);
        var definition = network.ToDefinition(1650, PipeOutputMode.PipeAccessorySegment, P(0, 0)).InlineFittings.Single();
        Assert.Equal(new BypassSetSpec(BypassValve, 0, -1, CheckValve, 300), definition.Bypass);

        Assert.True(network.SwapBypassValves(edge.Id, fitting.Id));

        Assert.Equal(new BypassSetSpec(CheckValve, 0, -1, BypassValve, 300), network.Edges.Single().InlineFittings.Single().Bypass);
    }

    [Fact]
    public void SwapBypassValves_OneValveSet_ReturnsFalse()
    {
        var network = MainPipe();
        var edge = network.Edges.Single();
        network.AddInlineFitting(edge.Id, BypassSet.TypeKey, "", 0.5, bypass: new BypassSetSpec(BypassValve, 0, 1));
        var fitting = network.Edges.Single().InlineFittings.Single();

        Assert.False(network.SwapBypassValves(edge.Id, fitting.Id));
        Assert.Equal(new BypassSetSpec(BypassValve, 0, 1), network.Edges.Single().InlineFittings.Single().Bypass);
    }

    [Fact]
    public void UpdateBypassSet_SwitchesBetweenOneAndTwoValves_KeepingSide()
    {
        var network = MainPipe();
        var edge = network.Edges.Single();
        network.AddInlineFitting(edge.Id, BypassSet.TypeKey, "", 0.5, bypass: new BypassSetSpec(BypassValve, 0, -1));
        var id = network.Edges.Single().InlineFittings.Single().Id;

        Assert.True(network.UpdateBypassSet(edge.Id, id, BypassValve, 0, CheckValve, 250));
        Assert.Equal(new BypassSetSpec(BypassValve, 0, -1, CheckValve, 250), network.Edges.Single().InlineFittings.Single().Bypass);

        // V2를 비우면 1개 세트 — 단관도 0으로 돌아간다.
        Assert.True(network.UpdateBypassSet(edge.Id, id, BypassValve, 0, null, 250));
        Assert.Equal(new BypassSetSpec(BypassValve, 0, -1), network.Edges.Single().InlineFittings.Single().Bypass);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, false)]
    [InlineData(10, true)]
    [InlineData(300, true)]
    [InlineData(-1, false)]
    public void IsValidSpoolLength_ZeroOrAtLeastMinSegment(double spool, bool expected)
        => Assert.Equal(expected, BypassSet.IsValidSpoolLength(spool));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(-1, -1)]
    [InlineData(-5, -1)]
    public void NormalizedSide_OnlySign(int side, int expected)
        => Assert.Equal(expected, new BypassSetSpec(BypassValve, 300, side).NormalizedSide);

    /// <summary>캔버스 개략 그림 전용 바이패스 관경 추정(docs/244 § E) — 본관 20%를 50 단위, 최소 50.</summary>
    [Theory]
    [InlineData(100, 50)]
    [InlineData(500, 100)]
    [InlineData(1000, 200)]
    public void PreviewBypassDiameter_Boundaries(double mainMm, double expected)
        => Assert.Equal(expected, BypassSet.PreviewBypassDiameterMm(mainMm));

    /// <summary>본관 (0,0)→(4000,0) D1650.</summary>
    private static PipeNetwork MainPipe()
    {
        var network = new PipeNetwork();
        network.AddSegment(P(0, 0), P(4000, 0), 1650);
        return network;
    }

    private static Point2D P(double x, double y) => new(x, y);
}
