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
    public void Resolve_MudRoom_RunEndsAtFarFaceOfPickedMiddleWall()
    {
        // 중간벽(두께 300, 좌측면 x 2500)을 피킹한 외곽 — 관은 중간벽을 지나 방류실 쪽 면(x 2800)에서 끝남. 비율 위치와 무관.
        var template = ValveRoomPipingTemplateCatalog.Default("이토밸브실")!;
        var outline = new ValveRoomOutline([.. Outline().Walls,
            new(5, new Point2D(2500, 0), new Point2D(2500, Height), new Point2D(2800, 0), new Point2D(2800, Height), 300)]);

        var resolved = ValveRoomPipingTemplateResolver.Resolve(template, outline, fromFarEdge: false, Diameter);

        var port = Assert.Single(resolved.Ports);
        Assert.Equal(PortFlow.In, port.Flow);
        var run = Assert.Single(resolved.Segments);
        Assert.Equal(0, run.Start.X, 6);
        Assert.Equal(1350, run.Start.Y, 6);
        Assert.Equal(2800, run.End.X, 6);
        Assert.Equal(1350, run.End.Y, 6);
        var valve = Assert.Single(resolved.Fittings!);
        Assert.False(valve.IsBypassSet);
        Assert.Equal(0.35, valve.T, 6);
    }

    [Fact]
    public void Resolve_MudRoom_NoMiddleWall_EndsAtCenter()
    {
        var template = ValveRoomPipingTemplateCatalog.Default("이토밸브실")!;

        var run = Assert.Single(ValveRoomPipingTemplateResolver.Resolve(template, Outline(), fromFarEdge: false, Diameter).Segments);

        Assert.Equal(Width / 2, run.End.X, 6);
    }

    [Fact]
    public void ForMainPipeVoid_YAxis_RunsBottomToTopThroughVoidX_WithVoidDiameter_NoPorts()
    {
        var template = ValveRoomPipingTemplateCatalog.ForMainPipeVoid("공기밸브실", Outline(), new MainPipeVoid(new Point2D(1000, 500), false, 1500, 0), Diameter)!;

        var resolved = ValveRoomPipingTemplateResolver.Resolve(template, Outline(), fromFarEdge: false, Diameter);

        var run = Assert.Single(resolved.Segments);
        Assert.Equal(new Point2D(1000, 0), run.Start);
        Assert.Equal(new Point2D(1000, Height), run.End);
        Assert.Equal(1500, run.DiameterMm, 6);
        Assert.Empty(resolved.Ports);   // Top/Bottom 화살표 미지원(docs/172 § 0-5)
    }

    [Fact]
    public void ForMainPipeVoid_FoundationFaces_YAxisRunReachesFoundation()
    {
        var outline = new ValveRoomOutline(Outline().Walls, null, null, [new OutlineFoundation(1, -300, -400, Width + 300, Height + 400)], useFoundationFaces: true);
        var template = ValveRoomPipingTemplateCatalog.ForMainPipeVoid("공기밸브실", outline, new MainPipeVoid(new Point2D(1000, 500), false, 300, 0), Diameter)!;

        var run = Assert.Single(ValveRoomPipingTemplateResolver.Resolve(template, outline, false, Diameter).Segments);

        Assert.Equal(-400, run.Start.Y, 6);
        Assert.Equal(Height + 400, run.End.Y, 6);
    }

    [Fact]
    public void PortFaceBounds_VoidRun_OverridesFoundationBoxAlongAxis()
    {
        // 솔리드 교점(-250~4250)이 기초 경계상자(-300~4300)보다 우선. 축 수직 방향(Y)은 경계상자 그대로.
        var pipeVoid = new MainPipeVoid(new Point2D(2000, 1500), true, 300, 0, new Point2D(-250, 1500), new Point2D(Width + 250, 1500));
        var outline = new ValveRoomOutline(Outline().Walls, null, pipeVoid, [new OutlineFoundation(1, -300, -400, Width + 300, Height + 400)], useFoundationFaces: true);

        Assert.Equal((-250d, -400d, Width + 250, Height + 400), outline.PortFaceBounds);
    }

    [Fact]
    public void UseFoundationFaces_VoidRunWithoutPickedFoundation_StillOn()
    {
        var pipeVoid = new MainPipeVoid(new Point2D(2000, 1500), true, 300, 0, new Point2D(-250, 1500), new Point2D(Width + 250, 1500));
        var outline = new ValveRoomOutline(Outline().Walls, null, pipeVoid, useFoundationFaces: true);

        Assert.True(outline.UseFoundationFaces);
        Assert.Equal(-150, outline.Translate(new Point2D(100, 0)).PortFaceBounds.MinX, 6);   // 평행이동 시 교점도 같이 이동
    }

    [Fact]
    public void AnchorOf_FoundationFaces_MovesFaceOnly_KeepsOffsetFromWallInner()
    {
        var outline = new ValveRoomOutline(Outline().Walls, null, null, [new OutlineFoundation(1, -300, -400, Width + 300, Height + 400)], useFoundationFaces: true);

        var anchor = outline.AnchorOf(PipePort.Create(PortFlow.In, OutlineSide.Left, 1000, "IN"), fromFarEdge: false);

        Assert.Equal(new Point2D(-300, 1000), anchor);
        Assert.False(outline.WithFoundationFaces(false).UseFoundationFaces);
    }

    [Fact]
    public void Resolve_SluiceRoom_MainRunWithValveAndBypassAtSamePoint()
    {
        // 표준도: IN2 없음(2026-10-28 사용자 결정). 본관 밸브 + 부제수 바이패스 세트가 같은 자리(A–A 사이 본관 밸브).
        var template = ValveRoomPipingTemplateCatalog.Default("제수밸브실")!;

        var resolved = ValveRoomPipingTemplateResolver.Resolve(template, Outline(), fromFarEdge: false, Diameter);

        Assert.Single(resolved.Ports, x => x.Flow == PortFlow.In);
        Assert.Single(resolved.Ports, x => x.Flow == PortFlow.Out);
        var run = Assert.Single(resolved.Segments);
        Assert.Equal(new Point2D(0, 1650), run.Start);
        Assert.Equal(new Point2D(Width, 1650), run.End);
        Assert.Equal(2, resolved.Fittings!.Count);
        Assert.All(resolved.Fittings, f => Assert.Equal(0.443, f.T, 6));
        Assert.Single(resolved.Fittings, f => f.IsBypassSet && f.Side == 1);
    }

    [Fact]
    public void Resolve_FromFarEdgeToggle_KeepsSegmentGeometry_OnlyOffsetsChange()
    {
        // 프리셋 비율은 항상 하단 기준이다(docs/172 § B-1). 상단 기준으로 풀어도 선은 같은 자리, 포트 이격만 반대 모서리 값이 된다.
        var template = ValveRoomPipingTemplateCatalog.Default("제수밸브실")!;

        var fromBottom = ValveRoomPipingTemplateResolver.Resolve(template, Outline(), fromFarEdge: false, Diameter);
        var fromTop = ValveRoomPipingTemplateResolver.Resolve(template, Outline(), fromFarEdge: true, Diameter);

        Assert.Equal(fromBottom.Segments.Select(x => (x.Start, x.End)), fromTop.Segments.Select(x => (x.Start, x.End)));
        var inBottom = fromBottom.Ports.Single(x => x.Label == "IN");
        var inTop = fromTop.Ports.Single(x => x.Label == "IN");
        Assert.Equal(1650, inBottom.OffsetMm, 6);
        Assert.Equal(1350, inTop.OffsetMm, 6);
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
    public void Resolve_TemplateWithValveAndBypass_ReturnsFittingsOnTheirSegments()
    {
        var template = new ValveRoomPipingTemplate("테스트", "테스트",
            [new(PortFlow.In, OutlineSide.Left, 0.5, "IN"), new(PortFlow.Out, OutlineSide.Right, 0.5, "OUT")],
            [new(TemplatePoint.Port("IN"), TemplatePoint.Port("OUT"))],
            [new(0, 0.5, ValveRole.Sluice)],
            [new(0, 0.25, -1)]);

        var resolved = ValveRoomPipingTemplateResolver.Resolve(template, Outline(), false, Diameter);

        Assert.Equal(2, resolved.Fittings!.Count);
        Assert.All(resolved.Fittings, f => Assert.Equal(resolved.Segments[0].Start, f.SegmentStart));
        Assert.Single(resolved.Fittings, f => f.IsBypassSet && f.Side == -1 && f.T == 0.25);
    }

    [Fact]
    public void Resolve_FittingOnMissingSegment_Throws()
    {
        var template = new ValveRoomPipingTemplate("테스트", "테스트",
            [new(PortFlow.In, OutlineSide.Left, 0.5, "IN"), new(PortFlow.Out, OutlineSide.Right, 0.5, "OUT")],
            [new(TemplatePoint.Port("IN"), TemplatePoint.Port("OUT"))],
            [new(5, 0.5, ValveRole.Sluice)]);

        Assert.Throws<DHBIMWATER.Shared.Errors.DhbimwaterException>(() => ValveRoomPipingTemplateResolver.Resolve(template, Outline(), false, Diameter));
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

    // 타이밸브실(2026-11-09): IN 2·OUT 2, 위·아래 본관 + 가운데 연결관(H 모양). 선 5본·바이패스 3세트·세트마다 버터플라이 2개
    [Fact]
    public void TieValveRoom_HShape_ButterflyWithBypassOnEveryRun()
    {
        var template = ValveRoomPipingTemplateCatalog.Default("타이밸브실")!;

        var resolved = ValveRoomPipingTemplateResolver.Resolve(template, Outline(), fromFarEdge: false, Diameter);

        Assert.Equal(2, resolved.Ports.Count(x => x.Flow == PortFlow.In));
        Assert.Equal(2, resolved.Ports.Count(x => x.Flow == PortFlow.Out));
        Assert.Equal(5, resolved.Segments.Count);
        // 위·아래 T 절점 = 본관 두 선 공유 끝 + 연결관 양 끝
        var upperTee = resolved.Segments[0].End;
        var lowerTee = resolved.Segments[2].End;
        Assert.Equal(upperTee, resolved.Segments[1].Start);
        Assert.Equal(lowerTee, resolved.Segments[3].Start);
        Assert.Equal(lowerTee, resolved.Segments[4].Start);
        Assert.Equal(upperTee, resolved.Segments[4].End);
        Assert.Equal(6, resolved.Fittings!.Count(f => !f.IsBypassSet && f.Role == ValveRole.Butterfly));
        var sets = resolved.Fittings!.Where(f => f.IsBypassSet).ToList();
        Assert.Equal(3, sets.Count);
        // 위·아래 세트 = T 절점 중심(본관 선 끝)
        Assert.Contains(sets, f => f.SegmentEnd == upperTee && f.T == 1.0 && f.Side == 1);
        Assert.Contains(sets, f => f.SegmentEnd == lowerTee && f.T == 1.0 && f.Side == -1);
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
            foreach (var v in template.Valves) Assert.InRange(v.SegmentIndex, 0, template.Segments.Count - 1);
            foreach (var b in template.BypassSets ?? []) Assert.InRange(b.SegmentIndex, 0, template.Segments.Count - 1);
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
