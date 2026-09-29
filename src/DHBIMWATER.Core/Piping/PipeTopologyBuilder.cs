using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Core.Piping;

[Flags]
public enum PipeSnapMode { None = 0, Endpoint = 1, Midpoint = 2, Quadrant = 4, Nearest = 8, Intersection = 16, Perpendicular = 32 }

/// <summary>스냅된 점 하나의 종류. 우선순위 비교와 표식·툴팁 표시에 함께 쓴다.</summary>
public enum PipeSnapKind { Intersection, Endpoint, Midpoint, Quadrant, Perpendicular, Nearest }

public readonly record struct PipeSnapResult(Point2D Point, PipeSnapKind Kind, double Distance);

public sealed class PipeTopologyBuilder
{
    public const double SnapTolerance = 100.0;
    private const double GeometryTolerance = 1e-6;
    private const double StraightAngleToleranceDegrees = 2.0;
    private readonly PipeNetwork _network;

    public PipeTopologyBuilder(PipeNetwork network) => _network = network;

    public PipeSnapResult? FindSnapPoint(Point2D point, PipeSnapMode modes, Point2D? from = null)
    {
        var candidates = new List<PipeSnapResult>();
        if (modes.HasFlag(PipeSnapMode.Endpoint))
            candidates.AddRange(_network.Nodes.Select(x => new PipeSnapResult(x.Position, NodeKindOf(x), x.Position.DistanceTo(point))));
        if (modes.HasFlag(PipeSnapMode.Intersection))
            candidates.AddRange(_network.Nodes.Where(x => x.Degree >= 3).Select(x => new PipeSnapResult(x.Position, PipeSnapKind.Intersection, x.Position.DistanceTo(point))));
        foreach (var edge in _network.Edges)
        {
            var start = _network.FindNode(edge.StartNodeId)!.Position;
            var end = _network.FindNode(edge.EndNodeId)!.Position;
            if (modes.HasFlag(PipeSnapMode.Midpoint)) AddCandidate(candidates, point, start, end, 0.5, PipeSnapKind.Midpoint);
            if (modes.HasFlag(PipeSnapMode.Quadrant)) { AddCandidate(candidates, point, start, end, 0.25, PipeSnapKind.Quadrant); AddCandidate(candidates, point, start, end, 0.75, PipeSnapKind.Quadrant); }
            if (modes.HasFlag(PipeSnapMode.Nearest))
            {
                var t = Math.Clamp(Segment2D.ParameterOnSegment(point, start, end), 0, 1);
                AddCandidate(candidates, point, start, end, t, PipeSnapKind.Nearest);
            }
            // 직교점: 커서가 아니라 그리기 기준점(from)에서 엣지에 내린 수선의 발. 세그먼트 범위 밖이면 후보 제외.
            if (modes.HasFlag(PipeSnapMode.Perpendicular) && from is not null)
            {
                var t = Segment2D.ParameterOnSegment(from, start, end);
                if (t is >= 0 and <= 1) AddCandidate(candidates, point, start, end, t, PipeSnapKind.Perpendicular);
            }
        }
        return candidates
            .Where(x => x.Distance <= SnapTolerance)
            .OrderBy(x => Rank(x.Kind))
            .ThenBy(x => x.Distance)
            .Cast<PipeSnapResult?>()
            .FirstOrDefault();
    }

    public static int Rank(PipeSnapKind kind) => kind switch
    {
        PipeSnapKind.Endpoint => 0,
        PipeSnapKind.Intersection => 1,
        PipeSnapKind.Midpoint => 2,
        PipeSnapKind.Quadrant => 3,
        PipeSnapKind.Perpendicular => 4,
        _ => 5,
    };

    private static PipeSnapKind NodeKindOf(PipeNode node)
        => node.Degree >= 3 ? PipeSnapKind.Intersection : PipeSnapKind.Endpoint;

