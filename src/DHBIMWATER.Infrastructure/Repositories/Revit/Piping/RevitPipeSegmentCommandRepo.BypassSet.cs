using Autodesk.Revit.DB;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Infrastructure.Logging;
using DHBIMWATER.Shared.Errors;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

/// <summary>
/// 바이패스 세트 배치(docs/228 § C, docs/230). 본관 위 인라인 부속 하나를 밸브부관 A 2개 · 밸브부관 B 2개 ·
/// 바이패스 밸브 · 바이패스 쪽 틈 채움 단관으로 펼친다.
/// <para>A–A 사이 본관은 세트가 아니다(docs/230 § 0) — 예약하지 않으므로 기존 관 분할이 관·일반 밸브로 채운다.</para>
/// <para>치수는 패밀리가 호칭지름으로 정하므로 캔버스 값을 쓰지 않는다. 원점에 프로브로 놓아 커넥터를 잰 뒤(§ C-1)
/// 본관 점유 구간을 예약하고(§ C-2), 실제 배치에서는 부재마다 놓은 뒤 다시 재서 커넥터끼리 맞붙인다(§ C-3).
/// 연결은 기존 <see cref="ConnectCoincidentConnectors"/>가 접점 일치로 한다.</para>
/// </summary>
internal sealed partial class RevitPipeSegmentCommandRepo
{
    /// <summary>실측 캐시 키. 바이패스 관경은 핸드북 규격표가 본관 구경에서 정한다(docs/256) — 밸브 패밀리·본관·지관이 같으면 세트 치수가 같다.
    /// 밸브 2개 세트는 V2 패밀리·단관 길이도 치수를 바꾸므로 키에 넣는다(docs/255 § B-1). 1개 세트는 (null, 0)이다.</summary>
    private readonly record struct BypassLayoutKey(string BypassValveFamily, double MainDiameterMm, double BranchDiameterMm,
        string? SecondValveFamily = null, double SpoolLengthMm = 0);

    /// <summary>프로브로 잰 세트 치수(mm)와 생성 때 정한 값.</summary>
    /// <param name="TeeCenterOffsetMm">세트 중심 → 밸브부관 A 중심(주관 축 위) 거리 d.</param>
    /// <param name="TeeMainHalfMm">A 중심 → 주관 커넥터 거리.</param>
    /// <param name="TeeBranchMm">A 중심 → 수평분기 커넥터 거리.</param>
    /// <param name="BendTeeLegMm">B 코너 → A쪽 커넥터(짧은 레그) 거리.</param>
    /// <param name="BendValveLegMm">B 코너 → 밸브쪽 커넥터(긴 레그) 거리.</param>
    /// <param name="BypassDiameterMm">핸드북 규격표의 지관 관경 = 바이패스 관경(docs/256). 밸브부관 B 커넥터 지름은 이 값과 같은지 검증만 한다.</param>
    /// <param name="BypassValveSymbol">바이패스 관경과 DN이 같은 바이패스 밸브 유형(docs/244 § A-4). 2개 세트면 V1(엣지 시작 쪽)이다.</param>
    /// <param name="SecondValveSymbol">밸브 2개 세트의 V2(엣지 끝 쪽) 유형. V1과 같은 규칙으로 고른다(docs/255). 1개 세트면 null.</param>
    /// <param name="SecondValveWidthMm">V2 커넥터 간 폭(mm). 1개 세트면 0.</param>
    /// <param name="SpoolLengthMm">V1–V2 사이 단관 길이(mm). 0 = 직결. 1개 세트면 0.</param>
    private sealed record BypassLayout(
        double TeeCenterOffsetMm, double TeeMainHalfMm, double TeeBranchMm,
        double BendTeeLegMm, double BendValveLegMm, double BypassValveWidthMm,
        double BypassDiameterMm, FamilySymbol BypassValveSymbol,
        FamilySymbol? SecondValveSymbol = null, double SecondValveWidthMm = 0, double SpoolLengthMm = 0)
    {
        /// <summary>B–B 사이 밸브 묶음 폭(mm, docs/255 § B-2). 1개 = W1, 2개 = W1 + 단관 + W2.</summary>
        public double ValveRunWidthMm => SecondValveSymbol is null ? BypassValveWidthMm : BypassValveWidthMm + SpoolLengthMm + SecondValveWidthMm;

        /// <summary>본관에서 A 하나가 차지하는 구간(세트 중심 기준 축 거리). <paramref name="sign"/> -1 = 엣지 시작 쪽 A.</summary>
        public OccupiedSpan TeeSpan(double centerMm, int sign)
            => new(centerMm + sign * TeeCenterOffsetMm - TeeMainHalfMm, centerMm + sign * TeeCenterOffsetMm + TeeMainHalfMm);

        /// <summary>두 A 안쪽 주관 커넥터 사이 틈(mm). 사용자가 밸브를 놓지 않으면 관 분할이 이 구간을 관으로 채운다.</summary>
        public double InnerGapMm => 2 * (TeeCenterOffsetMm - TeeMainHalfMm);

        /// <summary>틈이 관 최소 조각(<see cref="PipeSegmentPlan.MinSegmentMm"/>)보다 짧은가. 그런 틈에 관을 넣지 않는다(docs/230 § E).</summary>
        public bool HasTinyInnerGap => InnerGapMm < PipeSegmentPlan.MinSegmentMm;
    }

    /// <summary>밸브부관 A 실측 결과(월드 좌표).</summary>
    private sealed record TeeGeometry(XYZ Center, XYZ MainAxis, XYZ BranchDirection, double MainHalfMm, double BranchMm,
        XYZ Main1, XYZ Main2, XYZ Branch, IReadOnlyList<XYZ> Verticals);

    /// <summary>밸브부관 B 실측 결과(월드 좌표). [0] = 밸브 쪽(긴 레그), [1] = A 쪽(짧은 레그) — docs/247.</summary>
    private sealed record BendGeometry(XYZ Corner, XYZ ValveSide, XYZ TeeSide, XYZ ValveDirection, XYZ TeeDirection,
        double ValveLegMm, double TeeLegMm);

    // ── § C-2 예약 ─────────────────────────────────────────────────────────────

    /// <summary>세트 하나의 건너뛰기 사유를 정하고 치수를 잰다(캐시 사용). 건너뛰면 실패를 기록하고 <paramref name="droppedFittings"/>에 넣은 뒤 null.
    /// 엣지 위 예약(<see cref="BypassSetSpans"/>)과 tee 중심 세트 사전 계획(<see cref="PlanTeeCenteredSets"/>)이 같이 쓴다.</summary>
    private static BypassLayout? ResolveBypassLayout(
        Document document, FamilySymbolCache symbols, PipeEdgeDefinition edge, PipeNetworkDefinition network,
        PipeSegmentFamilySelection families, SkipPlan skipPlan, IDictionary<string, string> unusableValves,
        ICollection<PipeConnectionFailure> skipped, ISet<Guid> droppedFittings,
        IDictionary<BypassLayoutKey, BypassLayout> cache, FittingPlacementDefinition fitting)
    {
        var spec = fitting.Bypass!;
        var canvasPoint = ToXyz(fitting.Position, fitting.Elevation, network.ReferencePoint);
        // 새 세트는 패밀리명만, 되가져온 세트는 "패밀리 : 유형"을 든다 — 유형은 생성 때 다시 고르므로 패밀리로 모은다(docs/244 § A-5).
        var valveFamily = BypassValveFamilyOf(spec.BypassValveFamilyTypeName);
        // 밸브 2개 세트의 V2(docs/255). 1개 세트면 null.
        var secondFamily = spec.HasTwoValves ? BypassValveFamilyOf(spec.SecondValveFamilyTypeName) : null;
        // 지관 관경은 핸드북 규격표가 정한다(docs/256). 표에 없는 주관·관종이면 세트를 만들지 않는다.
        var branchMm = ValveBypassSpecTable.BranchDiameterMm(network.Material, network.SteelFittingClass, edge.DiameterMm);
        var reason = skipPlan.BypassSetReason
            ?? (branchMm is null ? BypassSpecMissingReason(network, edge.DiameterMm) : null)
            ?? (string.IsNullOrWhiteSpace(valveFamily) ? "바이패스 밸브가 선택되지 않았습니다." : null)
            ?? (unusableValves.TryGetValue(valveFamily, out var bypassReason) ? bypassReason : null)
            ?? (secondFamily is not null && unusableValves.TryGetValue(secondFamily, out var secondReason) ? secondReason : null)
            ?? (secondFamily is not null && !BypassSet.IsValidSpoolLength(spec.SpoolLengthMm)
                ? FormattableString.Invariant($"바이패스 밸브 사이 단관 {spec.SpoolLengthMm:0.#}mm는 쓸 수 없습니다(0 = 직결, 또는 {PipeSegmentPlan.MinSegmentMm:0}mm 이상).")
                : null);
        if (reason is not null)
        {
            skipped.Add(CreateFailure(canvasPoint, BypassSet.TypeKey, valveFamily, 0, reason, network) with { FittingId = fitting.Id });
            droppedFittings.Add(fitting.Id);
            return null;
        }
        try
        {
            var key = new BypassLayoutKey(valveFamily, edge.DiameterMm, branchMm!.Value,
                secondFamily, secondFamily is null ? 0 : spec.SpoolLengthMm);
            if (!cache.TryGetValue(key, out var layout))
                cache[key] = layout = MeasureBypassLayout(document, symbols, families, key);
            return layout;
        }
        catch (Exception ex)
        {
            skipped.Add(CreateFailure(canvasPoint, BypassSet.TypeKey, valveFamily, 0,
                $"세트 치수를 잴 수 없습니다: {ex.Message}", network) with { FittingId = fitting.Id });
            droppedFittings.Add(fitting.Id);
            return null;
        }
    }

