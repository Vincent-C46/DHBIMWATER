using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Infrastructure.Helpers;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;
// Core.Gis를 통째로 열면 NodeKind가 Core.Piping과 충돌한다. 필요한 형식만 별칭으로 끌어온다.
using PipeAlignmentParameters = DHBIMWATER.Core.Gis.PipeAlignmentParameters;
using BendConnection = DHBIMWATER.Core.Gis.BendConnection;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

/// <summary>
/// 관을 MEP Pipe가 아니라 패밀리 인스턴스로 만든다(docs/39). 관·곡관·T형은 배관 이음쇠(<c>OST_PipeFitting</c>),
/// 인라인 밸브는 배관 밸브류(<c>OST_PipeAccessory</c>) 카테고리이며, 재료표 공유 매개변수는 두 카테고리에 모두 바인딩된다(docs/133).
/// 절점에는 곡관(90°/45°)·T형을 자동 배치하고, 그 부속의 커넥터 위치를 실측해 양쪽 관을 그만큼 뗀다.
/// <para>MEP 경로(<see cref="RevitPipeMepCommandRepo"/>)는 그대로 남겨 두고 <see cref="OutputMode"/>로 갈린다.</para>
/// </summary>
// partial — 바이패스 세트 배치는 RevitPipeSegmentCommandRepo.BypassSet.cs에 있다(docs/228 § C). 헬퍼를 같이 쓰려고 같은 클래스로 둔다.
internal sealed partial class RevitPipeSegmentCommandRepo : IPipeCommandRepo
{
    private const double ConnectionToleranceMm = 1;
    /// <summary>이음관을 밸브 커넥터에 밀착시키는 허용 거리(mm, docs/175 § D-2). 캔버스는 실척이 아니라 사용자가
    /// 밸브 옆에 정확히 붙여 찍을 수 없다 — 이음관 구간이 밸브 스팬과 겹치거나 이 거리 안이면 밸브 경계에 이어 붙인다.</summary>
    private const double JointSnapToleranceMm = 50;
    /// <summary>절점 부속의 실제 커넥터 방향이 캔버스 레그 방향과 이 각도를 넘게 벌어지면, 직경·각도가
    /// 요청대로 구동되지 않은 것으로 보고 그 부속을 건너뛴다. 이 커넥터를 배치 앵커로 쓰면 직관이
    /// 캔버스와 다른 각도로 놓이기 때문이다(docs/145 § 4-4).</summary>
    private const double LegAlignmentToleranceDeg = 1.0;
    private sealed record PlacedPart(FamilyInstance Instance, string Role);
    /// <summary>사전검사에서 정해지는 "이번 실행에서 쓸 수 없는 부속" 목록(docs/145 § 4-3).
    /// 관(직관·단관)은 여기 대상이 아니다 — 관이 못 놓이면 네트워크 자체가 성립하지 않으므로 예외로 중단한다.</summary>
    private sealed record SkipPlan(string? BendReason, string? TeeReason, string? CrossReason, string? WyeReason, IReadOnlyDictionary<string, string> ValveReasons,
        // 밸브부관 B(docs/225 § D). 곡관과 패밀리가 달라 사유도 따로 든다 — 곡관이 못 쓰여도 밸브부관은 놓일 수 있다.
        string? ValveBendReason = null,
        // 밸브부관 A(docs/227 § D). T형과 같은 이유로 따로 든다.
        string? ValveTeeReason = null,
        // 바이패스 세트(docs/228 § C-5). 밸브부관 A·B 중 하나라도 못 쓰면 세트 전체를 건너뛴다.
        string? BypassSetReason = null);
    /// <summary>밸브부관 A의 수직분기 커넥터 판정 기준(커넥터 방향 Z 성분, docs/227 § D). 역가져오기 픽 저장소와 같은 값이다.</summary>
    private const double VerticalConnectorMinZ = 0.7;
    /// <summary>배관재료표(docs/131)에 쓰는 한 인스턴스의 명칭·규격. 배치 시점에만 알 수 있는 길이·각도를
    /// 여기 담아 두고, 번호는 격점 전체를 본 뒤 § B-4 단계에서 매긴다.
    /// <para><see cref="PlacedPart"/>와 따로 두는 이유: 그쪽은 "커넥터를 연결할 대상" 목록이라
    /// 1포트 부속이 빠져 있는데, 재료표에는 1포트 부속도 반드시 들어가야 한다.</para></summary>
    /// <param name="DiameterMm">호칭지름(`DH_호칭지름`, docs/178 § C). T형·십자는 주관.</param>
    /// <param name="BranchDiameterMm">T형·십자의 분기 구경. 있으면 `DN300×300`으로 적는다.</param>
    /// <param name="LengthMm">직관·단관만 — `DH_연장`에 적을 실제 연장. 패밀리 길이 파라미터에 구동한 값과 같다.</param>
    /// <param name="DeflectionDeg">곡관만 — `DH_곡관규격각`에 적을 편각. 규격 문자열 `D300 45°`와 같은 값이다.</param>
    private sealed record MaterialPart(FamilyInstance Instance, string Name, string Spec,
        double DiameterMm, double? BranchDiameterMm = null, double? LengthMm = null, double? DeflectionDeg = null);
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
        var document = _document() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        // 같은 (패밀리, 타입)을 호출마다 문서 전체에서 다시 찾지 않는다(docs/206 § A).
        var symbols = new FamilySymbolCache(document);
        // docs/158 § F — 캔버스로 되가져온 원본을 지우고 다시 만든다. 창에서 "원본 삭제 후 생성"을 체크했을 때만 목록이 찬다.
        // 트랜잭션은 UseCase가 이미 열어 두었으므로(CLAUDE.md), 배치 도중 실패하면 이 삭제도 함께 롤백된다.
        var deletedCount = DeleteSourceElements(document, network.SourceElementIdsToDelete);
        var hostLevel = ResolveNearestLevel(document, UC.MmToFt(network.Elevation));
        var families = network.SegmentFamilies
            ?? throw new DhbimwaterException(ErrorCodes.PL_SegmentFamilyMissing, "직관·단관 패밀리 지정이 없습니다. 모델링 창에서 패밀리를 선택하세요.");

        var segment = symbols.Resolve(families.SegmentFamilyTypeName, "관");
        if (string.IsNullOrWhiteSpace(families.LengthParameterName))
            throw new DhbimwaterException(ErrorCodes.PL_LengthParameterMissing, "관 길이 파라미터가 지정되지 않았습니다.");
        if (!double.IsFinite(families.StraightLengthMm) || families.StraightLengthMm <= 0)
            throw new DhbimwaterException(ErrorCodes.PL_InvalidStraightLength, "직관 정척 길이는 0보다 큰 숫자여야 합니다.");

        // 직관·단관은 같은 패밀리를 길이 파라미터로 구동한다. 커넥터 간 거리는 배치 시점에 결정되므로
        // 여기서는 길이로 검증하지 않고, "2포트인가 / 길이를 쓸 수 있는가"만 확인한다.
        ValidateSegmentSymbol(document, segment, families.LengthParameterName);

        // 직경·각도 파라미터를 못 쓰면 부속이 기본 형상·기본 각도로 놓여 관이 통째로 어긋난다. 아무것도 배치하기 전에
        // 한 번에 검사하되(docs/122 §C), 관 실패만 중단이고 곡관·T형·밸브 실패는 그 부속을 건너뛴다(docs/145 § 4-3).
        var skipPlan = ValidateDrivenParameters(document, symbols, network, families, segment);

        var placedInstances = new List<PlacedPart>();
        // 캔버스 Guid(엣지·인라인 부속·절점) → 이번에 만든 ElementId. 창에서 [Revit에서 보기]로 되짚는다(docs/179 § D).
        // PlacedPart를 쓰지 않는 이유: 그쪽은 "커넥터를 연결할 대상" 목록이라 1포트 부속이 빠져 있다(:28 주석).
        var placedIdsByOwner = new Dictionary<Guid, List<long>>();
        void Track(Guid owner, Element? instance)
        {
            if (owner == Guid.Empty || instance is null) return;
            if (!placedIdsByOwner.TryGetValue(owner, out var list))
                placedIdsByOwner[owner] = list = [];
            list.Add(instance.Id.Value);
        }
        var materialParts = new List<MaterialPart>();
        var warnings = new List<string>();
        // 모델링하지 못해 건너뛴 부속. 커밋은 그대로 하고 호출측이 alert로 알린다(docs/145 § 4-7).
        var skipped = new List<PipeConnectionFailure>();
        // 건너뛴 부속 자리. 이 점에서는 커넥터를 서로 연결하지 않는다(docs/145 § 4-6).
        var openPoints = new List<XYZ>();
        // 요청 표고로 끌어올린 보정량(mm). 건수와 최대값만 끝에서 한 줄로 알린다(docs/201 § E).
        // 여기가 0건이면 배치 좌표가 처음부터 요청 표고를 지켰다는 뜻이다.
        var elevationFixes = new List<double>();
        var unusableValves = new Dictionary<string, string>(skipPlan.ValveReasons, StringComparer.Ordinal);
        var flowOrientation = PipeFlowOrientation.Build(network);

        // 1) 절점 부속을 먼저 배치하고 실측 커넥터 원점으로 엣지 배치 축을 정한다.
        var legAnchors = PlaceNodeFittings(document, symbols, network, families, flowOrientation, placedInstances, materialParts, hostLevel, warnings,
            skipPlan, skipped, openPoints, Track, elevationFixes);

        // 2) 엣지마다 구간을 분해해 직관·단관을 놓는다.
        // null은 물리 배관 커넥터가 1개인 비인라인 부속이다. 주관을 자르지 않고 위치만 배치한다.
        // 커넥터 스팬은 직경에 따라 달라진다. 심볼만으로 캐시하면 첫 엣지 직경으로 잰 폭이
        // 다른 직경 엣지에도 재사용돼 관 길이가 조용히 어긋난다(docs/152 § F-2).
        var spanCache = new Dictionary<(ElementId SymbolId, double DiameterMm), InlineSpan>();
        // 인라인 밸브도 관과 같은 축 위에 놓아야 하므로(docs/122 §A) 엣지별 축을 모아 3) 단계에서 다시 쓴다.
        var edgeAxes = new Dictionary<Guid, EdgeAxis>();
        // 부속 Id → 배치 축 위의 거리(mm). 2)에서 스팬을 예약하며 정한 값을 3)이 그대로 쓴다.
        // 이음관은 캔버스 좌표가 밸브와 같으므로(docs/167 § A-3) 이 값 없이는 자리를 알 수 없다.
        var inlineAxisCenters = new Dictionary<Guid, double>();
        // 부속 Id → 2)에서 관 틈으로 예약한 폭(mm). 4) 연결 뒤 실제 폭과 대조한다(docs/224 § A).
        var reservedWidths = new Dictionary<Guid, double>();
        // 2)에서 자리를 잡지 못해 이미 건너뛰기로 기록한 부속. 3)에서 다시 시도하면 alert에 두 번 뜬다.
        var droppedFittings = new HashSet<Guid>();
        // 바이패스 세트 Id → 2)에서 실측한 세트 치수. 3)이 같은 값으로 부재를 놓는다(docs/228 § C).
        var bypassLayouts = new Dictionary<Guid, BypassLayout>();
        var bypassLayoutCache = new Dictionary<BypassLayoutKey, BypassLayout>();
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
            // 배치 축은 항상 수평이다(docs/201 § D-2). D-1이 앵커 Z를 이미 맞춰 두지만, 절점 부속이 없는
            // 폴백 경로와 장차의 회귀를 막으려 여기서 한 번 더 못 박는다 — 축이 기울면 :137 회전(Z축뿐)과
            // 어긋나 부재가 축에서 점점 벗어난다.
            var direction = Direction(new XYZ(endAnchor.X - startAnchor.X, endAnchor.Y - startAnchor.Y, 0));
            var rotation = Math.Atan2(direction.Y, direction.X);
            edgeAxes[edge.Id] = new EdgeAxis(startAnchor, direction);

            var occupied = new List<OccupiedSpan>();
            occupied.AddRange(InlineFittingSpans(document, symbols, edge, network, families, spanCache, startAnchor, direction,
                unusableValves, skipped, inlineAxisCenters, droppedFittings, reservedWidths));
            occupied.AddRange(BypassSetSpans(document, symbols, edge, network, families, skipPlan, startAnchor, direction,
                unusableValves, skipped, inlineAxisCenters, droppedFittings, bypassLayouts, bypassLayoutCache, warnings));

            var plan = PipeSegmentPlan.Build(lengthMm, occupied, families.StraightLengthMm);
            if (plan.Errors.Count > 0)
            {
                var errors = plan.Errors.Distinct().Take(2).ToList();
                var remainingCount = plan.Errors.Distinct().Skip(2).Count();
                if (remainingCount > 0) errors.Add($"그 외 {remainingCount}건");
                throw new DhbimwaterException(ErrorCodes.PL_NodeFittingUnplaceable,
                    $"배관 #{edgeIndex + 1}에 부속을 놓을 수 없습니다.{Environment.NewLine}{Environment.NewLine}" +
                    $"{string.Join(Environment.NewLine, errors)}{Environment.NewLine}{Environment.NewLine}" +
                    $"위치: {FormatEdge(edge)}");
            }
            warnings.AddRange(plan.Warnings.Select(x => $"[{FormatEdge(edge)}] {x}"));

