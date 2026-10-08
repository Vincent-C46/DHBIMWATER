using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>역가져오기 재구성기(docs/158 § D). 정방향이 쪼갠 부재를 다시 엣지로 합치는지 본다.</summary>
public sealed class PipeNetworkReconstructorTests
{
    [Fact]
    public void Build_MergesCollinearSegmentsIntoOneEdge()
    {
        var import = PipeNetworkReconstructor.Build(
        [
            Segment(1, P(0, 0), P(2000, 0)),
            Segment(2, P(2000, 0), P(4000, 0)),
            Segment(3, P(4000, 0), P(5000, 0)),
        ]);

        var edge = Assert.Single(import.Edges);
        Assert.Equal(0, Math.Min(edge.Start.X, edge.End.X), 3);
        Assert.Equal(5000, Math.Max(edge.Start.X, edge.End.X), 3);
        Assert.Equal(3, import.SourceElementIds.Count);
    }

    [Fact]
    public void Build_BendBecomesSharedNodeAtLegAxisIntersection()
    {
        var import = PipeNetworkReconstructor.Build(
        [
            Segment(1, P(0, 0), P(1800, 0)),
            Bend(2, P(2000, 0), P(-1, 0), P(0, 1)),
            Segment(3, P(2000, 200), P(2000, 1500)),
        ]);

        Assert.Equal(2, import.Edges.Count);
        // 두 엣지가 곡관 코너 (2000,0)을 공유해야 한다 — 커넥터 자리(1800,0)/(2000,200)가 아니다.
        Assert.All(import.Edges, edge => Assert.True(
            Near(edge.Start, P(2000, 0)) || Near(edge.End, P(2000, 0)),
            $"엣지가 절점을 공유하지 않는다: {edge.Start} → {edge.End}"));
    }

    [Fact]
    public void Build_ExportsNodeFittingElementIdAtNodePosition()
    {
        // docs/216 § A-2 — 절점 부속의 ElementId와 절점 좌표가 결과에 실려야 VM이 절점 Guid에 걸 수 있다.
        var import = PipeNetworkReconstructor.Build(
        [
            Segment(1, P(0, 0), P(1800, 0)),
            Bend(2, P(2000, 0), P(-1, 0), P(0, 1)),
            Segment(3, P(2000, 200), P(2000, 1500)),
        ]);

        var nodeFitting = Assert.Single(import.NodeFittings!);
        Assert.Equal(2, nodeFitting.ElementId);
        Assert.True(Near(nodeFitting.Position, P(2000, 0)));
    }

    [Fact]
    public void Build_TeeJoinsThreeEdgesAtOneNode()
    {
        var import = PipeNetworkReconstructor.Build(
        [
            Tee(1, P(0, 0)),
            Segment(2, P(-200, 0), P(-3000, 0)),
            Segment(3, P(200, 0), P(3000, 0)),
            Segment(4, P(0, 200), P(0, 3000)),
        ]);

        Assert.Equal(3, import.Edges.Count);
        Assert.All(import.Edges, edge => Assert.True(Near(edge.Start, P(0, 0)) || Near(edge.End, P(0, 0))));
    }

    [Fact]
    public void Build_CrossJoinsFourEdgesAtAxisIntersection()
    {
        // docs/175 § B-4 — 커넥터 4개 부재는 십자 절점이 되고, 절점은 두 축의 교점이다.
        var import = PipeNetworkReconstructor.Build(
        [
            Cross(1, P(1000, 500)),
            Segment(2, P(800, 500), P(-2000, 500)),
            Segment(3, P(1200, 500), P(4000, 500)),
            Segment(4, P(1000, 700), P(1000, 3000)),
            Segment(5, P(1000, 300), P(1000, -2000)),
        ]);

        Assert.Equal(4, import.Edges.Count);
        Assert.All(import.Edges, edge => Assert.True(Near(edge.Start, P(1000, 500)) || Near(edge.End, P(1000, 500))));
        Assert.Equal("십자 : DN100", import.CrossFamilyTypeName);
        Assert.Null(import.TeeFamilyTypeName);
    }