    private static void AddCandidate(List<PipeSnapResult> candidates, Point2D target, Point2D start, Point2D end, double t, PipeSnapKind kind)
    {
        var candidate = new Point2D(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t);
        candidates.Add(new PipeSnapResult(candidate, kind, candidate.DistanceTo(target)));
    }
    /// <summary><paramref name="diameterMm"/>는 새로 만드는 엣지에만 붙는다. 기존 엣지를 쪼갠 조각은
    /// <see cref="SplitEdgeAt"/>에서 원본 관경을 물려받고, 공선 중첩으로 이미 있는 구간은 그대로 둔다(docs/152 § A-3).</summary>
    public void AddSegment(Point2D start, Point2D end, double diameterMm = PipeEdge.DefaultDiameterMm)
    {
        if (start.DistanceTo(end) <= SnapTolerance) return;
        // UI에서 선택된 OSNAP 좌표는 이미 정확한 기존 좌표다. 여기서 다시 100mm 범위의 노드로
        // 끌어당기면 다른 스냅 후보를 선택했거나 스냅을 끈 경우에도 선이 임의로 변형될 수 있다.
        var startNode = GetOrCreateNode(start, GeometryTolerance);
        var endNode = GetOrCreateNode(end, GeometryTolerance);
        var splitPoints = new List<(double T, PipeNode Node)> { (0, startNode), (1, endNode) };

        foreach (var edge in _network.Edges.ToList())
        {
            var a = _network.FindNode(edge.StartNodeId)!;
            var b = _network.FindNode(edge.EndNodeId)!;
            var intersection = Segment2D.Intersect(startNode.Position, endNode.Position, a.Position, b.Position, GeometryTolerance);
            if (intersection.Kind == SegmentIntersectionKind.Point && intersection.Point is not null)
            {
                var node = GetOrCreateNode(intersection.Point, GeometryTolerance);
                splitPoints.Add((intersection.TOnFirst, node));
                SplitEdgeAt(edge, node, intersection.TOnSecond);
            }
            else if (intersection.Kind == SegmentIntersectionKind.Overlap)
            {
                // 공선 중첩은 각 선의 끝점을 서로의 분할점으로 사용해 동일 그래프로 수렴시킨다.
                AddIfOnSegment(splitPoints, a, startNode.Position, endNode.Position);
                AddIfOnSegment(splitPoints, b, startNode.Position, endNode.Position);
                SplitEdgeAt(edge, startNode, Segment2D.ParameterOnSegment(startNode.Position, a.Position, b.Position));
                SplitEdgeAt(edge, endNode, Segment2D.ParameterOnSegment(endNode.Position, a.Position, b.Position));
            }
        }

        foreach (var pair in splitPoints.OrderBy(x => x.T).DistinctBy(x => x.Node.Id).Zip(splitPoints.OrderBy(x => x.T).DistinctBy(x => x.Node.Id).Skip(1)))
            AddEdgeIfMissing(pair.First.Node.Id, pair.Second.Node.Id, diameterMm);
        ClassifyNodes();
    }

    /// <summary>엣지 여러 개를 평행 이동한다. <paramref name="detachShared"/>가 true면 선택 밖 엣지와
    /// 공유하는 절점을 복제해 선택한 엣지만 떼어낸다(docs/152 § B-3).</summary>
    public void MoveEdges(IReadOnlyCollection<Guid> edgeIds, double dx, double dy, bool detachShared)
    {
        var moving = edgeIds.Select(_network.FindEdge).OfType<PipeEdge>().Select(x => x.Id).ToHashSet();
        if (moving.Count == 0) return;
        var nodeIds = _network.Edges.Where(x => moving.Contains(x.Id))
            .SelectMany(x => new[] { x.StartNodeId, x.EndNodeId })
            .Distinct().ToList();
        Move(nodeIds, dx, dy, detachShared ? moving : null);
    }

    /// <summary>절점을 옮긴다. 절점 자체를 잡아 끄는 조작이므로 물린 엣지는 모두 따라온다(분리 없음).</summary>
    public void MoveNodes(IReadOnlyCollection<Guid> nodeIds, double dx, double dy)
        => Move(nodeIds, dx, dy, null);

