using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>포트 접점·기본 포트·프리셋 해석 규칙(docs/172 § A, § B).</summary>
public sealed class ValveRoomPipingTemplateResolverTests
{
    private const double Width = 4000, Height = 3000, Diameter = 100;

    // ── § A-2 AnchorOf / SideLengthOf ───────────────────────────────────────────────

    [Theory]
    [InlineData(OutlineSide.Left, false, 0, 500)]
    [InlineData(OutlineSide.Left, true, 0, 2500)]
    [InlineData(OutlineSide.Right, false, 4000, 500)]
    [InlineData(OutlineSide.Right, true, 4000, 2500)]
    [InlineData(OutlineSide.Bottom, false, 500, 0)]
    [InlineData(OutlineSide.Top, true, 3500, 3000)]
    public void AnchorOf_PlacesPortOnTheInnerFace(OutlineSide side, bool fromFarEdge, double x, double y)
    {
        var port = PipePort.Create(PortFlow.In, side, 500, "IN");

        var anchor = Outline().AnchorOf(port, fromFarEdge);

        Assert.Equal(x, anchor.X, 6);
        Assert.Equal(y, anchor.Y, 6);
    }

    [Fact]
    public void SideLengthOf_LeftRightUseHeight_TopBottomUseWidth()
    {
        var outline = Outline();

        Assert.Equal(Height, outline.SideLengthOf(OutlineSide.Left));
        Assert.Equal(Height, outline.SideLengthOf(OutlineSide.Right));
        Assert.Equal(Width, outline.SideLengthOf(OutlineSide.Top));
        Assert.Equal(Width, outline.SideLengthOf(OutlineSide.Bottom));
    }

    // ── § A-1 PipePortDefaults ─────────────────────────────────────────────────────

    [Fact]
    public void Standard_IsInLeftAndOutRight_WithLegacyLabelsAndOffset()
    {
        var ports = PipePortDefaults.Standard();

        Assert.Collection(ports,
            p => { Assert.Equal(PortFlow.In, p.Flow); Assert.Equal(OutlineSide.Left, p.Side); Assert.Equal("IN", p.Label); Assert.Equal(500, p.OffsetMm); },
            p => { Assert.Equal(PortFlow.Out, p.Flow); Assert.Equal(OutlineSide.Right, p.Side); Assert.Equal("OUT", p.Label); Assert.Equal(500, p.OffsetMm); });
    }

    [Theory]
    [InlineData(PortFlow.In, 1, "IN")]
    [InlineData(PortFlow.In, 2, "IN2")]
    [InlineData(PortFlow.Out, 1, "OUT")]
    [InlineData(PortFlow.Out, 3, "OUT3")]
    public void LabelFor_FirstHasNoSuffix_AndOrdinalOfInvertsIt(PortFlow flow, int ordinal, string expected)
    {
        Assert.Equal(expected, PipePortDefaults.LabelFor(flow, ordinal));
        Assert.Equal(ordinal, PipePortDefaults.OrdinalOf(flow, expected));
    }

    [Fact]
    public void IsDefault_OnlyForUnsuffixedInAndOut()
    {
        Assert.True(PipePortDefaults.IsDefault(PortFlow.In, "IN"));
        Assert.True(PipePortDefaults.IsDefault(PortFlow.Out, "OUT"));
        Assert.False(PipePortDefaults.IsDefault(PortFlow.In, "IN2"));
        Assert.False(PipePortDefaults.IsDefault(PortFlow.Out, "OUT3"));
    }

    // ── § B-4 Resolve ──────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_MudRoom_OneStraightRunBetweenInAndOut()
    {
        var template = ValveRoomPipingTemplateCatalog.Default("이토밸브실")!;

        var resolved = ValveRoomPipingTemplateResolver.Resolve(template, Outline(), fromFarEdge: false, Diameter);

        Assert.Equal(2, resolved.Ports.Count);
        var run = Assert.Single(resolved.Segments);
        Assert.Equal(new Point2D(0, 1500), run.Start);
        Assert.Equal(new Point2D(Width, 1500), run.End);
        Assert.Equal(Diameter, run.DiameterMm);
    }

    [Fact]
    public void Resolve_SluiceRoom_TwoInletsAndTwoRuns_SecondRunReachesRightFace()
    {
        // IN 2개 기본 배치는 제수밸브실이다(사용자 수정 2026-09-15).
        var template = ValveRoomPipingTemplateCatalog.Default("제수밸브실")!;

        var resolved = ValveRoomPipingTemplateResolver.Resolve(template, Outline(), fromFarEdge: false, Diameter);

        Assert.Equal(2, resolved.Ports.Count(x => x.Flow == PortFlow.In));
        Assert.Equal(1, resolved.Ports.Count(x => x.Flow == PortFlow.Out));
        Assert.Equal(2, resolved.Segments.Count);
        Assert.Equal(new Point2D(0, 2250), resolved.Segments[1].Start);
        Assert.Equal(new Point2D(Width, 2250), resolved.Segments[1].End);
    }

