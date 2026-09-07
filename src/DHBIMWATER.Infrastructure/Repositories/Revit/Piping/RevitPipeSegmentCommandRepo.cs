using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

/// <summary>
/// 관을 MEP Pipe가 아니라 배관 밸브류(<c>OST_PipeAccessory</c>) 카테고리의 직관·단관 패밀리로 만든다(docs/39).
/// 절점에는 곡관(90°/45°)·T형을 자동 배치하고, 그 부속의 커넥터 위치를 실측해 양쪽 관을 그만큼 뗀다.
/// <para>MEP 경로(<see cref="RevitPipeMepCommandRepo"/>)는 그대로 남겨 두고 <see cref="OutputMode"/>로 갈린다.</para>
/// </summary>
internal sealed class RevitPipeSegmentCommandRepo : IPipeCommandRepo
{
    private const double ConnectionToleranceMm = 1;
    private sealed record PlacedPart(FamilyInstance Instance, string Role);
    /// <summary>배관재료표(docs/131)에 쓰는 한 인스턴스의 명칭·규격. 배치 시점에만 알 수 있는 길이·각도를
    /// 여기 담아 두고, 번호는 격점 전체를 본 뒤 § B-4 단계에서 매긴다.
    /// <para><see cref="PlacedPart"/>와 따로 두는 이유: 그쪽은 "커넥터를 연결할 대상" 목록이라
    /// 1포트 부속이 빠져 있는데, 재료표에는 1포트 부속도 반드시 들어가야 한다.</para></summary>
    private sealed record MaterialPart(FamilyInstance Instance, string Name, string Spec);
    // TODO: DistanceMm은 미사용이다 — 인라인 부속 보정은 docs/122에서 앵커 축 정사영 방식으로 대체됐다.
    private readonly record struct NodeLegAnchor(double DistanceMm, XYZ ConnectorOrigin);

    /// <summary>엣지 하나의 배치 축. 원점은 시작 앵커(절점 부속이 있으면 그 실측 커넥터), 방향은 단위벡터다.</summary>
    private readonly record struct EdgeAxis(XYZ Start, XYZ Direction)
    {
        /// <summary>축 원점에서 <paramref name="distanceMm"/>만큼 떨어진 축 위의 점.</summary>
        public XYZ PointAt(double distanceMm) => Start + Direction * UC.MmToFt(distanceMm);
    }

    /// <summary>캔버스 점을 배치 축에 정사영한 축 방향 거리(mm). 축 원점이 0이다.
    /// 점유 구간 계산과 부속 본체 배치가 반드시 같은 식을 써야 커넥터가 맞물린다(docs/122 §A).</summary>
    private static double AxisDistanceMm(XYZ point, EdgeAxis axis)
        => UC.FtToMm((point - axis.Start).DotProduct(axis.Direction));
    private readonly Func<Document?> _document;
    public RevitPipeSegmentCommandRepo(Func<Document?> document) => _document = document;
    public PipeOutputMode OutputMode => PipeOutputMode.PipeAccessorySegment;