    [Fact]
    public void Build_InlineValveRidesOnMergedEdge()
    {
        var import = PipeNetworkReconstructor.Build(
        [
            Segment(1, P(0, 0), P(1400, 0)),
            Accessory(2, P(1400, 0), P(1600, 0)),
            Segment(3, P(1600, 0), P(3000, 0)),
        ]);

        Assert.Single(import.Edges);
        var fitting = Assert.Single(import.Fittings);
        Assert.Equal(0, fitting.EdgeIndex);
        Assert.Equal("밸브 : DN100", fitting.FamilyTypeName);
        Assert.Equal(0.5, fitting.T, 3);
    }

    [Fact]
    public void Build_SinglePortAccessory_RidesNearestEdgeWithoutSplitting()
    {
        var import = PipeNetworkReconstructor.Build(
        [
            Segment(1, P(0, 0), P(4000, 0)),
            new PickedPipePart(2, PipePartRole.Accessory, "공기밸브 : DN100", P(1000, 0), 0,
                [new PickedConnector(P(1000, 0), 0, P(0, 1), 100, [])]),
        ]);

        Assert.Single(import.Edges);
        var fitting = Assert.Single(import.Fittings);
        Assert.Equal(0.25, fitting.T, 3);
    }

    [Fact]
    public void Build_MixedDiameterChain_UsesModeAndWarns()
    {
        var import = PipeNetworkReconstructor.Build(
        [
            Segment(1, P(0, 0), P(1000, 0), diameterMm: 200),
            Segment(2, P(1000, 0), P(2000, 0), diameterMm: 200),
            Segment(3, P(2000, 0), P(3000, 0), diameterMm: 100),
        ]);

        Assert.Equal(200, Assert.Single(import.Edges).DiameterMm, 3);
        Assert.Contains(import.Warnings, x => x.Contains("섞여"));
    }

    [Fact]
    /// <summary>판정에 실패해도 나머지는 살리되, 삭제 대상에서는 빼지 않는다 —
    /// 남겨 두면 재생성한 배관과 겹친다(docs/163 § A).</summary>
    public void Build_UnknownPart_IsSkippedButStillDeleted()
    {
        var broken = new PickedPipePart(9, PipePartRole.Unknown, "이상한것 : A", P(9000, 9000), 0, [],
            "배관 커넥터가 4개라 부재 종류를 판정할 수 없습니다");

        var import = PipeNetworkReconstructor.Build([Segment(1, P(0, 0), P(3000, 0)), broken]);

        Assert.Single(import.Edges);
        Assert.Contains(import.Warnings, x => x.Contains("ElementId 9") && x.Contains("삭제"));
        Assert.Contains(9L, import.SourceElementIds);
    }

    [Fact]
    public void Build_ValidPartNotPlacedOnCanvas_IsWarnedAndStillDeleted()
    {
        // 다른 배관과 한참 떨어져 아무 엣지에도 물리지 못하는 곡관 1개. 판정은 성공했지만 캔버스에 오르지 못한다(docs/163 § A-2).
        var orphan = Bend(7, P(50000, 50000), P(1, 0), P(0, 1));

        var import = PipeNetworkReconstructor.Build([Segment(1, P(0, 0), P(3000, 0)), orphan]);

        Assert.Single(import.Edges);
        Assert.Contains(import.Warnings, x => x.Contains("ElementId 7") && x.Contains("삭제"));
        Assert.Contains(7L, import.SourceElementIds);
    }

    [Fact]
    public void Build_WithoutAnyEdge_KeepsDeleteListEmpty()
    {
        // 캔버스를 바꾸지 않는 결과(ApplyPickedPiping이 IsEmpty면 되돌아간다)에서 삭제 목록만 차면
        // 가져오지도 않은 요소를 지우게 된다(docs/163 § A-3).
        var import = PipeNetworkReconstructor.Build(
            [new PickedPipePart(5, PipePartRole.Unknown, "이상한것 : A", P(0, 0), 0, [], "판정 불가")]);

        Assert.Empty(import.Edges);
        Assert.Empty(import.SourceElementIds);
    }