    // ── tee 중심 세트(docs/273 § C) ──────────────────────────────────────────────

    /// <summary>중심이 T 절점에 놓여 두 본관 엣지에 걸치는 세트. 세트는 한 엣지(<paramref name="OwnerEdgeId"/>) 소속이지만
    /// 밸브부관 A 하나는 반대편 본관 엣지(<paramref name="OtherEdgeId"/>)에 놓인다. 구간은 각 엣지 배치 축(mm) 기준이다.</summary>
    /// <param name="GapOpenPoints">A와 T 절점 부속 사이 틈이 관 최소 조각보다 짧아 단관을 넣지 않은 자리의 커넥터 점(연결 실패로 보고하지 않는다).</param>
    private sealed record TeeCenteredSet(
        Guid OwnerEdgeId, Guid OtherEdgeId, OccupiedSpan OwnerSpan, OccupiedSpan OtherSpan, IReadOnlyList<XYZ> GapOpenPoints);

    private readonly record struct EdgeFrame(EdgeAxis Axis, double LengthMm);

    /// <summary>엣지의 배치 축과 길이 — <c>CreateNetwork</c> 엣지 루프가 정하는 값과 같은 식이다(절점 부속 앵커 우선).</summary>
    private static EdgeFrame? FrameOf(
        PipeNetworkDefinition network, PipeEdgeDefinition edge, IReadOnlyDictionary<(Guid NodeId, Guid EdgeId), NodeLegAnchor> legAnchors)
    {
        var startXyz = ToXyz(edge.Start, edge.Elevation, network.ReferencePoint);
        var endXyz = ToXyz(edge.End, edge.Elevation, network.ReferencePoint);
        var startAnchor = legAnchors.TryGetValue((NodeIdAt(network, edge.Start), edge.Id), out var startLeg) ? startLeg.ConnectorOrigin : startXyz;
        var endAnchor = legAnchors.TryGetValue((NodeIdAt(network, edge.End), edge.Id), out var endLeg) ? endLeg.ConnectorOrigin : endXyz;
        var lengthMm = UC.FtToMm(startAnchor.DistanceTo(endAnchor));
        if (lengthMm <= 0) return null;
        return new EdgeFrame(new EdgeAxis(startAnchor, Direction(new XYZ(endAnchor.X - startAnchor.X, endAnchor.Y - startAnchor.Y, 0))), lengthMm);
    }

    /// <summary>A 구간과 T 절점 부속 커넥터 사이 틈이 0보다 크고 관 최소 조각보다 짧으면 구간을 엣지 끝까지 늘려 그 틈을 비워 둔다
    /// (docs/230 § E와 같은 규칙 — 세트를 통째로 건너뛰지 않는다). <paramref name="nodeAtStart"/> = T 절점이 이 엣지의 시작 쪽.</summary>
    private static OccupiedSpan CloseTinyNodeGap(OccupiedSpan span, double lengthMm, bool nodeAtStart, out double closedGapMm)
    {
        closedGapMm = 0;
        var gap = nodeAtStart ? span.StartMm : lengthMm - span.EndMm;
        if (gap <= 1e-6 || gap >= PipeSegmentPlan.MinSegmentMm) return span;
        closedGapMm = gap;
        return nodeAtStart ? new OccupiedSpan(0, span.EndMm) : new OccupiedSpan(span.StartMm, lengthMm);
    }