    public PipeCreationResult CreateNetwork(PipeNetworkDefinition network)
    {
        var document = _document() ?? throw new InvalidOperationException("활성 Revit 문서가 없습니다.");
        var hostLevel = ResolveNearestLevel(document, UC.MmToFt(network.Elevation));
        var families = network.SegmentFamilies
            ?? throw new InvalidOperationException("직관·단관 패밀리 지정이 없습니다. 모델링 창에서 패밀리를 선택하세요.");

        var segment = ResolveSymbol(document, families.SegmentFamilyTypeName, "관");
        if (string.IsNullOrWhiteSpace(families.LengthParameterName))
            throw new InvalidOperationException("관 길이 파라미터가 지정되지 않았습니다.");
        if (!double.IsFinite(families.StraightLengthMm) || families.StraightLengthMm <= 0)
            throw new InvalidOperationException("직관 정척 길이는 0보다 큰 숫자여야 합니다.");

        // 직관·단관은 같은 패밀리를 길이 파라미터로 구동한다. 커넥터 간 거리는 배치 시점에 결정되므로
        // 여기서는 길이로 검증하지 않고, "2포트인가 / 길이를 쓸 수 있는가"만 확인한다.
        ValidateSegmentSymbol(document, segment, families.LengthParameterName);

        // 직경·각도 파라미터를 못 쓰면 부속이 기본 형상·기본 각도로 놓여 관이 통째로 어긋난다. 경고만 남기고
        // 진행하면 사용자가 어긋난 모델을 받게 되므로, 아무것도 배치하기 전에 한 번에 검사하고 중단한다(docs/122 §C).
        ValidateDrivenParameters(document, network, families, segment);

        var placedInstances = new List<PlacedPart>();
        var materialParts = new List<MaterialPart>();
        var warnings = new List<string>();
        var flowOrientation = PipeFlowOrientation.Build(network);

        // 1) 절점 부속을 먼저 배치하고 실측 커넥터 원점으로 엣지 배치 축을 정한다.
        var legAnchors = PlaceNodeFittings(document, network, families, flowOrientation, placedInstances, materialParts, hostLevel, warnings);

        // 2) 엣지마다 구간을 분해해 직관·단관을 놓는다.
        // null은 물리 배관 커넥터가 1개인 비인라인 부속이다. 주관을 자르지 않고 위치만 배치한다.
        var spanCache = new Dictionary<ElementId, double?>();
        // 인라인 밸브도 관과 같은 축 위에 놓아야 하므로(docs/122 §A) 엣지별 축을 모아 3) 단계에서 다시 쓴다.
        var edgeAxes = new Dictionary<Guid, EdgeAxis>();
        var straightCount = 0;
        var shortCount = 0;
        for (var edgeIndex = 0; edgeIndex < network.Edges.Count; edgeIndex++)
        {
            var edge = network.Edges[edgeIndex];
            var startXyz = ToXyz(edge.Start, edge.Elevation, network.ReferencePoint);
            var endXyz = ToXyz(edge.End, edge.Elevation, network.ReferencePoint);
            var startAnchor = legAnchors.TryGetValue((NodeIdAt(network, edge.Start), edge.Id), out var startLeg)
                ? startLeg.ConnectorOrigin : startXyz;
            var endAnchor = legAnchors.TryGetValue((NodeIdAt(network, edge.End), edge.Id), out var endLeg)
                ? endLeg.ConnectorOrigin : endXyz;
            var lengthMm = UC.FtToMm(startAnchor.DistanceTo(endAnchor));
            if (lengthMm <= 0) continue;

            // 축 방향은 점유 구간 계산보다 먼저 있어야 한다 — 인라인 부속 위치를 이 축에 정사영해서 잡기 때문이다.
            var direction = (endAnchor - startAnchor).Normalize();
            var rotation = Math.Atan2(direction.Y, direction.X);
            edgeAxes[edge.Id] = new EdgeAxis(startAnchor, direction);

            var occupied = new List<OccupiedSpan>();
            occupied.AddRange(InlineFittingSpans(document, edge, network, families, spanCache, startAnchor, direction));

            var plan = PipeSegmentPlan.Build(lengthMm, occupied, families.StraightLengthMm);
            if (plan.Errors.Count > 0)
            {
                var errors = plan.Errors.Distinct().Take(2).ToList();
                var remainingCount = plan.Errors.Distinct().Skip(2).Count();
                if (remainingCount > 0) errors.Add($"그 외 {remainingCount}건");
                throw new InvalidOperationException(
                    $"배관 #{edgeIndex + 1}에 부속을 놓을 수 없습니다.{Environment.NewLine}{Environment.NewLine}" +
                    $"{string.Join(Environment.NewLine, errors)}{Environment.NewLine}{Environment.NewLine}" +
                    $"위치: {FormatEdge(edge)}");
            }
            warnings.AddRange(plan.Warnings.Select(x => $"[{FormatEdge(edge)}] {x}"));

            foreach (var placement in plan.Segments)
            {
                var center = startAnchor + direction * UC.MmToFt(placement.CenterMm);
                var instance = PlaceAt(document, segment, center, rotation, hostLevel);
                // 직관·단관이 같은 패밀리이므로 길이 구동과 검증 경로도 하나다.
                // 직관/단관 구분은 패밀리 내부 수식이 정척 길이로 판정하며, 여기서는 집계에만 쓴다.
                var role = placement.Kind == PipeSegmentKind.Straight ? "직관" : "단관";
                placedInstances.Add(new PlacedPart(instance, role));
                materialParts.Add(new MaterialPart(instance, role, SegmentSpecText(network.DiameterMm, placement.LengthMm)));
                // 직경을 먼저 구동한다. 직경이 형상을 바꾸는 패밀리에서, 뒤따르는 길이 구동·커넥터 실측(RecenterOnConnectors,
                // ValidateCenteredSpan)이 최종 형상 기준으로 이뤄져야 하기 때문이다.
                if (!string.IsNullOrWhiteSpace(families.DiameterParameterName))
                    SetDoubleParameterMm(instance, families.DiameterParameterName, network.DiameterMm, "관 직경");
                SetDoubleParameterMm(instance, families.LengthParameterName, placement.LengthMm, "관 길이");
                document.Regenerate();
                // 길이 파라미터가 한쪽 끝 기준으로 성장하는 패밀리는 커넥터 중앙이 원점에서 벗어난다.
                // 원점 규칙을 패밀리에 요구하는 대신, 실측한 중앙을 요청 중앙에 맞춰 통째로 옮긴다.
                RecenterOnConnectors(document, instance, center);
                var flow = flowOrientation.IsForward(edge.Id) ? direction : -direction;
                CorrectSegmentSocketDirection(document, instance, center, flow, role, warnings);
                ValidateCenteredSpan(instance, center, placement.LengthMm, role);
                if (placement.Kind == PipeSegmentKind.Straight) straightCount++; else shortCount++;
            }
        }

        // 3) 인라인 밸브류를 배치한다(파이프 분할이 없으므로 끼워 넣기만 하면 된다).
        // 위치·회전은 관과 같은 앵커 축을 쓴다(docs/122 §A). 캔버스 절점 기준으로 놓으면 절점 부속 레그 길이만큼
        // 관 조각과 어긋나 커넥터가 연결되지 않는다.
        var singlePortCount = 0;
        foreach (var fitting in network.InlineFittings.Where(x => !string.IsNullOrWhiteSpace(x.FamilyTypeName)))
        {
            var edge = network.Edges.FirstOrDefault(x => x.Id == fitting.EdgeId);
            if (edge is null) continue;
            var symbol = ResolveSymbol(document, fitting.FamilyTypeName, "배관부속");
            var canvasPoint = ToXyz(fitting.Position, fitting.Elevation, network.ReferencePoint);
            // 축이 없는 엣지(길이 0으로 건너뛴 경우)는 캔버스 좌표를 그대로 쓴다.
            var axis = edgeAxes.TryGetValue(edge.Id, out var found)
                ? found
                : new EdgeAxis(ToXyz(edge.Start, edge.Elevation, network.ReferencePoint),
                    (ToXyz(edge.End, edge.Elevation, network.ReferencePoint) - ToXyz(edge.Start, edge.Elevation, network.ReferencePoint)).Normalize());
            var point = axis.PointAt(AxisDistanceMm(canvasPoint, axis));
            var instance = PlaceAt(document, symbol, point, Math.Atan2(axis.Direction.Y, axis.Direction.X), hostLevel);
            var connectorsBeforeDriving = GetMepConnectors(instance).Count;
            SetOptionalDiameterParameter(instance, families.ValveDiameterParameterName, network.DiameterMm, "배관부속", warnings);
            document.Regenerate();
            // 커넥터 수와 무관하게 재료표에는 들어가야 한다. 1포트 부속(공기밸브 등)은 placedInstances에
            // 담기지 않으므로 여기서 따로 모으지 않으면 재료표에서 통째로 빠진다(docs/131 § B-4).
            materialParts.Add(new MaterialPart(instance, FittingNameOf(instance), ValvePipingParameters.DiameterText(network.DiameterMm)));
            var connectors = GetMepConnectors(instance);
            if (connectors.Count == 1)
            {
                // 공기밸브처럼 주관을 관통하는 2포트가 아닌 부속은 위치만 배치한다.
                // Tee/분기관 없이 주관 커넥터 2개와 한 점에서 연결하면 3중 접점이 되어 유효한 Revit 연결이 아니다.
                singlePortCount++;
            }
            else if (connectors.Count == 2)
            {
                ValidateCenteredSpan(instance, point, ConnectorSpanMm(connectors), "배관부속");
                placedInstances.Add(new PlacedPart(instance, "배관부속"));
            }
            else
            {
                throw new InvalidOperationException(BuildConnectorCountMessage(
                    instance, $"배관부속 '{fitting.FamilyTypeName}'", connectors.Count,
                    BuildConnectorInstruction(connectorsBeforeDriving is 1 or 2, connectorsBeforeDriving, network.DiameterMm,
                        [families.ValveDiameterParameterName], "1포트 위치 배치 또는 2포트 인라인 배치만 지원합니다.")));
            }
        }

        // 4) 접점이 일치하는 물리 배관 커넥터를 실제로 연결한다. 위치만 맞닿은 패밀리는 Revit 관망으로 연결되지 않는다.
        ConnectCoincidentConnectors(document, placedInstances, network);

        // 5) 격점상세도 배관재료표용 정보 파라미터를 한 번에 기록한다(docs/131 § B-4).
        // 번호가 격점 전체(명칭+규격 조합)를 보고 정해지므로 배치 즉시가 아니라 여기서 매긴다.
        RecordMaterialParameters(materialParts, network.JunctionId, warnings);

        if (singlePortCount > 0)
            warnings.Add($"1포트 배관부속 {singlePortCount}개는 주관을 분할하거나 연결하지 않고 지정 위치에 배치했습니다.");
        return new PipeCreationResult($"직관 {straightCount}본 · 단관 {shortCount}본을 배치했습니다.", warnings);
    }