    /// <param name="movingEdgeIds">null이면 분리하지 않는다(절점 그립 조정). 값이 있으면 그 엣지들만 떼어낸다.</param>
    private void Move(IReadOnlyCollection<Guid> nodeIds, double dx, double dy, HashSet<Guid>? movingEdgeIds)
    {
        if (nodeIds.Count == 0 || (Math.Abs(dx) <= GeometryTolerance && Math.Abs(dy) <= GeometryTolerance)) return;

        // 1) 분리 — 선택 밖 엣지에도 물린 절점은 복제해서 선택 엣지 쪽만 새 절점을 보게 한다.
        var movedNodeIds = new List<Guid>();
        foreach (var nodeId in nodeIds)
        {
            var node = _network.FindNode(nodeId);
            if (node is null) continue;
            var connected = _network.Edges.Where(x => x.StartNodeId == nodeId || x.EndNodeId == nodeId).ToList();
            var inside = movingEdgeIds is null ? connected : connected.Where(x => movingEdgeIds.Contains(x.Id)).ToList();
            var outside = movingEdgeIds is null ? [] : connected.Where(x => !movingEdgeIds.Contains(x.Id)).ToList();
            if (inside.Count == 0 || outside.Count == 0) { movedNodeIds.Add(nodeId); continue; }

            var clone = _network.AddNode(node.Position);
            foreach (var edge in inside)
                _network.ReplaceEdgeNodes(edge,
                    edge.StartNodeId == nodeId ? clone.Id : edge.StartNodeId,
                    edge.EndNodeId == nodeId ? clone.Id : edge.EndNodeId);
            movedNodeIds.Add(clone.Id);
        }

        // 2) 이동
        foreach (var nodeId in movedNodeIds)
        {
            var node = _network.FindNode(nodeId);
            if (node is not null) node.Position = new Point2D(node.Position.X + dx, node.Position.Y + dy);
        }

        // 3) 길이 0으로 접힌 엣지 제거
        foreach (var edge in _network.Edges.ToList())
        {
            var start = _network.FindNode(edge.StartNodeId);
            var end = _network.FindNode(edge.EndNodeId);
            if (start is null || end is null || start.Position.DistanceTo(end.Position) <= GeometryTolerance)
                _network.RemoveEdge(edge);
        }

        // 4) 같은 자리에 겹친 절점 병합 — 끝점을 다른 선 끝점에 갖다 붙이면 연결되는 동작이다.
        MergeCoincidentNodes(movedNodeIds);
        // 5) 이동 결과 생긴 교차를 작도할 때와 같은 규칙으로 분할한다.
        NormalizeIntersections();
        RemoveDuplicateEdges();
        _network.RemoveUnconnectedNodes();
        ClassifyNodes();
    }

    /// <summary>인라인 부속을 자기 엣지 위에서 슬라이드한다. 최소 간격 규칙은 배치할 때와 같다(docs/152 § B-4).</summary>
    public void MoveInlineFitting(Guid edgeId, Guid fittingId, double t)
    {
        var edge = _network.FindEdge(edgeId) ?? throw new ArgumentOutOfRangeException(nameof(edgeId));
        var target = edge.InlineFittings.FirstOrDefault(x => x.Id == fittingId)
            ?? throw new ArgumentOutOfRangeException(nameof(fittingId));
        var start = _network.FindNode(edge.StartNodeId)!.Position;
        var end = _network.FindNode(edge.EndNodeId)!.Position;
        var length = start.DistanceTo(end);
        var minSpacingT = Math.Min(0.45, 100 / length);
        var others = edge.InlineFittings.Where(x => x.Id != fittingId).ToList();
        // 최소 간격은 같은 부류끼리만 본다(docs/230 § B) — 세트와 그 안쪽 일반 밸브는 서로 밀어내지 않는다.
        var resolved = FindAvailableFittingT(others.Where(x => x.IsBypassSet == target.IsBypassSet).ToList(), Math.Clamp(t, 0, 1), minSpacingT);
        edge.ReplaceFittings(others.Append(target with { T = resolved })
            .OrderBy(x => x.T)
            .Select((x, i) => x with { Order = i }));
    }