            foreach (var placement in plan.Segments)
            {
                var center = startAnchor + direction * UC.MmToFt(placement.CenterMm);
                var instance = PlaceAt(document, segment, center, rotation, hostLevel, elevationFixes, regenerateAfterRotate: false);
                // 직관·단관이 같은 패밀리이므로 길이 구동과 검증 경로도 하나다.
                // 직관/단관 구분은 패밀리 내부 수식이 정척 길이로 판정하며, 여기서는 집계에만 쓴다.
                // 명칭 문자열은 비고 규칙(PipeMaterialSpecText.Remark)의 키이기도 하므로 같은 상수를 본다(docs/141 § B-2).
                var role = placement.Kind == PipeSegmentKind.Straight
                    ? PipeMaterialSpecText.StraightName
                    : PipeMaterialSpecText.ShortName;
                placedInstances.Add(new PlacedPart(instance, role));
                // 이 엣지가 정척 분할로 나뉜 관 조각 전부가 같은 캔버스 선에 매달린다(docs/179 § D-2).
                Track(edge.Id, instance);
                materialParts.Add(new MaterialPart(instance, role, SegmentSpecText(edge.DiameterMm, placement.LengthMm),
                    edge.DiameterMm, LengthMm: placement.LengthMm));
                // 직경을 먼저 구동한다. 직경이 형상을 바꾸는 패밀리에서, 뒤따르는 길이 구동·커넥터 실측(RecenterOnConnectors,
                // ValidateCenteredSpan)이 최종 형상 기준으로 이뤄져야 하기 때문이다.
                // 관경은 네트워크 전역이 아니라 이 엣지의 값이다(docs/152 § F-1).
                if (!string.IsNullOrWhiteSpace(families.DiameterParameterName))
                    SetDiameterParameterMm(instance, families.DiameterParameterName, edge.DiameterMm, "관 직경");
                SetDoubleParameterMm(instance, families.LengthParameterName, placement.LengthMm, "관 길이");
                try { document.Regenerate(); }
                catch (Exception ex)
                {
                    // 직경·길이 구동으로 패밀리 형상이 깨진 경우다. 관은 건너뛸 수 없으므로(네트워크가 성립하지 않는다) 중단하되,
                    // 어느 엣지·어떤 값이었는지를 붙인다(docs/206 § C).
                    throw new DhbimwaterException(ErrorCodes.PL_SegmentRegenerationFailed,
                        $"배관 #{edgeIndex + 1} 관 조각(직경 {edge.DiameterMm:N0}mm, 길이 {placement.LengthMm:N0}mm)을 구동한 뒤 재생성에 실패했습니다. " +
                        $"패밀리 '{families.SegmentFamilyTypeName}'가 이 값을 받을 수 있는지 확인하세요.{Environment.NewLine}위치: {FormatEdge(edge)}{Environment.NewLine}{ex.Message}", ex);
                }
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
        // 2포트로 배치된 인라인 부속과 배치 직후 폭. 4) 연결 뒤 다시 재서 예약 폭과 대조한다(docs/224 § A).
        var placedInlineWidths = new List<(FamilyInstance Instance, string Role, string FamilyTypeName, double ReservedMm, double PlacedMm)>();
        // 바이패스 세트는 본관 밸브가 없어 FamilyTypeName이 비어 있다(docs/230 § A) — 패밀리명으로 거르면 통째로 빠진다.
        foreach (var fitting in network.InlineFittings.Where(x => x.IsBypassSet || !string.IsNullOrWhiteSpace(x.FamilyTypeName)))
        {
            var edge = network.Edges.FirstOrDefault(x => x.Id == fitting.EdgeId);
            if (edge is null) continue;
            var canvasPoint = ToXyz(fitting.Position, fitting.Elevation, network.ReferencePoint);
            // 2)에서 이미 건너뛰기로 기록한 패밀리·개체다. 여기서 다시 기록하면 alert에 같은 항목이 두 번 뜬다.
            if (unusableValves.ContainsKey(fitting.FamilyTypeName) || droppedFittings.Contains(fitting.Id)) continue;
            // 바이패스 세트는 부재 여러 개를 한 번에 놓는 전용 경로다(docs/228 § C-3).
            if (fitting.IsBypassSet)
            {
                if (bypassLayouts.TryGetValue(fitting.Id, out var layout) && edgeAxes.TryGetValue(edge.Id, out var setAxis))
                    PlaceBypassSet(document, symbols, network, families, edge, fitting, setAxis, inlineAxisCenters[fitting.Id], layout,
                        segment, hostLevel, placedInstances, materialParts, openPoints, skipped, warnings, elevationFixes, Track);
                continue;
            }

            // 이음관은 밸브와 역할·직경 파라미터가 다르다(docs/167 § C·§ D). 구분은 TypeKey 하나다(docs/175 § C).
            var isJoint = fitting.IsJoint;
            var partRole = isJoint ? "이음관" : "배관부속";

            FamilyInstance? instance = null;
            try
            {
                var symbol = symbols.Resolve(fitting.FamilyTypeName, partRole);
                // 축이 없는 엣지(길이 0으로 건너뛴 경우)는 캔버스 좌표를 그대로 쓴다.
                var axis = edgeAxes.TryGetValue(edge.Id, out var found)
                    ? found
                    : new EdgeAxis(ToXyz(edge.Start, edge.Elevation, network.ReferencePoint),
                        (ToXyz(edge.End, edge.Elevation, network.ReferencePoint) - ToXyz(edge.Start, edge.Elevation, network.ReferencePoint)).Normalize());
                // 2)가 스팬을 예약하며 정한 축 거리를 그대로 쓴다. 다시 정사영하면 이음관이 밸브 위에 겹친다.
                var point = axis.PointAt(inlineAxisCenters.TryGetValue(fitting.Id, out var axisDistanceMm)
                    ? axisDistanceMm
                    : AxisDistanceMm(canvasPoint, axis));
                instance = PlaceAt(document, symbol, point, Math.Atan2(axis.Direction.Y, axis.Direction.X), hostLevel, elevationFixes, regenerateAfterRotate: false);
                var connectorsBeforeDriving = GetMepConnectors(instance).Count;
                var valveDiameterParameterName = isJoint
                    ? families.JointDiameterParameterName
                    : ValveDiameterParameterNameFor(families, symbol);
                // 밸브·이음관 직경은 올라탄 엣지의 관경을 따른다(docs/152 § F-2).
                SetOptionalDiameterParameter(instance, valveDiameterParameterName, edge.DiameterMm, partRole, warnings);
                document.Regenerate();
                var connectors = GetMepConnectors(instance);
                if (connectors.Count == 1 && !isJoint)
                {
                    // 공기밸브처럼 주관을 관통하는 2포트가 아닌 부속은 위치만 배치한다.
                    // Tee/분기관 없이 주관 커넥터 2개와 한 점에서 연결하면 3중 접점이 되어 유효한 Revit 연결이 아니다.
                    singlePortCount++;
                }
                else if (connectors.Count == 2)
                {
                    ValidateCenteredSpan(instance, point, ConnectorSpanMm(connectors), partRole);
                    placedInstances.Add(new PlacedPart(instance, partRole));
                    if (reservedWidths.TryGetValue(fitting.Id, out var reservedMm))
                        placedInlineWidths.Add((instance, partRole, fitting.FamilyTypeName, reservedMm, ConnectorSpanMm(connectors)));
                }
                else
                {
                    // 이음관은 2)에서 2포트임을 확인하고 스팬을 예약했다. 여기서 1포트가 나오면 관에 빈 자리가 남는다.
                    SkipValve(instance, BuildConnectorCountMessage(
                        instance, $"{partRole} '{fitting.FamilyTypeName}'", connectors.Count,
                        BuildConnectorInstruction(connectorsBeforeDriving is 1 or 2, connectorsBeforeDriving, edge.DiameterMm,
                            [valveDiameterParameterName], isJoint
                                ? "이음관은 2포트 인라인 배치만 지원합니다."
                                : "1포트 위치 배치 또는 2포트 인라인 배치만 지원합니다.")));
                    continue;
                }
                // 커넥터 수와 무관하게 재료표에는 들어가야 한다. 1포트 부속(공기밸브 등)은 placedInstances에
                // 담기지 않으므로 여기서 따로 모으지 않으면 재료표에서 통째로 빠진다(docs/131 § B-4).
                // 건너뛴 밸브가 재료표에 남지 않도록 커넥터 판정 뒤에 담는다(docs/145 § 4-5).
                materialParts.Add(new MaterialPart(instance, FittingNameOf(instance), ValvePipingParameters.DiameterText(edge.DiameterMm), edge.DiameterMm));
                // 하이라이트 매핑도 같은 이유로 여기서 건다 — 1포트·2포트 부속이 모두 지나는 유일한 지점이다(docs/179 § D-2).
                Track(fitting.Id, instance);
            }
            catch (Exception ex)
            {
                SkipValve(instance, ex.Message);
            }

            void SkipValve(FamilyInstance? placed, string reason)
            {
                var elementId = placed?.Id.Value ?? 0;
                if (placed is not null)
                {
                    // 2)에서 스팬을 예약해 둔 자리이므로 관에 빈 구간이 남는다. 그 끝의 커넥터는 열린 채로 두고
                    // 서로 연결하지 않는다 — 관 두 개가 밸브 없이 물리면 안 된다(docs/145 § 4-6).
                    foreach (var connector in GetMepConnectors(placed)) openPoints.Add(connector.Origin);
                    try { document.Delete(placed.Id); document.Regenerate(); }
                    catch (Exception deleteEx)
                    {
                        // 재생성 실패로 이미 무효화된 인스턴스는 Delete도 던진다. 여기서 던지면 "부속 하나만 건너뛴다"는
                        // 설계(docs/145 § 4-6)가 무너져 전체가 롤백된다 — 경고로만 남긴다(docs/206 § E).
                        warnings.Add($"{partRole} '{fitting.FamilyTypeName}'(ElementId {elementId})를 건너뛰었지만 인스턴스를 지우지 못했습니다: {deleteEx.Message}");
                    }
                }
                // 같은 패밀리의 다른 밸브는 각자 다시 시도한다 — 개체마다 alert에 남아야 하기 때문이다.
                skipped.Add(CreateFailure(canvasPoint, partRole, fitting.FamilyTypeName, elementId, reason, network));
            }
        }

        // 4) 접점이 일치하는 물리 배관 커넥터를 실제로 연결한다. 위치만 맞닿은 패밀리는 Revit 관망으로 연결되지 않는다.
        ConnectCoincidentConnectors(document, placedInstances, network, openPoints, skipped, warnings);
        WarnInlineWidthMismatches(document, placedInlineWidths, warnings);

        // 5) 격점상세도 배관재료표용 정보 파라미터를 한 번에 기록한다(docs/131 § B-4).
        // 번호가 격점 전체(명칭+규격 조합)를 보고 정해지므로 배치 즉시가 아니라 여기서 매긴다.
        RecordMaterialParameters(document, materialParts, network, warnings);

        if (singlePortCount > 0)
            warnings.Add($"1포트 배관부속 {singlePortCount}개는 주관을 분할하거나 연결하지 않고 지정 위치에 배치했습니다.");
        // 표고 보정은 조용히 넘기지 않는다 — 예전에는 RecenterOnConnectors가 Z 오차까지 지워
        // 틀린 표고가 검증을 통과한 채 커밋됐다(docs/201 § E). 부재별로 적지 않고 한 줄로 요약한다.
        if (elevationFixes.Count > 0)
            warnings.Add(FormattableString.Invariant(
                $"표고 보정: 부재 {elevationFixes.Count}개를 요청 표고 {network.Elevation:N1}mm로 끌어올렸습니다(최대 {elevationFixes.Max():N1}mm). 해당 패밀리가 작업기준면 기반이면 배치할 때마다 보정이 일어납니다."));
        var summary = $"직관 {straightCount}본 · 단관 {shortCount}본을 배치했습니다."
            + (deletedCount > 0 ? $" 원본 {deletedCount}개를 삭제했습니다." : "")
            + (skipped.Count > 0 ? $" 건너뛴 부속 {skipped.Count}개." : "");
        return new PipeCreationResult(summary, warnings)
        {
            Skipped = skipped,
            PlacedElementIds = placedIdsByOwner.ToDictionary(x => x.Key, x => (IReadOnlyList<long>)x.Value),
        };
    }

    /// <summary>캔버스로 되가져온 원본을 지운다(docs/158 § F). 이미 없는 Id는 조용히 건너뛴다 —
    /// 사용자가 그 사이 Revit에서 직접 지웠을 수 있고, 그것 때문에 생성 전체가 실패하면 안 된다.
    /// <para>배관 부속·배관 액세서리·배관 카테고리만 지운다(docs/237 § C) — Id 숫자만 보면 다른 문서·다른 부재(벽·치수 등)를
    /// 지울 수 있다. 역가져오기 피킹 필터(RevitValveRoomPipingPickRepo)와 생성 경로가 만드는 카테고리다. 그 밖은 조용히 건너뛴다.</para></summary>
    private static int DeleteSourceElements(Document document, IReadOnlyList<long>? elementIds)
    {
        if (elementIds is null || elementIds.Count == 0) return 0;

        var targets = elementIds.Distinct()
            .Select(x => new ElementId(x))
            .Where(x => document.GetElement(x) is { Category: { } category }
                && category.Id.Value is (long)BuiltInCategory.OST_PipeFitting
                    or (long)BuiltInCategory.OST_PipeAccessory
                    or (long)BuiltInCategory.OST_PipeCurves)
            .ToList();
        if (targets.Count == 0) return 0;

        document.Delete(targets);
        document.Regenerate();
        return targets.Count;
    }

    private static Guid NodeIdAt(PipeNetworkDefinition network, DHBIMWATER.Core.Geometry.Point2D point)
        => network.Nodes.FirstOrDefault(x => x.Position.DistanceTo(point) <= 0.01)?.Id ?? Guid.Empty;