    private static Guid NodeIdAt(PipeNetworkDefinition network, DHBIMWATER.Core.Geometry.Point2D point)
        => network.Nodes.FirstOrDefault(x => x.Position.DistanceTo(point) <= 0.01)?.Id ?? Guid.Empty;

    /// <summary>절점마다 곡관·T형을 배치하고, 각 레그의 실측 커넥터 원점을 돌려준다.</summary>
    private static Dictionary<(Guid NodeId, Guid EdgeId), NodeLegAnchor> PlaceNodeFittings(
        Document document, PipeNetworkDefinition network, PipeSegmentFamilySelection families, PipeFlowOrientation flowOrientation,
        ICollection<PlacedPart> placedInstances, ICollection<MaterialPart> materialParts, Level? hostLevel, List<string> warnings)
    {
        var legAnchors = new Dictionary<(Guid, Guid), NodeLegAnchor>();
        foreach (var node in network.Nodes)
        {
            var legs = PipeNodeAngle.Legs(node, network.Edges);
            var origin = ToXyz(node.Position, network.Elevation, network.ReferencePoint);

            FamilySymbol symbol;
            double targetRotation;
            int expectedConnectors;
            switch (node.NodeKind)
            {
                case NodeKind.Elbow when legs.Count == 2:
                    symbol = ResolveSymbol(document, families.BendFamilyTypeName, "곡관");
                    // 곡관은 두 레그의 이등분 방향을 기준으로 놓는다. 실제 각 레그 정렬은 커넥터 매칭으로 확인한다.
                    targetRotation = Bisector(legs[0], legs[1]);
                    expectedConnectors = 2;
                    break;

                case NodeKind.Tee when legs.Count == 3:
                    var order = PipeTeeResolver.Resolve(node, network.Edges)
                        ?? throw new InvalidOperationException("직교 T로 판별할 수 없습니다. 주관은 일직선, 분기관은 90°로 그려야 합니다.");
                    symbol = ResolveSymbol(document, families.TeeFamilyTypeName, "T형");
                    var branch = legs.First(x => x.Edge.Id == order.BranchEdgeId);
                    targetRotation = Math.Atan2(branch.DirectionY, branch.DirectionX);
                    expectedConnectors = 3;
                    break;

                default:
                    continue;   // EndPoint·Inline은 부속이 없고, Cross는 생성 전 검증에서 걸러진다.
            }

            var instance = PlaceAt(document, symbol, origin, 0, hostLevel);
            var connectorsBeforeDriving = GetMepConnectors(instance).Count;
            var diameterParameterName = node.NodeKind == NodeKind.Elbow
                ? families.BendDiameterParameterName : families.TeeDiameterParameterName;
            var role = node.NodeKind == NodeKind.Elbow ? "곡관" : "T형";
            SetOptionalDiameterParameter(instance, diameterParameterName, network.DiameterMm, role, warnings);
            if (node.NodeKind == NodeKind.Tee)
                // T형 주·분기 구경 같이 맞춰야 길이 수식 성립. 하나만 바꾸면 형상 깨짐(docs/130 §0).
                SetOptionalDiameterParameter(instance, families.TeeBranchDiameterParameterName, network.DiameterMm, "T형 분기", warnings);
            // 편각은 각도 파라미터 구동과 재료표 규격 문자열이 같은 값을 써야 하므로 한 번만 구한다(docs/131 § B-3).
            var deflectionDeg = node.NodeKind == NodeKind.Elbow ? PipeNodeAngle.Deflection(node, network.Edges) : null;
            if (node.NodeKind == NodeKind.Elbow && deflectionDeg is not null)
                SetOptionalAngleParameter(instance, families.BendAngleParameterName, deflectionDeg.Value, role, warnings);
            document.Regenerate();
            placedInstances.Add(new PlacedPart(instance, role));
            materialParts.Add(new MaterialPart(instance, role, node.NodeKind == NodeKind.Elbow
                ? BendSpecText(network.DiameterMm, deflectionDeg)
                // T형 분기 구경은 현재 주관과 같은 값으로 구동한다(docs/130 § A). 이경티는 범위 밖이다.
                : TeeSpecText(network.DiameterMm, network.DiameterMm)));
            var connectors = GetMepConnectors(instance);
            if (connectors.Count != expectedConnectors)
                throw new InvalidOperationException(BuildConnectorCountMessage(
                    instance, $"절점 부속 '{symbol.Family.Name} : {symbol.Name}'", connectors.Count,
                    BuildConnectorInstruction(connectorsBeforeDriving == expectedConnectors, connectorsBeforeDriving, network.DiameterMm,
                        [diameterParameterName, node.NodeKind == NodeKind.Tee ? families.TeeBranchDiameterParameterName : null,
                            node.NodeKind == NodeKind.Elbow ? families.BendAngleParameterName : null],
                        $"{expectedConnectors}개짜리 배관 밸브류 패밀리를 선택하세요.")));

            RotateTo(document, instance, origin, connectors, targetRotation, node.NodeKind);
            if (node.NodeKind == NodeKind.Elbow)
                CorrectElbowSocketDirection(document, instance, origin, targetRotation, node, legs, flowOrientation, warnings);

            // 회전 후 실측: 레그 방향과 가장 잘 맞는 실제 커넥터가 직관·단관 배치 축의 끝점이다.
            foreach (var leg in legs)
            {
                var legDirection = new XYZ(leg.DirectionX, leg.DirectionY, 0);
                var best = GetMepConnectors(instance)
                    .OrderByDescending(c => Direction(c.Origin - origin).DotProduct(legDirection))
                    .First();
                legAnchors[(node.Id, leg.Edge.Id)] = new NodeLegAnchor(
                    UC.FtToMm(best.Origin.DistanceTo(origin)), best.Origin);
            }
        }
        return legAnchors;
    }

    /// <summary>부속의 기준 방향(곡관은 커넥터 이등분, T형은 나머지 둘과 직교하는 분기 커넥터)이
    /// <paramref name="targetRotation"/>을 향하도록 Z축으로 돌린다.</summary>
    private static void RotateTo(Document document, FamilyInstance instance, XYZ origin,
        IReadOnlyList<Connector> connectors, double targetRotation, NodeKind kind)
    {
        var directions = connectors.Select(c => Direction(c.Origin - origin)).ToList();
        var current = kind == NodeKind.Tee ? BranchDirection(directions) : Bisector(directions[0], directions[1]);
        var delta = targetRotation - current;
        if (Math.Abs(delta) <= 1e-9) return;
        ElementTransformUtils.RotateElement(document, instance.Id, Line.CreateBound(origin, origin + XYZ.BasisZ), delta);
        document.Regenerate();
    }