    /// <summary>중심이 본관 직선 T 절점에 놓인 세트를 찾아 두 엣지의 A 구간을 미리 정한다. 엣지 루프가 시작되기 전에 부른다 —
    /// 반대편 엣지의 구간·실패 여부를 그 엣지를 처리하기 전에 알아야 반쪽 세트가 남지 않는다(docs/273 § C-3·C-4).
    /// <para>구간이 엣지 밖으로 나가거나 T 몸통과 겹치면 세트 전체를 건너뛰기로 기록한다.</para></summary>
    private static Dictionary<Guid, TeeCenteredSet> PlanTeeCenteredSets(
        Document document, FamilySymbolCache symbols, PipeNetworkDefinition network, PipeSegmentFamilySelection families, SkipPlan skipPlan,
        IReadOnlyDictionary<(Guid NodeId, Guid EdgeId), NodeLegAnchor> legAnchors,
        IDictionary<string, string> unusableValves, ICollection<PipeConnectionFailure> skipped, ISet<Guid> droppedFittings,
        IDictionary<Guid, BypassLayout> layouts, IDictionary<BypassLayoutKey, BypassLayout> cache, ICollection<string> warnings,
        Dictionary<(ElementId SymbolId, double DiameterMm), InlineSpan> spanCache, IDictionary<Guid, OccupiedSpan> forcedValveSpans)
    {
        var result = new Dictionary<Guid, TeeCenteredSet>();
        const double nodeToleranceMm = 1.0;
        foreach (var fitting in network.InlineFittings.Where(x => x.IsBypassSet && x.EdgeId is not null))
        {
            var owner = network.Edges.FirstOrDefault(x => x.Id == fitting.EdgeId);
            if (owner is null) continue;
            var atEnd = owner.End.DistanceTo(fitting.Position) <= nodeToleranceMm;
            var atStart = !atEnd && owner.Start.DistanceTo(fitting.Position) <= nodeToleranceMm;
            if (!atEnd && !atStart) continue;
            var nodePosition = atEnd ? owner.End : owner.Start;
            var node = network.Nodes.FirstOrDefault(x => x.NodeKind == NodeKind.Tee && x.Position.DistanceTo(nodePosition) <= 0.01);
            if (node is null || PipeTeeResolver.Resolve(node, network.Edges) is not { } order) continue;
            if (owner.Id != order.MainEdge1Id && owner.Id != order.MainEdge2Id) continue;
            var other = network.Edges.First(x => x.Id == (owner.Id == order.MainEdge1Id ? order.MainEdge2Id : order.MainEdge1Id));
            if (FrameOf(network, owner, legAnchors) is not { } ownerFrame || FrameOf(network, other, legAnchors) is not { } otherFrame) continue;

            var layout = ResolveBypassLayout(document, symbols, owner, network, families, skipPlan, unusableValves, skipped, droppedFittings, cache, fitting);
            if (layout is null) continue;
            layouts[fitting.Id] = layout;

            var canvasPoint = ToXyz(fitting.Position, fitting.Elevation, network.ReferencePoint);
            var center = AxisDistanceMm(canvasPoint, ownerFrame.Axis);
            // T 절점이 엣지 끝이면 소속 엣지 쪽 A는 시작 방향(−1)이고 반대편 A는 +1이다. 시작이면 반대다.
            var ownerSign = atEnd ? -1 : 1;
            var otherNodeAtStart = other.Start.DistanceTo(nodePosition) <= 0.01;

            // docs/297 — 세트 안 본관 밸브 폭만큼 A 오프셋 다시 잡음. 차이는 V1–V2 단관이 흡수. 밸브 2개 세트만 자리 고정.
            var ownerForced = new List<(Guid Id, OccupiedSpan Span)>();
            var otherForced = new List<(Guid Id, OccupiedSpan Span)>();
            string? spoolWarning = null;
            if (legAnchors.ContainsKey((node.Id, owner.Id)) && legAnchors.ContainsKey((node.Id, other.Id)))
            {
                var ownerValves = InsideSetValves(document, symbols, network, families, spanCache, unusableValves, droppedFittings,
                    owner, nodePosition, layout);
                var otherValves = InsideSetValves(document, symbols, network, families, spanCache, unusableValves, droppedFittings,
                    other, nodePosition, layout);
                if (ownerValves.Count + otherValves.Count > 0)
                {
                    // T 절점 중심 → 그 엣지 T 커넥터(축 0 또는 길이).
                    var ownerLeg = atEnd ? center - ownerFrame.LengthMm : -center;
                    var nodeOnOther = AxisDistanceMm(canvasPoint, otherFrame.Axis);
                    var otherLeg = otherNodeAtStart ? -nodeOnOther : nodeOnOther - otherFrame.LengthMm;
                    var halves = new[]
                    {
                        ownerLeg + ownerValves.Sum(x => x.WidthMm) + layout.TeeMainHalfMm,
                        otherLeg + otherValves.Sum(x => x.WidthMm) + layout.TeeMainHalfMm,
                    };
                    if (layout.SecondValveSymbol is not null)
                    {
                        var solution = BypassSpoolSolver.Solve(new BypassSpoolInput(layout.TeeCenterOffsetMm, halves,
                            layout.BendValveLegMm, layout.BypassValveWidthMm + layout.SecondValveWidthMm));
                        if (solution.Adjusted)
                        {
                            // 경고는 세트가 살아남은 뒤에 — 건너뛴 세트에 "늘렸다"가 남지 않게.
                            spoolWarning = FormattableString.Invariant(
                                $"바이패스 세트: 본관 부품 폭에 맞춰 바이패스 밸브 사이 단관을 {layout.SpoolLengthMm:N0}→{solution.SpoolMm:N0}mm로 늘렸습니다.");
                            // 캐시는 사용자 단관 기준 그대로 — 보정값은 이 세트만.
                            layout = layout with { SpoolLengthMm = solution.SpoolMm, TeeCenterOffsetMm = solution.OffsetMm };
                            layouts[fitting.Id] = layout;
                        }
                        ownerForced = StackFromNode(ownerValves, ownerFrame.LengthMm, !atEnd);
                        otherForced = StackFromNode(otherValves, otherFrame.LengthMm, otherNodeAtStart);
                    }
                    else if (halves.Max() > layout.TeeCenterOffsetMm + ConnectionToleranceMm)
                        warnings.Add("바이패스 세트: 밸브 1개 세트는 단관 보정을 할 수 없습니다 — 본관 밸브·T형관이 밸브부관 A와 겹칠 수 있습니다. 밸브 2개 세트로 바꾸세요.");
                }
            }

            var ownerRaw = layout.TeeSpan(center, ownerSign);
            var otherCenter = AxisDistanceMm(ownerFrame.Axis.PointAt(center - ownerSign * layout.TeeCenterOffsetMm), otherFrame.Axis);
            var otherRaw = new OccupiedSpan(otherCenter - layout.TeeMainHalfMm, otherCenter + layout.TeeMainHalfMm);

            // 밸브 고정한 쪽은 A–T 사이를 밸브가 차지 — 틈 닫기 안 함.
            var ownerGap = 0.0;
            var otherGap = 0.0;
            var ownerSpan = ownerForced.Count > 0 ? ownerRaw : CloseTinyNodeGap(ownerRaw, ownerFrame.LengthMm, !atEnd, out ownerGap);
            var otherSpan = otherForced.Count > 0 ? otherRaw : CloseTinyNodeGap(otherRaw, otherFrame.LengthMm, otherNodeAtStart, out otherGap);

            var dropReason = DropReasonOf(fitting.Id, ownerSpan, ownerFrame.LengthMm) ?? DropReasonOf(fitting.Id, otherSpan, otherFrame.LengthMm);
            if (dropReason is not null)
            {
                skipped.Add(CreateFailure(canvasPoint, BypassSet.TypeKey, BypassValveFamilyOf(fitting.Bypass!.BypassValveFamilyTypeName),
                    0, $"T형관 중심 세트의 밸브부관 A를 놓을 수 없습니다. {dropReason}", network) with { FittingId = fitting.Id });
                droppedFittings.Add(fitting.Id);
                layouts.Remove(fitting.Id);
                continue;
            }

            var gapPoints = new List<XYZ>();
            foreach (var (gap, frame, atNodeStart) in new[] { (ownerGap, ownerFrame, !atEnd), (otherGap, otherFrame, otherNodeAtStart) })
            {
                if (gap <= 0) continue;
                gapPoints.Add(frame.Axis.PointAt(atNodeStart ? 0 : frame.LengthMm));
                if (gap > ConnectionToleranceMm)
                    warnings.Add(FormattableString.Invariant(
                        $"바이패스 세트: 밸브부관 A와 T형관 사이 틈이 {gap:N1}mm라 관을 넣지 않았습니다(관 최소 {PipeSegmentPlan.MinSegmentMm:N0}mm). ") +
                        "두 부재는 연결되지 않은 채로 남습니다.");
            }
            result[fitting.Id] = new TeeCenteredSet(owner.Id, other.Id, ownerSpan, otherSpan, gapPoints);
            foreach (var (id, span) in ownerForced.Concat(otherForced)) forcedValveSpans[id] = span;
            if (spoolWarning is not null) warnings.Add(spoolWarning);
        }
        return result;

        static string? DropReasonOf(Guid id, OccupiedSpan span, double lengthMm)
            => InlineSpanFilter.Filter(lengthMm, [new InlineSpanCandidate(id, span, InlineSpanFilter.BypassSetPriority)])
                .Dropped.TryGetValue(id, out var reason) ? reason : null;
    }

    /// <summary>T형관 중심 세트 안 본관 밸브(docs/297 § B-1). T 절점에서 캔버스 A 그림(<see cref="BypassSet.EstimatedHalfSpanMm"/>) 안쪽에 찍은
    /// 2포트 밸브, T에 가까운 순. 실측 실패는 <paramref name="unusableValves"/>에만 남김 — 건너뛰기 기록은 <see cref="InlineFittingSpans"/> 몫.</summary>
    private static List<(Guid Id, double WidthMm)> InsideSetValves(
        Document document, FamilySymbolCache symbols, PipeNetworkDefinition network, PipeSegmentFamilySelection families,
        Dictionary<(ElementId SymbolId, double DiameterMm), InlineSpan> spanCache, IDictionary<string, string> unusableValves,
        ISet<Guid> droppedFittings, PipeEdgeDefinition edge, DHBIMWATER.Core.Geometry.Point2D nodePosition, BypassLayout layout)
    {
        var reach = BypassSet.EstimatedHalfSpanMm(edge.DiameterMm, layout.BypassDiameterMm);
        var valves = new List<(Guid Id, double WidthMm, double DistanceMm)>();
        foreach (var fitting in network.InlineFittings.Where(x => x.EdgeId == edge.Id && !x.IsJoint && !x.IsBypassSet
                     && !string.IsNullOrWhiteSpace(x.FamilyTypeName) && !droppedFittings.Contains(x.Id)))
        {
            var distance = fitting.Position.DistanceTo(nodePosition);
            if (distance > reach || unusableValves.ContainsKey(fitting.FamilyTypeName)) continue;
            try
            {
                // InlineFittingSpans와 같은 키·같은 실측 — 거기서 다시 안 잼.
                var symbol = symbols.Resolve(fitting.FamilyTypeName, "배관부속");
                var cacheKey = (symbol.Id, edge.DiameterMm);
                if (!spanCache.TryGetValue(cacheKey, out var measured))
                    spanCache[cacheKey] = measured = InlineConnectorSpanMm(
                        document, symbol, ValveDiameterParameterNameFor(families, symbol), edge.DiameterMm, "배관부속");
                if (measured.WidthMm is { } width) valves.Add((fitting.Id, width, distance));
            }
            catch (Exception ex) { unusableValves[fitting.FamilyTypeName] = ex.Message; }
        }
        return valves.OrderBy(x => x.DistanceMm).Select(x => (x.Id, x.WidthMm)).ToList();
    }

    /// <summary>세트 안 밸브를 T 커넥터부터 바깥쪽으로 차례로 붙인 엣지 축 구간(docs/297 § B-4). <paramref name="nodeAtStart"/> = T가 축 0 쪽.</summary>
    private static List<(Guid Id, OccupiedSpan Span)> StackFromNode(
        IReadOnlyList<(Guid Id, double WidthMm)> valves, double lengthMm, bool nodeAtStart)
    {
        var spans = new List<(Guid Id, OccupiedSpan Span)>();
        var cursor = nodeAtStart ? 0 : lengthMm;
        foreach (var (id, width) in valves)
        {
            var next = nodeAtStart ? cursor + width : cursor - width;
            spans.Add((id, nodeAtStart ? new OccupiedSpan(cursor, next) : new OccupiedSpan(next, cursor)));
            cursor = next;
        }
        return spans;
    }

