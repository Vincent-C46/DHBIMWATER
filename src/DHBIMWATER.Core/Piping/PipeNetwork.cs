using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Core.Piping;

/// <summary>모델 단위(mm)의 정규화된 배관 토폴로지 그래프.</summary>
public sealed class PipeNetwork
{
    private readonly List<PipeNode> _nodes = [];
    private readonly List<PipeEdge> _edges = [];
    // FindNode/FindEdge가 List.FirstOrDefault(O(N))라 교차 정리(PipeTopologyBuilder.SplitFirstIntersection)가
    // 엣지 쌍마다 4번 부르면 드래그 한 번에 수천만 비교가 됐다(docs/206 § G). 순서는 List가, 조회는 사전이 맡는다.
    private readonly Dictionary<Guid, PipeNode> _nodeById = new();
    private readonly Dictionary<Guid, PipeEdge> _edgeById = new();

    public PipeNetwork(double elevation = 0) => Elevation = elevation;

    /// <summary>Phase 2에서 사용자 입력으로 연결할 작업 표고(mm).</summary>
    public double Elevation { get; set; }
    public IReadOnlyList<PipeNode> Nodes => _nodes;
    public IReadOnlyList<PipeEdge> Edges => _edges;

    public PipeSnapResult? FindSnapPoint(Point2D point, PipeSnapMode modes, Point2D? from = null) => new PipeTopologyBuilder(this).FindSnapPoint(point, modes, from);
    /// <summary><paramref name="diameterMm"/>는 이 선에만 붙는다. 기존 선을 쪼갠 조각은 원본 관경을 유지한다(docs/152 § A-3).</summary>
    public void AddSegment(Point2D start, Point2D end, double diameterMm = PipeEdge.DefaultDiameterMm) => new PipeTopologyBuilder(this).AddSegment(start, end, diameterMm);
    public void RemoveSegment(Guid edgeId) => new PipeTopologyBuilder(this).RemoveSegment(edgeId);
    /// <summary>엣지를 t(0~1) 지점에서 둘로 나눈다. 나눈 두 조각의 Id를 돌려준다. 못 나누면 null(docs/179 § H-1).</summary>
    /// <param name="detach">true면 두 조각이 <b>같은 자리의 각자 끝점</b>을 갖는다(docs/181 § B).
    /// 사용자가 손으로 나눈 경우가 이쪽이다 — 끝점 하나를 끌면 그 조각만 움직인다.</param>
    public (Guid First, Guid Second)? SplitSegment(Guid edgeId, double t, bool detach = false)
        => new PipeTopologyBuilder(this).SplitSegment(edgeId, t, detach);
    /// <param name="bypass">바이패스 세트 입력(docs/228). 밸브·이음관이면 null.</param>
    public void AddInlineFitting(Guid edgeId, string typeKey, string familyTypeName, double desiredT, bool preservePosition = false,
        BypassSetSpec? bypass = null)
        => new PipeTopologyBuilder(this).AddInlineFitting(edgeId, typeKey, familyTypeName, desiredT, preservePosition, bypass);

    /// <summary>바이패스 세트의 바이패스 밸브·관경을 바꾼다(docs/229 § D, docs/230 § B). 방향(Side)은 그대로 둔다. 세트가 아니면 false.
    /// 본관 밸브는 세트가 아니다 — A–A 사이에 일반 밸브로 놓는다.</summary>
    public bool UpdateBypassSet(Guid edgeId, Guid fittingId, string bypassValveFamilyTypeName, double bypassDiameterMm)
    {
        var edge = FindEdge(edgeId) ?? throw new ArgumentOutOfRangeException(nameof(edgeId));
        var target = edge.InlineFittings.FirstOrDefault(x => x.Id == fittingId);
        if (target is not { IsBypassSet: true, Bypass: { } spec }) return false;
        edge.ReplaceFittings(edge.InlineFittings.Select(x => x.Id == fittingId
            ? x with { Bypass = spec with { BypassValveFamilyTypeName = bypassValveFamilyTypeName, BypassDiameterMm = bypassDiameterMm } }
            : x).ToList());
        return true;
    }

    /// <summary>바이패스 세트의 방향(본관 어느 쪽으로 뺄지)을 뒤집는다(docs/228 § A). 세트가 아니면 false.</summary>
    public bool FlipBypassSide(Guid edgeId, Guid fittingId)
    {
        var edge = FindEdge(edgeId) ?? throw new ArgumentOutOfRangeException(nameof(edgeId));
        var target = edge.InlineFittings.FirstOrDefault(x => x.Id == fittingId);
        if (target is not { IsBypassSet: true, Bypass: { } spec }) return false;
        // ToList로 먼저 굳힌다 — ReplaceFittings가 목록을 비운 뒤 읽으므로, 같은 목록을 지연 열거하면 빈 목록이 된다.
        edge.ReplaceFittings(edge.InlineFittings.Select(x => x.Id == fittingId
            ? x with { Bypass = spec with { Side = -spec.NormalizedSide } }
            : x).ToList());
        return true;
    }
    public void RemoveInlineFitting(Guid edgeId, Guid fittingId) => new PipeTopologyBuilder(this).RemoveInlineFitting(edgeId, fittingId);