    /// <summary>절점마다 곡관·T형을 배치하고, 각 레그의 실측 커넥터 원점을 돌려준다.</summary>
    private static Dictionary<(Guid NodeId, Guid EdgeId), NodeLegAnchor> PlaceNodeFittings(
        Document document, FamilySymbolCache symbols, PipeNetworkDefinition network, PipeSegmentFamilySelection families, PipeFlowOrientation flowOrientation,
        ICollection<PlacedPart> placedInstances, ICollection<MaterialPart> materialParts, Level? hostLevel, List<string> warnings,
        SkipPlan skipPlan, ICollection<PipeConnectionFailure> skipped, ICollection<XYZ> openPoints,
        // 절점 Guid → 배치한 인스턴스. static 메서드라 호출측 수집기를 위임으로 받는다(docs/179 § D-2).
        Action<Guid, Element?> track,
        // 표고 보정량 수집기(docs/201 § E). 수평축 180° 회전 보정도 여기에 쌓인다.
        ICollection<double> elevationFixes)
    {
        var legAnchors = new Dictionary<(Guid, Guid), NodeLegAnchor>();
        // 직경이 다른 관이 모인 절점 수. 0보다 크면 경고를 한 번만 남긴다(docs/152 § F-3).
        var reducingNodeCount = 0;
        foreach (var node in network.Nodes)
        {
            var legs = PipeNodeAngle.Legs(node, network.Edges);
            var origin = ToXyz(node.Position, network.Elevation, network.ReferencePoint);
            var isElbow = node.NodeKind == NodeKind.Elbow && legs.Count == 2;
            // 꺾인 절점 중 사용자가 밸브부관으로 바꾼 곳(docs/225 § D). 배치 흐름은 곡관과 같고 패밀리·방향 보정·재료표만 다르다.
            var isValveBend = isElbow && node.IsValveBend;
            var isTee = node.NodeKind == NodeKind.Tee && legs.Count == 3;
            // T 절점 중 사용자가 밸브부관 A로 바꾼 곳(docs/227 § D). 회전은 T형과 같고, 주관·분기 직경을 따로 구동한다.
            var isValveTee = isTee && node.IsValveTee;
            var isCross = node.NodeKind == NodeKind.Cross && legs.Count == 4;
            var isWye = node.NodeKind == NodeKind.Wye && legs.Count == 3;
            // EndPoint·Inline은 부속이 없다. 십자는 docs/175 § D-1, Y형은 docs/187 § 4-2부터 배치한다.
            if (!isElbow && !isTee && !isCross && !isWye) continue;

            // TODO(docs/152 § J): 이경 절점은 재기획 대상이다. T형 주관/분기 직경 분리 지정과
            // "바이패스관처럼 T형 없이 통과" 경우가 확정될 때까지의 잠정 규칙 — 큰 쪽 직경으로 부속을 구동한다.
            var nodeDiameterMm = legs.Max(x => x.Edge.DiameterMm);
            // 밸브부관 A는 원래 주관·분기 구경이 다른 부속이다 — 이경 경고 대상이 아니다(docs/227 § D).
            if (!isValveTee && legs.Select(x => x.Edge.DiameterMm).Distinct().Count() > 1) reducingNodeCount++;
            // 밸브부관 A의 주관(일직선 두 레그)·분기 구경. 레그 순서는 T형 판정과 같은 PipeTeeResolver로 구한다.
            var valveTeeOrder = isValveTee ? PipeTeeResolver.Resolve(node, network.Edges) : null;
            var valveTeeMainMm = valveTeeOrder is null ? nodeDiameterMm
                : legs.Where(x => x.Edge.Id != valveTeeOrder.BranchEdgeId).Max(x => x.Edge.DiameterMm);
            var valveTeeBranchMm = valveTeeOrder is null ? nodeDiameterMm
                : legs.First(x => x.Edge.Id == valveTeeOrder.BranchEdgeId).Edge.DiameterMm;

            var role = isValveBend ? PipeValveBend.PartNameB
                : isValveTee ? PipeValveBend.PartNameA
                : isElbow ? "곡관" : isTee ? "T형" : isWye ? PipeMaterialSpecText.WyeName : PipeMaterialSpecText.CrossName;
            var familyTypeName = isValveBend ? families.ValveBendFamilyTypeName
                : isValveTee ? families.ValveTeeFamilyTypeName
                : isElbow ? families.BendFamilyTypeName
                : isTee ? families.TeeFamilyTypeName
                : isWye ? families.WyeFamilyTypeName : families.CrossFamilyTypeName;
            // 밸브부관 A는 수직분기 커넥터가 가시성과 무관하게 남아 4개다(docs/227 § 0-5).
            var expectedConnectors = isElbow ? 2 : isCross || isValveTee ? 4 : 3;
            FamilyInstance? instance = null;
            // Y형 레그 순서. 규격 문자열의 분기각까지 같은 값을 써야 하므로 판정 결과를 들고 간다(docs/187 § 4-2).
            PipeWyeLegOrder? wyeOrder = null;

            // 사전검사에서 이미 못 쓴다고 판정된 부속은 배치 자체를 시도하지 않는다(docs/145 § 4-3).
            if ((isValveBend ? skipPlan.ValveBendReason
                : isValveTee ? skipPlan.ValveTeeReason
                : isElbow ? skipPlan.BendReason
                : isTee ? skipPlan.TeeReason
                : isWye ? skipPlan.WyeReason : skipPlan.CrossReason) is { } plannedReason)
            {
                Skip(null, plannedReason);
                continue;
            }

            try
            {
                var symbol = symbols.Resolve(familyTypeName, role);
                double targetRotation;
                if (isElbow)
                {
                    // 곡관은 두 레그의 이등분 방향을 기준으로 놓는다. 실제 각 레그 정렬은 커넥터 매칭으로 확인한다.
                    targetRotation = Bisector(legs[0], legs[1]);
                }
                else if (isTee)
                {
                    var order = PipeTeeResolver.Resolve(node, network.Edges);
                    if (order is null)
                    {
                        Skip(null, "직교 T로 판별할 수 없습니다. 주관은 일직선, 분기관은 90°로 그려야 합니다.");
                        continue;
                    }
                    var branch = legs.First(x => x.Edge.Id == order.BranchEdgeId);
                    targetRotation = Math.Atan2(branch.DirectionY, branch.DirectionX);
                }
                else if (isWye)
                {
                    // Y형은 분기 레그 방향을 기준으로 놓는다. 주관 어느 쪽이 예각인지는 회전만으로 정해지지 않으므로
                    // 최종 확인은 ResolveLegAnchors의 커넥터 매칭에 맡긴다(docs/187 § 4-4).
                    wyeOrder = PipeWyeResolver.Resolve(node, network.Edges);
                    if (wyeOrder is null)
                    {
                        Skip(null, "Y형으로 판별할 수 없습니다. 주관은 일직선, 분기는 30~60°로 그려야 합니다.");
                        continue;
                    }
                    var wyeBranch = legs.First(x => x.Edge.Id == wyeOrder.BranchEdgeId);
                    targetRotation = Math.Atan2(wyeBranch.DirectionY, wyeBranch.DirectionX);
                }
                else
                {
                    // 십자는 90° 대칭이라 커넥터 하나를 레그 0에 맞추면 나머지는 ResolveLegAnchors가 1:1로 맞춘다(docs/175 § D-1).
                    if (PipeCrossResolver.Resolve(node, network.Edges) is null)
                    {
                        Skip(null, "직교 십자로 판별할 수 없습니다. 두 주관은 일직선, 서로 90°로 그려야 합니다.");
                        continue;
                    }
                    targetRotation = Math.Atan2(legs[0].DirectionY, legs[0].DirectionX);
                }

                instance = PlaceAt(document, symbol, origin, 0, hostLevel, elevationFixes);
                var connectorsBeforeDriving = GetMepConnectors(instance).Count;
                var diameterParameterName = isValveBend ? families.ValveBendDiameterParameterName
                    : isValveTee ? families.ValveTeeMainDiameterParameterName
                    : isElbow ? families.BendDiameterParameterName
                    : isTee ? families.TeeDiameterParameterName
                    : isWye ? families.WyeDiameterParameterName : families.CrossDiameterParameterName;
                SetOptionalDiameterParameter(instance, diameterParameterName, isValveTee ? valveTeeMainMm : nodeDiameterMm, role, warnings);
                if (isValveTee)
                {
                    // 밸브부관 A는 주관·분기 구경이 원래 다르다(D1650 x D300). T형처럼 큰 쪽 하나로 맞추지 않는다(docs/227 § D).
                    SetOptionalDiameterParameter(instance, families.ValveTeeBranchDiameterParameterName, valveTeeBranchMm, $"{role} 분기", warnings);
                    // 캔버스 분기는 평면이다 — 수평분기만 보이게 한다. 수직분기는 범위 밖이다(docs/227 § 0-7).
                    SetOptionalFlagParameter(instance, families.ValveTeeHorizontalBranchParameterName, true, role, warnings);
                    SetOptionalFlagParameter(instance, families.ValveTeeVerticalBranchParameterName, false, role, warnings);
                }
                if (isTee && !isValveTee)
                    // T형 주·분기 구경 같이 맞춰야 길이 수식 성립. 하나만 바꾸면 형상 깨짐(docs/130 §0).
                    SetOptionalDiameterParameter(instance, families.TeeBranchDiameterParameterName, nodeDiameterMm, "T형 분기", warnings);
                if (isCross)
                    // 십자도 T형과 같은 규칙 — 주·분기 구경을 함께 구동한다(docs/175 § D-1).
                    SetOptionalDiameterParameter(instance, families.CrossBranchDiameterParameterName, nodeDiameterMm, "십자 분기", warnings);
                if (isWye)
                {
                    // Y형도 T형과 같은 규칙으로 주·분기 구경을 함께 구동한다(docs/130 § 0).
                    SetOptionalDiameterParameter(instance, families.WyeBranchDiameterParameterName, nodeDiameterMm, "Y형 분기", warnings);
                    // 분기각은 캔버스 실제 각도를 그대로 쓴다 — 재료표 규격 문자열도 같은 값을 쓴다(docs/187 § 4-2).
                    SetOptionalAngleParameter(instance, families.WyeBranchAngleParameterName, wyeOrder!.BranchAngleDeg, role, warnings);
                }
                // 편각은 각도 파라미터 구동과 재료표 규격 문자열이 같은 값을 써야 하므로 한 번만 구한다(docs/131 § B-3).
                var deflectionDeg = isElbow ? PipeNodeAngle.Deflection(node, network.Edges) : null;
                // 밸브부관은 90° 고정 형상이라 각도를 구동하지 않는다(docs/225 § D). 편각은 레그 정렬 메시지에만 쓴다.
                if (isElbow && !isValveBend && deflectionDeg is not null)
                    SetOptionalAngleParameter(instance, families.BendAngleParameterName, deflectionDeg.Value, role, warnings);
                document.Regenerate();

                var connectors = GetMepConnectors(instance);
                if (connectors.Count != expectedConnectors)
                {
                    Skip(instance, BuildConnectorCountMessage(
                        instance, $"절점 부속 '{symbol.Family.Name} : {symbol.Name}'", connectors.Count,
                        BuildConnectorInstruction(connectorsBeforeDriving == expectedConnectors, connectorsBeforeDriving, nodeDiameterMm,
                            [diameterParameterName,
                                isValveTee ? families.ValveTeeBranchDiameterParameterName
                                    : isTee ? families.TeeBranchDiameterParameterName
                                    : isCross ? families.CrossBranchDiameterParameterName
                                    : isWye ? families.WyeBranchDiameterParameterName : null,
                                isValveBend ? null : isElbow ? families.BendAngleParameterName : isWye ? families.WyeBranchAngleParameterName : null],
                            $"{expectedConnectors}개짜리 배관 밸브류 패밀리를 선택하세요.")));
                    continue;
                }

                // 밸브부관 A는 수직분기 커넥터를 빼고 돌린다 — 섞이면 "나머지와 직교하는 커넥터"로 수직 커넥터가 분기로 잡힌다(docs/227 § D).
                var verticalConnectors = isValveTee ? connectors.Where(IsVerticalConnector).ToList() : [];
                RotateTo(document, instance, origin, isValveTee ? connectors.Except(verticalConnectors).ToList() : connectors,
                    targetRotation, node.NodeKind);
                if (isValveBend)
                    // 밸브부관은 흐름 방향이 아니라 밸브 쪽 레그로 맞춘다 — 비대칭 형상의 기준이 밸브이기 때문이다(docs/225 § D).
                    CorrectValveBendSide(document, instance, origin, targetRotation, node, legs, warnings, elevationFixes);
                else if (isElbow)
                    CorrectElbowSocketDirection(document, instance, origin, targetRotation, node, legs, flowOrientation, warnings, elevationFixes);
                if (isWye)
                    // Y형 분기각은 크기만 있고 좌/우가 없어(PipeWyeResolver.cs:48) 패밀리가 캔버스와 반대쪽으로 분기하면
                    // Z축 회전만으로는 맞출 수 없다 — 거울상이기 때문이다. 분기축으로 180° 돌려 좌우를 맞춘다(docs/190 § A).
                    CorrectWyeBranchSide(document, instance, origin, targetRotation, legs, wyeOrder!, elevationFixes);

                // 회전 후 실측: 레그 방향과 가장 잘 맞는 실제 커넥터가 직관·단관 배치 축의 끝점이다.
                // 커넥터가 레그와 맞지 않으면 그 커넥터를 앵커로 쓸 수 없다 — 직관이 캔버스와 다른 각도로 놓인다(docs/145 § 4-4).
                var anchors = ResolveLegAnchors(instance, origin, legs, deflectionDeg, out var alignmentReason);
                if (anchors is null)
                {
                    // Y형 좌우 반전은 CorrectWyeBranchSide가 이미 시도했다 — 여기까지 왔다는 건 그래도 안 맞았다는 뜻이다(docs/190 § A-4).
                    Skip(instance, isWye
                        ? alignmentReason + " 분기 좌우를 반전해도 맞지 않았습니다 — 주관 두 커넥터가 일직선인지, " +
                          "분기각 파라미터가 실제로 구동되는지 확인하세요."
                        : alignmentReason!);
                    continue;
                }

                // 밸브부관 A의 수직분기 커넥터는 쓰지 않는다. 짝 없는 커넥터를 연결 실패로 보고하지 않게 열린 자리로 둔다(docs/227 § D).
                // 회전 뒤라 Connector를 다시 읽는다 — 원점이 바뀌었다.
                if (isValveTee)
                    foreach (var vertical in GetMepConnectors(instance).Where(IsVerticalConnector)) openPoints.Add(vertical.Origin);

                placedInstances.Add(new PlacedPart(instance, role));
                // 절점 부속은 캔버스에서 직접 고를 수 없지만, 향후 확장을 위해 매핑에 담아 둔다(docs/179 § E-3).
                track(node.Id, instance);
                materialParts.Add(isValveBend
                    // 밸브부관은 배관 부속류지만 품목명을 패밀리명 그대로 쓴다 — PipeJointCountWriter.IsAccessory가 밸브류로 묶어 접합 표기·절단수가 붙지 않는다.
                    // 규격은 주관 x 분기(`D1650xD300`)다. B의 분기는 자기 관경, 주관은 이어진 A의 주관이다(docs/227 § 0-2).
                    ? node.MainDiameterMm is { } bendMainMm
                        ? new MaterialPart(instance, FittingNameOf(instance), PipeMaterialSpecText.ValveBranch(bendMainMm, nodeDiameterMm),
                            bendMainMm, BranchDiameterMm: nodeDiameterMm)
                        : new MaterialPart(instance, FittingNameOf(instance), ValvePipingParameters.DiameterText(nodeDiameterMm), nodeDiameterMm)
                    : isValveTee
                    ? new MaterialPart(instance, FittingNameOf(instance), PipeMaterialSpecText.ValveBranch(valveTeeMainMm, valveTeeBranchMm),
                        valveTeeMainMm, BranchDiameterMm: valveTeeBranchMm)
                    : isElbow
                    ? new MaterialPart(instance, role, BendSpecText(nodeDiameterMm, deflectionDeg), nodeDiameterMm, DeflectionDeg: deflectionDeg)
                    // T형·십자 분기 구경은 현재 주관과 같은 값으로 구동한다(docs/130 § A). 이경티는 범위 밖이다.
                    : new MaterialPart(instance, role,
                        isTee ? TeeSpecText(nodeDiameterMm, nodeDiameterMm)
                            : isWye ? WyeSpecText(nodeDiameterMm, nodeDiameterMm, wyeOrder!.BranchAngleDeg)
                            : CrossSpecText(nodeDiameterMm, nodeDiameterMm),
                        nodeDiameterMm, BranchDiameterMm: nodeDiameterMm));
                foreach (var anchor in anchors) legAnchors[(node.Id, anchor.Key)] = anchor.Value;
            }
            catch (Exception ex)
            {
                // 부속 하나의 실패로 네트워크 전체를 접지 않는다. 이 인스턴스만 지우고 계속 진행한다(docs/145 § 4-4).
                Skip(instance, ex.Message);
            }

            void Skip(FamilyInstance? placed, string reason)
            {
                var elementId = placed?.Id.Value ?? 0;
                var name = placed is null ? familyTypeName : $"{placed.Symbol.Family.Name} : {placed.Symbol.Name}";
                if (placed is not null)
                {
                    try { document.Delete(placed.Id); document.Regenerate(); }
                    catch (Exception deleteEx)
                    {
                        // SkipValve와 같은 이유(docs/206 § E).
                        warnings.Add($"{role} '{name}'(ElementId {elementId})를 건너뛰었지만 인스턴스를 지우지 못했습니다: {deleteEx.Message}");
                    }
                }
                // 절점 원점을 연결 금지 지점으로 남긴다 — 이 자리에 모이는 직관 두 개가 서로 물리면 안 된다(docs/145 § 4-6).
                openPoints.Add(origin);
                skipped.Add(CreateFailure(origin, role, name, elementId, reason, network));
            }
        }

        // 절점마다 쌓으면 경고 목록이 폭주하므로 한 번만 남긴다(docs/152 § F-3).
        if (reducingNodeCount > 0)
            warnings.Add($"직경이 다른 배관이 만나는 절점 {reducingNodeCount}개는 큰 쪽 직경으로 부속을 배치했습니다. " +
                "이경관은 아직 자동 배치하지 않습니다.");
        return legAnchors;
    }