    private void MergeCoincidentNodes(IReadOnlyCollection<Guid> movedNodeIds)
    {
        foreach (var nodeId in movedNodeIds)
        {
            var node = _network.FindNode(nodeId);
            if (node is null) continue;
            var target = _network.Nodes.FirstOrDefault(x => x.Id != node.Id && x.Position.DistanceTo(node.Position) <= GeometryTolerance);
            if (target is null) continue;
            foreach (var edge in _network.Edges.ToList())
            {
                if (edge.StartNodeId != node.Id && edge.EndNodeId != node.Id) continue;
                var start = edge.StartNodeId == node.Id ? target.Id : edge.StartNodeId;
                var end = edge.EndNodeId == node.Id ? target.Id : edge.EndNodeId;
                if (start == end) { _network.RemoveEdge(edge); continue; }
                _network.ReplaceEdgeNodes(edge, start, end);
            }
            _network.RemoveNode(node);
        }
    }

    /// <summary>교차·공선 중첩이 남지 않을 때까지 엣지를 쪼갠다. 이미 정규화된 그래프에서는 아무것도 하지 않는다.
    /// 분할이 일어날 때마다 컬렉션이 통째로 바뀌므로 한 건 처리 후 처음부터 다시 훑는다.</summary>
    private void NormalizeIntersections()
    {
        // 엣지 수만큼만 쪼개질 수 있으므로 상한은 넉넉히 잡되, 기하 오차로 무한 루프가 되지 않도록 가드를 둔다.
        for (var guard = 0; guard < 1000 && SplitFirstIntersection(); guard++) { }
    }

    private bool SplitFirstIntersection()
    {
        var edges = _network.Edges.ToList();
        for (var i = 0; i < edges.Count; i++)
        {
            for (var j = i + 1; j < edges.Count; j++)
            {
                var first = edges[i];
                var second = edges[j];
                var a = _network.FindNode(first.StartNodeId)!;
                var b = _network.FindNode(first.EndNodeId)!;
                var c = _network.FindNode(second.StartNodeId)!;
                var d = _network.FindNode(second.EndNodeId)!;
                var intersection = Segment2D.Intersect(a.Position, b.Position, c.Position, d.Position, GeometryTolerance);
                if (intersection.Kind == SegmentIntersectionKind.Point && intersection.Point is not null)
                {
                    var node = GetOrCreateNode(intersection.Point, GeometryTolerance);
                    // 두 엣지 모두 시도한다. 끝점이 상대 엣지 내부에 닿은 경우 한쪽만 쪼개진다(T 접점).
                    var split = SplitEdgeAt(first, node, intersection.TOnFirst) is not null;
                    split |= SplitEdgeAt(second, node, intersection.TOnSecond) is not null;
                    if (split) return true;
                }
                else if (intersection.Kind == SegmentIntersectionKind.Overlap)
                {
                    var split = SplitEdgeAt(first, c, Segment2D.ParameterOnSegment(c.Position, a.Position, b.Position)) is not null;
                    split |= SplitEdgeAt(first, d, Segment2D.ParameterOnSegment(d.Position, a.Position, b.Position)) is not null;
                    split |= SplitEdgeAt(second, a, Segment2D.ParameterOnSegment(a.Position, c.Position, d.Position)) is not null;
                    split |= SplitEdgeAt(second, b, Segment2D.ParameterOnSegment(b.Position, c.Position, d.Position)) is not null;
                    if (split) return true;
                }
            }
        }
        return false;
    }

    /// <summary>같은 두 절점을 잇는 엣지가 여럿이면 처음 것만 남긴다. 이동으로 선이 다른 선 위에 정확히 포개진 경우다.</summary>
    private void RemoveDuplicateEdges()
    {
        var seen = new HashSet<(Guid, Guid)>();
        foreach (var edge in _network.Edges.ToList())
        {
            var key = edge.StartNodeId.CompareTo(edge.EndNodeId) <= 0
                ? (edge.StartNodeId, edge.EndNodeId)
                : (edge.EndNodeId, edge.StartNodeId);
            if (!seen.Add(key)) _network.RemoveEdge(edge);
        }
    }