    /// <summary>이미 그려진 엣지의 관경을 바꾼다. 토폴로지는 건드리지 않는다(docs/152 § A-4).</summary>
    public void SetEdgeDiameter(Guid edgeId, double diameterMm)
    {
        if (!double.IsFinite(diameterMm) || diameterMm <= 0)
            throw new DhbimwaterException(ErrorCodes.PL_InvalidDiameter, "관경은 0보다 큰 숫자여야 합니다.");
        var edge = FindEdge(edgeId) ?? throw new ArgumentOutOfRangeException(nameof(edgeId));
        edge.DiameterMm = diameterMm;
    }

    /// <summary>절점 한 개를 옮긴다(끝점 그립 조정). 그 절점에 물린 엣지는 모두 따라온다 —
    /// 절점 자체를 잡아 끄는 조작이므로 분리하지 않는다(docs/152 § B-2).</summary>
    public void MoveNode(Guid nodeId, Point2D position)
    {
        var node = FindNode(nodeId) ?? throw new ArgumentOutOfRangeException(nameof(nodeId));
        new PipeTopologyBuilder(this).MoveNodes([nodeId], position.X - node.Position.X, position.Y - node.Position.Y);
    }

    /// <summary>엣지 여러 개를 평행 이동한다. <paramref name="detachShared"/>가 true면 선택 밖 엣지와
    /// 공유하는 절점을 복제해 선택한 엣지만 떼어낸다(docs/152 § B-3).</summary>
    public void MoveEdges(IReadOnlyCollection<Guid> edgeIds, double dx, double dy, bool detachShared = true)
        => new PipeTopologyBuilder(this).MoveEdges(edgeIds, dx, dy, detachShared);

    /// <summary>인라인 부속을 자기 엣지 위에서 슬라이드한다(docs/152 § B-4).</summary>
    public void MoveInlineFitting(Guid edgeId, Guid fittingId, double t) => new PipeTopologyBuilder(this).MoveInlineFitting(edgeId, fittingId, t);

    public void Clear() { _nodes.Clear(); _edges.Clear(); _nodeById.Clear(); _edgeById.Clear(); }
    public PipeNetwork DeepClone()
    {
        var clone = new PipeNetwork(Elevation);
        // 밸브부관 지정을 빠뜨리면 Undo 한 번에 모든 밸브부관이 곡관으로 돌아간다(docs/225 § A-3).
        clone._nodes.AddRange(_nodes.Select(x => new PipeNode(x.Id, x.Position)
        {
            Degree = x.Degree, NodeKind = x.NodeKind, FittingKind = x.FittingKind, ValveSideDirection = x.ValveSideDirection
        }));
        // 관경을 빠뜨리면 Undo 한 번에 모든 엣지가 기본 관경으로 돌아간다(docs/152 § A-2).
        clone._edges.AddRange(_edges.Select(x => new PipeEdge(x.Id, x.StartNodeId, x.EndNodeId, x.InlineFittings, x.DiameterMm)));
        foreach (var node in clone._nodes) clone._nodeById[node.Id] = node;
        foreach (var edge in clone._edges) clone._edgeById[edge.Id] = edge;
        return clone;
    }

    /// <param name="diameterMm">이 배치의 <b>기본</b> 관경. 실제 관·부속은 엣지별
    /// <see cref="PipeEdgeDefinition.DiameterMm"/>를 쓰며, 이 값은 폴백·검증용이다(docs/152 § A-5).</param>
    public PipeNetworkDefinition ToDefinition(
        double diameterMm,
        PipeOutputMode outputMode,
        Point2D referencePoint,
        string pipingSystemTypeName = "",
        string pipeTypeName = "",
        string levelName = "",
        PipeSegmentFamilySelection? segmentFamilies = null,
        string junctionId = "",
        Gis.PipeMaterial material = Gis.PipeMaterial.DuctileIron,
        bool isNightWork = false,
        IReadOnlyList<long>? sourceElementIdsToDelete = null,
        string zone = "",
        Gis.BendConnection jointConnection = Gis.BendConnection.Flanged)
    {
        var edges = EdgeDefinitions();
        var fittings = FittingDefinitions();
        // 밸브부관 B의 밸브 쪽·주관 구경은 엣지·부속·절점 종류가 확정된 뒤에 정한다 —
        // 자동 판정이 "밸브가 올라탄 레그"와 "이어진 밸브부관 A"를 보기 때문이다(docs/225 § A-3, docs/227 § A).
        var nodes = NodeDefinitions(edges, fittings);
        return new PipeNetworkDefinition(nodes, edges, fittings, Elevation, diameterMm, outputMode, referencePoint,
            pipingSystemTypeName, pipeTypeName, levelName, segmentFamilies, junctionId, material, isNightWork,
            sourceElementIdsToDelete, zone, jointConnection);
    }