    private static void CorrectSegmentSocketDirection(
        Document document, FamilyInstance instance, XYZ center, XYZ flow, string role, List<string> warnings)
    {
        var connectors = GetMepConnectors(instance).OrderBy(x => x.Id).ToList();
        if (connectors.Count != 2 || connectors[0].Id == connectors[1].Id)
        {
            warnings.Add($"{role} '{instance.Symbol.Family.Name} : {instance.Symbol.Name}'의 소켓 방향 보정을 건너뛰었습니다(구분 가능한 2개 커넥터가 필요합니다).");
            return;
        }

        if ((connectors[0].Origin - center).DotProduct(flow) <= 0) return;
        ElementTransformUtils.RotateElement(document, instance.Id, Line.CreateBound(center, center + XYZ.BasisZ), Math.PI);
        document.Regenerate();
    }

    private static void CorrectElbowSocketDirection(
        Document document, FamilyInstance instance, XYZ origin, double targetRotation, PipeNodeDefinition node,
        IReadOnlyList<PipeNodeLeg> legs, PipeFlowOrientation flowOrientation, List<string> warnings)
    {
        var inboundLegs = legs.Where(leg => IsInboundAtNode(leg.Edge, node, flowOrientation)).ToList();
        if (inboundLegs.Count != 1)
        {
            warnings.Add($"곡관 소켓 방향 보정을 건너뛰었습니다: 절점 ({node.Position.X:N0}, {node.Position.Y:N0})mm에서 상류 레그를 하나로 정할 수 없습니다.");
            return;
        }

        var connectors = GetMepConnectors(instance).OrderBy(x => x.Id).ToList();
        if (connectors.Count != 2 || connectors[0].Id == connectors[1].Id)
        {
            warnings.Add($"곡관 소켓 방향 보정을 건너뛰었습니다: 절점 ({node.Position.X:N0}, {node.Position.Y:N0})mm의 커넥터가 구분 가능한 2개가 아닙니다.");
            return;
        }

        var leg = inboundLegs[0];
        var legDirection = new XYZ(leg.DirectionX, leg.DirectionY, 0);
        if (Direction(connectors[0].Origin - origin).DotProduct(legDirection) >= 0) return;
        var axisDirection = new XYZ(Math.Cos(targetRotation), Math.Sin(targetRotation), 0);
        ElementTransformUtils.RotateElement(document, instance.Id, Line.CreateBound(origin, origin + axisDirection), Math.PI);
        document.Regenerate();
    }

    private static bool IsInboundAtNode(
        PipeEdgeDefinition edge, PipeNodeDefinition node, PipeFlowOrientation flowOrientation)
    {
        var flowArrival = flowOrientation.IsForward(edge.Id) ? edge.End : edge.Start;
        return flowArrival.DistanceTo(node.Position) <= 0.01;
    }

    /// <summary>3개 커넥터 중 나머지 둘과 거의 직교하는 것이 분기 커넥터다.</summary>
    private static double BranchDirection(IReadOnlyList<XYZ> directions)
    {
        var branch = directions
            .OrderBy(d => directions.Where(o => !ReferenceEquals(o, d)).Sum(o => Math.Abs(d.DotProduct(o))))
            .First();
        return Math.Atan2(branch.Y, branch.X);
    }

    private static double Bisector(PipeNodeLeg a, PipeNodeLeg b)
        => Bisector(new XYZ(a.DirectionX, a.DirectionY, 0), new XYZ(b.DirectionX, b.DirectionY, 0));

    private static double Bisector(XYZ a, XYZ b)
    {
        var sum = a + b;
        // 두 레그가 정반대(일직선)면 합이 0이라 방향을 못 정한다 — 그때는 한쪽 방향을 쓴다.
        return sum.GetLength() <= 1e-9 ? Math.Atan2(a.Y, a.X) : Math.Atan2(sum.Y, sum.X);
    }

    private static XYZ Direction(XYZ vector)
    {
        var flat = new XYZ(vector.X, vector.Y, 0);
        return flat.GetLength() <= 1e-9 ? XYZ.BasisX : flat.Normalize();
    }

    /// <summary>인라인 밸브가 축 위에서 차지하는 구간. 커넥터 2개 사이 길이를 실측해 쓴다.
    /// 구간 중심은 캔버스 점을 배치 축(<paramref name="startAnchor"/>, <paramref name="direction"/>)에
    /// 정사영해 잡는다 — 관 조각과 같은 축 좌표라야 커넥터가 맞물린다(docs/122 §A).</summary>
    private static IEnumerable<OccupiedSpan> InlineFittingSpans(
        Document document, PipeEdgeDefinition edge, PipeNetworkDefinition network,
        PipeSegmentFamilySelection families,
        Dictionary<ElementId, double?> spanCache, XYZ startAnchor, XYZ direction)
    {
        var axis = new EdgeAxis(startAnchor, direction);
        foreach (var fitting in network.InlineFittings.Where(x => x.EdgeId == edge.Id && !string.IsNullOrWhiteSpace(x.FamilyTypeName)))
        {
            var symbol = ResolveSymbol(document, fitting.FamilyTypeName, "배관부속");
            if (!spanCache.TryGetValue(symbol.Id, out var widthMm))
                spanCache[symbol.Id] = widthMm = InlineConnectorSpanMm(
                    document, symbol, families.ValveDiameterParameterName, network.DiameterMm);
            if (widthMm is null) continue; // 1포트는 주관을 자르지 않는 위치 배치다.
            var center = AxisDistanceMm(ToXyz(fitting.Position, fitting.Elevation, network.ReferencePoint), axis);
            yield return new OccupiedSpan(center - widthMm.Value / 2, center + widthMm.Value / 2);
        }
    }

    /// <summary>인라인 부속이면 커넥터 간 길이, 1포트 위치 부속이면 null. 그 외 커넥터 수는 지원하지 않는다.</summary>
    private static double? InlineConnectorSpanMm(
        Document document, FamilySymbol symbol, string? diameterParameterName, double diameterMm)
    {
        var probe = PlaceAt(document, symbol, XYZ.Zero, 0, null);
        try
        {
            if (!string.IsNullOrWhiteSpace(diameterParameterName))
            {
                var parameter = probe.LookupParameter(diameterParameterName);
                if (parameter is not null && !parameter.IsReadOnly && parameter.StorageType == StorageType.Double)
                {
                    parameter.Set(UC.MmToFt(diameterMm));
                    document.Regenerate();
                }
            }
            var connectors = GetMepConnectors(probe);
            if (connectors.Count == 1) return null;
            if (connectors.Count != 2)
                throw new InvalidOperationException(BuildConnectorCountMessage(
                    probe, $"배관부속 '{symbol.Family.Name} : {symbol.Name}'", connectors.Count, "1포트 위치 배치 또는 2포트 인라인 배치만 지원합니다."));
            ValidateConnectorMidpoint(symbol, connectors, XYZ.Zero, "배관부속");
            return ConnectorSpanMm(connectors);
        }
        finally
        {
            document.Delete(probe.Id);
            document.Regenerate();
        }
    }