    /// <summary>회전까지 끝난 절점 부속에서 레그별 배치 앵커(실측 커넥터 원점)를 뽑는다.
    /// 커넥터가 레그와 1:1로, <see cref="LegAlignmentToleranceDeg"/> 안에서 맞을 때만 성공이다.
    /// <para>맞지 않는다는 것은 직경·각도가 요청대로 구동되지 않아 부속이 기본 형상으로 남았다는 뜻이고,
    /// 그 커넥터를 앵커로 쓰면 직관이 캔버스와 다른 각도로 놓인 채 커넥터끼리만 맞물린다 — 오류 없이
    /// 틀어진 모델이 커밋되던 경로다(docs/145 § 4-0).</para></summary>
    private static Dictionary<Guid, NodeLegAnchor>? ResolveLegAnchors(
        FamilyInstance instance, XYZ origin, IReadOnlyList<PipeNodeLeg> legs, double? deflectionDeg, out string? reason)
    {
        var connectors = GetMepConnectors(instance);
        var anchors = new Dictionary<Guid, NodeLegAnchor>();
        var used = new HashSet<int>();
        foreach (var leg in legs)
        {
            var legDirection = new XYZ(leg.DirectionX, leg.DirectionY, 0);
            var bestIndex = -1;
            var bestDot = double.NegativeInfinity;
            for (var i = 0; i < connectors.Count; i++)
            {
                if (used.Contains(i)) continue;
                var dot = Direction(connectors[i].Origin - origin).DotProduct(legDirection);
                if (dot <= bestDot) continue;
                bestDot = dot;
                bestIndex = i;
            }
            if (bestIndex < 0)
            {
                reason = $"레그 {legs.Count}개에 1:1로 대응하는 커넥터가 없습니다(배관 커넥터 {connectors.Count}개).";
                return null;
            }
            used.Add(bestIndex);
            var deviationDeg = Math.Acos(Math.Clamp(bestDot, -1, 1)) * 180 / Math.PI;
            if (deviationDeg > LegAlignmentToleranceDeg)
            {
                var deflectionText = deflectionDeg is null ? "" : FormattableString.Invariant($", 요청 편각 {deflectionDeg.Value:N1}°");
                reason = FormattableString.Invariant(
                    $"커넥터 방향이 캔버스 레그와 {deviationDeg:N1}° 어긋납니다(허용 {LegAlignmentToleranceDeg:N1}°{deflectionText}). ") +
                    "직경·각도 파라미터가 이 패밀리에서 실제로 구동되는지, 부속 원점이 레그 교점인지 확인하세요.";
                return null;
            }
            // 앵커 Z는 언제나 요청 표고다(docs/201 § D-1). 패밀리가 커넥터를 다른 Z에 두더라도 그 Z가
            // 엣지 축 방향으로 흘러 들어가 축을 기울이면 안 된다 — 기울면 관 길이(3D 거리)까지 함께 부푼다.
            // 위 방향 판정(bestDot·deviationDeg)은 원본 커넥터 좌표 그대로 둔다. 거기서 Z를 죽이면
            // Z가 틀어진 부속이 검사를 통과해 버린다(docs/145 § 4-4의 그물).
            var connectorOrigin = connectors[bestIndex].Origin;
            var anchorOrigin = new XYZ(connectorOrigin.X, connectorOrigin.Y, origin.Z);
            anchors[leg.Edge.Id] = new NodeLegAnchor(
                UC.FtToMm(anchorOrigin.DistanceTo(origin)), anchorOrigin);
        }
        reason = null;
        return anchors;
    }