    /// <summary>엣지 위 바이패스 세트의 본관 점유 구간을 예약한다. 실측에 실패한 세트는 건너뛰기로 기록하고 예약하지 않는다 —
    /// 그러면 본관 직관이 그 자리를 연속으로 채운다(밸브 실측 실패와 같은 규칙, docs/145 § 4-5).
    /// <para><paramref name="teeCentered"/>에 든 세트는 소속 엣지 쪽 A 하나만 예약한다 — 반대편 A는 그 엣지가 따로 받는다(docs/273 § C-3).</para></summary>
    private static List<(Guid Id, OccupiedSpan Span)> BypassSetSpans(
        Document document, FamilySymbolCache symbols, PipeEdgeDefinition edge, PipeNetworkDefinition network,
        PipeSegmentFamilySelection families, SkipPlan skipPlan, XYZ startAnchor, XYZ direction,
        IDictionary<string, string> unusableValves, ICollection<PipeConnectionFailure> skipped,
        IDictionary<Guid, double> axisCenters, ISet<Guid> droppedFittings,
        IDictionary<Guid, BypassLayout> layouts, IDictionary<BypassLayoutKey, BypassLayout> cache, ICollection<string> warnings,
        IReadOnlyDictionary<Guid, TeeCenteredSet> teeCentered)
    {
        var axis = new EdgeAxis(startAnchor, direction);
        // 구간마다 세트 Id를 단다 — 위치 오류면 세트 통째로 건너뛴다(docs/251 § B).
        var spans = new List<(Guid Id, OccupiedSpan Span)>();
        foreach (var fitting in network.InlineFittings.Where(x => x.EdgeId == edge.Id && x.IsBypassSet && !droppedFittings.Contains(x.Id)))
        {
            var canvasPoint = ToXyz(fitting.Position, fitting.Elevation, network.ReferencePoint);
            var valveFamily = BypassValveFamilyOf(fitting.Bypass!.BypassValveFamilyTypeName);
            var layout = layouts.TryGetValue(fitting.Id, out var known)
                ? known
                : ResolveBypassLayout(document, symbols, edge, network, families, skipPlan, unusableValves, skipped, droppedFittings, cache, fitting);
            if (layout is null) continue;
            try
            {
                var center = AxisDistanceMm(canvasPoint, axis);
                axisCenters[fitting.Id] = center;
                layouts[fitting.Id] = layout;
                if (teeCentered.TryGetValue(fitting.Id, out var teeSet))
                {
                    spans.Add((fitting.Id, teeSet.OwnerSpan));
                    continue;
                }
                // A 두 구간만 예약한다 — 그 사이는 관 분할이 관·일반 밸브로 채운다(docs/230 § A).
                spans.Add((fitting.Id, layout.TeeSpan(center, -1)));
                spans.Add((fitting.Id, layout.TeeSpan(center, 1)));
                if (layout.HasTinyInnerGap && layout.InnerGapMm > 0)
                {
                    // 관 최소 조각보다 짧은 틈에는 관을 넣지 않는다(2026-09-28 사용자 결정, docs/230 § E) — 틈을 비워 두는 구간으로 잡아
                    // 관 분할이 "관 조각이 너무 짧다"로 모델 생성 전체를 멈추지 않게 하고, 1mm를 넘으면 경고만 남긴다.
                    spans.Add((fitting.Id, new OccupiedSpan(center - layout.InnerGapMm / 2, center + layout.InnerGapMm / 2)));
                    if (layout.InnerGapMm > ConnectionToleranceMm)
                        warnings.Add(FormattableString.Invariant(
                            $"바이패스 세트: 밸브부관 A 사이 틈이 {layout.InnerGapMm:N1}mm라 관을 넣지 않았습니다(관 최소 {PipeSegmentPlan.MinSegmentMm:N0}mm). ") +
                            "두 A는 연결되지 않은 채로 남습니다 — 밸브부관·바이패스 밸브 패밀리 치수를 확인하세요.");
                }
            }
            catch (Exception ex)
            {
                skipped.Add(CreateFailure(canvasPoint, BypassSet.TypeKey, valveFamily, 0,
                    $"세트 치수를 잴 수 없습니다: {ex.Message}", network) with { FittingId = fitting.Id });
                droppedFittings.Add(fitting.Id);
            }
        }
        return spans;
    }

    // ── § C-1 실측 ─────────────────────────────────────────────────────────────

    /// <summary>원점 프로브로 B·A·바이패스 밸브를 재서 세트 치수를 정한다.
    /// <para>바이패스 관경은 핸드북 규격표가 정하고(<see cref="BypassLayoutKey.BranchDiameterMm"/>, docs/256) B는 검증만 한다 —
    /// B의 <c>부관_구경</c>은 붙는 본관 구경을 고르는 조회 키라(docs/244 § A-2) 본관 구경을 넣고, 커넥터 지름이 표 값과 다르면
    /// 패밀리 조회표가 핸드북과 어긋난 것이므로 세트를 건너뛴다. 그다음 표 값으로 A 분기와 바이패스 밸브 유형을 정한다.</para>
    /// <para>A 중심 간격은 바이패스 쪽(바이패스 밸브 + B 밸브쪽 레그 ×2)이 정한다(docs/230 § A). 두 A가 겹치지 않도록
    /// A 주관 반폭보다는 작아지지 않는다. A–A 사이 본관은 관 분할이 채우고, 거기 놓인 일반 밸브가 너무 크면 PL-301로 막힌다.</para></summary>
    private static BypassLayout MeasureBypassLayout(
        Document document, FamilySymbolCache symbols, PipeSegmentFamilySelection families, BypassLayoutKey key)
    {
        var teeSymbol = symbols.Resolve(families.ValveTeeFamilyTypeName, PipeValveBend.PartNameA);
        var bendSymbol = symbols.Resolve(families.ValveBendFamilyTypeName, PipeValveBend.PartNameB);
        // 프로브 구동 경고 — 실측이 성공하면 실배치에서 다시 나오므로 버리고, 실패하면 원인 단서로 사유에 붙인다(docs/241).
        var probeWarnings = new List<string>();
        if (string.IsNullOrWhiteSpace(families.ValveTeeHorizontalBranchParameterName))
            probeWarnings.Add($"{PipeValveBend.PartNameA}의 수평분기 예/아니오 파라미터를 찾지 못해 값을 넣지 않았습니다(패밀리 기본값 그대로).");

        // 1) B — 본관 구경을 넣고 커넥터 지름이 핸드북 지관 관경과 같은지 본다(docs/256). 여기서 실패하면 A는 재지 않는다.
        BendGeometry bend;
        var bypassMm = key.BranchDiameterMm;
        var bendProbe = PlaceAt(document, bendSymbol, XYZ.Zero, 0, null);
        try
        {
            // 구동 전 상태를 떠 둔다 — 실패 시 "값을 넣은 뒤 커넥터가 사라짐/접힘"을 로그만으로 가른다(docs/241).
            document.Regenerate();
            var before = ProbeSnapshot(bendProbe);
            DriveValveBend(bendProbe, families, key.MainDiameterMm, probeWarnings);
            document.Regenerate();
            try
            {
                bend = MeasureBend(bendProbe);
                var measuredMm = ValveBendBranchDiameterMm(bendProbe, key.MainDiameterMm);
                if (Math.Abs(measuredMm - bypassMm) > 0.5)
                    throw new DhbimwaterException(ErrorCodes.PL_ConnectorSpanMismatch, FormattableString.Invariant(
                        $"{PipeValveBend.PartNameB}가 본관 {key.MainDiameterMm:0.#}mm에서 부관 {measuredMm:0.#}mm를 만들었지만 핸드북 규격은 {bypassMm:0.#}mm입니다. 패밀리의 '부관_구경' 조회표를 확인하세요."));
            }
            catch (Exception ex) { throw ProbeFailure(ex, bendProbe, PipeValveBend.PartNameB, families, key, null, probeWarnings, before); }
        }
        finally { document.Delete(bendProbe.Id); document.Regenerate(); }

        // 2) A — 주관 = 본관, 수평분기 = 핸드북 지관 관경(B 검증 통과값).
        TeeGeometry tee;
        var teeProbe = PlaceAt(document, teeSymbol, XYZ.Zero, 0, null);
        try
        {
            document.Regenerate();
            var before = ProbeSnapshot(teeProbe);
            DriveValveTee(teeProbe, families, key.MainDiameterMm, bypassMm, probeWarnings);
            document.Regenerate();
            try { tee = MeasureTee(teeProbe); }
            catch (Exception ex) { throw ProbeFailure(ex, teeProbe, PipeValveBend.PartNameA, families, key, bypassMm, probeWarnings, before); }
        }
        finally { document.Delete(teeProbe.Id); document.Regenerate(); }

        // 3) 바이패스 밸브 — 같은 DN 유형을 골라 폭을 잰다.
        var bypassValveSymbol = ResolveBypassValveType(symbols, key.BypassValveFamily, bypassMm, key.MainDiameterMm);
        var bypassWidth = InlineConnectorSpanMm(document, bypassValveSymbol, ValveDiameterParameterNameFor(families, bypassValveSymbol),
            bypassMm, "바이패스 밸브").WidthMm
            ?? throw new DhbimwaterException(ErrorCodes.PL_ConnectorCountMismatch, "바이패스 밸브는 2포트 인라인 패밀리여야 합니다.");

        // 3-2) 밸브 2개 세트의 V2 — V1과 같은 규칙(같은 DN 유형)으로 고르고 폭을 잰다(docs/255 § B-3).
        FamilySymbol? secondSymbol = null;
        var secondWidth = 0.0;
        if (!string.IsNullOrWhiteSpace(key.SecondValveFamily))
        {
            secondSymbol = ResolveBypassValveType(symbols, key.SecondValveFamily, bypassMm, key.MainDiameterMm);
            secondWidth = InlineConnectorSpanMm(document, secondSymbol, ValveDiameterParameterNameFor(families, secondSymbol),
                bypassMm, "바이패스 밸브 2").WidthMm
                ?? throw new DhbimwaterException(ErrorCodes.PL_ConnectorCountMismatch, "바이패스 밸브 2는 2포트 인라인 패밀리여야 합니다.");
        }

        var layout = new BypassLayout(0, tee.MainHalfMm, tee.BranchMm, bend.TeeLegMm, bend.ValveLegMm, bypassWidth, bypassMm, bypassValveSymbol,
            secondSymbol, secondWidth, secondSymbol is null ? 0 : key.SpoolLengthMm);
        // 1개 세트면 밸브 묶음 폭 = 밸브 폭이라 종전 식(bypassWidth / 2 + B 레그)과 같다.
        return layout with { TeeCenterOffsetMm = Math.Max(layout.ValveRunWidthMm / 2 + bend.ValveLegMm, tee.MainHalfMm) };
    }