    public void AddInlineFitting(Guid edgeId, string typeKey, string familyTypeName, double desiredT, bool preservePosition = false,
        BypassSetSpec? bypass = null)
    {
        var edge = _network.FindEdge(edgeId) ?? throw new ArgumentOutOfRangeException(nameof(edgeId));
        var start = _network.FindNode(edge.StartNodeId)!.Position;
        var end = _network.FindNode(edge.EndNodeId)!.Position;
        var length = start.DistanceTo(end);
        var minSpacingT = Math.Min(0.45, 100 / length);
        // 최소 간격은 같은 부류끼리만 본다 — 바이패스 세트 안쪽(A–A 사이)에 일반 밸브를 놓을 수 있어야 한다(docs/230 § B).
        var isBypassSet = bypass is not null && string.Equals(typeKey, BypassSet.TypeKey, StringComparison.Ordinal);
        var peers = edge.InlineFittings.Where(x => x.IsBypassSet == isBypassSet).ToList();
        var t = preservePosition ? Math.Clamp(desiredT, 0, 1) : FindAvailableFittingT(peers, desiredT, minSpacingT);
        edge.AddFitting(new InlineFitting(typeKey, familyTypeName, t, edge.InlineFittings.Count) { Bypass = bypass });
    }

    private static double FindAvailableFittingT(IReadOnlyList<InlineFitting> fittings, double desiredT, double minSpacingT)
    {
        var lower = minSpacingT;
        var upper = 1 - minSpacingT;
        for (var offset = 0; offset <= 100; offset++)
        {
            foreach (var direction in offset == 0 ? new[] { 0 } : new[] { -1, 1 })
            {
                var candidate = Math.Clamp(desiredT + direction * offset * minSpacingT, lower, upper);
                if (fittings.All(x => Math.Abs(x.T - candidate) >= minSpacingT)) return candidate;
            }
        }
        throw new DhbimwaterException(ErrorCodes.PL_NoRoomForFitting, "선 위에 부속품을 배치할 공간이 부족합니다.");
    }

    public void RemoveInlineFitting(Guid edgeId, Guid fittingId)
    {
        var edge = _network.FindEdge(edgeId) ?? throw new ArgumentOutOfRangeException(nameof(edgeId));
        var remaining = edge.InlineFittings.Where(x => x.Id != fittingId).Select((x, i) => x with { Order = i }).ToList();
        _network.RemoveEdge(edge);
        _network.AddEdge(edge.StartNodeId, edge.EndNodeId, edge.DiameterMm, remaining);
    }

    public void RemoveSegment(Guid edgeId)
    {
        var edge = _network.FindEdge(edgeId) ?? throw new ArgumentOutOfRangeException(nameof(edgeId));
        _network.RemoveEdge(edge);
        _network.RemoveUnconnectedNodes();
        ClassifyNodes();
    }

    private PipeNode GetOrCreateNode(Point2D position, double tolerance) => _network.Nodes.FirstOrDefault(x => x.Position.DistanceTo(position) <= tolerance) ?? _network.AddNode(position);

    private void AddIfOnSegment(List<(double T, PipeNode Node)> points, PipeNode node, Point2D start, Point2D end)
    {
        var t = Segment2D.ParameterOnSegment(node.Position, start, end);
        var projected = new Point2D(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t);
        var length = start.DistanceTo(end);
        var parameterTolerance = length <= double.Epsilon ? 0 : GeometryTolerance / length;
        if (t >= -parameterTolerance && t <= 1 + parameterTolerance && projected.DistanceTo(node.Position) <= GeometryTolerance)
            points.Add((Math.Clamp(t, 0, 1), node));
    }

    /// <summary>쪼갠 두 조각은 원본 엣지의 관경을 물려받는다 — 기존 선을 가로질렀다고 관경이 바뀌면 안 된다(docs/152 § A-3).</summary>
    /// <returns>실제로 쪼갰으면 두 조각의 Id, 아니면 null(docs/179 § H-2에서 반환 타입만 넓혔다).</returns>
    private (Guid First, Guid Second)? SplitEdgeAt(PipeEdge edge, PipeNode node, double t)
    {
        if (node.Id == edge.StartNodeId || node.Id == edge.EndNodeId || t <= 0.0001 || t >= 0.9999) return null;
        var first = edge.InlineFittings.Where(x => x.T < t).Select((x, i) => x with { T = x.T / t, Order = i }).ToList();
        var second = edge.InlineFittings.Where(x => x.T >= t).Select((x, i) => x with { T = (x.T - t) / (1 - t), Order = i }).ToList();
        _network.RemoveEdge(edge);
        var a = _network.AddEdge(edge.StartNodeId, node.Id, edge.DiameterMm, first);
        var b = _network.AddEdge(node.Id, edge.EndNodeId, edge.DiameterMm, second);
        return (a.Id, b.Id);
    }