    /// <summary>부속의 기준 방향(곡관은 커넥터 이등분, T형은 나머지 둘과 직교하는 분기 커넥터)이
    /// <paramref name="targetRotation"/>을 향하도록 Z축으로 돌린다.</summary>
    private static void RotateTo(Document document, FamilyInstance instance, XYZ origin,
        IReadOnlyList<Connector> connectors, double targetRotation, NodeKind kind)
    {
        var directions = connectors.Select(c => Direction(c.Origin - origin)).ToList();
        var current = kind switch
        {
            NodeKind.Tee => BranchDirection(directions),
            // Y형은 직교 커넥터가 없어 T형 규칙을 쓸 수 없다 — 마주보는 짝을 주관으로 보고 남은 하나가 분기다(docs/187 § 4-4).
            NodeKind.Wye => WyeBranchDirection(directions),
            // 십자는 대칭이라 아무 커넥터나 기준이 된다 — 레그 0에 맞춘다(docs/175 § D-1).
            NodeKind.Cross => Math.Atan2(directions[0].Y, directions[0].X),
            _ => Bisector(directions[0], directions[1])
        };
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
        IReadOnlyList<PipeNodeLeg> legs, PipeFlowOrientation flowOrientation, List<string> warnings,
        ICollection<double> elevationFixes)
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
        // 수평축 180°는 z → 2h − z 로 Z를 튕긴다. 인스턴스가 축과 같은 Z에 있지 않았다면 여기서 날아간다(docs/201 § C).
        ForceElevation(document, instance, origin.Z, elevationFixes);
    }

    /// <summary>밸브부관의 밸브 쪽 커넥터(최소 Id)가 <see cref="PipeNodeDefinition.ValveSideEdgeId"/> 레그를 향하게 한다(docs/225 § D).
    /// <para>규약·보정 방식은 <see cref="CorrectElbowSocketDirection"/>과 같다 — 이등분 수평축으로 180° 돌리면 두 커넥터가 서로 자리를 바꾼다.
    /// 기준 레그만 "상류"가 아니라 "밸브 쪽"이다.</para></summary>
    private static void CorrectValveBendSide(
        Document document, FamilyInstance instance, XYZ origin, double targetRotation, PipeNodeDefinition node,
        IReadOnlyList<PipeNodeLeg> legs, List<string> warnings, ICollection<double> elevationFixes)
    {
        var leg = legs.FirstOrDefault(x => x.Edge.Id == node.ValveSideEdgeId);
        if (leg.Edge is null)
        {
            // 캔버스 검증이 막으므로 정상 경로로는 오지 않는다. 방어적으로 경고만 남기고 회전 결과를 그대로 둔다.
            warnings.Add($"밸브부관 방향 보정을 건너뛰었습니다: 절점 ({node.Position.X:N0}, {node.Position.Y:N0})mm에서 밸브 쪽 레그를 정할 수 없습니다.");
            return;
        }

        var connectors = GetMepConnectors(instance).OrderBy(x => x.Id).ToList();
        if (connectors.Count != 2 || connectors[0].Id == connectors[1].Id)
        {
            warnings.Add($"밸브부관 방향 보정을 건너뛰었습니다: 절점 ({node.Position.X:N0}, {node.Position.Y:N0})mm의 커넥터가 구분 가능한 2개가 아닙니다.");
            return;
        }

        var legDirection = new XYZ(leg.DirectionX, leg.DirectionY, 0);
        if (Direction(connectors[0].Origin - origin).DotProduct(legDirection) >= 0) return;
        var axisDirection = new XYZ(Math.Cos(targetRotation), Math.Sin(targetRotation), 0);
        ElementTransformUtils.RotateElement(document, instance.Id, Line.CreateBound(origin, origin + axisDirection), Math.PI);
        document.Regenerate();
        // 수평축 180°는 Z를 튕긴다 — 곡관 보정과 같은 이유로 표고를 다시 고정한다(docs/201 § C).
        ForceElevation(document, instance, origin.Z, elevationFixes);
    }

    /// <summary>
    /// Y형 분기가 캔버스와 반대쪽에 있으면 분기축으로 180° 돌려 거울상을 만든다(docs/190 § A).
    /// <para>분기각 파라미터는 크기(30~60°)만 받으므로 패밀리는 자기 고정된 쪽으로만 분기를 만든다.
    /// 캔버스 분기가 반대쪽이면 Z축 회전으로는 주관이 <c>2 × 분기각</c>만큼 어긋난 채로 남는다 —
    /// 45° 분기에서 정확히 90.0°로 나오던 실패가 이것이다.</para>
    /// <para>축은 <paramref name="targetRotation"/> 방향(= RotateTo가 이미 정렬해 둔 분기)이다.
    /// 축 위의 분기는 제자리에 있고 주관 둘만 축선에 대해 반사된다.
    /// 평면 부속이라 Z 뒤집힘은 형상에 영향이 없고, 주관 축 대칭이라 소켓 암/수 순서도 보존된다.</para>
    /// </summary>
    private static void CorrectWyeBranchSide(Document document, FamilyInstance instance, XYZ origin,
        double targetRotation, IReadOnlyList<PipeNodeLeg> legs, PipeWyeLegOrder order,
        ICollection<double> elevationFixes)
    {
        var connectors = GetMepConnectors(instance);
        if (connectors.Count != 3) return;      // 개수 검증은 배치 직후에 이미 끝났다. 방어적으로만 둔다.

        var directions = connectors.Select(c => Direction(c.Origin - origin)).ToList();
        var branchIndex = WyeBranchIndex(directions);
        var branch = directions[branchIndex];
        // 예각측 주관 = 분기와 내적이 가장 큰(양수인) 쪽. 캔버스의 MainEdge1(예각측)과 짝이 맞는 기준이다.
        var mainIndex = Enumerable.Range(0, 3)
            .Where(i => i != branchIndex)
            .OrderByDescending(i => directions[i].DotProduct(branch))
            .First();

        var canvasBranch = legs.First(x => x.Edge.Id == order.BranchEdgeId);
        var canvasMain = legs.First(x => x.Edge.Id == order.MainEdge1Id);
        var canvasSide = Cross2D(
            new XYZ(canvasMain.DirectionX, canvasMain.DirectionY, 0),
            new XYZ(canvasBranch.DirectionX, canvasBranch.DirectionY, 0));
        var familySide = Cross2D(directions[mainIndex], branch);

        // 한쪽이라도 0에 가까우면(주관과 분기가 일직선) 좌우를 가릴 수 없다 — 손대지 않고 ResolveLegAnchors 판정에 맡긴다.
        // 그런 절점은 애초에 PipeWyeResolver가 Y형으로 통과시키지 않는다.
        if (Math.Abs(canvasSide) < 1e-6 || Math.Abs(familySide) < 1e-6) return;
        if (Math.Sign(canvasSide) == Math.Sign(familySide)) return;

        var axis = new XYZ(Math.Cos(targetRotation), Math.Sin(targetRotation), 0);
        ElementTransformUtils.RotateElement(document, instance.Id, Line.CreateBound(origin, origin + axis), Math.PI);
        document.Regenerate();
        // 분기축도 수평이다 — 위 주석의 "Z 뒤집힘은 형상에 영향이 없다"는 인스턴스가 축과 같은 Z에 있을 때만 성립한다(docs/201 § C).
        ForceElevation(document, instance, origin.Z, elevationFixes);
    }

    /// <summary>평면 외적의 z 성분. 부호가 좌/우를 가른다.</summary>
    private static double Cross2D(XYZ a, XYZ b) => a.X * b.Y - a.Y * b.X;

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

    /// <summary>Y형의 분기 커넥터 방향(docs/187 § 4-4).</summary>
    private static double WyeBranchDirection(IReadOnlyList<XYZ> directions)
    {
        if (directions.Count != 3) return Math.Atan2(directions[0].Y, directions[0].X);
        var branch = directions[WyeBranchIndex(directions)];
        return Math.Atan2(branch.Y, branch.X);
    }

    /// <summary>Y형 커넥터 셋 중 분기의 인덱스. 주관 두 커넥터는 서로 정반대(내적 ≈ -1)이므로,
    /// 내적이 -1에 가장 가까운 짝을 주관으로 보고 남은 하나가 분기다(docs/187 § 4-4).
    /// <para>회전 기준(<see cref="WyeBranchDirection"/>)과 좌우 판정(<see cref="CorrectWyeBranchSide"/>)이
    /// 반드시 같은 커넥터를 분기로 봐야 해서 규칙을 여기 한곳에 둔다(docs/190 § A-3).</para></summary>
    private static int WyeBranchIndex(IReadOnlyList<XYZ> directions)
    {
        var pairs = new[] { (A: 0, B: 1, Branch: 2), (A: 0, B: 2, Branch: 1), (A: 1, B: 2, Branch: 0) };
        return pairs.OrderBy(x => directions[x.A].DotProduct(directions[x.B])).First().Branch;
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

    /// <summary>이 밸브 심볼에 쓸 직경 파라미터명. 패밀리별 사전에서 찾는다(docs/135 § B).
    /// <para>사전이 있는데 이 패밀리가 없으면 <b>null(구동 안 함)</b>이다(docs/232 § C-3) — 종전에는 4번 섹션에 떠 있는 다른 패밀리의
    /// <see cref="PipeSegmentFamilySelection.ValveDiameterParameterName"/>을 빌려 썼고, 바이패스 밸브가 엉뚱한 파라미터로 검사돼 세트 전체가 건너뛰어졌다.
    /// 창은 표시 중인 밸브·바이패스 밸브 패밀리를 항상 사전에 넣으므로 사전 미스는 "사용자가 빈 값으로 지운 패밀리"뿐이다.
    /// 사전 자체가 없는 호출부(구형 설정)만 단일 지정으로 내려간다.</para>
    /// <para>실배치와 스팬 실측이 반드시 같은 값을 써야 한다 — 어긋나면 실측한 밸브 폭과 실제 형상이 달라져
    /// 관 조각 길이가 어긋나고 커넥터가 연결되지 않는다.</para></summary>
    private static string? ValveDiameterParameterNameFor(PipeSegmentFamilySelection families, FamilySymbol symbol)
        => families.ValveDiameterParameterNamesByFamily is { } byFamily
            ? byFamily.TryGetValue(symbol.Family.Name, out var name) ? name : null
            : families.ValveDiameterParameterName;

    /// <summary>인라인 밸브가 축 위에서 차지하는 구간. 커넥터 2개 사이 길이를 실측해 쓴다.
    /// 구간 중심은 캔버스 점을 배치 축(<paramref name="startAnchor"/>, <paramref name="direction"/>)에
    /// 정사영해 잡는다 — 관 조각과 같은 축 좌표라야 커넥터가 맞물린다(docs/122 §A).
    /// <para>지연 열거(yield)를 쓰지 않는 이유: 실측 실패를 try/catch로 잡아 그 밸브만 건너뛰어야 하는데
    /// yield가 있는 메서드에서는 catch로 감쌀 수 없다(docs/145 § 4-5). 건너뛴 밸브는 스팬을 예약하지 않으므로
    /// 직관이 그 자리를 연속으로 채운다.</para></summary>
    private static List<OccupiedSpan> InlineFittingSpans(
        Document document, FamilySymbolCache symbols, PipeEdgeDefinition edge, PipeNetworkDefinition network,
        PipeSegmentFamilySelection families,
        Dictionary<(ElementId SymbolId, double DiameterMm), InlineSpan> spanCache, XYZ startAnchor, XYZ direction,
        IDictionary<string, string> unusableValves, ICollection<PipeConnectionFailure> skipped,
        IDictionary<Guid, double> axisCenters, ISet<Guid> droppedFittings, IDictionary<Guid, double> reservedWidths)
    {
        var axis = new EdgeAxis(startAnchor, direction);
        var spans = new List<OccupiedSpan>();
        // 밸브 본체가 차지한 구간. 이음관은 이 경계에 그대로 이어 붙는다(docs/167 § B).
        var valveSpans = new Dictionary<Guid, OccupiedSpan>();

        // 1단 — 밸브 본체. 캔버스 점을 축에 정사영해 중심을 잡고 커넥터 간 거리로 폭을 실측한다.
        // 바이패스 세트는 BypassSetSpans가 따로 예약한다(docs/228 § C-2) — 본관 밸브 폭만 예약하면 A·채움관 자리가 빈다.
        foreach (var fitting in network.InlineFittings.Where(x => x.EdgeId == edge.Id
                     && !x.IsJoint && !x.IsBypassSet && !string.IsNullOrWhiteSpace(x.FamilyTypeName)))
        {
            var canvasPoint = ToXyz(fitting.Position, fitting.Elevation, network.ReferencePoint);
            if (unusableValves.TryGetValue(fitting.FamilyTypeName, out var plannedReason))
            {
                skipped.Add(CreateFailure(canvasPoint, "배관부속", fitting.FamilyTypeName, 0, plannedReason, network));
                droppedFittings.Add(fitting.Id);
                continue;
            }
            try
            {
                var symbol = symbols.Resolve(fitting.FamilyTypeName, "배관부속");
                var cacheKey = (symbol.Id, edge.DiameterMm);
                if (!spanCache.TryGetValue(cacheKey, out var measured))
                    spanCache[cacheKey] = measured = InlineConnectorSpanMm(
                        document, symbol, ValveDiameterParameterNameFor(families, symbol), edge.DiameterMm, "배관부속");
                var widthMm = measured.WidthMm;
                var center = AxisDistanceMm(canvasPoint, axis);
                axisCenters[fitting.Id] = center;
                if (widthMm is null) continue; // 1포트는 주관을 자르지 않는 위치 배치다(밸브는 이것이 정상 경우다).
                var span = new OccupiedSpan(center - widthMm.Value / 2, center + widthMm.Value / 2);
                valveSpans[fitting.Id] = span;
                reservedWidths[fitting.Id] = widthMm.Value;
                spans.Add(span);
            }
            catch (Exception ex)
            {
                // 같은 패밀리는 3) 실배치에서도 쓸 수 없다. 실측과 실배치가 반드시 같은 판단을 써야 한다.
                unusableValves[fitting.FamilyTypeName] = ex.Message;
                skipped.Add(CreateFailure(canvasPoint, "배관부속", fitting.FamilyTypeName, 0, ex.Message, network));
                droppedFittings.Add(fitting.Id);
            }
        }

        // 2단 — 이음관. 자기 캔버스 점을 축에 정사영해 중심을 잡되, 밸브 스팬과 겹치거나 가까우면 밸브 경계에 밀착한다(docs/175 § D-2).
        // 폭은 `이음관_구경`을 엣지 관경으로 구동한 뒤 실측한 값이므로 패밀리가 계산한 `이음관_길이`를 그대로 따른다.
        foreach (var fitting in network.InlineFittings.Where(x => x.EdgeId == edge.Id
                     && x.IsJoint && !string.IsNullOrWhiteSpace(x.FamilyTypeName)))
        {
            var canvasPoint = ToXyz(fitting.Position, fitting.Elevation, network.ReferencePoint);
            if (unusableValves.TryGetValue(fitting.FamilyTypeName, out var plannedReason))
            {
                skipped.Add(CreateFailure(canvasPoint, "이음관", fitting.FamilyTypeName, 0, plannedReason, network));
                droppedFittings.Add(fitting.Id);
                continue;
            }
            try
            {
                var symbol = symbols.Resolve(fitting.FamilyTypeName, "이음관");
                var cacheKey = (symbol.Id, edge.DiameterMm);
                if (!spanCache.TryGetValue(cacheKey, out var measured))
                    spanCache[cacheKey] = measured = InlineConnectorSpanMm(
                        document, symbol, families.JointDiameterParameterName, edge.DiameterMm, "이음관");
                var widthMm = measured.WidthMm;
                if (widthMm is null)
                    // 밸브와 달리 이음관은 1포트를 허용하지 않는다. 패밀리 문제인지 구동 문제인지 가리도록 실측 진단을 붙인다(docs/190 § B-2).
                    throw new DhbimwaterException(ErrorCodes.PL_ConnectorCountMismatch,
                        "배관 커넥터가 1개입니다. 이음관은 관을 통과하는 2포트 패밀리여야 합니다."
                        + Environment.NewLine + measured.Diagnostics);

                var center = AxisDistanceMm(canvasPoint, axis);
                var half = widthMm.Value / 2;
                var span = SnapToValve(center, widthMm.Value, valveSpans.Values)
                           ?? new OccupiedSpan(center - half, center + half);
                axisCenters[fitting.Id] = (span.StartMm + span.EndMm) / 2;
                reservedWidths[fitting.Id] = widthMm.Value;
                spans.Add(span);
            }
            catch (Exception ex)
            {
                unusableValves[fitting.FamilyTypeName] = ex.Message;
                skipped.Add(CreateFailure(canvasPoint, "이음관", fitting.FamilyTypeName, 0, ex.Message, network));
                droppedFittings.Add(fitting.Id);
            }
        }
        return spans;
    }

    /// <summary>연결 뒤 인라인 부속 폭을 다시 재서 2)에서 예약한 틈과 다르면 경고하고 3D 뷰에 적색으로 표시한다(docs/224 § A).
    /// <para>3)의 <see cref="ValidateCenteredSpan"/>은 기대 길이로 자기 실측값을 받으므로 폭 불일치를 잡지 못한다.
    /// 예약 폭은 원점에 놓은 프로브로 잰 값이라 실제 배치·연결 후 폭과 다를 수 있다 — 게이트밸브 300A가 예약 186mm,
    /// 실제 356mm로 관과 겹친 채 커밋됐다. 원인이 확정될 때까지 배치는 바꾸지 않고 세 값(예약·배치 직후·연결 후)을 알린다.</para></summary>
    private static void WarnInlineWidthMismatches(
        Document document,
        IReadOnlyList<(FamilyInstance Instance, string Role, string FamilyTypeName, double ReservedMm, double PlacedMm)> placed,
        List<string> warnings)
    {
        if (placed.Count == 0) return;
        document.Regenerate();
        View3D? view = null;
        var solidFillPatternId = ElementId.InvalidElementId;
        var viewResolved = false;
        foreach (var item in placed)
        {
            if (!item.Instance.IsValidObject) continue;
            var connectors = GetMepConnectors(item.Instance);
            if (connectors.Count != 2) continue;
            var connectedMm = ConnectorSpanMm(connectors);
            if (Math.Abs(connectedMm - item.ReservedMm) <= ConnectionToleranceMm) continue;

            warnings.Add($"{item.Role} '{item.FamilyTypeName}'(ElementId {item.Instance.Id.Value}) 폭이 예약한 틈과 다릅니다 — " +
                         $"예약 {item.ReservedMm:N0}mm · 배치 직후 {item.PlacedMm:N0}mm · 연결 후 {connectedMm:N0}mm. " +
                         "관과 겹치거나 틈이 남았을 수 있습니다(3D 뷰 적색 표시).");
            if (!viewResolved)
            {
                view = BendOverrideGraphics.ResolveOverrideView(document);
                solidFillPatternId = BendOverrideGraphics.FindSolidFillPatternId(document);
                viewResolved = true;
                if (view is null) warnings.Add("폭이 다른 인라인 부속을 표시할 3D 뷰를 찾지 못해 적색 표시를 건너뛰었습니다.");
            }
            if (view is null) continue;
            try
            {
                // 색·채움은 곡관 허용 초과 표시(BendOverrideGraphics.ApplyExceededOverride)와 같게 맞춘다. 그쪽 경고 문구가 곡관 전용이라 여기서 직접 건다.
                var red = new Color(229, 57, 53);
                var settings = new OverrideGraphicSettings().SetProjectionLineColor(red).SetSurfaceForegroundPatternColor(red);
                if (solidFillPatternId != ElementId.InvalidElementId) settings.SetSurfaceForegroundPatternId(solidFillPatternId);
                view.SetElementOverrides(item.Instance.Id, settings);
            }
            catch (Exception ex) { warnings.Add($"ElementId {item.Instance.Id.Value}: 3D 뷰 적색 표시를 적용하지 못했습니다({ex.Message})."); }
        }
    }

    /// <summary>이음관 밀착 규칙(docs/175 § D-2). 이음관 구간 [center±width/2]가 어느 밸브 스팬과 겹치거나
    /// <see cref="JointSnapToleranceMm"/> 안에 있으면 그 밸브의 가까운 쪽 경계에 폭만큼 이어 붙인 구간을 돌려준다.
    /// 후보가 여럿이면 틈이 가장 작은 밸브. 후보가 없으면 null — 그때는 찍은 자리를 그대로 쓴다.
    /// 이음관끼리의 밀착은 하지 않는다(범위 밖).</summary>
    private static OccupiedSpan? SnapToValve(double centerMm, double widthMm, IEnumerable<OccupiedSpan> valves)
    {
        OccupiedSpan? best = null;
        var bestGap = double.MaxValue;
        foreach (var valve in valves)
        {
            // 이음관이 밸브 앞(시작쪽)에 오는 경우와 뒤(끝쪽)에 오는 경우의 틈. 음수면 겹친 것이다.
            var gapBefore = valve.StartMm - (centerMm + widthMm / 2);
            var gapAfter = (centerMm - widthMm / 2) - valve.EndMm;
            // 중심이 밸브 스팬 안이면 가까운 쪽 경계로 보낸다.
            var beforeSide = centerMm <= (valve.StartMm + valve.EndMm) / 2;
            var gap = beforeSide ? gapBefore : gapAfter;
            if (gap > JointSnapToleranceMm || gap < -widthMm) continue;
            var distance = Math.Abs(gap);
            if (distance >= bestGap) continue;
            bestGap = distance;
            best = beforeSide
                ? new OccupiedSpan(valve.StartMm - widthMm, valve.StartMm)
                : new OccupiedSpan(valve.EndMm, valve.EndMm + widthMm);
        }
        return best;
    }

    /// <summary>인라인 부속 실측 결과(docs/190 § B-1). <see cref="WidthMm"/>가 null이면 1포트 위치 배치 부속이다.
    /// <para><see cref="Diagnostics"/>는 실패 안내에 붙일 실측 정보다 — 프로브가 떠 있는 동안에만 만들 수 있어
    /// 성공 경로에서도 함께 만들어 두고, 쓰지 않으면 버린다.</para></summary>
    private readonly record struct InlineSpan(double? WidthMm, string Diagnostics);

    /// <summary>인라인 부속이면 커넥터 간 길이, 1포트 위치 부속이면 <see cref="InlineSpan.WidthMm"/>가 null.
    /// 그 외 커넥터 수는 지원하지 않는다.
    /// <para>커넥터는 직경을 구동한 <b>뒤에</b> 센다. 구동 전 개수를 함께 실측해 두는 이유 —
    /// "패밀리가 원래 1포트"인지 "구동이 형상을 깨뜨려 커넥터가 사라졌다"인지 구분하기 위해서다(docs/190 § B-1).</para></summary>
    private static InlineSpan InlineConnectorSpanMm(
        Document document, FamilySymbol symbol, string? diameterParameterName, double diameterMm, string role)
    {
        var probe = PlaceAt(document, symbol, XYZ.Zero, 0, null);
        try
        {
            var before = GetMepConnectors(probe).Count;      // 직경 구동 전
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
            var diagnostics = BuildConnectorCountMessage(
                probe, $"{role} '{symbol.Family.Name} : {symbol.Name}'", connectors.Count,
                BuildConnectorInstruction(before == 2, before, diameterMm, [diameterParameterName],
                    "1포트 위치 배치 또는 2포트 인라인 배치만 지원합니다."));

            if (connectors.Count == 1) return new InlineSpan(null, diagnostics);
            if (connectors.Count != 2) throw new DhbimwaterException(ErrorCodes.PL_ConnectorCountMismatch, diagnostics);
            ValidateConnectorMidpoint(symbol, connectors, XYZ.Zero, role);
            return new InlineSpan(ConnectorSpanMm(connectors), diagnostics);
        }
        finally
        {
            document.Delete(probe.Id);
            document.Regenerate();
        }
    }

    /// <summary>이번 네트워크에서 실제로 쓰는 패밀리·파라미터 조합을 배치 전에 모두 검사한다(docs/122 §C).
    /// 파라미터명이 비어 있으면 "구동하지 않음"이 정상 설정이므로 검사 대상이 아니다.
    /// <para>관(직관·단관) 실패는 예외로 중단한다 — 관이 못 놓이면 네트워크 자체가 성립하지 않는다.
    /// 곡관·T형·밸브 실패는 예외 대신 "이 부속은 건너뛴다"는 계획으로 돌려준다(docs/145 § 4-3).
    /// 검사 결과를 중단이 아니라 건너뛰기 판단에 쓰는 것일 뿐, 판정 로직은 종전과 같다.</para></summary>
    private static SkipPlan ValidateDrivenParameters(
        Document document, FamilySymbolCache symbols, PipeNetworkDefinition network, PipeSegmentFamilySelection families, FamilySymbol segment)
    {
        var segmentFailures = new List<string>();
        var bendFailures = new List<string>();
        var teeFailures = new List<string>();
        var crossFailures = new List<string>();
        var wyeFailures = new List<string>();
        var valveBendFailures = new List<string>();
        var valveTeeFailures = new List<string>();
        var bypassSetFailures = new List<string>();
        var valveFailures = new Dictionary<string, string>(StringComparer.Ordinal);
        var probeCache = new Dictionary<ElementId, FamilyInstance>();
        try
        {
            // 관 직경만 무차원(숫자) 파라미터를 허용한다(docs/137 § 2).
            Check(segmentFailures, segment, "관", families.DiameterParameterName, "직경", false, allowNumberType: true);

            // 곡관 검사는 곡관으로 남은 꺾인 절점이 있을 때만 한다 — 전부 밸브부관이면 곡관 패밀리가 비어 있어도 된다(docs/225 § D).
            if (network.Nodes.Any(x => x.NodeKind == NodeKind.Elbow && !x.IsValveBend))
            {
                // 패밀리를 못 찾는 것도 이제는 "그 부속을 건너뛰는" 사유다. 곡관 하나 때문에 전체를 접지 않는다.
                if (symbols.TryResolve(families.BendFamilyTypeName, "곡관", out var bend, out var bendReason))
                {
                    Check(bendFailures, bend!, "곡관", families.BendDiameterParameterName, "직경", false);
                    Check(bendFailures, bend!, "곡관", families.BendAngleParameterName, "각도", true);
                }
                else bendFailures.Add($"- {bendReason}");
            }
            if (network.Nodes.Any(x => x.IsValveBend))
            {
                // 밸브부관 B는 90° 고정 형상이라 각도 파라미터를 보지 않는다. 직경만 곡관과 같은 규칙으로 검사한다.
                // 주관구경은 구동하지 않는다 — 규격 매개변수에만 적는다(docs/229 § A).
                if (symbols.TryResolve(families.ValveBendFamilyTypeName, PipeValveBend.PartNameB, out var valveBend, out var valveBendReason))
                    Check(valveBendFailures, valveBend!, PipeValveBend.PartNameB, families.ValveBendDiameterParameterName, "직경", false);
                else valveBendFailures.Add($"- {valveBendReason}");
            }
            if (network.Nodes.Any(x => x.IsValveTee))
            {
                // 밸브부관 A — 주관·분기 직경을 검사한다. 수평·수직분기(예/아니오)는 길이·각도 검사 대상이 아니라 구동 시 경고로만 다룬다.
                if (symbols.TryResolve(families.ValveTeeFamilyTypeName, PipeValveBend.PartNameA, out var valveTee, out var valveTeeReason))
                {
                    Check(valveTeeFailures, valveTee!, PipeValveBend.PartNameA, families.ValveTeeMainDiameterParameterName, "주관 직경", false);
                    Check(valveTeeFailures, valveTee!, PipeValveBend.PartNameA, families.ValveTeeBranchDiameterParameterName, "분기 직경", false);
                }
                else valveTeeFailures.Add($"- {valveTeeReason}");
            }
            // T형 검사는 밸브부관 A가 아닌 T 절점이 있을 때만 한다(docs/227 § D).
            if (network.Nodes.Any(x => x.NodeKind == NodeKind.Tee && !x.IsValveTee))
            {
                if (symbols.TryResolve(families.TeeFamilyTypeName, "T형", out var tee, out var teeReason))
                {
                    Check(teeFailures, tee!, "T형", families.TeeDiameterParameterName, "직경", false);
                    Check(teeFailures, tee!, "T형", families.TeeBranchDiameterParameterName, "분기 직경", false);
                }
                else teeFailures.Add($"- {teeReason}");
            }
            if (network.Nodes.Any(x => x.NodeKind == NodeKind.Cross))
            {
                if (symbols.TryResolve(families.CrossFamilyTypeName, PipeMaterialSpecText.CrossName, out var cross, out var crossReason))
                {
                    Check(crossFailures, cross!, PipeMaterialSpecText.CrossName, families.CrossDiameterParameterName, "직경", false);
                    Check(crossFailures, cross!, PipeMaterialSpecText.CrossName, families.CrossBranchDiameterParameterName, "분기 직경", false);
                }
                else crossFailures.Add($"- {crossReason}");
            }
            if (network.Nodes.Any(x => x.NodeKind == NodeKind.Wye))
            {
                if (symbols.TryResolve(families.WyeFamilyTypeName, PipeMaterialSpecText.WyeName, out var wye, out var wyeReason))
                {
                    Check(wyeFailures, wye!, PipeMaterialSpecText.WyeName, families.WyeDiameterParameterName, "직경", false);
                    Check(wyeFailures, wye!, PipeMaterialSpecText.WyeName, families.WyeBranchDiameterParameterName, "분기 직경", false);
                    // 분기각은 곡관 각도와 같이 선택 항목이다 — 각도 파라미터가 없는 Y형 패밀리도 받아들인다(docs/187 § 4-1).
                    Check(wyeFailures, wye!, PipeMaterialSpecText.WyeName, families.WyeBranchAngleParameterName, "분기각", true);
                }
                else wyeFailures.Add($"- {wyeReason}");
            }

            // 밸브는 패밀리를 섞어 배치할 수 있으므로 심볼마다 그 패밀리의 파라미터명으로 검사한다(docs/135 § B).
            // 이음관도 같은 목록에 있지만 직경 파라미터는 전역 1개를 쓴다(docs/167 § C).
            // 바이패스 세트 — 밸브부관 A·B(docs/228 § C-5). 본관 밸브는 아래 밸브 검사에 이미 들어가고, 바이패스 밸브는 거기 덧붙인다.
            if (network.InlineFittings.Any(x => x.IsBypassSet))
            {
                if (symbols.TryResolve(families.ValveTeeFamilyTypeName, PipeValveBend.PartNameA, out var setTee, out var setTeeReason))
                {
                    Check(bypassSetFailures, setTee!, PipeValveBend.PartNameA, families.ValveTeeMainDiameterParameterName, "주관 직경", false);
                    Check(bypassSetFailures, setTee!, PipeValveBend.PartNameA, families.ValveTeeBranchDiameterParameterName, "분기 직경", false);
                }
                else bypassSetFailures.Add($"- {setTeeReason}");
                if (symbols.TryResolve(families.ValveBendFamilyTypeName, PipeValveBend.PartNameB, out var setBend, out var setBendReason))
                    Check(bypassSetFailures, setBend!, PipeValveBend.PartNameB, families.ValveBendDiameterParameterName, "직경", false);
                else bypassSetFailures.Add($"- {setBendReason}");
            }
            var bypassValveFittings = network.InlineFittings
                .Where(x => x.IsBypassSet && !string.IsNullOrWhiteSpace(x.Bypass!.BypassValveFamilyTypeName))
                .Select(x => x with { FamilyTypeName = x.Bypass!.BypassValveFamilyTypeName, TypeKey = "배관 밸브류" });
            foreach (var group in network.InlineFittings
                         .Where(x => !string.IsNullOrWhiteSpace(x.FamilyTypeName))
                         .Concat(bypassValveFittings)
                         .GroupBy(x => (x.FamilyTypeName, x.IsJoint)))
            {
                var (familyTypeName, isJoint) = group.Key;
                var role = isJoint ? "이음관" : "배관부속";
                var single = new List<string>();
                if (symbols.TryResolve(familyTypeName, role, out var valve, out var valveReason))
                    Check(single, valve!, role,
                        isJoint ? families.JointDiameterParameterName : ValveDiameterParameterNameFor(families, valve!),
                        "직경", false);
                else single.Add($"- {valveReason}");
                // 같은 패밀리를 밸브와 이음관 양쪽으로 쓰면 사유가 덮어써진다. 실사용상 없는 조합이고,
                // 덮어써져도 "이 패밀리는 못 쓴다"는 판단 자체는 같으므로 문구만 한쪽이 남는다.
                if (single.Count > 0) valveFailures[familyTypeName] = FormatSkipReason(single);
            }
        }
        finally
        {
            foreach (var probe in probeCache.Values) document.Delete(probe.Id);
            if (probeCache.Count > 0) document.Regenerate();
        }

        if (segmentFailures.Count > 0)
            throw new DhbimwaterException(ErrorCodes.PL_DrivenParameterUnwritable,
                $"아래 파라미터에 값을 쓸 수 없어 모델을 생성하지 않았습니다.{Environment.NewLine}{Environment.NewLine}" +
                $"{string.Join(Environment.NewLine, segmentFailures)}{Environment.NewLine}{Environment.NewLine}" +
                "직경은 길이 계열(길이·배관 크기 등 mm로 표시되는) 파라미터, 각도는 각도형 파라미터여야 합니다. " +
                "관 직경만 예외로 숫자(무차원) 파라미터도 지원하며, 이때 mm 값을 단위 변환 없이 그대로 씁니다. " +
                "패밀리에서 인스턴스 파라미터로 바꾸거나, 모델링 창에서 해당 파라미터 선택을 비우세요(비우면 구동하지 않습니다).");

        return new SkipPlan(
            bendFailures.Count == 0 ? null : FormatSkipReason(bendFailures),
            teeFailures.Count == 0 ? null : FormatSkipReason(teeFailures),
            crossFailures.Count == 0 ? null : FormatSkipReason(crossFailures),
            wyeFailures.Count == 0 ? null : FormatSkipReason(wyeFailures),
            valveFailures,
            valveBendFailures.Count == 0 ? null : FormatSkipReason(valveBendFailures),
            valveTeeFailures.Count == 0 ? null : FormatSkipReason(valveTeeFailures),
            bypassSetFailures.Count == 0 ? null : FormatSkipReason(bypassSetFailures));

        // allowNumberType: 무차원(숫자) 파라미터도 통과시킨다. 관 직경 전용이며 값은 mm 그대로 쓴다(docs/137).
        void Check(ICollection<string> failures, FamilySymbol symbol, string role, string? parameterName, string kind,
            bool requiresAngleType, bool allowNumberType = false)
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
                // 관 직경은 무차원도 받는다 — 값을 mm 그대로 쓰는 경로가 따로 있다(docs/137 § 3-2).
                : !requiresAngleType && allowNumberType
                    && !IsMillimeterSpec(parameter.Definition.GetDataType())
                    && !parameter.Definition.GetDataType().Equals(SpecTypeId.Number)
                    ? "길이 계열(길이·배관 크기 등)도 숫자형도 아닙니다."
                // 무차원(숫자) 파라미터는 내부값이 곧 표시값이라 mm→ft 변환값이 그대로 들어간다. Set이 true를 돌려줘
                // 경고도 예외도 걸리지 않으므로, 조용히 틀린 형상이 나오기 전에 여기서 막는다(docs/135 § A).
                : !requiresAngleType && !allowNumberType
                    && !IsMillimeterSpec(parameter.Definition.GetDataType())
                    ? "길이 계열(길이·배관 크기 등) 파라미터가 아닙니다(숫자 등 무차원 파라미터는 값 단위가 달라 지원하지 않습니다)."
                : null;
            if (reason is not null)
                failures.Add($"- {role} '{symbol.Family.Name} : {symbol.Name}'의 {kind} 파라미터 '{parameterName}': {reason}");
        }
    }

    /// <summary>사전검사 실패 문구를 alert 한 줄에 들어갈 사유 문자열로 압축한다(docs/145 § 4-3).</summary>
    private static string FormatSkipReason(IEnumerable<string> failures)
        => string.Join(" · ", failures.Select(x => x.TrimStart('-', ' '))) +
           " 직경은 길이 계열, 각도는 각도형 인스턴스 파라미터여야 합니다(구동하지 않으려면 모델링 창에서 해당 선택을 비우세요).";

    /// <summary>관 패밀리가 인라인 배치 조건을 만족하는지 임시 인스턴스로 확인한다.
    /// 길이는 배치 시점에 파라미터로 구동하므로 여기서 길이를 검증하지 않는다.</summary>
    private static void ValidateSegmentSymbol(Document document, FamilySymbol symbol, string lengthParameterName)
    {
        var probe = PlaceAt(document, symbol, XYZ.Zero, 0, null);
        try
        {
            var connectors = GetMepConnectors(probe);
            if (connectors.Count != 2)
                throw new DhbimwaterException(ErrorCodes.PL_ConnectorCountMismatch, BuildConnectorCountMessage(
                    probe, $"관 '{symbol.Family.Name} : {symbol.Name}'", connectors.Count, "인라인 배치에는 양쪽 끝 커넥터가 있는 2포트 패밀리가 필요합니다."));
            // 관 원점이 커넥터 중앙일 필요는 없다 — 길이 구동 후 RecenterOnConnectors가 실측으로 맞춘다.

            // 길이 파라미터를 실제로 쓸 수 있는지 배치 전에 확인한다(전체 롤백보다 이른 실패가 낫다).
            var parameter = probe.LookupParameter(lengthParameterName);
            // 길이형까지 확인한다 — 무차원 파라미터면 mm→ft 변환값이 조용히 들어간다(docs/135 § A).
            if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double
                || !parameter.Definition.GetDataType().Equals(SpecTypeId.Length))
                throw new DhbimwaterException(ErrorCodes.PL_LengthParameterUnwritable,
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

    /// <param name="elevationFixes">표고 보정량(mm)을 모을 수집기(docs/201 § E). null이면 집계하지 않는다(사전검사 프로브용).</param>
    /// <param name="regenerateAfterRotate">회전 직후 재생성할지. 호출부가 바로 이어서 파라미터를 쓰고 스스로 Regenerate하면
    /// (관 조각·밸브 경로) 여기서의 재생성은 순수 중복이라 false로 넘긴다(docs/206 § B). 절점 부속은 rotation이 0이라 이 분기에 들어오지 않는다.</param>
    private static FamilyInstance PlaceAt(
        Document document, FamilySymbol symbol, XYZ point, double rotation, Level? level,
        ICollection<double>? elevationFixes = null, bool regenerateAfterRotate = true)
    {
        if (!symbol.IsActive) { symbol.Activate(); document.Regenerate(); }
        var instance = level is null
            ? document.Create.NewFamilyInstance(point, symbol, StructuralType.NonStructural)
            : document.Create.NewFamilyInstance(point, symbol, level, StructuralType.NonStructural);
        document.Regenerate();
        // 작업기준면 기반 패밀리가 레벨 평면에 붙는 것을 여기서 되돌린다(docs/201 § B).
        ForceElevation(document, instance, point.Z, elevationFixes);
        // 이 회전은 수직축(BasisZ)이라 Z를 바꾸지 않는다 — 보정 뒤에 와도 무방하다.
        if (Math.Abs(rotation) > 1e-9)
        {
            ElementTransformUtils.RotateElement(document, instance.Id, Line.CreateBound(point, point + XYZ.BasisZ), rotation);
            if (regenerateAfterRotate) document.Regenerate();
        }
        return instance;
    }

    /// <summary>인스턴스의 실제 Z를 요청 Z로 끌어 맞춘다(docs/201 § A).
    /// <para>작업기준면 기반 패밀리는 <c>NewFamilyInstance(point, symbol, level, …)</c>에서 <c>point.Z</c>를 버리고
    /// 레벨 평면에 붙는다. 레벨 기반 패밀리는 지키므로, 같은 네트워크 안에서도 패밀리마다 Z가 갈린다.
    /// 패밀리 템플릿을 요구하는 대신 배치 직후 실측해서 맞춘다.</para>
    /// <para>수평축 180° 회전(<see cref="CorrectElbowSocketDirection"/>·<see cref="CorrectWyeBranchSide"/>)은
    /// <c>z → 2h − z</c>로 Z를 튕기므로, 그 회전 뒤에도 반드시 다시 불러야 한다.</para></summary>
    /// <returns>보정한 거리(mm). 0이면 이미 맞아 있었다.</returns>
    private static double ForceElevation(Document document, FamilyInstance instance, double targetZFt,
        ICollection<double>? elevationFixes = null)
    {
        // 이 저장소가 놓는 부속은 전부 점 배치다. 아니라면 보정할 기준이 없으므로 그대로 둔다.
        if (instance.Location is not LocationPoint location) return 0d;
        var dzFt = targetZFt - location.Point.Z;
        if (Math.Abs(dzFt) <= UC.MmToFt(ConnectionToleranceMm) / 10) return 0d;
        ElementTransformUtils.MoveElement(document, instance.Id, new XYZ(0, 0, dzFt));
        document.Regenerate();
        var dzMm = UC.FtToMm(dzFt);
        elevationFixes?.Add(Math.Abs(dzMm));
        return dzMm;
    }

    /// <summary>mm를 유효 단위로 갖는 계측 스펙인가 — 이러면 내부 단위가 ft라 UC.MmToFt가 그대로 맞다(docs/143).
    /// <para>SpecTypeId.Length만 나열하면 "배관 크기"(SpecTypeId.PipeSize)처럼 길이 계열이면서 이름이 다른 스펙이
    /// 걸러진다. 실제로 DH_소켓_직관의 '공칭_지름'이 이 경우였다. 스펙을 하나씩 나열하는 대신
    /// "mm로 표시할 수 있는가"로 판정해 같은 오류가 반복되지 않게 한다.</para></summary>
    private static bool IsMillimeterSpec(ForgeTypeId dataType)
        => UnitUtils.IsMeasurableSpec(dataType)
           && UnitUtils.GetValidUnits(dataType).Any(x => x.Equals(UnitTypeId.Millimeters));

    /// <summary>관 직경을 구동한다. 길이 계열(mm 표시 가능 스펙)이면 mm→ft로 변환하고, 무차원(숫자) 파라미터면 mm 값을 그대로 쓴다(docs/137·143).
    /// <para>관 직경에만 무차원을 허용하는 이유: 관 길이는 배치 후 커넥터 실측(<see cref="RecenterOnConnectors"/>·
    /// <see cref="ValidateCenteredSpan"/>)과 맞물려야 해서 단위 모호성을 허용할 수 없고,
    /// 곡관·T형·밸브 직경은 이번 범위 밖이다.</para>
    /// <para>무차원 값의 단위는 mm로 간주한다 — 파라미터에 단위 정보가 없으므로 이건 규약이다.
    /// 패밀리가 m·cm 기준이면 형상이 조용히 틀어진다(docs/137 § 6).</para></summary>
    private static void SetDiameterParameterMm(FamilyInstance instance, string parameterName, double valueMm, string label)
    {
        var parameter = instance.LookupParameter(parameterName);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double)
            throw new DhbimwaterException(ErrorCodes.PL_ParameterUnwritable,
                $"{label} 파라미터 '{parameterName}'에 값을 쓸 수 없습니다. " +
                "쓰기 가능한 길이 계열 또는 숫자형 인스턴스 파라미터인지 확인하세요.");

        var dataType = parameter.Definition.GetDataType();
        var isMillimeterSpec = IsMillimeterSpec(dataType);
        if (!isMillimeterSpec && !dataType.Equals(SpecTypeId.Number))
            throw new DhbimwaterException(ErrorCodes.PL_ParameterUnwritable,
                $"{label} 파라미터 '{parameterName}'는 길이 계열도 숫자형도 아닙니다. 값 단위를 알 수 없어 지원하지 않습니다.");

        if (!parameter.Set(isMillimeterSpec ? UC.MmToFt(valueMm) : valueMm))
            throw new DhbimwaterException(ErrorCodes.PL_ParameterSetFailed, $"{label} 파라미터 '{parameterName}' 설정에 실패했습니다.");
    }

    private static void SetDoubleParameterMm(FamilyInstance instance, string parameterName, double valueMm, string label)
    {
        var parameter = instance.LookupParameter(parameterName);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double)
            throw new DhbimwaterException(ErrorCodes.PL_LengthParameterUnwritable,
                $"{label} 파라미터 '{parameterName}'에 값을 쓸 수 없습니다. 쓰기 가능한 길이형 인스턴스 파라미터인지 확인하세요.");
        if (!parameter.Set(UC.MmToFt(valueMm)))
            throw new DhbimwaterException(ErrorCodes.PL_ParameterSetFailed, $"{label} 파라미터 '{parameterName}' 설정에 실패했습니다.");
    }

    // ── 배관재료표(docs/131 § B) ──────────────────────────────────────────────

    /// <summary>인라인 부속의 재료표 명칭. Role("배관부속")은 총칭이라 못 쓰고 패밀리명을 쓴다.
    /// 사용자가 `DH_제수밸브`처럼 접두어를 붙여 쓰므로 그것만 떼고 나머지는 가공하지 않는다.</summary>
    private static string FittingNameOf(FamilyInstance instance)
    {
        var name = instance.Symbol.Family.Name ?? string.Empty;
        return name.StartsWith("DH_", StringComparison.Ordinal) ? name["DH_".Length..] : name;
    }

    // 규격 문자열 본문은 Core의 PipeMaterialSpecText에 있다 — 재기록 경로(docs/136)와 같은 문자열이어야 하기 때문이다.
    // 호출부를 건드리지 않으려고 이름과 시그니처는 그대로 두고 위임만 한다.

    /// <summary>직관·단관 규격: `D160x6.00m`. 길이는 m로 환산해 소수 두 자리로 고정한다(docs/203 § A).</summary>
    private static string SegmentSpecText(double diameterMm, double lengthMm)
        => PipeMaterialSpecText.Segment(diameterMm, lengthMm);

    /// <summary>곡관 규격: `D160x45°`. 편각을 못 구한 절점은 직경만 적는다(docs/203 § B).</summary>
    private static string BendSpecText(double diameterMm, double? deflectionDeg)
        => PipeMaterialSpecText.Bend(diameterMm, deflectionDeg);

    /// <summary>T형 규격: `D160xD90`(주관 x 분기). 분기경에도 `D`를 붙인다(docs/203 § C).</summary>
    private static string TeeSpecText(double mainDiameterMm, double branchDiameterMm)
        => PipeMaterialSpecText.Tee(mainDiameterMm, branchDiameterMm);

    /// <summary>십자 규격: `D160x160`(주관 x 분기). T형과 같은 표기(docs/175 § B-3).</summary>
    private static string CrossSpecText(double mainDiameterMm, double branchDiameterMm)
        => PipeMaterialSpecText.Cross(mainDiameterMm, branchDiameterMm);

    /// <summary>Y형 규격 — 주관 x 분기 구경에 분기각을 붙인다(docs/187 § 2).</summary>
    private static string WyeSpecText(double mainDiameterMm, double branchDiameterMm, double branchAngleDeg)
        => PipeMaterialSpecText.Wye(mainDiameterMm, branchDiameterMm, branchAngleDeg);

    /// <summary>배치가 끝난 뒤 재료표용 정보 파라미터를 한 번에 기록한다(docs/131 § B-4).
    /// 부재번호는 `명칭 + 규격` 조합별 일련번호다 — 같은 조합은 같은 번호를 받아야
    /// 일람표 항목화를 끄고 수량을 집계했을 때 행 1개에 번호 1개가 대응한다.
    /// <para>모델은 이미 만들어졌고 트랜잭션도 진행 중이므로 쓰기 실패는 예외로 올리지 않고 경고로 남긴다.</para></summary>
    private static void RecordMaterialParameters(Document document,
        IReadOnlyList<MaterialPart> parts, PipeNetworkDefinition network, List<string> warnings)
    {
        if (parts.Count == 0) return;
        var junctionId = network.JunctionId;

        // 접합 방식을 채번보다 먼저 정한다 — 부재명 표기·채번·비고·접합수가 모두 이 결과를 본다(docs/170 § C-⚠2).
        // 신규 배치는 인스턴스가 방금 만들어져 DH_부재명칭이 비어 있으므로 사실상 창에서 고른 값이 그대로 쓰인다.
        // 접합 구성(`2S1F`)도 채번보다 먼저 정한다 — 부재명 접두가 이 값에서 나온다(2026-09-15, 부재명·접합구성 둘 다 유지).
        var connections = new Dictionary<ElementId, BendConnection>();
        var compositions = new Dictionary<ElementId, string?>();
        var flagsById = new Dictionary<ElementId, IReadOnlyDictionary<string, bool>>();
        var marked = new List<MaterialPart>(parts.Count);
        foreach (var part in parts)
        {
            // 밸브류는 명칭이 곧 패밀리명이고 접합 개소도 세지 않는다. 표기를 붙이지 않는다.
            if (PipeJointCountWriter.IsAccessory(part.Instance)) { marked.Add(part); continue; }
            var connection = PipeJointCountWriter.ResolveConnection(part.Instance, network.JointConnection);
            var endCount = PipeMaterialSpecText.EndCountFor(part.Name);
            // 인스턴스+유형 파라미터 전수 열거는 부재당 1회면 된다 — Write에도 같은 결과를 넘긴다(docs/206 § F).
            var flags = PipeJointCountWriter.ReadFlags(part.Instance);
            flagsById[part.Instance.Id] = flags;
            var composition = PipeJointCountWriter.ResolveComposition(part.Instance, network.Material, connection, useRestraintGland: false, endCount, flags);
            connections[part.Instance.Id] = connection;
            compositions[part.Instance.Id] = composition;
            marked.Add(part with { Name = PipeMaterialSpecText.WithJointMark(part.Name, composition, connection, endCount) });
        }
        parts = marked;

        // 사전순이 아니라 최초 배치 순서로 번호를 매긴다. 도면을 읽는 순서에 가깝다.
        // 같은 격점에 이미 있는 부재의 번호 체계를 이어받는다(docs/180 § C) — 이 조회가 없으면
        // 이어 그릴 때마다 1부터 다시 매겨져 서로 다른 부재가 같은 번호를 받는다.
        // 이번에 만든 인스턴스는 DH_Addin을 아직 쓰지 않아 AddinInstances에 걸리지 않지만(바로 아래 루프에서 쓴다),
        // 기록 순서가 바뀌어도 안전하도록 ElementId로 한 번 더 걸러 낸다.
        var placedIds = parts.Select(x => x.Instance.Id).ToHashSet();
        var existing = PipeMaterialPartCollector.Read(document, placedIds)
            .Where(x => PipeMaterialNumbering.SameJunction(x.JunctionId, junctionId))
            .ToList();
        var numbers = PipeMaterialNumbering.Assign(
            existing, parts.Select(x => new PipeMaterialNumbering.PartKey(x.Name, x.Spec)));

        var failed = new List<string>();
        var diagnostics = new JointCountDiagnostics();
        foreach (var part in parts)
        {
            SetOptionalTextParameter(part.Instance, ValvePipingParameters.Addin, ValvePipingParameters.AddinValue, failed);
            SetOptionalTextParameter(part.Instance, ValvePipingParameters.ItemName, part.Name, failed);
            SetOptionalTextParameter(part.Instance, ValvePipingParameters.ItemSpec, part.Spec, failed);
            SetOptionalIntegerParameter(part.Instance, ValvePipingParameters.ItemNumber,
                numbers[new PipeMaterialNumbering.PartKey(part.Name, part.Spec)], failed);
            // 계획관로와 같은 이름·값 형태로 한 벌 더 적는다(docs/178 § C). 재기록 경로(RevitPipeMaterialBackfillRepo)와 같은 값이어야 한다.
            // DH_Part는 접두를 뗀 순수 종류(`직관`·`곡관`·`T형`), 밸브류는 접두가 없으므로 패밀리명 그대로다.
            SetOptionalTextParameter(part.Instance, ValvePipingParameters.Part, PipeMaterialSpecText.StripJointMark(part.Name), failed);
            SetOptionalTextParameter(part.Instance, ValvePipingParameters.NominalDiameter, part.BranchDiameterMm is { } branchMm
                ? PipeAlignmentParameters.DiameterText(part.DiameterMm, branchMm)
                : PipeAlignmentParameters.DiameterText(part.DiameterMm), failed);
            // 수량산출 규격2(DH_분기규격). 분기가 있는 부재만 값이 있고, 십자관처럼 구경이 다 다르면
            // 사용자가 `D600xD300`으로 고쳐 쓴다 — 그래서 비어 있을 때만 적는다(DH_비고와 같은 규칙).
            if (part.BranchDiameterMm is { } branchSpecMm)
                SetOptionalTextParameterIfEmpty(part.Instance, ValvePipingParameters.BranchSpec,
                    PipeAlignmentParameters.BranchSpecText(branchSpecMm), failed);
            // 현장 절단 개소. 관·이형관만 받고 기본값은 1이다 — 사용자가 고치거나 0으로 지운다.
            if (PipeMaterialSpecText.IsCutTarget(part.Name))
                SetOptionalIntegerParameterIfEmpty(part.Instance, ValvePipingParameters.CutCount,
                    PipeAlignmentParameters.DefaultCutCount, failed);
            if (part.LengthMm is { } lengthMm) SetOptionalLengthInfoParameter(part.Instance, ValvePipingParameters.Length, lengthMm, failed);
            if (part.DeflectionDeg is { } deflectionDeg) SetOptionalAngleInfoParameter(part.Instance, ValvePipingParameters.BendAngle, deflectionDeg, failed);
            SetOptionalTextParameter(part.Instance, ValvePipingParameters.Material, PipeAlignmentParameters.Code(network.Material), failed);
            // 밸브실 유형은 선택 입력이다. 비어 있으면 빈 문자열을 쓰지 않고 건너뛴다 — 격점번호와 같은 규칙(docs/161 § E).
            if (!string.IsNullOrWhiteSpace(network.Zone))
                SetOptionalTextParameter(part.Instance, ValvePipingParameters.Zone, network.Zone, failed);
            // 야간 여부는 해제 상태도 명시적으로 0을 쓴다. 집계에서 "값 없음"이 나오지 않게 하기 위해서다(docs/141 § C-6).
            SetOptionalYesNoParameter(part.Instance, ValvePipingParameters.NightWork, network.IsNightWork, failed);
            // 격점번호는 필수 입력이 아니다. 비어 있으면 빈 문자열을 쓰지 않고 건너뛴다.
            if (!string.IsNullOrWhiteSpace(junctionId))
                SetOptionalTextParameter(part.Instance, ValvePipingParameters.JunctionId, junctionId, failed);
            // DH_비고는 직관·단관만 관 재료 코드를 받는다. 나머지 부재는 빈칸으로 두어 사용자가 채운다(docs/141 § B).
            var remark = PipeMaterialSpecText.Remark(part.Name, network.Material);
            if (!string.IsNullOrEmpty(remark))
                SetOptionalTextParameterIfEmpty(part.Instance, ValvePipingParameters.Remark, remark, failed);
            // 접합·부설 개소(docs/166 § 7, docs/170 § D). 부재당 1개소이며 매개변수는 관종 + 접합 방식 + 압륜이 정한다.
            // 압륜은 창에서 지정하지 않는다(docs/175 § A). 사용자가 인스턴스 접합 슬롯 종류(이탈방지압륜접합)로 제어하며,
            // 재기록은 그 값을 보존한다(PipeJointCountWriter.ReadRestraintGland).
            PipeJointCountWriter.Write(part.Instance, network.Material,
                connections.TryGetValue(part.Instance.Id, out var connection) ? connection : network.JointConnection,
                useRestraintGland: false, compositions.GetValueOrDefault(part.Instance.Id), failed, diagnostics,
                flagsById.GetValueOrDefault(part.Instance.Id));
        }

        if (diagnostics.Warning is { } jointWarning) warnings.Add(jointWarning);

        // 인스턴스마다 경고를 쌓으면 목록이 폭주하므로 파라미터명 기준으로 한 번만 남긴다.
        var distinct = failed.Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count > 0)
            warnings.Add($"배관재료표 매개변수 {string.Join(", ", distinct)}에 값을 쓰지 못했습니다. " +
                "배관 밸브류·배관 이음쇠 카테고리에 공유 매개변수가 바인딩됐는지 확인하세요(모델 생성 자체는 정상입니다).");
    }

    private static void SetOptionalTextParameter(FamilyInstance instance, string parameterName, string value, ICollection<string> failed)
    {
        var parameter = instance.LookupParameter(parameterName);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.String || !parameter.Set(value ?? string.Empty))
            failed.Add(parameterName);
    }

    /// <summary>이미 값이 있으면 덮어쓰지 않는다. 사용자가 손으로 적은 비고를 지우지 않기 위한 것이다(docs/141 § B-3).
    /// <para>건너뛴 경우는 실패가 아니므로 <paramref name="failed"/>에 넣지 않는다.</para></summary>
    private static void SetOptionalTextParameterIfEmpty(FamilyInstance instance, string parameterName, string value, ICollection<string> failed)
    {
        var parameter = instance.LookupParameter(parameterName);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.String)
        {
            failed.Add(parameterName);
            return;
        }
        if (!string.IsNullOrWhiteSpace(parameter.AsString())) return;
        if (!parameter.Set(value ?? string.Empty)) failed.Add(parameterName);
    }

    /// <inheritdoc cref="SetOptionalTextParameterIfEmpty"/>
    private static void SetOptionalIntegerParameterIfEmpty(FamilyInstance instance, string parameterName, int value, ICollection<string> failed)
    {
        var parameter = instance.LookupParameter(parameterName);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Integer)
        {
            failed.Add(parameterName);
            return;
        }
        // Integer는 0도 뜻이 있는 값이라(자르지 않는다) 미태깅만 채운다.
        if (parameter.HasValue) return;
        if (!parameter.Set(value)) failed.Add(parameterName);
    }

    private static void SetOptionalYesNoParameter(FamilyInstance instance, string parameterName, bool value, ICollection<string> failed)
    {
        // Yes/No 파라미터의 StorageType은 Integer다. 0/1로 쓴다.
        var parameter = instance.LookupParameter(parameterName);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Integer || !parameter.Set(value ? 1 : 0))
            failed.Add(parameterName);
    }

    private static void SetOptionalIntegerParameter(FamilyInstance instance, string parameterName, int value, ICollection<string> failed)
    {
        var parameter = instance.LookupParameter(parameterName);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Integer || !parameter.Set(value))
            failed.Add(parameterName);
    }

    /// <summary>정보용 길이 매개변수(`DH_연장`, docs/178 § C). 아래 <see cref="SetOptionalDiameterParameter"/>는 패밀리 형상 구동용이라
    /// 경고 목록을 받고 실패해도 계속 가는 반면, 이쪽은 재료표 매개변수와 같은 <paramref name="failed"/> 방식이다.</summary>
    private static void SetOptionalLengthInfoParameter(FamilyInstance instance, string parameterName, double valueMm, ICollection<string> failed)
    {
        var parameter = instance.LookupParameter(parameterName);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double || !parameter.Set(UC.MmToFt(valueMm)))
            failed.Add(parameterName);
    }

    /// <summary>정보용 각도 매개변수(`DH_곡관규격각`, docs/178 § C). 구동용 <see cref="SetOptionalAngleParameter"/>와 같은 이유로 따로 둔다.</summary>
    private static void SetOptionalAngleInfoParameter(FamilyInstance instance, string parameterName, double valueDeg, ICollection<string> failed)
    {
        var parameter = instance.LookupParameter(parameterName);
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double || !parameter.Set(UC.DegToRad(valueDeg)))
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

    /// <summary>형상 구동용 Yes/No 파라미터(밸브부관 A 수평·수직분기, docs/227 § D). 이름이 비면 건너뛰고, 못 쓰면 경고만 남긴다.</summary>
    private static void SetOptionalFlagParameter(
        FamilyInstance instance, string? parameterName, bool value, string label, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(parameterName)) return;
        var parameter = instance.LookupParameter(parameterName);
        // Yes/No 파라미터의 StorageType은 Integer다.
        if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Integer || !parameter.Set(value ? 1 : 0))
            warnings.Add(
                $"{label} '{instance.Symbol.Family.Name} : {instance.Symbol.Name}'에서 예/아니오 파라미터 '{parameterName}'에 " +
                "값을 쓸 수 없어 건너뛰었습니다(이름이 없거나, 유형 파라미터이거나, 예/아니오 형식이 아닌 경우입니다).");
    }

    /// <summary>커넥터가 위·아래를 향하는지. 밸브부관 A의 수직분기 커넥터를 가른다(docs/227 § D).</summary>
    private static bool IsVerticalConnector(Connector connector)
        => Math.Abs(connector.CoordinateSystem.BasisZ.Z) >= VerticalConnectorMinZ;

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
            throw new DhbimwaterException(ErrorCodes.PL_ConnectorCountMismatch, $"{role} '{instance.Symbol.Family.Name} : {instance.Symbol.Name}'의 물리 배관 커넥터는 {connectors.Count}개입니다.");
        var midpoint = (connectors[0].Origin + connectors[1].Origin) / 2;
        var centerErrorMm = UC.FtToMm(midpoint.DistanceTo(expectedCenter));
        var spanMm = ConnectorSpanMm(connectors);
        if (centerErrorMm > ConnectionToleranceMm || Math.Abs(spanMm - expectedLengthMm) > ConnectionToleranceMm)
            throw new DhbimwaterException(ErrorCodes.PL_ConnectorSpanMismatch,
                $"{role} '{instance.Symbol.Family.Name} : {instance.Symbol.Name}'의 커넥터 형상이 요청 구간과 맞지 않습니다. " +
                $"중앙 오차 {centerErrorMm:N1}mm, 길이 {spanMm:N1}mm(요청 {expectedLengthMm:N1}mm)입니다.");
    }

    private static void ValidateConnectorMidpoint(
        FamilySymbol symbol, IReadOnlyList<Connector> connectors, XYZ expectedCenter, string role)
    {
        var midpoint = (connectors[0].Origin + connectors[1].Origin) / 2;
        if (UC.FtToMm(midpoint.DistanceTo(expectedCenter)) <= ConnectionToleranceMm) return;
        throw new DhbimwaterException(ErrorCodes.PL_OriginNotCentered,
            $"{role} '{symbol.Family.Name} : {symbol.Name}'의 원점이 두 배관 커넥터의 중앙과 일치하지 않습니다. " +
            "관 중앙이 원점인 패밀리를 선택하세요.");
    }

    private static double ConnectorSpanMm(IReadOnlyList<Connector> connectors)
        => UC.FtToMm(connectors[0].Origin.DistanceTo(connectors[1].Origin));

    /// <summary>서로 다른 패밀리의 물리 배관 커넥터가 같은 접점에 하나씩 있으면 실제 Revit 연결을 만든다.
    /// <para>관 끝점(EndPoint) 이외의 열린 커넥터나 한 점에 3개 이상 모인 모호한 연결은 관망 단절이지만,
    /// 이제 전체 생성을 중단하지 않고 건너뛴 목록에 담아 alert로 알린다(docs/145 § 4-6).</para>
    /// <para><paramref name="openPoints"/>는 건너뛴 부속 자리다. 그 자리에 모인 커넥터는 **연결하지 않는다** —
    /// 곡관 없이 직관 두 개가 서로 물리면 캔버스와 다른 각도로 꺾인 관이 만들어진다.</para></summary>
    private static void ConnectCoincidentConnectors(
        Document document, IReadOnlyCollection<PlacedPart> instances, PipeNetworkDefinition network,
        IReadOnlyCollection<XYZ> openPoints, ICollection<PipeConnectionFailure> skipped, ICollection<string> warnings)
    {
        var tolerance = UC.MmToFt(ConnectionToleranceMm);
        var endPoints = network.Nodes.Where(x => x.NodeKind == NodeKind.EndPoint)
            .Select(x => ToXyz(x.Position, network.Elevation, network.ReferencePoint)).ToList();
        var partsById = instances.ToDictionary(x => x.Instance.Id);
        // Connector.Origin은 호출마다 네이티브 마샬링이다. n²로 읽으므로 한 번만 읽어 둔다(docs/206 § D).
        var connectors = instances.SelectMany(x => GetMepConnectors(x.Instance))
            .Select(x => (Connector: x, Origin: x.Origin)).ToList();
        var handled = new HashSet<Connector>();

        foreach (var (connector, origin) in connectors)
        {
            if (handled.Contains(connector) || connector.IsConnected) continue;
            // 건너뛴 부속 자리. 여기서 연결하면 관끼리 물려 캔버스와 다른 각도로 꺾인다(docs/145 § 4-6).
            if (openPoints.Any(x => x.DistanceTo(origin) <= tolerance))
            {
                handled.Add(connector);
                continue;
            }
            var candidates = connectors.Where(other =>
                    !ReferenceEquals(other.Connector, connector) &&
                    !handled.Contains(other.Connector) &&
                    !other.Connector.IsConnected &&
                    other.Connector.Owner.Id != connector.Owner.Id &&
                    other.Origin.DistanceTo(origin) <= tolerance)
                .Select(other => other.Connector)
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
                    // 연결 실패는 이제 중단 사유가 아니다. 두 커넥터를 열린 채로 두고 alert에만 남긴다(docs/145 § 4-6).
                    handled.Add(connector);
                    handled.Add(candidates[0]);
                    Report(connector, part,
                        $"{FormatPart(part)}의 커넥터를 {FormatPart(otherPart)}의 커넥터에 연결하지 못했습니다.{Environment.NewLine}" +
                        $"접점: {FormatPoint(origin, network.ReferencePoint)}{Environment.NewLine}" +
                        "두 패밀리 방향과 커넥터 규격을 확인하세요.", $"Revit ConnectTo 호출 실패: {ex.Message}");
                }
                continue;
            }

            if (candidates.Count > 1)
            {
                var part = partsById[connector.Owner.Id];
                var otherParts = candidates.Select(x => FormatPart(partsById[x.Owner.Id])).Distinct();
                // 어느 쌍을 골라야 할지 알 수 없는 접점이다. 임의로 두 개를 붙이면 관이 엉뚱하게 꺾이므로
                // 겹친 커넥터를 통째로 열린 채 남긴다.
                handled.Add(connector);
                foreach (var other in candidates) handled.Add(other);
                Report(connector, part,
                    $"{FormatPart(part)}의 커넥터 접점: {FormatPoint(origin, network.ReferencePoint)}{Environment.NewLine}" +
                    $"연결 가능한 배관 커넥터가 {candidates.Count + 1}개 겹쳐 있습니다. 상대: {string.Join(", ", otherParts)}.{Environment.NewLine}" +
                    "1포트 분기 부속은 인라인 부속으로 직접 연결할 수 없습니다.", "커넥터 중첩");
                continue;
            }

            if (endPoints.Any(x => x.DistanceTo(origin) <= tolerance))
            {
                handled.Add(connector);
                continue;
            }

            var unmatchedPart = partsById[connector.Owner.Id];
            var nearest = connectors
                .Where(other => other.Connector.Owner.Id != connector.Owner.Id)
                .OrderBy(other => other.Origin.DistanceTo(origin))
                .Select(other => (ValueTuple<Connector, XYZ>?)other)
                .FirstOrDefault();
            var nearestMessage = nearest is null
                ? "주변에 다른 커넥터가 없습니다."
                : $"가장 가까운 커넥터는 {FormatPart(partsById[nearest.Value.Item1.Owner.Id])}의 커넥터로 " +
                  $"{UC.FtToMm(nearest.Value.Item2.DistanceTo(origin)):N1}mm 떨어져 있습니다(허용 {ConnectionToleranceMm:N0}mm).";
            var nearestDistanceMm = nearest is null ? 0 : UC.FtToMm(nearest.Value.Item2.DistanceTo(origin));
            var hint = nearestDistanceMm >= ConnectionToleranceMm * 10
                ? $"{Environment.NewLine}(10mm 이상 이탈 — 부속이 관 배치 축에서 벗어났을 가능성이 큽니다. " +
                  "곡관 꺾임각이 캔버스와 다르게 적용됐는지, 곡관/T형/밸브 직경·각도 파라미터가 실제 패밀리의 " +
                  "인스턴스 파라미터와 이름이 맞는지 확인하세요.)"
                : "";
            Report(connector, unmatchedPart,
                $"{FormatPart(unmatchedPart)}의 커넥터가 연결 상대를 찾지 못했습니다.{Environment.NewLine}" +
                $"접점: {FormatPoint(origin, network.ReferencePoint)}{Environment.NewLine}" +
                $"{nearestMessage}{Environment.NewLine}" +
                $"부속 커넥터가 배관 축에서 벗어났는지, 직관·단관 길이와 곡관/T형 방향을 확인하세요.{hint}",
                nearestMessage);
        }

        document.Regenerate();
        return;

        // 연결 실패를 경고 + 건너뛴 목록으로 남긴다. 예외로 던지면 UseCase가 전체를 롤백한다(docs/145 § 4-6).
        void Report(Connector failed, PlacedPart part, string message, string detail)
        {
            warnings.Add(message);
            skipped.Add(CreateFailure(failed.Origin, part.Role,
                $"{part.Instance.Symbol.Family.Name} : {part.Instance.Symbol.Name}", failed.Owner.Id.Value, detail, network));
        }
    }

    /// <summary>"패밀리명 : 타입명" → FamilySymbol. 문서 전체 심볼을 <b>한 번만</b> 훑어 사전을 만든다(docs/206 § A).
    /// <para>종전 ResolveSymbol은 호출마다 FilteredElementCollector를 돌리고 심볼마다 <c>Family.Name</c>(요소 조회)을 불렀다.
    /// 여기서는 <see cref="ElementType.FamilyName"/>(문자열 프로퍼티, 조회 없음)으로 키를 만든다.</para>
    /// <para>같은 (패밀리, 타입)이 카테고리를 달리해 둘 이상이면 종전 FirstOrDefault와 같이 <b>먼저 온 것</b>을 쓴다(TryAdd).</para></summary>
    private sealed class FamilySymbolCache
    {
        private readonly Document _document;
        private Dictionary<(string Family, string Type), FamilySymbol>? _symbols;

        public FamilySymbolCache(Document document) => _document = document;

        public FamilySymbol Resolve(string familyTypeName, string role)
        {
            if (string.IsNullOrWhiteSpace(familyTypeName))
                throw new DhbimwaterException(ErrorCodes.PL_FamilyNotSelected, $"{role} 패밀리가 선택되지 않았습니다.");
            var separator = familyTypeName.LastIndexOf(" : ", StringComparison.Ordinal);
            if (separator <= 0 || separator >= familyTypeName.Length - 3)
                throw new DhbimwaterException(ErrorCodes.PL_FamilyNameFormat,
                    $"{role} 패밀리명 '{familyTypeName}' 형식이 올바르지 않습니다(\"패밀리명 : 타입명\").");
            var key = (familyTypeName[..separator], familyTypeName[(separator + 3)..]);
            return Symbols().TryGetValue(key, out var symbol)
                ? symbol
                : throw new DhbimwaterException(ErrorCodes.PL_FamilyNotFound,
                    $"{role} 패밀리 '{familyTypeName}'를 찾을 수 없습니다. 프로젝트에 로드돼 있는지 확인하세요.");
        }

        /// <summary>종전 TryResolveSymbol과 같은 계약 — 못 찾으면 사유를 돌려주고 false.</summary>
        public bool TryResolve(string familyTypeName, string role, out FamilySymbol? symbol, out string? reason)
        {
            try { symbol = Resolve(familyTypeName, role); reason = null; return true; }
            catch (DhbimwaterException ex) { symbol = null; reason = ex.Message; return false; }
        }

        private Dictionary<(string, string), FamilySymbol> Symbols()
        {
            if (_symbols is not null) return _symbols;
            _symbols = new Dictionary<(string, string), FamilySymbol>();
            foreach (var symbol in new FilteredElementCollector(_document).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>())
                _symbols.TryAdd((symbol.FamilyName, symbol.Name), symbol);
            return _symbols;
        }
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

    /// <summary>건너뛴 부속·연결 실패 지점 한 건. 좌표는 캔버스 기준(mm)이라 UI가 그대로 표식으로 쓴다(docs/145 § 4-2).</summary>
    private static PipeConnectionFailure CreateFailure(
        XYZ point, string role, string familyTypeName, long elementId, string detail, PipeNetworkDefinition network)
        => new(new DHBIMWATER.Core.Geometry.Point2D(
                UC.FtToMm(point.X) - network.ReferencePoint.X, UC.FtToMm(point.Y) - network.ReferencePoint.Y),
            UC.FtToMm(point.Z), role, familyTypeName, elementId, detail);

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