    /// <summary>이번 네트워크에서 실제로 쓰는 패밀리·파라미터 조합을 배치 전에 모두 검사한다(docs/122 §C).
    /// 파라미터명이 비어 있으면 "구동하지 않음"이 정상 설정이므로 검사 대상이 아니다.
    /// 실패는 모아서 한 번에 던진다 — 하나 고칠 때마다 다시 돌려 다음 실패를 만나는 일을 막는다.</summary>
    private static void ValidateDrivenParameters(
        Document document, PipeNetworkDefinition network, PipeSegmentFamilySelection families, FamilySymbol segment)
    {
        var failures = new List<string>();
        var probeCache = new Dictionary<ElementId, FamilyInstance>();
        try
        {
            Check(segment, "관", families.DiameterParameterName, "직경", false);

            if (network.Nodes.Any(x => x.NodeKind == NodeKind.Elbow))
            {
                var bend = ResolveSymbol(document, families.BendFamilyTypeName, "곡관");
                Check(bend, "곡관", families.BendDiameterParameterName, "직경", false);
                Check(bend, "곡관", families.BendAngleParameterName, "각도", true);
            }
            if (network.Nodes.Any(x => x.NodeKind == NodeKind.Tee))
            {
                var tee = ResolveSymbol(document, families.TeeFamilyTypeName, "T형");
                Check(tee, "T형", families.TeeDiameterParameterName, "직경", false);
                Check(tee, "T형", families.TeeBranchDiameterParameterName, "분기 직경", false);
            }

            foreach (var familyTypeName in network.InlineFittings
                         .Where(x => !string.IsNullOrWhiteSpace(x.FamilyTypeName))
                         .Select(x => x.FamilyTypeName).Distinct())
                Check(ResolveSymbol(document, familyTypeName, "배관부속"), "배관부속", families.ValveDiameterParameterName, "직경", false);
        }
        finally
        {
            foreach (var probe in probeCache.Values) document.Delete(probe.Id);
            if (probeCache.Count > 0) document.Regenerate();
        }

        if (failures.Count == 0) return;
        throw new InvalidOperationException(
            $"아래 파라미터에 값을 쓸 수 없어 모델을 생성하지 않았습니다.{Environment.NewLine}{Environment.NewLine}" +
            $"{string.Join(Environment.NewLine, failures)}{Environment.NewLine}{Environment.NewLine}" +
            "패밀리에서 인스턴스 파라미터로 바꾸거나, 모델링 창에서 해당 파라미터 선택을 비우세요(비우면 구동하지 않습니다).");

        void Check(FamilySymbol symbol, string role, string? parameterName, string kind, bool requiresAngleType)
        {
            if (string.IsNullOrWhiteSpace(parameterName)) return;
            if (!probeCache.TryGetValue(symbol.Id, out var probe))
                probeCache[symbol.Id] = probe = PlaceAt(document, symbol, XYZ.Zero, 0, null);

            var parameter = probe.LookupParameter(parameterName);
            var reason = parameter is null ? "인스턴스 파라미터가 없습니다(유형 파라미터이거나 이름이 다릅니다)."
                : parameter.IsReadOnly ? "읽기 전용입니다."
                : parameter.StorageType != StorageType.Double ? "실수형 파라미터가 아닙니다."
                // 라디안 값을 길이 파라미터에 쓰면 조용히 엉뚱한 형상이 나오므로 각도는 데이터 타입까지 본다.
                : requiresAngleType && !parameter.Definition.GetDataType().Equals(SpecTypeId.Angle) ? "각도형 파라미터가 아닙니다."
                : null;
            if (reason is not null)
                failures.Add($"- {role} '{symbol.Family.Name} : {symbol.Name}'의 {kind} 파라미터 '{parameterName}': {reason}");
        }
    }