    /// <summary>세트 스펙의 바이패스 밸브 값에서 패밀리명만 뽑는다. 새 세트는 패밀리명, 되가져온 세트는 "패밀리 : 유형"이다(docs/244 § A-4).</summary>
    private static string BypassValveFamilyOf(string? specValue)
    {
        if (string.IsNullOrWhiteSpace(specValue)) return string.Empty;
        var separator = specValue.LastIndexOf(" : ", StringComparison.Ordinal);
        return separator > 0 ? specValue[..separator] : specValue;
    }

    /// <summary>핸드북 밸브부관 규격이 없을 때의 건너뛰기 사유(docs/256 § C-1). PE·PVC는 관종 자체에 규격이 없다.</summary>
    private static string BypassSpecMissingReason(PipeNetworkDefinition network, double mainMm)
    {
        var label = DHBIMWATER.Core.Gis.PipeAlignmentParameters.Label(network.Material);
        if (!ValveBypassSpecTable.HasSpec(network.Material))
            return $"{label}은 핸드북 밸브부관 규격이 없어 바이패스 세트를 만들지 않았습니다.";
        var grade = network.Material == DHBIMWATER.Core.Piping.PipeMaterial.Steel
            ? $" {ValveBypassSpecTable.NormalizeSteelClass(network.SteelFittingClass)}" : "";
        return FormattableString.Invariant(
            $"핸드북 밸브부관 규격에 없는 주관입니다({label}{grade} DN{mainMm:0.#}, 규격 범위 {ValveBypassSpecTable.RangeText(network.Material, network.SteelFittingClass)}). 바이패스 세트를 만들지 않았습니다.");
    }

    /// <summary>바이패스 관경과 유형명 DN이 같은 바이패스 밸브 유형(docs/244 § A-4). 없으면 세트를 건너뛰도록 예외.</summary>
    private static FamilySymbol ResolveBypassValveType(FamilySymbolCache symbols, string familyName, double bypassMm, double mainMm)
        => symbols.FindByNominalDiameter(familyName, bypassMm)
           ?? throw new DhbimwaterException(ErrorCodes.PL_FamilyNotFound, FormattableString.Invariant(
               $"바이패스 밸브 '{familyName}'에 {bypassMm:0}A 유형이 없습니다. 본관 {mainMm:0.#}mm의 핸드북 바이패스 관경은 {bypassMm:0.#}mm입니다 — 그 규격의 유형을 프로젝트에 로드하세요."));

    /// <summary>밸브부관 B의 부관 관경 = 두 배관 커넥터 지름(docs/244 § A-2). 파라미터명 의존을 피하려고 커넥터를 읽는다(§ F-1).
    /// 소수 첫째 자리로 반올림해 ft↔mm 왕복 오차를 없앤다.</summary>
    private static double ValveBendBranchDiameterMm(FamilyInstance instance, double mainMm)
    {
        var diameters = GetMepConnectors(instance).Select(PipeMaterialConnectorReader.DiameterMm).ToList();
        if (diameters.Count == 0 || diameters.Any(x => x is null or <= 0))
            throw new DhbimwaterException(ErrorCodes.PL_ConnectorSpanMismatch, FormattableString.Invariant(
                $"{PipeValveBend.PartNameB}의 부관 관경을 읽을 수 없습니다(커넥터가 원형이 아니거나 지름이 0). '부관_구경'에 본관 {mainMm:0.#}mm를 넣었을 때 패밀리가 부관을 만드는지 확인하세요."));
        var min = diameters.Min(x => x!.Value);
        var max = diameters.Max(x => x!.Value);
        if (max - min > 0.5)
            throw new DhbimwaterException(ErrorCodes.PL_ConnectorSpanMismatch, FormattableString.Invariant(
                $"{PipeValveBend.PartNameB}의 두 커넥터 지름이 다릅니다({diameters[0]:0.#}/{diameters[^1]:0.#}mm)."));
        return Math.Round(diameters[0]!.Value, 1);
    }

    /// <summary>밸브부관 A 파라미터 구동 — 주관 = 본관, 분기 = 바이패스, 수평분기 예, 수직분기 아니오(docs/227 § D).</summary>
    private static void DriveValveTee(FamilyInstance instance, PipeSegmentFamilySelection families, double mainMm, double bypassMm, List<string> warnings)
    {
        SetOptionalDiameterParameter(instance, families.ValveTeeMainDiameterParameterName, mainMm, PipeValveBend.PartNameA, warnings);
        SetOptionalDiameterParameter(instance, families.ValveTeeBranchDiameterParameterName, bypassMm, $"{PipeValveBend.PartNameA} 분기", warnings);
        SetOptionalFlagParameter(instance, families.ValveTeeHorizontalBranchParameterName, true, PipeValveBend.PartNameA, warnings);
        SetOptionalFlagParameter(instance, families.ValveTeeVerticalBranchParameterName, false, PipeValveBend.PartNameA, warnings);
    }

    /// <summary>밸브부관 B 파라미터 구동 — <c>부관_구경</c>(본관 구경 키)에 본관 구경을 넣는다. 부관 관경은 패밀리가 조회표로 정한다(docs/244, docs/229 § A 번복).</summary>
    private static void DriveValveBend(FamilyInstance instance, PipeSegmentFamilySelection families, double mainMm, List<string> warnings)
        => SetOptionalDiameterParameter(instance, families.ValveBendDiameterParameterName, mainMm, PipeValveBend.PartNameB, warnings);

    /// <summary>A의 수평 커넥터 3개에서 주관(마주보는 쌍)·분기·중심을 구한다. 수직분기 커넥터는 뺀다.</summary>
    private static TeeGeometry MeasureTee(FamilyInstance instance)
    {
        var all = GetMepConnectors(instance);
        var horizontal = all.Where(x => !IsVerticalConnector(x)).ToList();
        if (horizontal.Count != 3)
            throw new DhbimwaterException(ErrorCodes.PL_ConnectorCountMismatch,
                $"{PipeValveBend.PartNameA} '{instance.Symbol.Family.Name} : {instance.Symbol.Name}'의 수평 배관 커넥터가 {horizontal.Count}개입니다(3개여야 합니다 — 주관 2 · 수평분기 1).");

        var pairs = new[] { (0, 1, 2), (0, 2, 1), (1, 2, 0) };
        var (a, b, branchIndex) = pairs.OrderBy(p => Flat(horizontal[p.Item1].CoordinateSystem.BasisZ)
            .DotProduct(Flat(horizontal[p.Item2].CoordinateSystem.BasisZ))).First();
        var main1 = horizontal[a].Origin;
        var main2 = horizontal[b].Origin;
        var branch = horizontal[branchIndex].Origin;
        var mainAxis = Flat(main2 - main1).Normalize();
        var center = main1 + mainAxis * Flat(branch - main1).DotProduct(mainAxis);
        var branchVector = Flat(branch - center);
        if (branchVector.GetLength() < 1e-9)
            throw new DhbimwaterException(ErrorCodes.PL_ConnectorSpanMismatch,
                $"{PipeValveBend.PartNameA}의 분기 커넥터가 주관 축 위에 있어 분기 방향을 알 수 없습니다.");
        var mainHalf = (UC.FtToMm(Flat(main1 - center).GetLength()) + UC.FtToMm(Flat(main2 - center).GetLength())) / 2;
        return new TeeGeometry(center, mainAxis, branchVector.Normalize(), mainHalf, UC.FtToMm(branchVector.GetLength()),
            main1, main2, branch, all.Where(IsVerticalConnector).Select(x => x.Origin).ToList());
    }