    [Fact]
    public void Build_MixedElevation_AveragesAndWarns()
    {
        var import = PipeNetworkReconstructor.Build(
        [
            Segment(1, P(0, 0), P(2000, 0), zMm: 1000),
            Segment(2, P(2000, 0), P(4000, 0), zMm: 1100),
        ]);

        Assert.Equal(1050, import.ElevationMm, 3);
        Assert.Contains(import.Warnings, x => x.Contains("표고"));
    }

    /// <summary>EL 0 배관을 되가져왔을 때 ft↔mm 왕복 잔차가 남지 않아야 한다(docs/176 § A).
    /// 남으면 창의 "관 중심 높이(EL)"에 3E-14 같은 지수 표기가 뜬다.</summary>
    [Fact]
    public void Build_ZeroElevationWithFloatingResidue_ReturnsExactZero()
    {
        const double residue = 3.0198066269804258E-14;   // ft↔mm 왕복에서 실제로 관측되는 크기

        var import = PipeNetworkReconstructor.Build(
        [
            Segment(1, P(0, 0), P(2000, 0), zMm: residue),
            Segment(2, P(2000, 0), P(4000, 0), zMm: -residue),
        ]);

        Assert.Equal(0d, import.ElevationMm);          // 근사 비교가 아니라 정확히 0
        Assert.DoesNotContain(import.Warnings, x => x.Contains("표고"));
    }

    [Fact]
    public void Build_BentChainWithoutBend_SplitsIntoTwoEdges()
    {
        var import = PipeNetworkReconstructor.Build(
        [
            Segment(1, P(0, 0), P(2000, 0)),
            Segment(2, P(2000, 0), P(3000, 1000)),   // 45° — 곡관이 없는데 꺾였다
        ]);

        Assert.Equal(2, import.Edges.Count);
        Assert.Contains(import.Warnings, x => x.Contains("꺾여"));
    }

    [Fact]
    public void Build_EmptyInput_ReturnsEmpty()
    {
        Assert.True(PipeNetworkReconstructor.Build([]).IsEmpty);
        Assert.Same(PipeLayoutImport.Empty, PipeNetworkReconstructor.Build([]));
    }

    [Fact]
    public void Build_ReportsDominantFamilyNamesAndDiameter()
    {
        var import = PipeNetworkReconstructor.Build(
        [
            Segment(1, P(0, 0), P(1800, 0)),
            Bend(2, P(2000, 0), P(-1, 0), P(0, 1)),
            Segment(3, P(2000, 200), P(2000, 1500)),
        ]);

        Assert.Equal("직관 : DN100", import.SegmentFamilyTypeName);
        Assert.Equal("곡관 : DN100", import.BendFamilyTypeName);
        Assert.Null(import.TeeFamilyTypeName);
        Assert.Null(import.CrossFamilyTypeName);
        Assert.Equal(100, import.DominantDiameterMm);
    }

    [Fact]
    public void Build_MixedSegmentFamilies_WarnsWithEachNameAndCount()
    {
        var import = PipeNetworkReconstructor.Build(
        [
            Segment(1, P(0, 0), P(1000, 0)),
            Segment(2, P(1000, 0), P(2000, 0)),
            Segment(3, P(2000, 0), P(3000, 0)) with { FamilyTypeName = "플랜지직관 : DN100" },
        ]);

        Assert.Equal("직관 : DN100", import.SegmentFamilyTypeName);
        Assert.Contains(import.Warnings, x => x.Contains("'직관 : DN100' 2개")
                                           && x.Contains("'플랜지직관 : DN100' 1개")
                                           && x.Contains("가장 많은"));
    }