    /// <summary>엣지를 t(0~1) 지점에서 둘로 나눈다. 수동 분할(docs/179 § H)의 진입점이다.
    /// <para>교차 정리(<see cref="SplitFirstIntersection"/>)와 같은 <see cref="SplitEdgeAt"/>를 쓰므로
    /// 관경 상속·인라인 부속 재분배 규칙이 저절로 같다.</para>
    /// <para><see cref="NormalizeIntersections"/>·<see cref="RemoveDuplicateEdges"/>는 부르지 않는다 —
    /// 분할점은 원래 엣지 위에 있어 새 교차가 생기지 않으며, 부르면 사용자가 지시하지 않은 다른 선까지 재정렬된다.</para></summary>
    /// <param name="detach">true면 분할점에 <b>같은 좌표의 절점을 2개</b> 만들어 두 조각이 각자 자기 끝점을 갖게 한다
    /// (docs/181 § B). 끝점 하나를 끌면 그 조각만 움직인다.
    /// <para>false(기본)는 절점 하나를 공유한다 — 교차 정리(<see cref="SplitFirstIntersection"/>)가 쓰는 길이며,
    /// 진짜 교차점은 연결된 채로 있어야 하므로 그쪽은 절대 분리하지 않는다.</para>
    /// <para>Revit 모델 생성은 절점이 아니라 <b>좌표가 겹친 커넥터</b>를 보고 접합하므로
    /// (<c>RevitPipeSegmentCommandRepo.ConnectCoincidentConnectors</c>), 분리해도 두 관은 그대로 연결된다.</para></param>
    public (Guid First, Guid Second)? SplitSegment(Guid edgeId, double t, bool detach = false)
    {
        var edge = _network.FindEdge(edgeId) ?? throw new ArgumentOutOfRangeException(nameof(edgeId));
        var start = _network.FindNode(edge.StartNodeId)!.Position;
        var end = _network.FindNode(edge.EndNodeId)!.Position;
        var point = new Point2D(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t);
        var node = GetOrCreateNode(point, GeometryTolerance);
        var result = SplitEdgeAt(edge, node, t);
        // 쪼개지 못했으면 방금 만들었을 수도 있는 고아 절점을 남기지 않는다.
        if (result is null) { _network.RemoveUnconnectedNodes(); return null; }
        // 뒷 조각의 시작 절점만 같은 자리의 새 절점으로 갈아 끼운다. 앞 조각은 원래 절점을 그대로 쓴다.
        if (detach)
        {
            var second = _network.FindEdge(result.Value.Second)!;
            var clone = _network.AddNode(node.Position);
            _network.ReplaceEdgeNodes(second, clone.Id, second.EndNodeId);
        }
        ClassifyNodes();
        return result;
    }

    private void AddEdgeIfMissing(Guid start, Guid end, double diameterMm)
    {
        if (start == end || _network.Edges.Any(x => (x.StartNodeId == start && x.EndNodeId == end) || (x.StartNodeId == end && x.EndNodeId == start))) return;
        _network.AddEdge(start, end, diameterMm);
    }