    /// <summary>B의 두 커넥터 축 교점(코너)과 레그 길이를 구한다. [0] = 긴 레그 = 밸브 쪽(docs/247).</summary>
    private static BendGeometry MeasureBend(FamilyInstance instance)
    {
        var connectors = PipeMaterialConnectorReader.ValveBendValveSideFirst(GetMepConnectors(instance));
        if (connectors.Count != 2)
            throw new DhbimwaterException(ErrorCodes.PL_ConnectorCountMismatch,
                $"{PipeValveBend.PartNameB} '{instance.Symbol.Family.Name} : {instance.Symbol.Name}'의 배관 커넥터가 {connectors.Count}개입니다(2개여야 합니다).");
        var p0 = connectors[0].Origin; var d0 = Flat(connectors[0].CoordinateSystem.BasisZ).Normalize();
        var p1 = connectors[1].Origin; var d1 = Flat(connectors[1].CoordinateSystem.BasisZ).Normalize();
        // p0 + t·d0 = p1 + s·d1 (평면). 커넥터 바깥 방향 두 축이 만나는 점이 코너다.
        var cross = d0.X * d1.Y - d0.Y * d1.X;
        if (Math.Abs(cross) < 1e-6)
            throw new DhbimwaterException(ErrorCodes.PL_ConnectorSpanMismatch,
                $"{PipeValveBend.PartNameB}의 두 커넥터가 평행해 코너를 구할 수 없습니다(90° 꺾인 패밀리여야 합니다).");
        var delta = p1 - p0;
        var t = (delta.X * d1.Y - delta.Y * d1.X) / cross;
        var corner = new XYZ(p0.X + d0.X * t, p0.Y + d0.Y * t, p0.Z);
        return new BendGeometry(corner, p0, p1, d0, d1,
            UC.FtToMm(Flat(p0 - corner).GetLength()), UC.FtToMm(Flat(p1 - corner).GetLength()));
    }

    /// <summary>프로브 실측 실패를 진단 정보와 함께 다시 던진다(docs/241). 안내 텍스트에는 원인 단서(수평분기 값·구동 경고)를,
    /// 로그에는 커넥터 전체(좌표·방향)와 구동 파라미터 값을 남긴다. 프로브는 원점에 놓이므로 좌표가 곧 패밀리 원점 기준이다.</summary>
    private static DhbimwaterException ProbeFailure(
        Exception ex, FamilyInstance probe, string role, PipeSegmentFamilySelection families, BypassLayoutKey key, double? bypassMm,
        IReadOnlyList<string> probeWarnings, ProbeState before)
    {
        var isTee = role == PipeValveBend.PartNameA;
        var after = ProbeSnapshot(probe);
        var hints = new List<string>();
        // 구동 전에는 있던 커넥터가 구동 뒤 줄었으면 패밀리가 이 구경에서 형상을 만들지 못한 것이다(docs/241 — D형 B 200mm 사례).
        // B는 본관 구경을, A는 B가 정한 바이패스 관경을 분기에 넣는다(docs/244 § A-6).
        var drivenText = isTee
            ? FormattableString.Invariant($"수평분기_구경 {bypassMm ?? 0:0.#}mm")
            : FormattableString.Invariant($"부관_구경(본관) {key.MainDiameterMm:0.#}mm");
        if (after.PipingEndCount < before.PipingEndCount)
            hints.Add(
                $"파라미터를 넣기 전에는 배관 커넥터가 {before.PipingEndCount}개였으나 넣은 뒤 {after.PipingEndCount}개입니다 — 패밀리가 {drivenText}에서 형상을 만들지 못하는지 확인하세요");
        if (isTee)
        {
            var horizontal = DescribeParameter(probe, families.ValveTeeHorizontalBranchParameterName);
            hints.Add($"현재 수평분기 = {horizontal}");
            if (ex.Message.Contains("주관 축 위", StringComparison.Ordinal))
                hints.Add($"{drivenText}에서 분기 커넥터가 주관 옆(측면)으로 나오는지 패밀리를 확인하세요");
        }
        hints.AddRange(probeWarnings);

        var log = new System.Text.StringBuilder();
        log.AppendLine($"BYPASS_DIAG {role} '{probe.Symbol.Family.Name} : {probe.Symbol.Name}' 실측 실패 — {ex.Message}");
        var bypassText = bypassMm is { } b ? FormattableString.Invariant($"{b:0.#}mm") : "미결정";
        log.AppendLine(FormattableString.Invariant(
            $"세트: 바이패스 밸브 패밀리 '{key.BypassValveFamily}', 본관 {key.MainDiameterMm:0.#}mm, 바이패스 {bypassText}"));
        if (isTee)
        {
            log.AppendLine($"주관 직경 '{families.ValveTeeMainDiameterParameterName}' = {DescribeParameter(probe, families.ValveTeeMainDiameterParameterName)}");
            log.AppendLine($"분기 직경 '{families.ValveTeeBranchDiameterParameterName}' = {DescribeParameter(probe, families.ValveTeeBranchDiameterParameterName)}");
            log.AppendLine($"수평분기 '{families.ValveTeeHorizontalBranchParameterName}' = {DescribeParameter(probe, families.ValveTeeHorizontalBranchParameterName)}");
            log.AppendLine($"수직분기 '{families.ValveTeeVerticalBranchParameterName}' = {DescribeParameter(probe, families.ValveTeeVerticalBranchParameterName)}");
        }
        else
        {
            log.AppendLine($"직경 '{families.ValveBendDiameterParameterName}' = {DescribeParameter(probe, families.ValveBendDiameterParameterName)}");
        }
        // 구동 전 → 구동 후. 파라미터는 되가져오기 진단(RevitValveRoomPipingPickRepo.LogValveTeeDiagnostics)과 같은 필터다.
        log.AppendLine("[구동 전 — 패밀리 기본값]");
        foreach (var line in before.Lines) log.AppendLine(line);
        log.AppendLine("[구동 후]");
        foreach (var line in after.Lines) log.AppendLine(line);
        foreach (var warning in probeWarnings) log.AppendLine($"구동 경고: {warning}");
        LogManager.Logger.Warn(log.ToString().TrimEnd());

        var code = ex is DhbimwaterException dh ? dh.Code : ErrorCodes.PL_ConnectorSpanMismatch;
        var message = ex.Message.TrimEnd('.', ' ') + ". " + string.Join(" / ", hints)
            + $". 커넥터 좌표 등 상세는 로그({LogManager.LogFilePath})의 BYPASS_DIAG 항목을 보세요.";
        return new DhbimwaterException(code, message, ex);
    }

    /// <summary>프로브 한 시점의 진단 상태. <paramref name="PipingEndCount"/>는 실측이 쓰는 배관 End 커넥터 수다.</summary>
    private sealed record ProbeState(int PipingEndCount, IReadOnlyList<string> Lines);

    /// <summary>프로브의 분기·구경·주관·부관 파라미터와 커넥터 전체를 뜬다(docs/241). 커넥터는 도메인·종류를 가리지 않고 모두 적되,
    /// 실측에서 빠지는 것(배관 아님·End 아님)은 표시한다 — "커넥터 0개"가 사라진 것인지 종류가 다른 것인지 가르기 위해서다.</summary>
    private static ProbeState ProbeSnapshot(FamilyInstance probe)
    {
        var lines = new List<string>();
        try
        {
            foreach (var parameter in probe.Parameters.Cast<Parameter>()
                         .Where(x => x.Definition?.Name is { } name
                                     && (name.Contains("분기") || name.Contains("구경") || name.Contains("주관") || name.Contains("부관")))
                         .OrderBy(x => x.Definition.Name))
                lines.Add($"인스턴스 '{parameter.Definition.Name}' = {DescribeParameter(probe, parameter.Definition.Name)}");

            var all = probe.MEPModel?.ConnectorManager?.Connectors.Cast<Connector>().OrderBy(x => x.Id).ToList() ?? [];
            var pipingEnd = all.Count(x => x.Domain == Domain.DomainPiping && x.ConnectorType == ConnectorType.End);
            lines.Add(probe.MEPModel?.ConnectorManager is null
                ? "커넥터 관리자 없음(MEPModel 없음)"
                : $"커넥터 {all.Count}개 · 그중 배관 End {pipingEnd}개(원점 기준 mm, 바깥 방향):");
            foreach (var connector in all)
            {
                var isPipingEnd = connector.Domain == Domain.DomainPiping && connector.ConnectorType == ConnectorType.End;
                var text = $"  #{connector.Id}";
                if (connector.ConnectorType is ConnectorType.End or ConnectorType.Curve or ConnectorType.Physical)
                {
                    var o = connector.Origin;
                    var d = connector.CoordinateSystem.BasisZ;
                    text += FormattableString.Invariant(
                        $" 위치({UC.FtToMm(o.X):0.#}, {UC.FtToMm(o.Y):0.#}, {UC.FtToMm(o.Z):0.#}) 방향({d.X:0.###}, {d.Y:0.###}, {d.Z:0.###})");
                    if (isPipingEnd && IsVerticalConnector(connector)) text += " [수직]";
                }
                if (!isPipingEnd) text += $" [실측 제외: {connector.Domain}/{connector.ConnectorType}]";
                lines.Add(text);
            }
            return new ProbeState(pipingEnd, lines);
        }
        catch (Exception ex)
        {
            // 진단은 부가 기능이다 — 뜨다 실패해도 배치·원래 실측 오류를 막지 않는다. 개수 -1 = 모름(커넥터 감소 힌트를 내지 않는다).
            lines.Add($"(진단 수집 실패: {ex.Message})");
            return new ProbeState(-1, lines);
        }
    }