    /// <summary>docs/224 § B — 예약 틈(186)보다 밸브(356)가 커서 왼쪽 관과 170mm 겹친 현장 형태.
    /// 오른쪽 커넥터만 관과 맞물려 있고 Revit 연결은 없다.</summary>
    [Fact]
    public void Build_ValveWiderThanGap_MergesIntoOneEdgeAtConnectorMidpoint()
    {
        var import = PipeNetworkReconstructor.Build(
        [
            Segment(1, P(0, 0), P(740, 0)),
            Accessory(2, P(570, 0), P(926, 0)),
            Segment(3, P(926, 0), P(1610, 0)),
        ]);

        var edge = Assert.Single(import.Edges);
        Assert.Equal(0, Math.Min(edge.Start.X, edge.End.X), 3);
        Assert.Equal(1610, Math.Max(edge.Start.X, edge.End.X), 3);
        var fitting = Assert.Single(import.Fittings);
        var t = edge.Start.X < edge.End.X ? fitting.T : 1 - fitting.T;
        Assert.Equal(748d / 1610, t, 6);
        Assert.Contains(import.Warnings, x => x.Contains("어긋나"));
    }

    [Fact]
    public void Build_ValveNarrowerThanGap_BothSidesMerge()
    {
        var import = PipeNetworkReconstructor.Build(
        [
            Segment(1, P(0, 0), P(700, 0)),
            Accessory(2, P(750, 0), P(900, 0)),
            Segment(3, P(950, 0), P(1600, 0)),
        ]);

        Assert.Single(import.Edges);
        Assert.Single(import.Fittings);
    }

    [Fact]
    public void Build_GapWithoutValve_StaysSplit()
    {
        var import = PipeNetworkReconstructor.Build(
        [
            Segment(1, P(0, 0), P(700, 0)),
            Segment(2, P(750, 0), P(1600, 0)),
        ]);

        Assert.Equal(2, import.Edges.Count);
        Assert.DoesNotContain(import.Warnings, x => x.Contains("어긋나"));
    }

    [Fact]
    public void Build_OffAxisPipeEnd_NotMerged()
    {
        var import = PipeNetworkReconstructor.Build(
        [
            Segment(1, P(0, 50), P(740, 50)),        // 밸브 축에서 50mm 벗어난 평행 관
            Accessory(2, P(570, 0), P(926, 0)),
            Segment(3, P(926, 0), P(1610, 0)),
        ]);

        Assert.Equal(2, import.Edges.Count);
        Assert.DoesNotContain(import.Warnings, x => x.Contains("어긋나"));
    }

    [Fact]
    public void Build_TiedSegmentFamilies_PicksByNameAndSaysSo()
    {
        var import = PipeNetworkReconstructor.Build(
        [
            Segment(1, P(0, 0), P(1000, 0)) with { FamilyTypeName = "B직관 : DN100" },
            Segment(2, P(1000, 0), P(2000, 0)) with { FamilyTypeName = "A직관 : DN100" },
        ]);

        Assert.Equal("A직관 : DN100", import.SegmentFamilyTypeName);
        Assert.Contains(import.Warnings, x => x.Contains("이름순"));
    }

    [Fact]
    public void Build_SameZoneOnEveryPart_IsCarriedToImport()
    {
        var import = PipeNetworkReconstructor.Build(
        [
            Segment(1, P(0, 0), P(2000, 0)) with { Zone = "이토밸브실" },
            Segment(2, P(2000, 0), P(4000, 0)) with { Zone = "이토밸브실" },
        ]);

        Assert.Equal("이토밸브실", import.Zone);
    }

    [Fact]
    public void Build_MixedZone_LeavesImportZoneEmptyAndWarns()
    {
        var import = PipeNetworkReconstructor.Build(
        [
            Segment(1, P(0, 0), P(2000, 0)) with { Zone = "이토밸브실" },
            Segment(2, P(2000, 0), P(4000, 0)) with { Zone = "제수밸브실" },
        ]);

        Assert.Equal(string.Empty, import.Zone);
        Assert.Contains(import.Warnings, x => x.Contains("밸브실 유형"));
    }

