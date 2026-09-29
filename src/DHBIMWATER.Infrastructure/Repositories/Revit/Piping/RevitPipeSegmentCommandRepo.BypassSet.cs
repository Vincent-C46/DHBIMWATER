using Autodesk.Revit.DB;
using DHBIMWATER.Core.Piping;
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
    /// <summary>실측 캐시 키. 같은 밸브·관경 조합이면 세트 치수가 같다.</summary>
    private readonly record struct BypassLayoutKey(string BypassValve, double MainDiameterMm, double BypassDiameterMm);

    /// <summary>프로브로 잰 세트 치수(mm).</summary>
    /// <param name="TeeCenterOffsetMm">세트 중심 → 밸브부관 A 중심(주관 축 위) 거리 d.</param>
    /// <param name="TeeMainHalfMm">A 중심 → 주관 커넥터 거리.</param>
    /// <param name="TeeBranchMm">A 중심 → 수평분기 커넥터 거리.</param>
    /// <param name="BendTeeLegMm">B 코너 → A쪽 커넥터(Id 큰 쪽) 거리.</param>
    /// <param name="BendValveLegMm">B 코너 → 밸브쪽 커넥터(Id 작은 쪽) 거리.</param>
    private sealed record BypassLayout(
        double TeeCenterOffsetMm, double TeeMainHalfMm, double TeeBranchMm,
        double BendTeeLegMm, double BendValveLegMm, double BypassValveWidthMm)
    {
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

    /// <summary>밸브부관 B 실측 결과(월드 좌표). [0] = 밸브 쪽(최소 Id), [1] = A 쪽.</summary>
    private sealed record BendGeometry(XYZ Corner, XYZ ValveSide, XYZ TeeSide, XYZ ValveDirection, XYZ TeeDirection,
        double ValveLegMm, double TeeLegMm);

    // ── § C-2 예약 ─────────────────────────────────────────────────────────────

    /// <summary>엣지 위 바이패스 세트의 본관 점유 구간을 예약한다. 실측에 실패한 세트는 건너뛰기로 기록하고 예약하지 않는다 —
    /// 그러면 본관 직관이 그 자리를 연속으로 채운다(밸브 실측 실패와 같은 규칙, docs/145 § 4-5).</summary>
    private static List<OccupiedSpan> BypassSetSpans(
        Document document, FamilySymbolCache symbols, PipeEdgeDefinition edge, PipeNetworkDefinition network,
        PipeSegmentFamilySelection families, SkipPlan skipPlan, XYZ startAnchor, XYZ direction,
        IDictionary<string, string> unusableValves, ICollection<PipeConnectionFailure> skipped,
        IDictionary<Guid, double> axisCenters, ISet<Guid> droppedFittings,
        IDictionary<Guid, BypassLayout> layouts, IDictionary<BypassLayoutKey, BypassLayout> cache, ICollection<string> warnings)
    {
        var axis = new EdgeAxis(startAnchor, direction);
        var spans = new List<OccupiedSpan>();
        foreach (var fitting in network.InlineFittings.Where(x => x.EdgeId == edge.Id && x.IsBypassSet))
        {
            var spec = fitting.Bypass!;
            var canvasPoint = ToXyz(fitting.Position, fitting.Elevation, network.ReferencePoint);
            var reason = skipPlan.BypassSetReason
                ?? (string.IsNullOrWhiteSpace(spec.BypassValveFamilyTypeName) ? "바이패스 밸브가 선택되지 않았습니다." : null)
                ?? (unusableValves.TryGetValue(spec.BypassValveFamilyTypeName, out var bypassReason) ? bypassReason : null);
            if (reason is not null)
            {
                skipped.Add(CreateFailure(canvasPoint, BypassSet.TypeKey, spec.BypassValveFamilyTypeName, 0, reason, network));
                droppedFittings.Add(fitting.Id);
                continue;
            }
            try
            {
                var key = new BypassLayoutKey(spec.BypassValveFamilyTypeName, edge.DiameterMm, spec.BypassDiameterMm);
                if (!cache.TryGetValue(key, out var layout))
                    cache[key] = layout = MeasureBypassLayout(document, symbols, families, key);
                var center = AxisDistanceMm(canvasPoint, axis);
                axisCenters[fitting.Id] = center;
                layouts[fitting.Id] = layout;
                // A 두 구간만 예약한다 — 그 사이는 관 분할이 관·일반 밸브로 채운다(docs/230 § A).
                spans.Add(layout.TeeSpan(center, -1));
                spans.Add(layout.TeeSpan(center, 1));
                if (layout.HasTinyInnerGap && layout.InnerGapMm > 0)
                {
                    // 관 최소 조각보다 짧은 틈에는 관을 넣지 않는다(2026-09-28 사용자 결정, docs/230 § E) — 틈을 비워 두는 구간으로 잡아
                    // 관 분할이 "관 조각이 너무 짧다"로 모델 생성 전체를 멈추지 않게 하고, 1mm를 넘으면 경고만 남긴다.
                    spans.Add(new OccupiedSpan(center - layout.InnerGapMm / 2, center + layout.InnerGapMm / 2));
                    if (layout.InnerGapMm > ConnectionToleranceMm)
                        warnings.Add(FormattableString.Invariant(
                            $"바이패스 세트: 밸브부관 A 사이 틈이 {layout.InnerGapMm:N1}mm라 관을 넣지 않았습니다(관 최소 {PipeSegmentPlan.MinSegmentMm:N0}mm). ") +
                            "두 A는 연결되지 않은 채로 남습니다 — 밸브부관·바이패스 밸브 패밀리 치수를 확인하세요.");
                }
            }
            catch (Exception ex)
            {
                skipped.Add(CreateFailure(canvasPoint, BypassSet.TypeKey, spec.BypassValveFamilyTypeName, 0,
                    $"세트 치수를 잴 수 없습니다: {ex.Message}", network));
                droppedFittings.Add(fitting.Id);
            }
        }
        return spans;
    }

    // ── § C-1 실측 ─────────────────────────────────────────────────────────────

    /// <summary>원점 프로브로 A·B·바이패스 밸브를 재서 세트 치수를 정한다.
    /// <para>A 중심 간격은 바이패스 쪽(바이패스 밸브 + B 밸브쪽 레그 ×2)이 정한다(docs/230 § A). 두 A가 겹치지 않도록
    /// A 주관 반폭보다는 작아지지 않는다. A–A 사이 본관은 관 분할이 채우고, 거기 놓인 일반 밸브가 너무 크면 PL-301로 막힌다.</para></summary>
    private static BypassLayout MeasureBypassLayout(
        Document document, FamilySymbolCache symbols, PipeSegmentFamilySelection families, BypassLayoutKey key)
    {
        var teeSymbol = symbols.Resolve(families.ValveTeeFamilyTypeName, PipeValveBend.PartNameA);
        var bendSymbol = symbols.Resolve(families.ValveBendFamilyTypeName, PipeValveBend.PartNameB);
        var bypassValveSymbol = symbols.Resolve(key.BypassValve, "바이패스 밸브");
        var ignored = new List<string>();   // 프로브 구동 경고는 실배치에서 다시 나오므로 여기서는 버린다.

        TeeGeometry tee;
        var teeProbe = PlaceAt(document, teeSymbol, XYZ.Zero, 0, null);
        try
        {
            DriveValveTee(teeProbe, families, key.MainDiameterMm, key.BypassDiameterMm, ignored);
            document.Regenerate();
            tee = MeasureTee(teeProbe);
        }
        finally { document.Delete(teeProbe.Id); document.Regenerate(); }

        BendGeometry bend;
        var bendProbe = PlaceAt(document, bendSymbol, XYZ.Zero, 0, null);
        try
        {
            DriveValveBend(bendProbe, families, key.BypassDiameterMm, ignored);
            document.Regenerate();
            bend = MeasureBend(bendProbe);
        }
        finally { document.Delete(bendProbe.Id); document.Regenerate(); }

        var bypassWidth = InlineConnectorSpanMm(document, bypassValveSymbol, ValveDiameterParameterNameFor(families, bypassValveSymbol),
            key.BypassDiameterMm, "바이패스 밸브").WidthMm
            ?? throw new DhbimwaterException(ErrorCodes.PL_ConnectorCountMismatch, "바이패스 밸브는 2포트 인라인 패밀리여야 합니다.");

        var offset = Math.Max(bypassWidth / 2 + bend.ValveLegMm, tee.MainHalfMm);
        return new BypassLayout(offset, tee.MainHalfMm, tee.BranchMm, bend.TeeLegMm, bend.ValveLegMm, bypassWidth);
    }

    /// <summary>밸브부관 A 파라미터 구동 — 주관 = 본관, 분기 = 바이패스, 수평분기 예, 수직분기 아니오(docs/227 § D).</summary>
    private static void DriveValveTee(FamilyInstance instance, PipeSegmentFamilySelection families, double mainMm, double bypassMm, List<string> warnings)
    {
        SetOptionalDiameterParameter(instance, families.ValveTeeMainDiameterParameterName, mainMm, PipeValveBend.PartNameA, warnings);
        SetOptionalDiameterParameter(instance, families.ValveTeeBranchDiameterParameterName, bypassMm, $"{PipeValveBend.PartNameA} 분기", warnings);
        SetOptionalFlagParameter(instance, families.ValveTeeHorizontalBranchParameterName, true, PipeValveBend.PartNameA, warnings);
        SetOptionalFlagParameter(instance, families.ValveTeeVerticalBranchParameterName, false, PipeValveBend.PartNameA, warnings);
    }

    /// <summary>밸브부관 B 파라미터 구동 — 자기 관경 = 바이패스만 구동한다. 본관 구경은 규격 매개변수에만 적는다(docs/229 § A).</summary>
    private static void DriveValveBend(FamilyInstance instance, PipeSegmentFamilySelection families, double bypassMm, List<string> warnings)
        => SetOptionalDiameterParameter(instance, families.ValveBendDiameterParameterName, bypassMm, PipeValveBend.PartNameB, warnings);

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

    /// <summary>B의 두 커넥터 축 교점(코너)과 레그 길이를 구한다. [0] = 최소 Id = 밸브 쪽(docs/225 § 0-5).</summary>
    private static BendGeometry MeasureBend(FamilyInstance instance)
    {
        var connectors = GetMepConnectors(instance).OrderBy(x => x.Id).ToList();
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

    // ── § C-3 배치 ─────────────────────────────────────────────────────────────

    /// <summary>세트 하나를 놓는다. 도중에 실패하면 이 세트에서 만든 부재를 모두 지우고 건너뛰기로 기록한다 —
    /// 반쯤 놓인 세트가 커밋되면 연결 실패가 줄줄이 뜨고 재료표도 틀어진다.</summary>
    private static void PlaceBypassSet(
        Document document, FamilySymbolCache symbols, PipeNetworkDefinition network, PipeSegmentFamilySelection families,
        PipeEdgeDefinition edge, FittingPlacementDefinition fitting, EdgeAxis axis, double centerMm, BypassLayout layout,
        FamilySymbol segment, Level? hostLevel,
        ICollection<PlacedPart> placedInstances, ICollection<MaterialPart> materialParts, ICollection<XYZ> openPoints,
        ICollection<PipeConnectionFailure> skipped, List<string> warnings, ICollection<double> elevationFixes,
        Action<Guid, Element?> track)
    {
        var spec = fitting.Bypass!;
        var mainMm = edge.DiameterMm;
        var bypassMm = spec.BypassDiameterMm;
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
            var bypassValveSymbol = symbols.Resolve(spec.BypassValveFamilyTypeName, "바이패스 밸브");

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
                    BranchDiameterMm: bypassMm));
            }

            // A–A 사이 본관은 세트가 아니다 — 관 분할(2단계)이 이미 관·일반 밸브를 놓았다(docs/230 § A).

            // 2) 밸브부관 B 두 개 — A쪽 커넥터를 A 분기 커넥터에 맞붙이고, 밸브쪽 커넥터가 세트 중심을 향하게 한다.
            var bendValveEnds = new List<XYZ>();
            for (var i = 0; i < tees.Count; i++)
            {
                var towardCenter = i == 0 ? u : -u;
                var bend = PlaceAt(document, bendSymbol, tees[i].Branch, 0, hostLevel, elevationFixes);
                created.Add(bend);
                DriveValveBend(bend, families, bypassMm, warnings);
                document.Regenerate();
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
                    BranchDiameterMm: bypassMm));
            }

            // 3) 바이패스 밸브 — 두 B 밸브쪽 커넥터의 중점. 틈은 바이패스 관경 단관으로 채운다.
            var bypassCenter = (bendValveEnds[0] + bendValveEnds[1]) / 2;
            var (bypassValve, bypassValveEnds) = PlaceSetValve(document, bypassValveSymbol, families, bypassCenter, u, bypassMm, "바이패스 밸브",
                hostLevel, warnings, elevationFixes);
            created.Add(bypassValve);
            parts.Add(new PlacedPart(bypassValve, "배관부속"));
            materials.Add(new MaterialPart(bypassValve, FittingNameOf(bypassValve), ValvePipingParameters.DiameterText(bypassMm), bypassMm));
            foreach (var end in bendValveEnds)
                PlaceSetFiller(document, segment, families, end, Nearest(bypassValveEnds, end), bypassMm,
                    hostLevel, elevationFixes, created, parts, materials);
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
            skipped.Add(CreateFailure(center, BypassSet.TypeKey, spec.BypassValveFamilyTypeName, 0, ex.Message, network));
            return;
        }

        foreach (var part in parts) placedInstances.Add(part);
        foreach (var material in materials) materialParts.Add(material);
        foreach (var point in opens) openPoints.Add(point);
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