    [Fact]
    public void Resolve_FromFarEdgeToggle_KeepsSegmentGeometry_OnlyOffsetsChange()
    {
        // 프리셋 비율은 항상 하단 기준이다(docs/172 § B-1). 상단 기준으로 풀어도 선은 같은 자리, 포트 이격만 반대 모서리 값이 된다.
        var template = ValveRoomPipingTemplateCatalog.Default("제수밸브실")!;

        var fromBottom = ValveRoomPipingTemplateResolver.Resolve(template, Outline(), fromFarEdge: false, Diameter);
        var fromTop = ValveRoomPipingTemplateResolver.Resolve(template, Outline(), fromFarEdge: true, Diameter);

        Assert.Equal(fromBottom.Segments.Select(x => (x.Start, x.End)), fromTop.Segments.Select(x => (x.Start, x.End)));
        var in2Bottom = fromBottom.Ports.Single(x => x.Label == "IN2");
        var in2Top = fromTop.Ports.Single(x => x.Label == "IN2");
        Assert.Equal(2250, in2Bottom.OffsetMm, 6);
        Assert.Equal(750, in2Top.OffsetMm, 6);
    }

    [Fact]
    public void Resolve_DiameterRatio_ScalesDefaultDiameter()
    {
        var template = new ValveRoomPipingTemplate("테스트", "테스트",
            [new(PortFlow.In, OutlineSide.Left, 0.5, "IN")],
            [new(TemplatePoint.Port("IN"), TemplatePoint.Relative(0.5, 0.5), 0.5)],
            []);

        var resolved = ValveRoomPipingTemplateResolver.Resolve(template, Outline(), false, 300);

        Assert.Equal(150, Assert.Single(resolved.Segments).DiameterMm);
    }

    [Fact]
    public void Resolve_EmptyOutline_Throws()
    {
        var template = ValveRoomPipingTemplateCatalog.Default("제수밸브실")!;

        Assert.Throws<DHBIMWATER.Shared.Errors.DhbimwaterException>(() => ValveRoomPipingTemplateResolver.Resolve(template, ValveRoomOutline.Empty, false, Diameter));
    }

    [Fact]
    public void Resolve_TemplateWithValves_IsNotSupportedYet()
    {
        var template = new ValveRoomPipingTemplate("테스트", "테스트",
            [new(PortFlow.In, OutlineSide.Left, 0.5, "IN"), new(PortFlow.Out, OutlineSide.Right, 0.5, "OUT")],
            [new(TemplatePoint.Port("IN"), TemplatePoint.Port("OUT"))],
            [new(0, 0.5, ValveRole.Sluice)]);

        Assert.Equal("PL-111", Assert.Throws<DHBIMWATER.Shared.Errors.DhbimwaterException>(() => ValveRoomPipingTemplateResolver.Resolve(template, Outline(), false, Diameter)).Code);   // docs/240 § B
    }

    [Fact]
    public void Resolve_UnknownPortReference_Throws()
    {
        var template = new ValveRoomPipingTemplate("테스트", "테스트",
            [new(PortFlow.In, OutlineSide.Left, 0.5, "IN")],
            [new(TemplatePoint.Port("IN"), TemplatePoint.Port("없음"))],
            []);

        Assert.Throws<DHBIMWATER.Shared.Errors.DhbimwaterException>(() => ValveRoomPipingTemplateResolver.Resolve(template, Outline(), false, Diameter));
    }

    // ── § B-3 카탈로그 무결성 ────────────────────────────────────────────────────

    [Fact]
    public void Catalog_EveryTemplateIsConsistent()
    {
        foreach (var template in ValveRoomPipingTemplateCatalog.BuiltIn)
        {
            var labels = template.Ports.Select(x => x.Label).ToList();
            Assert.Equal(labels.Count, labels.Distinct(StringComparer.Ordinal).Count());
            Assert.All(template.Ports, p => Assert.InRange(p.OffsetRatio, 0, 1));
            foreach (var point in template.Segments.SelectMany(s => new[] { s.Start, s.End }))
            {
                if (point.Kind == TemplatePointKind.Port) Assert.Contains(point.PortLabel, labels);
                else { Assert.InRange(point.RatioX, 0, 1); Assert.InRange(point.RatioY, 0, 1); }
            }
            Assert.Empty(template.Valves);   // 1차는 직관만(docs/172 § 0-3 Q1)
            Assert.NotNull(ValveRoomPipingTemplateCatalog.Default(template.Zone));
        }
    }

    [Fact]
    public void Default_BlankOrUnknownZone_ReturnsNull()
    {
        Assert.Null(ValveRoomPipingTemplateCatalog.Default(""));
        Assert.Null(ValveRoomPipingTemplateCatalog.Default(null));
        Assert.Null(ValveRoomPipingTemplateCatalog.Default("감압밸브실"));
    }

    /// <summary>원점에서 시작하는 4000×3000 직사각형 외곽(내측면 기준).</summary>
    private static ValveRoomOutline Outline()
    {
        const double t = 200;
        var bl = new Point2D(0, 0); var br = new Point2D(Width, 0); var tr = new Point2D(Width, Height); var tl = new Point2D(0, Height);
        return new ValveRoomOutline(
        [
            new(1, bl, br, new Point2D(0, -t), new Point2D(Width, -t), t),
            new(2, br, tr, new Point2D(Width + t, 0), new Point2D(Width + t, Height), t),
            new(3, tr, tl, new Point2D(Width, Height + t), new Point2D(0, Height + t), t),
            new(4, tl, bl, new Point2D(-t, Height), new Point2D(-t, 0), t),
        ]);
    }
}