    [Fact]
    public void Build_BypassSetWithTeeBetweenA_WarnsWithTeeIdAndDoesNotRestoreSet()
    {
        var import = PipeNetworkReconstructor.Build(
        [
            .. BypassParts(),
            Segment(41, P(-2000, 0), P(-200, 0), 1650),
            Segment(42, P(2200, 0), P(4000, 0), 1650),
            Segment(51, P(200, 0), P(800, 0), 1650),
            Tee(52, P(1000, 0), diameterMm: 1650),
            Segment(53, P(1200, 0), P(1800, 0), 1650)
        ]);

        Assert.Contains(import.Warnings, x => x.Contains("T형관") && x.Contains("ElementId 52")
                                               && x.Contains("재생성 시 세트 부재가 삭제됩니다."));
        Assert.DoesNotContain(import.Fittings, x => x.Bypass is not null);
    }

    [Fact]
    public void Build_BypassSetWithoutTee_RestoresSetWithoutWarning()
    {
        var import = PipeNetworkReconstructor.Build(
        [
            .. BypassParts(),
            Segment(31, P(200, 0), P(900, 0), 1650),
            Accessory(32, P(900, 0), P(1100, 0), 1650),
            Segment(33, P(1100, 0), P(1800, 0), 1650),
            Segment(41, P(-2000, 0), P(-200, 0), 1650),
            Segment(42, P(2200, 0), P(4000, 0), 1650)
        ]);

        Assert.DoesNotContain(import.Warnings, x => x.Contains("바이패스 세트(ElementId"));
        Assert.NotNull(Assert.Single(import.Fittings, x => x.Bypass is not null).Bypass);
    }

    [Fact]
    public void Build_BypassSetWithoutTeeAndContinuousMainLine_KeepsOriginalWarning()
    {
        var import = PipeNetworkReconstructor.Build(
        [
            .. BypassParts(),
            Segment(55, P(200, 0), P(1000, 0), 1650)
        ]);

        Assert.Contains(import.Warnings, x => x ==
            "바이패스 세트(ElementId 1)를 올릴 본관 선을 찾지 못했습니다. 재생성 시 세트 부재가 삭제됩니다.");
        Assert.DoesNotContain(import.Fittings, x => x.Bypass is not null);
    }

    // ── 헬퍼 ────────────────────────────────────────────────────────────────────

    private static Point2D P(double x, double y) => new(x, y);

    private static bool Near(Point2D a, Point2D b) => a.DistanceTo(b) <= 0.001;

    /// <summary>직관·단관 1본. 커넥터는 양 끝에 있고 바깥(관 밖)을 향한다.</summary>
    private static PickedPipePart Segment(long id, Point2D start, Point2D end, double diameterMm = 100, double zMm = 0)
        => new(id, PipePartRole.Segment, "직관 : DN100", Mid(start, end), zMm,
        [
            new PickedConnector(start, zMm, Unit(start, end, reverse: true), diameterMm, []),
            new PickedConnector(end, zMm, Unit(start, end), diameterMm, [])
        ]);

    /// <summary>인라인 밸브 1개. 관과 같은 축 위에서 두 커넥터로 관을 관통한다.</summary>
    private static PickedPipePart Accessory(long id, Point2D start, Point2D end, double diameterMm = 100)
        => new(id, PipePartRole.Accessory, "밸브 : DN100", Mid(start, end), 0,
        [
            new PickedConnector(start, 0, Unit(start, end, reverse: true), diameterMm, []),
            new PickedConnector(end, 0, Unit(start, end), diameterMm, [])
        ]);

    /// <summary>곡관 1개. <paramref name="legA"/>·<paramref name="legB"/> 방향으로 레그가 뻗고,
    /// 커넥터는 절점에서 <paramref name="legMm"/>만큼 물러난 자리에 있다(정방향 배치와 같은 형태).</summary>
    private static PickedPipePart Bend(long id, Point2D corner, Point2D legA, Point2D legB, double legMm = 200, double diameterMm = 100)
    {
        var a = Offset(corner, legA, legMm);
        var b = Offset(corner, legB, legMm);
        return new PickedPipePart(id, PipePartRole.Bend, "곡관 : DN100", corner, 0,
        [
            new PickedConnector(a, 0, Normalize(legA), diameterMm, []),
            new PickedConnector(b, 0, Normalize(legB), diameterMm, [])
        ]);
    }