    private void ClassifyNodes()
    {
        foreach (var node in _network.Nodes)
        {
            var connected = _network.Edges.Where(x => x.StartNodeId == node.Id || x.EndNodeId == node.Id).ToList();
            node.Degree = connected.Count;
            node.NodeKind = node.Degree switch
            {
                1 => NodeKind.EndPoint,
                2 => IsStraight(node, connected) ? NodeKind.Inline : NodeKind.Elbow,
                // 경사 분기(30~60°)만 Y형으로 뗀다. 직교도 아니고 Y 범위도 아닌 각도는 Tee로 남아
                // 배치 경계에서 종전과 같은 사유로 건너뛴다(docs/187 § 1-2).
                3 => IsWye(node, connected) ? NodeKind.Wye : NodeKind.Tee,
                >= 4 => NodeKind.Cross,
                _ => NodeKind.Inline
            };
            // 밸브부관 B는 꺾인 절점, A는 T 절점에만 뜻이 있다. 선을 잇거나 지워 모양이 바뀌면 기본값으로 되돌린다
            // (docs/225 § A-4, docs/227 § A). 규칙은 PipeNetwork.SetNodeFittingKind와 같은 IsAllowed다.
            if (node.FittingKind != PipeNodeFittingKind.Bend && !PipeNetwork.IsAllowed(node.NodeKind, node.FittingKind))
                node.FittingKind = PipeNodeFittingKind.Bend;
            if (node.FittingKind != PipeNodeFittingKind.ValveBend) node.ValveSideDirection = null;
        }
    }

    /// <summary>차수 3 절점이 Y형인지 — 주관 2레그가 일직선이고 나머지가 30~60°로 붙었는지 본다(docs/187 § 1-2).
    /// 판정 상수는 <see cref="PipeWyeResolver"/>의 것을 그대로 쓴다. 배치 경계와 값이 갈리면 안 된다.</summary>
    private bool IsWye(PipeNode node, IReadOnlyList<PipeEdge> edges)
    {
        if (edges.Count != 3) return false;
        var directions = edges.Select(x => DirectionAwayFrom(node, x)).ToList();
        if (directions.Any(x => x is null)) return false;

        var pairs = new[] { (A: 0, B: 1, Branch: 2), (A: 0, B: 2, Branch: 1), (A: 1, B: 2, Branch: 0) };
        var main = pairs.Select(x => (Pair: x, Angle: AngleDeg(directions[x.A]!.Value, directions[x.B]!.Value)))
            .OrderByDescending(x => x.Angle).First();
        if (Math.Abs(180 - main.Angle) > StraightAngleToleranceDegrees) return false;

        var branch = directions[main.Pair.Branch]!.Value;
        var branchAngleDeg = Math.Min(
            AngleDeg(directions[main.Pair.A]!.Value, branch),
            AngleDeg(directions[main.Pair.B]!.Value, branch));
        return PipeWyeResolver.IsWyeBranchAngle(branchAngleDeg);
    }

    private (double X, double Y)? DirectionAwayFrom(PipeNode node, PipeEdge edge)
    {
        var otherId = edge.StartNodeId == node.Id ? edge.EndNodeId : edge.StartNodeId;
        var other = _network.FindNode(otherId);
        if (other is null) return null;
        var length = node.Position.DistanceTo(other.Position);
        return length <= GeometryTolerance
            ? null
            : ((other.Position.X - node.Position.X) / length, (other.Position.Y - node.Position.Y) / length);
    }

    private static double AngleDeg((double X, double Y) a, (double X, double Y) b)
        => Math.Acos(Math.Clamp(a.X * b.X + a.Y * b.Y, -1, 1)) * 180 / Math.PI;

    private bool IsStraight(PipeNode node, IReadOnlyList<PipeEdge> edges)
    {
        var other1 = _network.FindNode(edges[0].StartNodeId == node.Id ? edges[0].EndNodeId : edges[0].StartNodeId)!;
        var other2 = _network.FindNode(edges[1].StartNodeId == node.Id ? edges[1].EndNodeId : edges[1].StartNodeId)!;
        var dot = (other1.Position.X - node.Position.X) * (other2.Position.X - node.Position.X) + (other1.Position.Y - node.Position.Y) * (other2.Position.Y - node.Position.Y);
        var lengths = node.Position.DistanceTo(other1.Position) * node.Position.DistanceTo(other2.Position);
        return lengths > 0 && Math.Acos(Math.Clamp(dot / lengths, -1, 1)) * 180 / Math.PI >= 180 - StraightAngleToleranceDegrees;
    }
}