    // ── 밸브부관 A·B(docs/225 § A-3, docs/227 § A) ───────────────────────────────

    /// <summary>절점의 부속 종류를 바꾼다. 허용 조합은 B ↔ 꺾인 절점, A ↔ T 절점, 기본 ↔ 둘 다다.
    /// 맞지 않으면 바꾸지 않고 false를 돌려준다. B가 아니게 되면 밸브 쪽 지정도 지운다 —
    /// 다시 B로 바꿀 때 자동 판정부터 시작한다.</summary>
    public bool SetNodeFittingKind(Guid nodeId, PipeNodeFittingKind kind)
    {
        var node = FindNode(nodeId) ?? throw new ArgumentOutOfRangeException(nameof(nodeId));
        if (!IsAllowed(node.NodeKind, kind)) return false;
        node.FittingKind = kind;
        if (kind != PipeNodeFittingKind.ValveBend) node.ValveSideDirection = null;
        return true;
    }

    /// <summary>절점 모양과 부속 종류 지정이 어울리는지. <see cref="PipeTopologyBuilder"/>가 재분류할 때도 같은 규칙으로 되돌린다.</summary>
    internal static bool IsAllowed(NodeKind nodeKind, PipeNodeFittingKind kind) => kind switch
    {
        PipeNodeFittingKind.ValveBend => nodeKind == NodeKind.Elbow,
        PipeNodeFittingKind.ValveTee => nodeKind == NodeKind.Tee,
        _ => nodeKind is NodeKind.Elbow or NodeKind.Tee
    };

    /// <summary>밸브부관 B의 밸브 쪽을 방향으로 지정한다. 역가져오기가 실제 부재의 커넥터 방향을 되살릴 때 쓴다.
    /// 길이 0 벡터는 무시한다(수직 커넥터).</summary>
    public void SetValveBendSide(Guid nodeId, Point2D direction)
    {
        var node = FindNode(nodeId) ?? throw new ArgumentOutOfRangeException(nameof(nodeId));
        var length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        if (length <= 1e-9) return;
        node.ValveSideDirection = new Point2D(direction.X / length, direction.Y / length);
    }

    /// <summary>밸브 쪽을 반대 레그로 바꾼다. 지금 밸브 쪽을 정할 수 없으면(모호) 첫 레그를 밸브 쪽으로 지정한다 —
    /// 사용자가 한 번 누르면 어느 쪽이든 정해지고, 틀렸으면 다시 누르면 된다. 바꿨으면 true.</summary>
    public bool FlipValveBendSide(Guid nodeId)
    {
        var node = FindNode(nodeId) ?? throw new ArgumentOutOfRangeException(nameof(nodeId));
        if (node.NodeKind != NodeKind.Elbow || node.FittingKind != PipeNodeFittingKind.ValveBend) return false;
        var edges = EdgeDefinitions();
        var definition = NodeDefinitions(edges, FittingDefinitions()).First(x => x.Id == nodeId);
        var legs = PipeNodeAngle.Legs(definition, edges);
        if (legs.Count != 2) return false;
        var target = definition.ValveSideEdgeId is { } current
            ? legs.First(x => x.Edge.Id != current)
            : legs[0];
        node.ValveSideDirection = new Point2D(target.DirectionX, target.DirectionY);
        return true;
    }

    /// <summary>캔버스 표시용: 밸브부관 B의 밸브 쪽 레그 방향(단위벡터). B가 아니거나 정할 수 없으면 null.</summary>
    public Point2D? ResolveValveSideDirection(Guid nodeId)
    {
        var node = FindNode(nodeId);
        if (node is null || node.NodeKind != NodeKind.Elbow || node.FittingKind != PipeNodeFittingKind.ValveBend) return null;
        var edges = EdgeDefinitions();
        var definition = NodeDefinitions(edges, FittingDefinitions()).First(x => x.Id == nodeId);
        if (definition.ValveSideEdgeId is not { } edgeId) return null;
        var leg = PipeNodeAngle.Legs(definition, edges).FirstOrDefault(x => x.Edge.Id == edgeId);
        return leg.Edge is null ? null : new Point2D(leg.DirectionX, leg.DirectionY);
    }