    /// <summary>진단용 파라미터 값 문자열. 이름이 비었거나 없는 파라미터·유형 파라미터를 구분해 알려 준다.</summary>
    private static string DescribeParameter(FamilyInstance instance, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "(파라미터 미지정)";
        var parameter = instance.LookupParameter(name);
        if (parameter is null)
            return instance.Symbol.LookupParameter(name) is null ? "(파라미터 없음)" : "(유형 파라미터 — 인스턴스에서 바꿀 수 없음)";
        var value = parameter.StorageType == StorageType.Integer && parameter.Definition.GetDataType() == SpecTypeId.Boolean.YesNo
            ? (parameter.AsInteger() == 1 ? "예" : "아니오")
            : parameter.AsValueString() ?? parameter.AsString() ?? "(값 없음)";
        return parameter.IsReadOnly ? $"{value} (읽기 전용)" : value;
    }

    // ── § C-3 배치 ─────────────────────────────────────────────────────────────

    /// <summary>세트 하나를 놓는다. 도중에 실패하면 이 세트에서 만든 부재를 모두 지우고 건너뛰기로 기록한다 —
    /// 반쯤 놓인 세트가 커밋되면 연결 실패가 줄줄이 뜨고 재료표도 틀어진다.</summary>
    private static void PlaceBypassSet(
        Document document, FamilySymbolCache symbols, PipeNetworkDefinition network, PipeSegmentFamilySelection families,
        PipeEdgeDefinition edge, FittingPlacementDefinition fitting, EdgeAxis axis, double centerMm, BypassLayout layout,
        FamilySymbol segment, Level? hostLevel,
        ICollection<PlacedPart> placedInstances, ICollection<MaterialPart> materialParts, ICollection<XYZ> openPoints,
        ICollection<PipeConnectionFailure> skipped, List<string> warnings, ICollection<double> elevationFixes,
        Action<Guid, Element?> track, IReadOnlyList<XYZ>? gapOpenPoints = null)
    {
        var spec = fitting.Bypass!;
        var mainMm = edge.DiameterMm;
        // 바이패스 관경·밸브 유형은 핸드북 규격표 값(docs/256)과 그에 맞춰 고른 유형이다 — 캔버스 스펙 값은 쓰지 않는다(docs/244 § A-5).
        var bypassMm = layout.BypassDiameterMm;
        var bypassValveSymbol = layout.BypassValveSymbol;
        var u = axis.Direction;
        var n = new XYZ(-u.Y, u.X, 0) * spec.NormalizedSide;
        var center = axis.PointAt(centerMm);

        var created = new List<FamilyInstance>();
        var parts = new List<PlacedPart>();
        var materials = new List<MaterialPart>();
        var opens = new List<XYZ>();
        try
        {
            var teeSymbol = symbols.Resolve(families.ValveTeeFamilyTypeName, PipeValveBend.PartNameA);
            var bendSymbol = symbols.Resolve(families.ValveBendFamilyTypeName, PipeValveBend.PartNameB);

            // 1) 밸브부관 A 두 개 — 세트 중심 ∓ d. [0]이 엣지 시작 쪽이다.
            var tees = new List<TeeGeometry>();
            foreach (var sign in new[] { -1, 1 })
            {
                var target = center + u * (sign * UC.MmToFt(layout.TeeCenterOffsetMm));
                var tee = PlaceAt(document, teeSymbol, target, 0, hostLevel, elevationFixes);
                created.Add(tee);
                DriveValveTee(tee, families, mainMm, bypassMm, warnings);
                document.Regenerate();
                var geometry = MeasureTee(tee);
                RotateAboutZ(document, tee, geometry.Center, SignedAngle(geometry.BranchDirection, n));
                geometry = MeasureTee(tee);
                MoveBy(document, tee, target - geometry.Center);
                geometry = MeasureTee(tee);
                if (Math.Abs(geometry.MainAxis.DotProduct(u)) < 0.999 || geometry.BranchDirection.DotProduct(n) < 0.999)
                    throw new DhbimwaterException(ErrorCodes.PL_ConnectorSpanMismatch,
                        $"{PipeValveBend.PartNameA}의 주관·분기 방향을 본관·바이패스 방향에 맞추지 못했습니다.");
                tees.Add(geometry);
                opens.AddRange(geometry.Verticals);   // 수직분기 커넥터는 쓰지 않는다 — 연결 실패로 보고하지 않게 연다.
                // 틈이 너무 짧아 관을 넣지 않은 경우(docs/230 § E) — 경고는 예약 단계에서 이미 남겼다. 연결 실패로 또 보고하지 않게 연다.
                if (layout.HasTinyInnerGap && layout.InnerGapMm > ConnectionToleranceMm)
                    opens.Add(Nearest(new[] { geometry.Main1, geometry.Main2 }, center));
                parts.Add(new PlacedPart(tee, PipeValveBend.PartNameA));
                materials.Add(new MaterialPart(tee, FittingNameOf(tee), PipeMaterialSpecText.ValveBranch(mainMm, bypassMm), mainMm,
                    BranchDiameterMm: bypassMm, ValveFitting: ValveFittingKind.Tee));
            }

            // A–A 사이 본관은 세트가 아니다 — 관 분할(2단계)이 이미 관·일반 밸브를 놓았다(docs/230 § A).

            // 2) 밸브부관 B 두 개 — A쪽 커넥터를 A 분기 커넥터에 맞붙이고, 밸브쪽 커넥터가 세트 중심을 향하게 한다.
            var bendValveEnds = new List<XYZ>();
            for (var i = 0; i < tees.Count; i++)
            {
                var towardCenter = i == 0 ? u : -u;
                var bend = PlaceAt(document, bendSymbol, tees[i].Branch, 0, hostLevel, elevationFixes);
                created.Add(bend);
                DriveValveBend(bend, families, mainMm, warnings);
                document.Regenerate();
                // 프로브와 실배치가 다를 이유는 없지만, 다르면 A 분기·밸브·채움관이 틀어지므로 세트를 되돌린다(docs/244 § A-5).
                var placedBypassMm = ValveBendBranchDiameterMm(bend, mainMm);
                if (Math.Abs(placedBypassMm - bypassMm) > 0.5)
                    throw new DhbimwaterException(ErrorCodes.PL_ConnectorSpanMismatch, FormattableString.Invariant(
                        $"{PipeValveBend.PartNameB}의 부관 관경이 실측({bypassMm:0.#}mm)과 실배치({placedBypassMm:0.#}mm)에서 다릅니다."));
                var geometry = MeasureBend(bend);
                RotateAboutZ(document, bend, geometry.Corner, SignedAngle(geometry.TeeDirection, -n));
                geometry = MeasureBend(bend);
                if (geometry.ValveDirection.DotProduct(towardCenter) < 0)
                {
                    // 비대칭 90° 부속은 Z 회전만으로 밸브 쪽을 바꿀 수 없다 — A쪽 레그 축으로 180° 돌려 거울상을 만든다(docs/225 § D와 같은 방식).
                    ElementTransformUtils.RotateElement(document, bend.Id, Line.CreateBound(geometry.Corner, geometry.Corner + n), Math.PI);
                    document.Regenerate();
                    geometry = MeasureBend(bend);
                }
                MoveBy(document, bend, tees[i].Branch - geometry.TeeSide);
                geometry = MeasureBend(bend);
                if (geometry.TeeDirection.DotProduct(-n) < 0.999 || geometry.ValveDirection.DotProduct(towardCenter) < 0.999)
                    throw new DhbimwaterException(ErrorCodes.PL_ConnectorSpanMismatch,
                        $"{PipeValveBend.PartNameB}의 방향을 바이패스 경로에 맞추지 못했습니다. 90° 2포트 패밀리인지 확인하세요.");
                bendValveEnds.Add(geometry.ValveSide);
                parts.Add(new PlacedPart(bend, PipeValveBend.PartNameB));
                materials.Add(new MaterialPart(bend, FittingNameOf(bend), PipeMaterialSpecText.ValveBranch(mainMm, bypassMm), mainMm,
                    BranchDiameterMm: bypassMm, ValveFitting: ValveFittingKind.Bend));
            }

            // 3) 바이패스 밸브 — 두 B 밸브쪽 커넥터의 중점. 틈은 바이패스 관경 단관으로 채운다.
            var bypassCenter = (bendValveEnds[0] + bendValveEnds[1]) / 2;
            if (layout.SecondValveSymbol is not { } secondValveSymbol)
            {
                var (bypassValve, bypassValveEnds) = PlaceSetValve(document, bypassValveSymbol, families, bypassCenter, u, bypassMm, "바이패스 밸브",
                    hostLevel, warnings, elevationFixes);
                created.Add(bypassValve);
                parts.Add(new PlacedPart(bypassValve, "배관부속"));
                materials.Add(new MaterialPart(bypassValve, FittingNameOf(bypassValve), ValvePipingParameters.DiameterText(bypassMm), bypassMm));
                foreach (var end in bendValveEnds)
                    PlaceSetFiller(document, segment, families, end, Nearest(bypassValveEnds, end), bypassMm,
                        hostLevel, elevationFixes, created, parts, materials);
            }
            else
            {
                // 밸브 2개(docs/255 § B-5) — B ─ 채움관 ─ V1 ─ 단관 ─ V2 ─ 채움관 ─ B. 묶음 폭 W를 두 B 중점에 맞춘다.
                // bendValveEnds[0]은 엣지 시작 쪽 A(sign −1)에서 나왔으므로 V1을 −u 쪽에 두면 V1 = 엣지 시작 쪽이다.
                var halfRun = layout.ValveRunWidthMm / 2;
                var v1Center = bypassCenter + u * UC.MmToFt(-halfRun + layout.BypassValveWidthMm / 2);
                var v2Center = bypassCenter + u * UC.MmToFt(halfRun - layout.SecondValveWidthMm / 2);
                var (valve1, valve1Ends) = PlaceSetValve(document, bypassValveSymbol, families, v1Center, u, bypassMm, "바이패스 밸브",
                    hostLevel, warnings, elevationFixes);
                created.Add(valve1);
                var (valve2, valve2Ends) = PlaceSetValve(document, secondValveSymbol, families, v2Center, u, bypassMm, "바이패스 밸브 2",
                    hostLevel, warnings, elevationFixes);
                created.Add(valve2);
                foreach (var valve in new[] { valve1, valve2 })
                {
                    parts.Add(new PlacedPart(valve, "배관부속"));
                    materials.Add(new MaterialPart(valve, FittingNameOf(valve), ValvePipingParameters.DiameterText(bypassMm), bypassMm));
                }
                PlaceSetFiller(document, segment, families, bendValveEnds[0], Nearest(valve1Ends, bendValveEnds[0]), bypassMm,
                    hostLevel, elevationFixes, created, parts, materials);
                // 단관 0(직결)이면 PlaceSetFiller가 접합 허용치 이하로 보고 놓지 않는다.
                PlaceSetFiller(document, segment, families, Nearest(valve1Ends, v2Center), Nearest(valve2Ends, v1Center), bypassMm,
                    hostLevel, elevationFixes, created, parts, materials);
                PlaceSetFiller(document, segment, families, Nearest(valve2Ends, bendValveEnds[1]), bendValveEnds[1], bypassMm,
                    hostLevel, elevationFixes, created, parts, materials);
            }
        }
        catch (Exception ex)
        {
            foreach (var instance in created.Where(x => x.IsValidObject))
            {
                try { document.Delete(instance.Id); }
                catch (Exception deleteEx) { warnings.Add($"바이패스 세트 부재(ElementId {instance.Id.Value})를 지우지 못했습니다: {deleteEx.Message}"); }
            }
            document.Regenerate();
            // 본관에 예약한 A 두 구간의 양 끝 — 관 끝이 서로 물리지 않게 열어 둔다(밸브 건너뛰기와 같은 규칙, docs/145 § 4-6).
            foreach (var sign in new[] { -1, 1 })
            {
                var span = layout.TeeSpan(centerMm, sign);
                openPoints.Add(axis.PointAt(span.StartMm));
                openPoints.Add(axis.PointAt(span.EndMm));
            }
            // tee 중심 세트: A와 T형관 사이에 단관을 넣지 않은 자리도 열어 둔다(docs/273 § C-3b).
            if (gapOpenPoints is not null) foreach (var point in gapOpenPoints) openPoints.Add(point);
            var failedName = $"{bypassValveSymbol.FamilyName} : {bypassValveSymbol.Name}";
            if (layout.SecondValveSymbol is { } failedSecond) failedName += $" / {failedSecond.FamilyName} : {failedSecond.Name}";
            skipped.Add(CreateFailure(center, BypassSet.TypeKey, failedName, 0, ex.Message, network) with { FittingId = fitting.Id });
            return;
        }

        foreach (var part in parts) placedInstances.Add(part);
        foreach (var material in materials) materialParts.Add(material);
        foreach (var point in opens) openPoints.Add(point);
        if (gapOpenPoints is not null) foreach (var point in gapOpenPoints) openPoints.Add(point);
        // 세트 부재 전부를 캔버스 세트 하나에 매단다 — [Revit에서 보기]가 세트를 통째로 고른다(docs/179 § D).
        foreach (var instance in created) track(fitting.Id, instance);
    }