    /// <summary>T형 1개. 주관은 ±X, 분기는 +Y다.</summary>
    private static PickedPipePart Tee(long id, Point2D corner, double legMm = 200, double diameterMm = 100)
        => new(id, PipePartRole.Tee, "T형 : DN100", corner, 0,
        [
            new PickedConnector(Offset(corner, P(-1, 0), legMm), 0, P(-1, 0), diameterMm, []),
            new PickedConnector(Offset(corner, P(1, 0), legMm), 0, P(1, 0), diameterMm, []),
            new PickedConnector(Offset(corner, P(0, 1), legMm), 0, P(0, 1), diameterMm, [])
        ]);

    /// <summary>십자 1개. 주관 ±X, 분기 ±Y(docs/175 § B-4).</summary>
    private static PickedPipePart Cross(long id, Point2D corner, double legMm = 200, double diameterMm = 100)
        => new(id, PipePartRole.Cross, "십자 : DN100", corner, 0,
        [
            new PickedConnector(Offset(corner, P(-1, 0), legMm), 0, P(-1, 0), diameterMm, []),
            new PickedConnector(Offset(corner, P(1, 0), legMm), 0, P(1, 0), diameterMm, []),
            new PickedConnector(Offset(corner, P(0, 1), legMm), 0, P(0, 1), diameterMm, []),
            new PickedConnector(Offset(corner, P(0, -1), legMm), 0, P(0, -1), diameterMm, [])
        ]);

    private static IEnumerable<PickedPipePart> BypassParts()
    {
        yield return ValveTee(1, P(0, 0));
        yield return ValveTee(2, P(2000, 0));
        yield return ValveBend(3, P(200, 700), P(0, 300));
        yield return ValveBend(4, P(1800, 700), P(2000, 300));
        yield return Segment(21, P(200, 700), P(900, 700), 300);
        yield return Accessory(22, P(900, 700), P(1100, 700), 300);
        yield return Segment(23, P(1100, 700), P(1800, 700), 300);
    }

    private static PickedPipePart ValveTee(long id, Point2D center)
        => new(id, PipePartRole.ValveTee, "DH_밸브부관A : 1650A", center, 0,
        [
            new PickedConnector(P(center.X - 200, 0), 0, P(-1, 0), 1650, []),
            new PickedConnector(P(center.X + 200, 0), 0, P(1, 0), 1650, []),
            new PickedConnector(P(center.X, 300), 0, P(0, 1), 300, [])
        ]);

    private static PickedPipePart ValveBend(long id, Point2D valveSide, Point2D teeSide)
        => new(id, PipePartRole.ValveBend, "DH_밸브부관B : 300A", valveSide, 0,
        [
            new PickedConnector(valveSide, 0, P(1, 0), 300, []),
            new PickedConnector(teeSide, 0, P(0, -1), 300, [])
        ]);

    private static Point2D Mid(Point2D a, Point2D b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private static Point2D Offset(Point2D origin, Point2D direction, double distance)
    {
        var unit = Normalize(direction);
        return new Point2D(origin.X + unit.X * distance, origin.Y + unit.Y * distance);
    }

    private static Point2D Unit(Point2D start, Point2D end, bool reverse = false)
    {
        var vector = reverse ? new Point2D(start.X - end.X, start.Y - end.Y) : new Point2D(end.X - start.X, end.Y - start.Y);
        return Normalize(vector);
    }

    private static Point2D Normalize(Point2D vector)
    {
        var length = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
        return length <= 1e-12 ? new Point2D(0, 0) : new Point2D(vector.X / length, vector.Y / length);
    }
}