    /// <summary>절점 정의 두 단계 생성. 1차로 종류(A·B)를 확정하고, 2차로 B의 밸브 쪽과 주관 구경을 채운다 —
    /// B의 판정이 이웃 A의 종류를 봐야 하기 때문이다.</summary>
    private List<PipeNodeDefinition> NodeDefinitions(
        IReadOnlyList<PipeEdgeDefinition> edges, IReadOnlyList<FittingPlacementDefinition> fittings)
    {
        var nodes = _nodes.Select(x => new PipeNodeDefinition(x.Id, x.Position, x.NodeKind,
            x.FittingKind != PipeNodeFittingKind.Bend && IsAllowed(x.NodeKind, x.FittingKind) ? x.FittingKind : PipeNodeFittingKind.Bend)).ToList();
        for (var i = 0; i < nodes.Count; i++)
        {
            if (!nodes[i].IsValveBend) continue;
            var withSide = nodes[i] with
            {
                ValveSideEdgeId = PipeValveBend.ResolveValveSideEdge(nodes[i], edges, fittings, _nodes[i].ValveSideDirection, nodes)
            };
            nodes[i] = withSide with { MainDiameterMm = PipeValveBend.ResolveMainDiameter(withSide, nodes, edges) };
        }
        return nodes;
    }

    private List<PipeEdgeDefinition> EdgeDefinitions() => _edges.Select(x =>
    {
        var start = FindNode(x.StartNodeId)!;
        var end = FindNode(x.EndNodeId)!;
        return new PipeEdgeDefinition(x.Id, start.Position, end.Position, Elevation, x.DiameterMm);
    }).ToList();

    /// <summary>부속 1개 = 정의 1개. 이음관도 밸브처럼 자기 캔버스 점을 싣는다(docs/175 § C-2) —
    /// 밸브 근처 밀착은 밸브 폭을 실측할 수 있는 Revit 경계가 정한다(docs/175 § D-2).</summary>
    private List<FittingPlacementDefinition> FittingDefinitions()
    {
        var fittings = new List<FittingPlacementDefinition>();
        foreach (var edge in _edges)
        {
            var start = FindNode(edge.StartNodeId)!;
            var end = FindNode(edge.EndNodeId)!;
            foreach (var fitting in edge.InlineFittings)
            {
                var point = new Point2D(start.Position.X + (end.Position.X - start.Position.X) * fitting.T,
                                        start.Position.Y + (end.Position.Y - start.Position.Y) * fitting.T);
                fittings.Add(new FittingPlacementDefinition(fitting.Id, fitting.TypeKey, fitting.FamilyTypeName, point, Elevation, edge.Id,
                    Bypass: fitting.Bypass));
            }
        }
        return fittings;
    }

    internal PipeNode AddNode(Point2D position) { var node = new PipeNode(Guid.NewGuid(), position); _nodes.Add(node); _nodeById[node.Id] = node; return node; }
    /// <summary>만든 엣지를 돌려준다 — 분할이 새 조각의 Id를 알아야 하기 때문이다(docs/179 § H-1).
    /// 기존 호출부는 반환값을 무시하면 종전과 같다.</summary>
    internal PipeEdge AddEdge(Guid start, Guid end, double diameterMm, IEnumerable<InlineFitting>? fittings = null)
    {
        var edge = new PipeEdge(Guid.NewGuid(), start, end, fittings, diameterMm);
        _edges.Add(edge);
        _edgeById[edge.Id] = edge;
        return edge;
    }
    internal void RemoveEdge(PipeEdge edge) { if (_edges.Remove(edge)) _edgeById.Remove(edge.Id); }

    /// <summary>엣지의 양 끝 절점을 갈아끼운다. <see cref="PipeEdge.StartNodeId"/>가 읽기 전용이므로
    /// 같은 Id·관경·부속으로 다시 만들어 같은 자리에 넣는다 — Id를 보존해야 선택·Undo·에러 마커가 유지된다(docs/152 § B-3).</summary>
    internal PipeEdge ReplaceEdgeNodes(PipeEdge edge, Guid start, Guid end)
    {
        var index = _edges.IndexOf(edge);
        if (index < 0) return edge;
        var replacement = new PipeEdge(edge.Id, start, end, edge.InlineFittings, edge.DiameterMm);
        _edges[index] = replacement;
        _edgeById[replacement.Id] = replacement;
        return replacement;
    }

    internal void RemoveUnconnectedNodes()
    {
        var connected = new HashSet<Guid>(_edges.Count * 2);
        foreach (var edge in _edges) { connected.Add(edge.StartNodeId); connected.Add(edge.EndNodeId); }
        _nodes.RemoveAll(node => !connected.Contains(node.Id) && _nodeById.Remove(node.Id));
    }
    internal void RemoveNode(PipeNode node) { if (_nodes.Remove(node)) _nodeById.Remove(node.Id); }
    internal PipeNode? FindNode(Guid id) => _nodeById.GetValueOrDefault(id);
    internal PipeEdge? FindEdge(Guid id) => _edgeById.GetValueOrDefault(id);
}