    /// <summary>2포트 밸브를 축 방향으로 놓고 커넥터 중앙을 <paramref name="point"/>에 맞춘다. 두 커넥터 원점을 돌려준다.</summary>
    private static (FamilyInstance Instance, XYZ[] Ends) PlaceSetValve(
        Document document, FamilySymbol symbol, PipeSegmentFamilySelection families, XYZ point, XYZ axis, double diameterMm, string role,
        Level? hostLevel, List<string> warnings, ICollection<double> elevationFixes)
    {
        var instance = PlaceAt(document, symbol, point, Math.Atan2(axis.Y, axis.X), hostLevel, elevationFixes, regenerateAfterRotate: false);
        SetOptionalDiameterParameter(instance, ValveDiameterParameterNameFor(families, symbol), diameterMm, role, warnings);
        document.Regenerate();
        var connectors = GetMepConnectors(instance);
        if (connectors.Count != 2)
            throw new DhbimwaterException(ErrorCodes.PL_ConnectorCountMismatch,
                $"{role} '{symbol.Family.Name} : {symbol.Name}'의 배관 커넥터가 {connectors.Count}개입니다(2포트 인라인 밸브여야 합니다).");
        RecenterOnConnectors(document, instance, point);
        return (instance, GetMepConnectors(instance).Select(x => x.Origin).ToArray());
    }

    /// <summary>두 커넥터 사이 틈을 관 패밀리로 채운다. 틈이 접합 허용치 이하면 놓지 않는다(맞닿은 것으로 본다).</summary>
    private static void PlaceSetFiller(
        Document document, FamilySymbol segment, PipeSegmentFamilySelection families, XYZ from, XYZ to, double diameterMm,
        Level? hostLevel, ICollection<double> elevationFixes,
        ICollection<FamilyInstance> created, ICollection<PlacedPart> parts, ICollection<MaterialPart> materials)
    {
        var lengthMm = UC.FtToMm(from.DistanceTo(to));
        if (lengthMm <= ConnectionToleranceMm) return;
        var center = (from + to) / 2;
        var direction = to - from;
        var instance = PlaceAt(document, segment, center, Math.Atan2(direction.Y, direction.X), hostLevel, elevationFixes, regenerateAfterRotate: false);
        created.Add(instance);
        if (!string.IsNullOrWhiteSpace(families.DiameterParameterName))
            SetDiameterParameterMm(instance, families.DiameterParameterName, diameterMm, "관 직경");
        SetDoubleParameterMm(instance, families.LengthParameterName, lengthMm, "관 길이");
        document.Regenerate();
        RecenterOnConnectors(document, instance, center);
        var role = PipeMaterialSpecText.SegmentName(lengthMm, families.StraightLengthMm);
        ValidateCenteredSpan(instance, center, lengthMm, role);
        parts.Add(new PlacedPart(instance, role));
        materials.Add(new MaterialPart(instance, role, SegmentSpecText(diameterMm, lengthMm), diameterMm, LengthMm: lengthMm));
    }

    private static void RotateAboutZ(Document document, FamilyInstance instance, XYZ pivot, double angle)
    {
        if (Math.Abs(angle) <= 1e-9) return;
        ElementTransformUtils.RotateElement(document, instance.Id, Line.CreateBound(pivot, pivot + XYZ.BasisZ), angle);
        document.Regenerate();
    }

    private static void MoveBy(Document document, FamilyInstance instance, XYZ offset)
    {
        if (offset.GetLength() <= UC.MmToFt(ConnectionToleranceMm) / 10) return;
        ElementTransformUtils.MoveElement(document, instance.Id, offset);
        document.Regenerate();
    }

    /// <summary>평면에서 <paramref name="from"/>을 <paramref name="to"/>로 돌리는 부호 있는 각(라디안, 반시계 +).</summary>
    private static double SignedAngle(XYZ from, XYZ to)
        => Math.Atan2(from.X * to.Y - from.Y * to.X, from.X * to.X + from.Y * to.Y);

    private static XYZ Flat(XYZ vector) => new(vector.X, vector.Y, 0);

    private static XYZ Nearest(IEnumerable<XYZ> points, XYZ target) => points.OrderBy(x => x.DistanceTo(target)).First();
}