    /// <summary>관 패밀리가 인라인 배치 조건을 만족하는지 임시 인스턴스로 확인한다.
    /// 길이는 배치 시점에 파라미터로 구동하므로 여기서 길이를 검증하지 않는다.</summary>
    private static void ValidateSegmentSymbol(Document document, FamilySymbol symbol, string lengthParameterName)
    {
        var probe = PlaceAt(document, symbol, XYZ.Zero, 0, null);
        try
        {
            var connectors = GetMepConnectors(probe);
            if (connectors.Count != 2)
                throw new InvalidOperationException(BuildConnectorCountMessage(
                    probe, $"관 '{symbol.Family.Name} : {symbol.Name}'", connectors.Count, "인라인 배치에는 양쪽 끝 커넥터가 있는 2포트 패밀리가 필요합니다."));
            // 관 원점이 커넥터 중앙일 필요는 없다 — 길이 구동 후 RecenterOnConnectors가 실측으로 맞춘다.

            // 길이 파라미터를 실제로 쓸 수 있는지 배치 전에 확인한다(전체 롤백보다 이른 실패가 낫다).
            var parameter = probe.LookupParameter(lengthParameterName);
            if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double)
                throw new InvalidOperationException(
                    $"관 길이 파라미터 '{lengthParameterName}'에 값을 쓸 수 없습니다. " +
                    "쓰기 가능한 길이형 인스턴스 파라미터인지 확인하세요.");
        }
        finally
        {
            document.Delete(probe.Id);
            document.Regenerate();
        }
    }

    /// <summary>표고(ft)에 가장 가까운 Level. Level 없으면 null.</summary>
    private static Level? ResolveNearestLevel(Document document, double elevationFt)
        => new FilteredElementCollector(document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(x => Math.Abs(x.Elevation - elevationFt))
            .FirstOrDefault();

    private static FamilyInstance PlaceAt(
        Document document, FamilySymbol symbol, XYZ point, double rotation, Level? level)
    {
        if (!symbol.IsActive) { symbol.Activate(); document.Regenerate(); }
        var instance = level is null
            ? document.Create.NewFamilyInstance(point, symbol, StructuralType.NonStructural)
            : document.Create.NewFamilyInstance(point, symbol, level, StructuralType.NonStructural);
        document.Regenerate();
        if (Math.Abs(rotation) > 1e-9)
        {
            ElementTransformUtils.RotateElement(document, instance.Id, Line.CreateBound(point, point + XYZ.BasisZ), rotation);
            document.Regenerate();
        }
        return instance;
    }

    private static void SetDoubleParameterMm(FamilyInstance instance, string parameterName, double valueMm, string label)
    {
        var parameter = instance.LookupParameter(parameterName);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double)
            throw new InvalidOperationException(
                $"{label} 파라미터 '{parameterName}'에 값을 쓸 수 없습니다. 쓰기 가능한 길이형 인스턴스 파라미터인지 확인하세요.");
        if (!parameter.Set(UC.MmToFt(valueMm)))
            throw new InvalidOperationException($"{label} 파라미터 '{parameterName}' 설정에 실패했습니다.");
    }

    // ── 배관재료표(docs/131 § B) ──────────────────────────────────────────────

    /// <summary>인라인 부속의 재료표 명칭. Role("배관부속")은 총칭이라 못 쓰고 패밀리명을 쓴다.
    /// 사용자가 `DH_제수밸브`처럼 접두어를 붙여 쓰므로 그것만 떼고 나머지는 가공하지 않는다.</summary>
    private static string FittingNameOf(FamilyInstance instance)
    {
        var name = instance.Symbol.Family.Name ?? string.Empty;
        return name.StartsWith("DH_", StringComparison.Ordinal) ? name["DH_".Length..] : name;
    }

    /// <summary>직관·단관 규격: `D160 L=6000`. 길이는 mm 정수로 반올림한다.</summary>
    private static string SegmentSpecText(double diameterMm, double lengthMm)
        => $"{ValvePipingParameters.DiameterText(diameterMm)} L={Math.Round(lengthMm):0}";

    /// <summary>곡관 규격: `D160 45°`. 편각을 못 구한 절점은 직경만 적는다.</summary>
    private static string BendSpecText(double diameterMm, double? deflectionDeg)
        => deflectionDeg is null
            ? ValvePipingParameters.DiameterText(diameterMm)
            : $"{ValvePipingParameters.DiameterText(diameterMm)} {deflectionDeg.Value:0.##}°";

    /// <summary>T형 규격: `D160x90`(주관 x 분기).</summary>
    private static string TeeSpecText(double mainDiameterMm, double branchDiameterMm)
        => $"{ValvePipingParameters.DiameterText(mainDiameterMm)}x{branchDiameterMm:0.##}";

    /// <summary>배치가 끝난 뒤 재료표용 정보 파라미터를 한 번에 기록한다(docs/131 § B-4).
    /// 부재번호는 `명칭 + 규격` 조합별 일련번호다 — 같은 조합은 같은 번호를 받아야
    /// 일람표 항목화를 끄고 수량을 집계했을 때 행 1개에 번호 1개가 대응한다.
    /// <para>모델은 이미 만들어졌고 트랜잭션도 진행 중이므로 쓰기 실패는 예외로 올리지 않고 경고로 남긴다.</para></summary>
    private static void RecordMaterialParameters(IReadOnlyList<MaterialPart> parts, string junctionId, List<string> warnings)
    {
        if (parts.Count == 0) return;

        // 사전순이 아니라 최초 배치 순서로 번호를 매긴다. 도면을 읽는 순서에 가깝다.
        var numbers = new Dictionary<(string Name, string Spec), int>();
        foreach (var part in parts)
        {
            var key = (part.Name, part.Spec);
            if (!numbers.ContainsKey(key)) numbers[key] = numbers.Count + 1;
        }

        var failed = new List<string>();
        foreach (var part in parts)
        {
            SetOptionalTextParameter(part.Instance, ValvePipingParameters.Addin, ValvePipingParameters.AddinValue, failed);
            SetOptionalTextParameter(part.Instance, ValvePipingParameters.Part, part.Name, failed);
            SetOptionalTextParameter(part.Instance, ValvePipingParameters.ItemName, part.Name, failed);
            SetOptionalTextParameter(part.Instance, ValvePipingParameters.ItemSpec, part.Spec, failed);
            SetOptionalIntegerParameter(part.Instance, ValvePipingParameters.ItemNumber, numbers[(part.Name, part.Spec)], failed);
            // 격점번호는 필수 입력이 아니다. 비어 있으면 빈 문자열을 쓰지 않고 건너뛴다.
            if (!string.IsNullOrWhiteSpace(junctionId))
                SetOptionalTextParameter(part.Instance, ValvePipingParameters.JunctionId, junctionId, failed);
            // DH_비고는 쓰지 않는다. 사용자가 Revit 속성창에서 직접 채우는 열이다(docs/131 § A-2).
        }

        // 인스턴스마다 경고를 쌓으면 목록이 폭주하므로 파라미터명 기준으로 한 번만 남긴다.
        var distinct = failed.Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count > 0)
            warnings.Add($"배관재료표 매개변수 {string.Join(", ", distinct)}에 값을 쓰지 못했습니다. " +
                "배관 밸브류 카테고리에 공유 매개변수가 바인딩됐는지 확인하세요(모델 생성 자체는 정상입니다).");
    }

    private static void SetOptionalTextParameter(FamilyInstance instance, string parameterName, string value, ICollection<string> failed)
    {
        var parameter = instance.LookupParameter(parameterName);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.String || !parameter.Set(value ?? string.Empty))
            failed.Add(parameterName);
    }

    private static void SetOptionalIntegerParameter(FamilyInstance instance, string parameterName, int value, ICollection<string> failed)
    {
        var parameter = instance.LookupParameter(parameterName);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Integer || !parameter.Set(value))
            failed.Add(parameterName);
    }

    private static void SetOptionalDiameterParameter(
        FamilyInstance instance, string? parameterName, double valueMm, string label, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(parameterName)) return;
        var parameter = instance.LookupParameter(parameterName);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double)
        {
            warnings.Add(
                $"{label} '{instance.Symbol.Family.Name} : {instance.Symbol.Name}'에서 직경 파라미터 '{parameterName}'에 " +
                "값을 쓸 수 없어 건너뛰었습니다(이름이 없거나, 유형 파라미터이거나, 읽기 전용/실수형이 아닌 경우입니다).");
            return;
        }
        if (!parameter.Set(UC.MmToFt(valueMm)))
            warnings.Add($"{label} '{instance.Symbol.Family.Name} : {instance.Symbol.Name}'의 직경 파라미터 '{parameterName}' 설정에 실패해 건너뛰었습니다.");
    }

    private static void SetOptionalAngleParameter(
        FamilyInstance instance, string? parameterName, double valueDeg, string label, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(parameterName)) return;
        var parameter = instance.LookupParameter(parameterName);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double)
        {
            warnings.Add(
                $"{label} '{instance.Symbol.Family.Name} : {instance.Symbol.Name}'에서 각도 파라미터 '{parameterName}'에 " +
                "값을 쓸 수 없어 건너뛰었습니다(이름이 없거나, 유형 파라미터이거나, 읽기 전용/실수형이 아닌 경우입니다).");
            return;
        }
        if (!parameter.Set(UC.DegToRad(valueDeg)))
            warnings.Add($"{label} '{instance.Symbol.Family.Name} : {instance.Symbol.Name}'의 각도 파라미터 '{parameterName}' 설정에 실패해 건너뛰었습니다.");
    }

    /// <summary>길이 구동 뒤 실제 커넥터 중앙이 요청 중앙에 오도록 인스턴스를 평행이동한다.
    /// 길이 치수가 중심 대칭이 아닌 패밀리(한쪽 끝 기준 성장)를 그대로 수용하기 위한 보정이다.</summary>
    private static void RecenterOnConnectors(Document document, FamilyInstance instance, XYZ expectedCenter)
    {
        var connectors = GetMepConnectors(instance);
        if (connectors.Count != 2) return;  // 커넥터 개수 오류는 ValidateCenteredSpan이 보고한다.
        var offset = expectedCenter - (connectors[0].Origin + connectors[1].Origin) / 2;
        if (offset.GetLength() <= UC.MmToFt(ConnectionToleranceMm) / 10) return;
        ElementTransformUtils.MoveElement(document, instance.Id, offset);
        document.Regenerate();
    }

    private static void ValidateCenteredSpan(FamilyInstance instance, XYZ expectedCenter, double expectedLengthMm, string role)
    {
        var connectors = GetMepConnectors(instance);
        if (connectors.Count != 2)
            throw new InvalidOperationException($"{role} '{instance.Symbol.Family.Name} : {instance.Symbol.Name}'의 물리 배관 커넥터는 {connectors.Count}개입니다.");
        var midpoint = (connectors[0].Origin + connectors[1].Origin) / 2;
        var centerErrorMm = UC.FtToMm(midpoint.DistanceTo(expectedCenter));
        var spanMm = ConnectorSpanMm(connectors);
        if (centerErrorMm > ConnectionToleranceMm || Math.Abs(spanMm - expectedLengthMm) > ConnectionToleranceMm)
            throw new InvalidOperationException(
                $"{role} '{instance.Symbol.Family.Name} : {instance.Symbol.Name}'의 커넥터 형상이 요청 구간과 맞지 않습니다. " +
                $"중앙 오차 {centerErrorMm:N1}mm, 길이 {spanMm:N1}mm(요청 {expectedLengthMm:N1}mm)입니다.");
    }

    private static void ValidateConnectorMidpoint(
        FamilySymbol symbol, IReadOnlyList<Connector> connectors, XYZ expectedCenter, string role)
    {
        var midpoint = (connectors[0].Origin + connectors[1].Origin) / 2;
        if (UC.FtToMm(midpoint.DistanceTo(expectedCenter)) <= ConnectionToleranceMm) return;
        throw new InvalidOperationException(
            $"{role} '{symbol.Family.Name} : {symbol.Name}'의 원점이 두 배관 커넥터의 중앙과 일치하지 않습니다. " +
            "관 중앙이 원점인 패밀리를 선택하세요.");
    }

    private static double ConnectorSpanMm(IReadOnlyList<Connector> connectors)
        => UC.FtToMm(connectors[0].Origin.DistanceTo(connectors[1].Origin));

    /// <summary>서로 다른 패밀리의 물리 배관 커넥터가 같은 접점에 하나씩 있으면 실제 Revit 연결을 만든다.
    /// 관 끝점(EndPoint) 이외의 열린 커넥터나 한 점에 3개 이상 모인 모호한 연결은 관망 단절이므로 전체 생성을 중단한다.</summary>
    private static void ConnectCoincidentConnectors(
        Document document, IReadOnlyCollection<PlacedPart> instances, PipeNetworkDefinition network)
    {
        var tolerance = UC.MmToFt(ConnectionToleranceMm);
        var endPoints = network.Nodes.Where(x => x.NodeKind == NodeKind.EndPoint)
            .Select(x => ToXyz(x.Position, network.Elevation, network.ReferencePoint)).ToList();
        var partsById = instances.ToDictionary(x => x.Instance.Id);
        var connectors = instances.SelectMany(x => GetMepConnectors(x.Instance)).ToList();
        var handled = new HashSet<Connector>();

        foreach (var connector in connectors)
        {
            if (handled.Contains(connector) || connector.IsConnected) continue;
            var candidates = connectors.Where(other =>
                    !ReferenceEquals(other, connector) &&
                    !handled.Contains(other) &&
                    !other.IsConnected &&
                    other.Owner.Id != connector.Owner.Id &&
                    other.Origin.DistanceTo(connector.Origin) <= tolerance)
                .ToList();

            if (candidates.Count == 1)
            {
                try
                {
                    connector.ConnectTo(candidates[0]);
                    handled.Add(connector);
                    handled.Add(candidates[0]);
                }
                catch (Exception ex)
                {
                    var part = partsById[connector.Owner.Id];
                    var otherPart = partsById[candidates[0].Owner.Id];
                    throw BuildFailure(
                        $"{FormatPart(part)}의 커넥터를 {FormatPart(otherPart)}의 커넥터에 연결하지 못했습니다.{Environment.NewLine}" +
                        $"접점: {FormatPoint(connector.Origin, network.ReferencePoint)}{Environment.NewLine}" +
                        "두 패밀리 방향과 커넥터 규격을 확인하세요.", connector, part, network, "Revit ConnectTo 호출 실패", ex);
                }
                continue;
            }

            if (candidates.Count > 1)
            {
                var part = partsById[connector.Owner.Id];
                var otherParts = candidates.Select(x => FormatPart(partsById[x.Owner.Id])).Distinct();
                throw BuildFailure(
                    $"{FormatPart(part)}의 커넥터 접점: {FormatPoint(connector.Origin, network.ReferencePoint)}{Environment.NewLine}" +
                    $"연결 가능한 배관 커넥터가 {candidates.Count + 1}개 겹쳐 있습니다. 상대: {string.Join(", ", otherParts)}.{Environment.NewLine}" +
                    "1포트 분기 부속은 인라인 부속으로 직접 연결할 수 없습니다.", connector, part, network, "커넥터 중첩");
            }

            if (endPoints.Any(x => x.DistanceTo(connector.Origin) <= tolerance))
            {
                handled.Add(connector);
                continue;
            }

            var unmatchedPart = partsById[connector.Owner.Id];
            var nearest = connectors
                .Where(other => other.Owner.Id != connector.Owner.Id)
                .OrderBy(other => other.Origin.DistanceTo(connector.Origin))
                .FirstOrDefault();
            var nearestMessage = nearest is null
                ? "주변에 다른 커넥터가 없습니다."
                : $"가장 가까운 커넥터는 {FormatPart(partsById[nearest.Owner.Id])}의 커넥터로 " +
                  $"{UC.FtToMm(nearest.Origin.DistanceTo(connector.Origin)):N1}mm 떨어져 있습니다(허용 {ConnectionToleranceMm:N0}mm).";
            var nearestDistanceMm = nearest is null ? 0 : UC.FtToMm(nearest.Origin.DistanceTo(connector.Origin));
            var hint = nearestDistanceMm >= ConnectionToleranceMm * 10
                ? $"{Environment.NewLine}(10mm 이상 이탈 — 부속이 관 배치 축에서 벗어났을 가능성이 큽니다. " +
                  "곡관 꺾임각이 캔버스와 다르게 적용됐는지, 곡관/T형/밸브 직경·각도 파라미터가 실제 패밀리의 " +
                  "인스턴스 파라미터와 이름이 맞는지 확인하세요.)"
                : "";
            throw BuildFailure(
                $"{FormatPart(unmatchedPart)}의 커넥터가 연결 상대를 찾지 못했습니다.{Environment.NewLine}" +
                $"접점: {FormatPoint(connector.Origin, network.ReferencePoint)}{Environment.NewLine}" +
                $"{nearestMessage}{Environment.NewLine}" +
                $"부속 커넥터가 배관 축에서 벗어났는지, 직관·단관 길이와 곡관/T형 방향을 확인하세요.{hint}",
                connector, unmatchedPart, network, nearestMessage);
        }

        document.Regenerate();
    }

    private static FamilySymbol ResolveSymbol(Document document, string familyTypeName, string role)
    {
        if (string.IsNullOrWhiteSpace(familyTypeName))
            throw new InvalidOperationException($"{role} 패밀리가 선택되지 않았습니다.");
        var separator = familyTypeName.LastIndexOf(" : ", StringComparison.Ordinal);
        if (separator <= 0 || separator >= familyTypeName.Length - 3)
            throw new InvalidOperationException($"{role} 패밀리명 '{familyTypeName}' 형식이 올바르지 않습니다(\"패밀리명 : 타입명\").");
        var familyName = familyTypeName[..separator];
        var typeName = familyTypeName[(separator + 3)..];
        return new FilteredElementCollector(document).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                   .FirstOrDefault(x => x.Family.Name == familyName && x.Name == typeName)
               ?? throw new InvalidOperationException($"{role} 패밀리 '{familyTypeName}'를 찾을 수 없습니다. 프로젝트에 로드돼 있는지 확인하세요.");
    }

    /// <summary>논리·전기 커넥터를 제외하고 실제 관망 연결에 쓰는 끝단 배관 커넥터만 반환한다.</summary>
    private static List<Connector> GetMepConnectors(FamilyInstance instance)
        => instance.MEPModel?.ConnectorManager?.Connectors.Cast<Connector>()
            .Where(x => x.Domain == Domain.DomainPiping && x.ConnectorType == ConnectorType.End)
            .ToList() ?? [];

    private static string BuildConnectorCountMessage(FamilyInstance instance, string role, int connectorCount, string instruction)
    {
        var manager = instance.MEPModel?.ConnectorManager;
        var allConnectors = manager?.Connectors.Cast<Connector>().ToList() ?? [];
        var subComponents = instance.GetSubComponentIds()
            .Select(id => instance.Document.GetElement(id))
            .OfType<FamilyInstance>()
            .ToList();
        var nestedPipingEndCount = subComponents.Sum(child => GetMepConnectors(child).Count);
        var connectorDetails = allConnectors.Count == 0
            ? "없음"
            : string.Join(", ", allConnectors.Select(x => $"{x.Domain}/{x.ConnectorType}"));

        return $"{role}의 배관 커넥터가 {connectorCount}개입니다. {instruction}{Environment.NewLine}" +
               $"카테고리={instance.Category?.Name ?? "없음"}, MEPModel={(instance.MEPModel is null ? "없음" : "있음")}, " +
               $"ConnectorManager={(manager is null ? "없음" : "있음")}, 전체 커넥터={allConnectors.Count}개({connectorDetails}){Environment.NewLine}" +
               $"중첩 구성요소 {subComponents.Count}개, 그 안의 배관 End 커넥터 {nestedPipingEndCount}개";
    }

    private static string BuildConnectorInstruction(bool wasValidBeforeDriving, int connectorsBeforeDriving,
        double diameterMm, IEnumerable<string?> drivenParameterNames, string fallback)
    {
        if (!wasValidBeforeDriving) return fallback;
        var names = drivenParameterNames.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        var parameterText = names.Count == 0 ? "없음(구동 안 함)" : string.Join(", ", names.Select(x => $"'{x}'"));
        return $"배치 직후에는 커넥터가 {connectorsBeforeDriving}개였으나 직경·각도 파라미터를 쓴 뒤 사라졌습니다. " +
               $"구동한 값(직경 {diameterMm:N0}mm)과 파라미터명({parameterText})이 이 패밀리에서 유효한지, " +
               "함께 맞춰야 하는 다른 구경 파라미터가 남아 있는지 확인하세요. Revit 경고창에 패밀리 형상 오류가 함께 떴는지도 확인하세요.";
    }

    private static PipeNetworkBuildException BuildFailure(
        string message, Connector connector, PlacedPart part, PipeNetworkDefinition network, string detail, Exception? innerException = null)
    {
        var point = connector.Origin;
        var failure = new PipeConnectionFailure(
            new DHBIMWATER.Core.Geometry.Point2D(UC.FtToMm(point.X) - network.ReferencePoint.X, UC.FtToMm(point.Y) - network.ReferencePoint.Y),
            UC.FtToMm(point.Z), part.Role, $"{part.Instance.Symbol.Family.Name} : {part.Instance.Symbol.Name}", connector.Owner.Id.Value, detail);
        return innerException is null
            ? new PipeNetworkBuildException(message, [failure])
            : new PipeNetworkBuildException(message, [failure]) { Source = innerException.Source };
    }

    private static string FormatPart(PlacedPart part)
        => $"{part.Role} '{part.Instance.Symbol.Family.Name} : {part.Instance.Symbol.Name}'(ID {part.Instance.Id.Value})";

    private static string FormatPoint(XYZ point, DHBIMWATER.Core.Geometry.Point2D referencePoint)
        => FormattableString.Invariant(
            $"캔버스 ({UC.FtToMm(point.X) - referencePoint.X:N0}, {UC.FtToMm(point.Y) - referencePoint.Y:N0})mm, Elev {UC.FtToMm(point.Z):N0}mm  [Revit 좌표 ({UC.FtToMm(point.X):N0}, {UC.FtToMm(point.Y):N0}, {UC.FtToMm(point.Z):N0})mm]");

    private static string FormatEdge(PipeEdgeDefinition edge)
        => FormattableString.Invariant($"({edge.Start.X:N0}, {edge.Start.Y:N0}) → ({edge.End.X:N0}, {edge.End.Y:N0})");

    private static XYZ ToXyz(DHBIMWATER.Core.Geometry.Point2D point, double elevation, DHBIMWATER.Core.Geometry.Point2D referencePoint)
        => new(UC.MmToFt(point.X + referencePoint.X), UC.MmToFt(point.Y + referencePoint.Y), UC.MmToFt(elevation));
}
