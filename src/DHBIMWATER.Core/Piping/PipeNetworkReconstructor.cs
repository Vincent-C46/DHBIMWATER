using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Core.Piping;

/// <summary>
/// 모델에서 읽어 온 배관 부재를 캔버스 배관망으로 되돌린다(docs/158 § D).
/// <para>정방향(<c>RevitPipeSegmentCommandRepo.CreateNetwork</c>)이 엣지 1개를 직관·단관 여러 본으로
/// <b>쪼갠다</b>. 여기서는 그 역산으로, 절점 부속(곡관·T형)과 자유단 사이의 부재 체인을
/// <b>엣지 1개로 다시 합친다</b>.</para>
/// <para>순수 계산이다. Revit 타입을 모르며, 실측은 Infrastructure가 끝내서 넘긴다.</para>
/// </summary>
public static class PipeNetworkReconstructor
{
    /// <summary>두 커넥터를 같은 접점으로 볼 거리(mm). 배치 경로의 같은 이름 상수와 값을 맞춘다
    /// (<c>RevitPipeSegmentCommandRepo.ConnectionToleranceMm</c>).</summary>
    public const double ConnectionToleranceMm = 1.0;

    /// <summary>체인 안에서 직선으로 볼 꺾임각(도). 넘으면 곡관 없이 꺾인 것이므로 엣지를 나눈다.</summary>
    private const double CollinearToleranceDeg = PipeMaterialSpecText.StraightDeflectionToleranceDeg;

    /// <summary>표고가 섞였다고 보는 편차(mm).</summary>
    private const double ElevationToleranceMm = 1.0;

    /// <summary>접점 후보를 추릴 격자 칸 크기(mm). 부재 수가 많아도 O(n²) 비교로 가지 않게 한다.</summary>
    private const double BucketSizeMm = 10.0;

    private const double GeometryTolerance = 1e-9;

    /// <summary>체인 안에서 부재 하나가 놓인 방향. Head가 엣지 시작 쪽, Tail이 끝 쪽 커넥터다.</summary>
    private sealed record ChainLink(int Part, int HeadConn, int TailConn);

    public static PipeLayoutImport Build(IReadOnlyList<PickedPipePart> parts)
    {
        var warnings = new List<string>();
        if (parts is null || parts.Count == 0) return PipeLayoutImport.Empty;

        // 1) 판정에 실패한 부재는 그래프에 넣지 않는다. 한 부재 때문에 전체가 실패하면 안 된다(docs/158 § D-8).
        var valid = new List<PickedPipePart>();
        foreach (var part in parts)
        {
            if (part.Role != PipePartRole.Unknown && string.IsNullOrWhiteSpace(part.SkipReason) && part.Connectors.Count > 0)
            {
                valid.Add(part);
                continue;
            }
            warnings.Add($"ElementId {part.ElementId} '{part.FamilyTypeName}'을(를) 건너뛰었습니다: " +
                         $"{part.SkipReason ?? "배관 커넥터가 없거나 부재 종류를 판정할 수 없습니다"}. 재생성 시 삭제됩니다.");
        }
        if (valid.Count == 0) return new PipeLayoutImport([], [], 0, [], warnings);

        var matches = MatchConnectors(valid);
        // 바이패스 세트(A 2 · B 2 · 바이패스 체인)를 떼어 내고, A 자리는 본관을 잇는 합성 관으로 바꾼다(docs/229 § C, docs/230 § D).
        // 부재 목록이 바뀌므로 접점을 다시 맺는다. A–A 사이 관·밸브는 그대로 남아 일반 관·인라인 밸브로 복원된다.
        var collapsed = CollapseBypassSets(valid, matches, warnings);
        var consumedSetParts = new List<PickedPipePart>();
        if (collapsed.Count > 0)
        {
            var consumed = collapsed.SelectMany(x => x.Members).ToHashSet();
            consumedSetParts.AddRange(consumed.Select(x => valid[x]));
            valid = valid.Where((_, i) => !consumed.Contains(i)).Concat(collapsed.SelectMany(x => x.Replacements)).ToList();
            matches = MatchConnectors(valid);
        }
        BridgeShiftedValves(valid, matches, warnings);
        var nodePositions = ResolveNodePositions(valid, warnings);

        var edges = new List<ImportedEdge>();
        var fittings = new List<ImportedFitting>();
        var used = new HashSet<int>();
        // 엣지별로 "이 엣지에 올라탄 밸브 부재 인덱스"를 모아 뒀다가, 엣지 좌표가 확정된 뒤 T를 계산한다.
        var ridingParts = new List<List<int>>();
        BuildChainEdges(valid, matches, nodePositions, edges, ridingParts, used, warnings);
        BuildNodeToNodeEdges(valid, matches, nodePositions, edges, ridingParts, used, warnings);

        // 2) 체인에 실려 있던 밸브·이음관을 엣지 위 T로 환산한다(docs/158 § D-5, docs/175 § C-4).
        for (var edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
        {
            foreach (var partIndex in ridingParts[edgeIndex])
            {
                fittings.Add(new ImportedFitting(edgeIndex, valid[partIndex].FamilyTypeName,
                    ParameterOn(edges[edgeIndex], CenterOf(valid[partIndex])),
                    IsJoint: valid[partIndex].Role == PipePartRole.Joint)
                    { ElementId = valid[partIndex].ElementId });
                used.Add(partIndex);
            }
        }

        // 바이패스 세트는 두 A 중심을 모두 지나는 엣지의 중점에 싣는다(docs/230 § D). 본관 밸브는 세트가 아니다 — FamilyTypeName은 빈 문자열.
        foreach (var set in collapsed)
        {
            var edgeIndex = edges.FindIndex(x => IsOnEdge(x, set.CenterA) && IsOnEdge(x, set.CenterA2));
            if (edgeIndex < 0)
            {
                warnings.Add($"바이패스 세트(ElementId {set.Bypass.ElementIds[0]})를 올릴 본관 선을 찾지 못했습니다. 재생성 시 세트 부재가 삭제됩니다.");
                continue;
            }
            fittings.Add(new ImportedFitting(edgeIndex, string.Empty, ParameterOn(edges[edgeIndex], Midpoint(set.CenterA, set.CenterA2)))
                { ElementId = set.Bypass.ElementIds[0], Bypass = set.Bypass });
        }

        AttachFloatingAccessories(valid, edges, fittings, used, warnings);

        // 유효한데도 엣지·부속 어디에도 실리지 못한 부재. 지금까지는 조용히 사라졌지만
        // 재생성 때 함께 지워지므로 반드시 알린다(docs/163 § A-2).
        for (var i = 0; i < valid.Count; i++)
        {
            if (used.Contains(i)) continue;
            warnings.Add($"ElementId {valid[i].ElementId} '{valid[i].FamilyTypeName}'은(는) 캔버스에 올리지 못했습니다. 재생성 시 삭제됩니다.");
        }

        if (edges.Count == 0)
        {
            warnings.Add("연결된 배관을 찾지 못했습니다. 관·곡관·T형이 커넥터로 이어져 있는지 확인하세요.");
            return new PipeLayoutImport([], [], 0, [], warnings);
        }

        // 세트로 접힌 부재도 대표 패밀리(밸브부관 A·B)·표고·관종 집계에 넣는다 — 빠지면 설정 칸이 비고 표고 평균이 틀어진다.
        var usedParts = used.Select(x => valid[x]).Concat(consumedSetParts).ToList();
        // 삭제 대상은 "그래프에 반영된 것"이 아니라 "피킹한 것 전부"다. 판정 실패분을 남기면
        // 재생성한 배관과 겹쳐 모델에 남는다(docs/163 § A-1). 대표 관경·패밀리·표고는 그대로 usedParts에서 뽑는다.
        var sourceIds = parts.Select(x => x.ElementId).Distinct().OrderBy(x => x).ToList();
        // 캔버스에 올라간 절점 부속만 싣는다. 못 올린 것은 위 경고가 이미 알린다(docs/216 § A-2).
        var nodeFittings = nodePositions.Where(x => used.Contains(x.Key)).OrderBy(x => x.Key)
            .Select(x => ToImportedNodeFitting(valid[x.Key], x.Value)).ToList();
        return new PipeLayoutImport(
            edges,
            fittings,
            AverageElevation(usedParts, warnings),
            sourceIds,
            warnings,
            Mode(edges.Select(x => x.DiameterMm)),
            DominantFamilyTypeName(usedParts, PipePartRole.Segment, "관", warnings),
            DominantFamilyTypeName(usedParts, PipePartRole.Bend, "곡관", warnings),
            DominantFamilyTypeName(usedParts, PipePartRole.Tee, "T형", warnings),
            SingleZone(usedParts, warnings),
            SingleMaterial(usedParts, warnings),
            DominantFamilyTypeName(usedParts, PipePartRole.Cross, "십자", warnings),
            DominantFamilyTypeName(usedParts, PipePartRole.Wye, "Y형", warnings),
            nodeFittings,
            DominantFamilyTypeName(usedParts, PipePartRole.ValveBend, PipeValveBend.PartNameB, warnings),
            DominantFamilyTypeName(usedParts, PipePartRole.ValveTee, PipeValveBend.PartNameA, warnings));
    }

    // ── 접점 그래프 ───────────────────────────────────────────────────────────────

    /// <summary>커넥터끼리 같은 접점인지 맺는다(docs/158 § D-1).
    /// Revit이 실제로 연결해 둔 짝이거나, 원점이 <see cref="ConnectionToleranceMm"/> 안이면 같은 접점이다.
    /// 후자를 함께 보는 이유: 배치 때 건너뛴 부속 자리는 커넥터를 일부러 열어 둔다(docs/145 § 4-6).</summary>
    private static Dictionary<(int Part, int Conn), (int Part, int Conn)> MatchConnectors(IReadOnlyList<PickedPipePart> parts)
    {
        var partByElementId = new Dictionary<long, int>();
        for (var i = 0; i < parts.Count; i++) partByElementId[parts[i].ElementId] = i;

        var buckets = new Dictionary<(long X, long Y), List<(int Part, int Conn)>>();
        for (var p = 0; p < parts.Count; p++)
            for (var c = 0; c < parts[p].Connectors.Count; c++)
            {
                var key = BucketKey(parts[p].Connectors[c].Point);
                if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = [];
                list.Add((p, c));
            }

        var seen = new HashSet<((int, int), (int, int))>();
        var candidates = new List<(double Distance, (int Part, int Conn) A, (int Part, int Conn) B)>();
        for (var p = 0; p < parts.Count; p++)
            for (var c = 0; c < parts[p].Connectors.Count; c++)
            {
                var connector = parts[p].Connectors[c];
                foreach (var other in NearbyConnectors(buckets, connector.Point).Concat(LinkedConnectors(parts, partByElementId, connector)))
                {
                    if (other.Part == p) continue;
                    var key = OrderedKey((p, c), other);
                    if (!seen.Add(key)) continue;

                    var distance = connector.Point.DistanceTo(parts[other.Part].Connectors[other.Conn].Point);
                    if (distance > ConnectionToleranceMm && !IsLinked(parts, (p, c), other)) continue;
                    candidates.Add((distance, (p, c), other));
                }
            }

        // 커넥터 하나는 접점 하나만 가진다. 가까운 짝부터 확정한다.
        var result = new Dictionary<(int Part, int Conn), (int Part, int Conn)>();
        foreach (var candidate in candidates.OrderBy(x => x.Distance))
        {
            if (result.ContainsKey(candidate.A) || result.ContainsKey(candidate.B)) continue;
            result[candidate.A] = candidate.B;
            result[candidate.B] = candidate.A;
        }
        return result;
    }

    /// <summary>밸브 폭이 관 틈과 달라 한쪽 커넥터가 관 끝과 어긋난 자리를 잇는다(docs/224 § B).
    /// 정방향이 예약한 틈과 실제 밸브 폭이 다르면 밸브가 한쪽 관과 겹치거나 틈이 남아, 짝이 없는 커넥터에서 체인이 끊긴다.
    /// 짝이 없는 밸브 커넥터와 같은 축 위, 밸브 폭 이내에 있는 짝 없는 관 끝을 짝으로 맺는다 —
    /// 사이에 밸브가 있을 때만 합친다(2026-09-28 사용자 결정). 밸브 없는 틈은 지금처럼 나눈다.</summary>
    private static void BridgeShiftedValves(
        IReadOnlyList<PickedPipePart> parts, Dictionary<(int Part, int Conn), (int Part, int Conn)> matches, ICollection<string> warnings)
    {
        var facingCos = Math.Cos(CollinearToleranceDeg * Math.PI / 180);
        for (var v = 0; v < parts.Count; v++)
        {
            var valve = parts[v];
            if (valve.Role != PipePartRole.Accessory || valve.Connectors.Count != 2) continue;
            for (var c = 0; c < 2; c++)
            {
                if (matches.ContainsKey((v, c))) continue;
                var own = valve.Connectors[c];
                var opposite = valve.Connectors[1 - c];
                var widthMm = own.Point.DistanceTo(opposite.Point);
                var axis = Normalize(new Point2D(own.Point.X - opposite.Point.X, own.Point.Y - opposite.Point.Y));
                if (axis is null || widthMm <= ConnectionToleranceMm) continue;

                (int Part, int Conn)? best = null;
                var bestAlong = double.MaxValue;
                for (var p = 0; p < parts.Count; p++)
                {
                    if (parts[p].Role != PipePartRole.Segment) continue;
                    for (var k = 0; k < parts[p].Connectors.Count; k++)
                    {
                        if (matches.ContainsKey((p, k))) continue;
                        var end = parts[p].Connectors[k];
                        var offset = new Point2D(end.Point.X - own.Point.X, end.Point.Y - own.Point.Y);
                        var along = Dot(offset, axis);
                        var across = Math.Abs(offset.X * axis.Y - offset.Y * axis.X);
                        if (across > ConnectionToleranceMm || Math.Abs(end.ZMm - own.ZMm) > ElevationToleranceMm) continue;
                        // 밸브 안으로 겹쳤거나(음수) 밸브 폭 이내로 떨어진 틈(양수). 그보다 먼 관 끝은 우연한 일직선으로 본다.
                        if (along <= -widthMm + ConnectionToleranceMm || along > widthMm) continue;
                        // 관 끝이 밸브 쪽을 향해야 한다 — 관 본체가 밸브 반대편으로 이어지는 경우만 체인의 다음 부재다.
                        if (Dot(end.Outward, axis) > -facingCos) continue;
                        if (Math.Abs(along) >= Math.Abs(bestAlong)) continue;
                        best = (p, k);
                        bestAlong = along;
                    }
                }
                if (best is not { } pipeEnd) continue;

                matches[(v, c)] = pipeEnd;
                matches[pipeEnd] = (v, c);
                warnings.Add($"밸브 '{valve.FamilyTypeName}'(ElementId {valve.ElementId})의 커넥터가 관(ElementId {parts[pipeEnd.Part].ElementId}) 끝과 " +
                             $"{Math.Abs(bestAlong):0}mm 어긋나 있어 한 배관으로 합쳤습니다. 모델의 밸브 폭({widthMm:0}mm)과 관 틈이 다릅니다.");
            }
        }
    }

    private static (long X, long Y) BucketKey(Point2D point)
        => ((long)Math.Floor(point.X / BucketSizeMm), (long)Math.Floor(point.Y / BucketSizeMm));

    private static IEnumerable<(int Part, int Conn)> NearbyConnectors(
        IReadOnlyDictionary<(long X, long Y), List<(int Part, int Conn)>> buckets, Point2D point)
    {
        var (x, y) = BucketKey(point);
        for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
                if (buckets.TryGetValue((x + dx, y + dy), out var list))
                    foreach (var item in list) yield return item;
    }

    private static IEnumerable<(int Part, int Conn)> LinkedConnectors(
        IReadOnlyList<PickedPipePart> parts, IReadOnlyDictionary<long, int> partByElementId, PickedConnector connector)
    {
        foreach (var elementId in connector.ConnectedElementIds)
        {
            if (!partByElementId.TryGetValue(elementId, out var peer)) continue;
            for (var c = 0; c < parts[peer].Connectors.Count; c++) yield return (peer, c);
        }
    }

    /// <summary>둘 중 한쪽이라도 상대 요소를 연결 상대로 들고 있으면 연결된 것으로 본다.</summary>
    private static bool IsLinked(IReadOnlyList<PickedPipePart> parts, (int Part, int Conn) a, (int Part, int Conn) b)
        => parts[a.Part].Connectors[a.Conn].ConnectedElementIds.Contains(parts[b.Part].ElementId)
        || parts[b.Part].Connectors[b.Conn].ConnectedElementIds.Contains(parts[a.Part].ElementId);

    private static ((int, int), (int, int)) OrderedKey((int Part, int Conn) a, (int Part, int Conn) b)
        => a.Part < b.Part || (a.Part == b.Part && a.Conn <= b.Conn) ? (a, b) : (b, a);

    // ── 절점 좌표 ────────────────────────────────────────────────────────────────

    /// <summary>곡관·T형의 캔버스 절점 좌표를 구한다(docs/158 § D-2).
    /// 정방향이 절점에서 레그 길이만큼 물러난 자리에 커넥터를 두므로, 레그 축의 교점이 원래 절점이다.</summary>
    private static Dictionary<int, Point2D> ResolveNodePositions(IReadOnlyList<PickedPipePart> parts, ICollection<string> warnings)
    {
        var result = new Dictionary<int, Point2D>();
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            switch (part.Role)
            {
                // 밸브부관도 곡관과 같이 두 레그 축의 교점이 절점이다(docs/225 § E).
                case PipePartRole.Bend or PipePartRole.ValveBend when part.Connectors.Count >= 2:
                {
                    var corner = LineIntersection(
                        part.Connectors[0].Point, Inward(part.Connectors[0]),
                        part.Connectors[1].Point, Inward(part.Connectors[1]));
                    if (corner is null)
                    {
                        warnings.Add($"곡관 ElementId {part.ElementId}의 레그 축이 만나지 않아 커넥터 중점을 절점으로 썼습니다.");
                        corner = Midpoint(part.Connectors[0].Point, part.Connectors[1].Point);
                    }
                    result[i] = corner;
                    break;
                }
                case PipePartRole.Tee when part.Connectors.Count >= 3:
                // Y형도 주관 축과 분기 축의 교점이 절점이다 — TeeNode는 분기각과 무관하게 성립한다(docs/187 § 5-1).
                case PipePartRole.Wye when part.Connectors.Count >= 3:
                // 밸브부관 A도 수직 커넥터를 뺀 3개로 T형과 같이 주관 축과 분기 축의 교점이 절점이다(docs/227 § E).
                case PipePartRole.ValveTee when part.Connectors.Count >= 3:
                {
                    result[i] = TeeNode(part, warnings);
                    break;
                }
                case PipePartRole.Cross when part.Connectors.Count >= 4:
                {
                    result[i] = CrossNode(part, warnings);
                    break;
                }
            }
        }
        return result;
    }

    /// <summary>십자의 캔버스 절점(docs/175 § B-4). 커넥터 0과 그 반대(편각 0°) 둘을 잇는 축 A,
    /// 나머지 둘을 잇는 축 B의 교점이다. 축이 만나지 않으면 커넥터 4점 평균으로 물러난다.</summary>
    private static Point2D CrossNode(PickedPipePart part, ICollection<string> warnings)
    {
        var connectors = part.Connectors;
        var opposite = 1;
        var best = double.MaxValue;
        for (var j = 1; j < connectors.Count; j++)
        {
            var deflection = PipeMaterialSpecText.DeflectionDeg(Dot(connectors[0].Outward, connectors[j].Outward));
            if (deflection >= best) continue;
            best = deflection;
            opposite = j;
        }
        var others = Enumerable.Range(1, connectors.Count - 1).Where(x => x != opposite).Take(2).ToList();
        var a0 = connectors[0].Point;
        var da = Normalize(new Point2D(connectors[opposite].Point.X - a0.X, connectors[opposite].Point.Y - a0.Y));
        var b0 = connectors[others[0]].Point;
        var db = Normalize(new Point2D(connectors[others[1]].Point.X - b0.X, connectors[others[1]].Point.Y - b0.Y));
        var intersection = LineIntersection(a0, da, b0, db);
        if (intersection is not null) return intersection;

        warnings.Add($"십자 ElementId {part.ElementId}의 축이 만나지 않아 커넥터 평균을 절점으로 썼습니다.");
        return new Point2D(connectors.Average(x => x.Point.X), connectors.Average(x => x.Point.Y));
    }

    private static Point2D TeeNode(PickedPipePart part, ICollection<string> warnings)
    {
        var branch = BranchIndex(part.Connectors);
        var mains = Enumerable.Range(0, part.Connectors.Count).Where(x => x != branch).Take(2).ToList();
        var mainStart = part.Connectors[mains[0]].Point;
        var mainDirection = Normalize(new Point2D(
            part.Connectors[mains[1]].Point.X - mainStart.X,
            part.Connectors[mains[1]].Point.Y - mainStart.Y));

        if (mainDirection is null)
        {
            warnings.Add($"T형 ElementId {part.ElementId}의 주관 축을 구하지 못해 인스턴스 원점을 절점으로 썼습니다.");
            return part.Origin;
        }

        var intersection = LineIntersection(mainStart, mainDirection, part.Connectors[branch].Point, Inward(part.Connectors[branch]));
        // 분기 커넥터가 수직이면(평면 성분 0) 교점이 없다. 그때는 분기점을 주관 축에 정사영한다.
        return intersection ?? ProjectOnLine(part.Connectors[branch].Point, mainStart, mainDirection);
    }

    /// <summary>T형의 분기 커넥터. 주관 2개는 서로 마주보므로(편각 0°) 그 짝이 아닌 하나가 분기다.
    /// <c>RevitPipeMaterialBackfillRepo.BranchConnector</c>와 같은 규칙이다.</summary>
    private static int BranchIndex(IReadOnlyList<PickedConnector> connectors)
    {
        var best = double.MaxValue;
        var branch = 0;
        for (var i = 0; i < connectors.Count; i++)
            for (var j = i + 1; j < connectors.Count; j++)
            {
                var deflection = PipeMaterialSpecText.DeflectionDeg(Dot(connectors[i].Outward, connectors[j].Outward));
                if (deflection >= best) continue;
                best = deflection;
                branch = Enumerable.Range(0, connectors.Count).First(x => x != i && x != j);
            }
        return branch;
    }

    // ── 체인 병합 ────────────────────────────────────────────────────────────────

    /// <summary>절점·자유단 사이의 관·밸브 체인을 엣지로 합친다(docs/158 § D-3).</summary>
    private static void BuildChainEdges(
        IReadOnlyList<PickedPipePart> parts,
        IReadOnlyDictionary<(int Part, int Conn), (int Part, int Conn)> matches,
        IReadOnlyDictionary<int, Point2D> nodePositions,
        List<ImportedEdge> edges, List<List<int>> ridingParts,
        HashSet<int> used, ICollection<string> warnings)
    {
        var visited = new HashSet<int>();
        for (var seed = 0; seed < parts.Count; seed++)
        {
            if (!IsChainPart(parts[seed]) || !visited.Add(seed)) continue;

            var links = new List<ChainLink> { new(seed, 0, 1) };
            var tail = Extend(seed, 1, append: true);
            var head = Extend(seed, 0, append: false);

            EmitChain(parts, links, EndpointOf(head), EndpointOf(tail), edges, ridingParts, used, warnings);

            (int Part, int Conn) Extend(int part, int conn, bool append)
            {
                while (true)
                {
                    if (!matches.TryGetValue((part, conn), out var next)) return (part, conn);
                    if (!IsChainPart(parts[next.Part]) || !visited.Add(next.Part)) return (part, conn);

                    var other = next.Conn == 0 ? 1 : 0;
                    if (append) links.Add(new ChainLink(next.Part, next.Conn, other));
                    else links.Insert(0, new ChainLink(next.Part, other, next.Conn));
                    part = next.Part;
                    conn = other;
                }
            }

            Point2D EndpointOf((int Part, int Conn) terminal)
            {
                // 절점 부속에 닿아 끝났으면 그 절점 좌표가 엣지 끝이다. 아니면 자유단 — 커넥터 자리가 곧 끝이다.
                if (matches.TryGetValue(terminal, out var peer) && nodePositions.TryGetValue(peer.Part, out var position))
                {
                    used.Add(peer.Part);
                    return position;
                }
                return parts[terminal.Part].Connectors[terminal.Conn].Point;
            }
        }
    }

    private static void EmitChain(
        IReadOnlyList<PickedPipePart> parts,
        IReadOnlyList<ChainLink> links, Point2D headEnd, Point2D tailEnd,
        List<ImportedEdge> edges, List<List<int>> ridingParts,
        HashSet<int> used, ICollection<string> warnings)
    {
        // 곡관 없이 꺾인 자리에서는 엣지를 나눈다 — 그대로 직선으로 이으면 재생성 때 어긋난다(docs/158 § D-3).
        var breaks = new List<int>();
        for (var k = 0; k + 1 < links.Count; k++)
        {
            var a = DirectionOf(parts, links[k]);
            var b = DirectionOf(parts, links[k + 1]);
            if (a is null || b is null) continue;
            var angle = AngleBetweenDeg(a, b);
            if (angle <= CollinearToleranceDeg) continue;
            breaks.Add(k);
            warnings.Add($"ElementId {parts[links[k].Part].ElementId}와 {parts[links[k + 1].Part].ElementId} 사이가 " +
                         $"곡관 없이 {angle:0.#}° 꺾여 있어 배관을 나눠 그렸습니다.");
        }

        var ranges = new List<(int From, int To)>();
        var from = 0;
        foreach (var index in breaks) { ranges.Add((from, index)); from = index + 1; }
        ranges.Add((from, links.Count - 1));

        for (var r = 0; r < ranges.Count; r++)
        {
            var (rangeFrom, rangeTo) = ranges[r];
            var start = r == 0 ? headEnd : JointPoint(parts, links[rangeFrom - 1], links[rangeFrom]);
            var end = r == ranges.Count - 1 ? tailEnd : JointPoint(parts, links[rangeTo], links[rangeTo + 1]);
            if (start.DistanceTo(end) <= ConnectionToleranceMm)
            {
                warnings.Add($"ElementId {parts[links[rangeFrom].Part].ElementId} 구간은 길이가 0에 가까워 제외했습니다.");
                continue;
            }

            var members = Enumerable.Range(rangeFrom, rangeTo - rangeFrom + 1).Select(x => links[x].Part).ToList();
            // 아래 riding 분류와 같은 기준이다. 밸브·이음관은 자기 부속 항목이 ElementId를 가지므로
            // 엣지에는 관(직관·단관)만 담는다 — 넣으면 하이라이트에서 이중으로 잡힌다(docs/179 § B-1).
            // 합성 관(음수 Id, 바이패스 세트의 A 자리·빈 틈, docs/230 § D)은 실제 부재가 아니므로 뺀다.
            var pipeIds = members
                .Where(x => parts[x].Role is not (PipePartRole.Accessory or PipePartRole.Joint) && parts[x].ElementId > 0)
                .Select(x => parts[x].ElementId)
                .ToList();
            edges.Add(new ImportedEdge(start, end, DiameterOf(parts, members, warnings)) { ElementIds = pipeIds });

            // members는 체인 순서(엣지 시작 → 끝)다. 밸브와 이음관은 엣지에 올라탄 독립 부속으로 되돌린다(docs/175 § C-4).
            var riding = new List<int>();
            foreach (var member in members)
            {
                if (parts[member].Role is PipePartRole.Accessory or PipePartRole.Joint) riding.Add(member);
                else used.Add(member);
            }
            ridingParts.Add(riding);
        }
    }

    /// <summary>곡관·T형이 관 없이 서로 물려 있는 자리도 엣지 하나로 잇는다(docs/158 § D-3 보완).</summary>
    private static void BuildNodeToNodeEdges(
        IReadOnlyList<PickedPipePart> parts,
        IReadOnlyDictionary<(int Part, int Conn), (int Part, int Conn)> matches,
        IReadOnlyDictionary<int, Point2D> nodePositions,
        List<ImportedEdge> edges, List<List<int>> ridingParts, HashSet<int> used, ICollection<string> warnings)
    {
        var emitted = new HashSet<(int, int)>();
        foreach (var (a, b) in matches)
        {
            if (!nodePositions.TryGetValue(a.Part, out var start) || !nodePositions.TryGetValue(b.Part, out var end)) continue;
            var key = a.Part < b.Part ? (a.Part, b.Part) : (b.Part, a.Part);
            if (!emitted.Add(key)) continue;
            if (start.DistanceTo(end) <= ConnectionToleranceMm)
            {
                warnings.Add($"ElementId {parts[a.Part].ElementId}와 {parts[b.Part].ElementId}의 절점이 같은 자리라 배관을 만들지 않았습니다.");
                continue;
            }

            // 곡관·T형이 관 없이 맞물린 자리다. 이 엣지에 대응하는 관 부재가 없으므로
            // ElementIds는 기본값(빈 목록)으로 둔다 — 절점 부속은 절점 Guid로 따로 잡힌다(docs/179 § B-2).
            edges.Add(new ImportedEdge(start, end,
                parts[a.Part].Connectors[a.Conn].DiameterMm ?? parts[b.Part].Connectors[b.Conn].DiameterMm ?? PipeEdge.DefaultDiameterMm));
            ridingParts.Add([]);
            used.Add(a.Part);
            used.Add(b.Part);
        }
    }

    /// <summary>공기밸브처럼 커넥터가 1개뿐인 부속은 체인에 끼지 못한다. 가장 가까운 엣지에 붙인다(docs/158 § D-5).</summary>
    private static void AttachFloatingAccessories(
        IReadOnlyList<PickedPipePart> parts,
        IReadOnlyList<ImportedEdge> edges, List<ImportedFitting> fittings, HashSet<int> used, ICollection<string> warnings)
    {
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            if (part.Role != PipePartRole.Accessory || part.Connectors.Count >= 2 || used.Contains(i)) continue;
            if (edges.Count == 0) continue;

            var center = CenterOf(part);
            var best = -1;
            var bestDistance = double.MaxValue;
            for (var e = 0; e < edges.Count; e++)
            {
                var distance = center.DistanceTo(PointOn(edges[e], ParameterOn(edges[e], center)));
                if (distance >= bestDistance) continue;
                best = e;
                bestDistance = distance;
            }
            if (best < 0) continue;

            fittings.Add(new ImportedFitting(best, part.FamilyTypeName, ParameterOn(edges[best], center))
                { ElementId = part.ElementId });
            used.Add(i);
            warnings.Add($"1포트 부속 '{part.FamilyTypeName}'(ElementId {part.ElementId})을(를) " +
                         $"가장 가까운 배관({bestDistance:0} mm)에 올렸습니다.");
        }
    }

    // ── 값 복원 ─────────────────────────────────────────────────────────────────

    /// <summary>엣지 관경(docs/158 § D-4). 체인의 관 커넥터 직경 최빈값을 쓴다.</summary>
    private static double DiameterOf(IReadOnlyList<PickedPipePart> parts, IReadOnlyList<int> members, ICollection<string> warnings)
    {
        var segmentDiameters = members.Where(x => parts[x].Role == PipePartRole.Segment)
            .SelectMany(x => parts[x].Connectors).Select(x => x.DiameterMm).OfType<double>().ToList();
        var diameters = segmentDiameters.Count > 0
            ? segmentDiameters
            : members.SelectMany(x => parts[x].Connectors).Select(x => x.DiameterMm).OfType<double>().ToList();

        var mode = Mode(diameters);
        if (mode is null)
        {
            warnings.Add($"ElementId {parts[members[0]].ElementId} 구간의 커넥터가 원형이 아니어서 관경을 " +
                         $"기본값 {PipeEdge.DefaultDiameterMm:0}mm로 뒀습니다.");
            return PipeEdge.DefaultDiameterMm;
        }
        var distinct = diameters.Select(x => Math.Round(x, 3)).Distinct().ToList();
        if (distinct.Count > 1)
            warnings.Add($"ElementId {parts[members[0]].ElementId} 구간에 관경이 {string.Join(", ", distinct.Select(x => $"{x:0.#}"))}mm로 " +
                         $"섞여 있어 {mode.Value:0.#}mm로 그렸습니다. 캔버스 배관 하나는 관경이 하나입니다.");
        return mode.Value;
    }

    /// <summary>표고(docs/158 § D-7). 캔버스는 2D·단일 EL이므로 평균을 쓰고, 섞였으면 알린다.</summary>
    private static double AverageElevation(IReadOnlyList<PickedPipePart> parts, ICollection<string> warnings)
    {
        var elevations = parts.SelectMany(x => x.Connectors).Select(x => x.ZMm).ToList();
        if (elevations.Count == 0) return 0;
        var average = elevations.Average();
        var min = elevations.Min();
        var max = elevations.Max();
        if (max - min > ElevationToleranceMm)
            warnings.Add($"표고가 {min:0} ~ {max:0} mm로 섞여 있습니다. 평균값 {average:0} mm로 가져왔습니다.");
        // ft↔mm 왕복 잔차를 여기서 턴다. 안 털면 EL 0 배관을 되가져왔을 때 3E-14 같은 값이
        // 창의 "관 중심 높이(EL)"에 지수 표기로 뜬다(docs/176 § A).
        // 0.1mm는 기준점(ReferencePoint)이 이미 쓰는 자리수다(PipeLayoutViewModel.cs:1118-1119, 1338-1339).
        return Math.Round(average, 1);
    }

    /// <summary>절점 부속 1개를 캔버스용으로 싣는다. 밸브부관 B는 밸브 쪽(<c>Connectors[0]</c>, 긴 레그) 방향까지,
    /// A는 종류만 싣는다(docs/225 § E, docs/227 § E).</summary>
    private static ImportedNodeFitting ToImportedNodeFitting(PickedPipePart part, Point2D position) => part.Role switch
    {
        PipePartRole.ValveBend when part.Connectors.Count >= 2
            => new ImportedNodeFitting(position, part.ElementId, PipeNodeFittingKind.ValveBend, part.Connectors[0].Outward),
        PipePartRole.ValveTee => new ImportedNodeFitting(position, part.ElementId, PipeNodeFittingKind.ValveTee),
        _ => new ImportedNodeFitting(position, part.ElementId)
    };

    // ── 바이패스 세트 묶음(docs/229 § C, docs/230 § D) ────────────────────────────

    /// <summary>떼어 낸 세트 하나. <see cref="Members"/>는 원래 목록에서 뺄 인덱스(A 2 · B 2 · 바이패스 체인),
    /// <see cref="Replacements"/>는 그 자리에 넣을 합성 관(A 자리 2개 + 필요하면 A–A 빈 틈 1개, 음수 Id)이다.</summary>
    private sealed record CollapsedBypassSet(
        IReadOnlyList<int> Members, IReadOnlyList<PickedPipePart> Replacements, ImportedBypass Bypass, Point2D CenterA, Point2D CenterA2);

    /// <summary>
    /// 밸브부관 A 분기 → B → (관·밸브 1~2개) → B → A 분기로 이어진 묶음을 찾아 바이패스 세트로 떼어 낸다.
    /// 밸브가 2개면 그 사이 관 길이를 단관 길이로 읽는다(docs/255 § C).
    /// <para>A–A 사이 본관(관·밸브)은 세트가 아니다(docs/230 § 0) — 건드리지 않아 일반 관·인라인 밸브로 복원된다.
    /// 각 A는 "바깥 주관 ↔ 안쪽 주관" 합성 관으로 바꿔 본관 체인이 끊기지 않게 하고, 두 A 안쪽 주관이 모두 비어 있으면
    /// (본관 밸브 없이 모델링됨) 그 사이에도 합성 관을 넣어 본관 선 하나로 잇는다.</para>
    /// </summary>
    private static List<CollapsedBypassSet> CollapseBypassSets(
        IReadOnlyList<PickedPipePart> parts, IReadOnlyDictionary<(int Part, int Conn), (int Part, int Conn)> matches, ICollection<string> warnings)
    {
        var result = new List<CollapsedBypassSet>();
        var claimed = new HashSet<int>();
        var syntheticId = 0L;
        for (var a = 0; a < parts.Count; a++)
        {
            if (claimed.Contains(a) || parts[a].Role != PipePartRole.ValveTee || parts[a].Connectors.Count != 3) continue;
            var branchA = BranchIndex(parts[a].Connectors);
            if (!matches.TryGetValue((a, branchA), out var bHit) || parts[bHit.Part].Role != PipePartRole.ValveBend
                || parts[bHit.Part].Connectors.Count != 2) continue;

            // 바이패스 쪽 — B의 다른 커넥터에서 체인을 따라가 다른 B에 닿아야 한다. 그 체인의 밸브는 1개 또는 2개(바이패스 밸브, docs/255).
            var b = bHit.Part;
            var (bypassChain, bypassEnd) = WalkChain(parts, matches, (b, 1 - bHit.Conn));
            if (bypassEnd is not { } b2Hit || parts[b2Hit.Part].Role != PipePartRole.ValveBend || b2Hit.Part == b
                || parts[b2Hit.Part].Connectors.Count != 2) continue;
            var bypassValves = bypassChain.Where(x => parts[x].Role == PipePartRole.Accessory).ToList();
            if (bypassValves.Count is not (1 or 2)) continue;
            // 밸브 2개 세트(docs/255 § C-2) — 두 밸브 사이는 관만 있어야 한다. 다른 부속이 끼면 세트가 아니다(낱개 경고로 간다).
            var spoolLengthMm = 0.0;
            if (bypassValves.Count == 2)
            {
                var between = bypassChain.Skip(bypassChain.IndexOf(bypassValves[0]) + 1)
                    .Take(bypassChain.IndexOf(bypassValves[1]) - bypassChain.IndexOf(bypassValves[0]) - 1).ToList();
                if (between.Any(x => parts[x].Role != PipePartRole.Segment)) continue;
                spoolLengthMm = Math.Round(between.Sum(x => parts[x].Connectors[0].Point.DistanceTo(parts[x].Connectors[1].Point)), 1);
            }
            var b2 = b2Hit.Part;
            if (!matches.TryGetValue((b2, 1 - b2Hit.Conn), out var a2Hit) || parts[a2Hit.Part].Role != PipePartRole.ValveTee
                || a2Hit.Part == a || claimed.Contains(a2Hit.Part) || parts[a2Hit.Part].Connectors.Count != 3
                || BranchIndex(parts[a2Hit.Part].Connectors) != a2Hit.Conn) continue;
            var a2 = a2Hit.Part;

            var centerA = TeeNode(parts[a], warnings);
            var centerA2 = TeeNode(parts[a2], warnings);
            var (innerA, outerA) = InnerOuterMains(parts[a], branchA, centerA2);
            var (innerA2, outerA2) = InnerOuterMains(parts[a2], a2Hit.Conn, centerA);

            var members = new List<int> { a, a2, b, b2 };
            members.AddRange(bypassChain);
            foreach (var member in members) claimed.Add(member);

            var replacements = new List<PickedPipePart>
            {
                TeeAsPipe(parts[a], outerA, innerA, --syntheticId),
                TeeAsPipe(parts[a2], outerA2, innerA2, --syntheticId)
            };
            // 본관 밸브 없이 모델링돼 두 A 안쪽이 모두 비어 있으면 그 사이를 관으로 잇는다 — 안 그러면 본관 선이 둘로 갈린다.
            if (!matches.ContainsKey((a, innerA)) && !matches.ContainsKey((a2, innerA2)))
            {
                var from = parts[a].Connectors[innerA];
                var to = parts[a2].Connectors[innerA2];
                replacements.Add(new PickedPipePart(--syntheticId, PipePartRole.Segment, string.Empty,
                    Midpoint(from.Point, to.Point), parts[a].OriginZMm,
                    [
                        new PickedConnector(from.Point, from.ZMm, Negate(from.Outward), from.DiameterMm, []),
                        new PickedConnector(to.Point, to.ZMm, Negate(to.Outward), to.DiameterMm, [])
                    ], null, parts[a].Zone, parts[a].Material));
            }

            var bypassDiameter = parts[b].Connectors.Select(x => x.DiameterMm).OfType<double>().FirstOrDefault(x => x > 0);
            if (bypassDiameter <= 0) bypassDiameter = parts[a].Connectors[branchA].DiameterMm ?? 0;
            if (bypassDiameter <= 0)
            {
                bypassDiameter = PipeEdge.DefaultDiameterMm;
                warnings.Add($"바이패스 세트(ElementId {parts[a].ElementId})의 바이패스 관경을 읽지 못해 {bypassDiameter:0}mm로 뒀습니다.");
            }
            var normal = Normalize(new Point2D(parts[a].Connectors[branchA].Point.X - centerA.X, parts[a].Connectors[branchA].Point.Y - centerA.Y))
                         ?? parts[a].Connectors[branchA].Outward;
            var bypass = new ImportedBypass(parts[bypassValves[0]].FamilyTypeName, bypassDiameter, normal,
                members.Select(x => parts[x].ElementId).Distinct().ToList())
            {
                SecondValveFamilyTypeName = bypassValves.Count == 2 ? parts[bypassValves[1]].FamilyTypeName : null,
                SpoolLengthMm = spoolLengthMm,
                ValvePositions = bypassValves.Count == 2 ? bypassValves.Select(x => parts[x].Origin).ToList() : []
            };
            result.Add(new CollapsedBypassSet(members, replacements, bypass, centerA, centerA2));
        }

        // 분기에 밸브부관 B가 물린 A인데 세트로 묶이지 못한 것 — 대개 세트 부재 일부를 선택하지 않은 경우다(2026-09-29).
        // 낱개 절점 부속으로는 복원되지만 재생성하면 세트가 아니게 되므로 알린다. 곡관·T 자리에 따로 쓴 A·B는 분기에 B가 없어 해당하지 않는다.
        var loose = Enumerable.Range(0, parts.Count)
            .Where(i => !claimed.Contains(i) && parts[i].Role == PipePartRole.ValveTee && parts[i].Connectors.Count == 3
                        && matches.TryGetValue((i, BranchIndex(parts[i].Connectors)), out var hit) && parts[hit.Part].Role == PipePartRole.ValveBend)
            .Select(i => parts[i].ElementId)
            .ToList();
        if (loose.Count > 0)
            warnings.Add($"밸브부관 A {loose.Count}개(ElementId {string.Join(", ", loose)})를 바이패스 세트로 묶지 못해 낱개 부속으로 가져왔습니다. " +
                         "세트 부재(밸브부관 A 2 · B 2 · 바이패스 밸브 · 채움관)를 모두 선택했는지 확인하세요.");
        return result;
    }

    /// <summary>밸브부관 A를 본관 체인 안의 2포트 합성 관으로 바꾼다. 패밀리명이 비어 대표 관 패밀리 투표에 들지 않는다.</summary>
    private static PickedPipePart TeeAsPipe(PickedPipePart tee, int outer, int inner, long syntheticId)
        => new(syntheticId, PipePartRole.Segment, string.Empty, tee.Origin, tee.OriginZMm,
            [tee.Connectors[outer], tee.Connectors[inner]], null, tee.Zone, tee.Material);

    private static Point2D Negate(Point2D vector) => new(-vector.X, -vector.Y);

    /// <summary>점이 엣지 선분 위(허용오차 안)에 있는지.</summary>
    private static bool IsOnEdge(ImportedEdge edge, Point2D point)
    {
        var dx = edge.End.X - edge.Start.X; var dy = edge.End.Y - edge.Start.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < GeometryTolerance) return edge.Start.DistanceTo(point) <= ConnectionToleranceMm;
        var t = ((point.X - edge.Start.X) * dx + (point.Y - edge.Start.Y) * dy) / lengthSquared;
        if (t < -GeometryTolerance || t > 1 + GeometryTolerance) return false;
        return new Point2D(edge.Start.X + dx * t, edge.Start.Y + dy * t).DistanceTo(point) <= ConnectionToleranceMm;
    }

    /// <summary>(부재, 커넥터)에서 출발해 관·이음관·2포트 밸브 체인을 따라간다. 체인 부재 목록과, 체인이 아닌 부재에 닿은 접점(없으면 null)을 돌려준다.</summary>
    private static (List<int> Chain, (int Part, int Conn)? End) WalkChain(
        IReadOnlyList<PickedPipePart> parts, IReadOnlyDictionary<(int Part, int Conn), (int Part, int Conn)> matches, (int Part, int Conn) start)
    {
        var chain = new List<int>();
        var current = start;
        while (matches.TryGetValue(current, out var next))
        {
            var part = parts[next.Part];
            if (!IsChainPart(part) || part.Connectors.Count != 2 || chain.Contains(next.Part)) return (chain, next);
            chain.Add(next.Part);
            current = (next.Part, 1 - next.Conn);
        }
        return (chain, null);
    }

    /// <summary>A의 두 주관 커넥터 중 <paramref name="otherCenter"/>(상대 A)에 가까운 쪽이 안쪽이다.</summary>
    private static (int Inner, int Outer) InnerOuterMains(PickedPipePart tee, int branch, Point2D otherCenter)
    {
        var mains = Enumerable.Range(0, tee.Connectors.Count).Where(x => x != branch).Take(2).ToList();
        return tee.Connectors[mains[0]].Point.DistanceTo(otherCenter) <= tee.Connectors[mains[1]].Point.DistanceTo(otherCenter)
            ? (mains[0], mains[1])
            : (mains[1], mains[0]);
    }

    /// <summary>역할별 대표 패밀리(docs/158 § D-6). 캔버스 설정은 역할당 하나뿐이므로 최빈값을 쓴다.</summary>
    private static string? DominantFamilyTypeName(
        IReadOnlyList<PickedPipePart> parts, PipePartRole role, string label, ICollection<string> warnings)
    {
        var names = parts.Where(x => x.Role == role && !string.IsNullOrWhiteSpace(x.FamilyTypeName))
            .Select(x => x.FamilyTypeName).ToList();
        if (names.Count == 0) return null;

        var groups = names.GroupBy(x => x, StringComparer.Ordinal)
            .OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
        if (groups.Count > 1)
        {
            // docs/221 — 무엇이 몇 개 섞였는지 한 줄로 보인다. TaskDialog가 경고를 줄 단위 20줄까지만 보여 주므로 줄바꿈을 넣지 않는다.
            var detail = string.Join(", ", groups.Select(x => $"'{x.Key}' {x.Count()}개"));
            var tie = groups[0].Count() == groups[1].Count();
            warnings.Add($"{label} 패밀리가 {groups.Count}종 섞여 있습니다 — {detail}. " +
                         (tie ? $"개수가 같아 이름순으로 '{groups[0].Key}'로 설정했습니다."
                              : $"가장 많은 '{groups[0].Key}'로 설정했습니다."));
        }
        return groups[0].Key;
    }

    /// <summary>가져온 부재의 밸브실 유형(docs/161 § G-3). 규칙은 <see cref="ValveRoomOutline.Zone"/>과 같다 —
    /// 값이 있는 것끼리 한 종류일 때만 그 값이고, 둘 이상이면 빈 문자열 + 경고다. 값을 지어내지 않는다.</summary>
    private static string SingleZone(IReadOnlyList<PickedPipePart> parts, ICollection<string> warnings)
    {
        var zones = parts.Select(x => x.Zone).Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal).ToList();
        if (zones.Count == 1) return zones[0];
        if (zones.Count > 1)
            warnings.Add($"밸브실 유형이 {zones.Count}종({string.Join(", ", zones)}) 섞여 있어 창에 채우지 않았습니다.");
        return string.Empty;
    }

    /// <summary>가져온 부재의 관종(docs/170 § G). 규칙은 <see cref="SingleZone"/>과 같다 —
    /// 값이 있는 것끼리 한 종류일 때만 그 값이고, 둘 이상이면 빈 문자열 + 경고다. 값을 지어내지 않는다.</summary>
    private static string SingleMaterial(IReadOnlyList<PickedPipePart> parts, ICollection<string> warnings)
    {
        var materials = parts.Select(x => x.Material).Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal).ToList();
        if (materials.Count == 1) return materials[0];
        if (materials.Count > 1)
            warnings.Add($"관종이 {materials.Count}종({string.Join(", ", materials)}) 섞여 있어 창에 채우지 않았습니다.");
        return string.Empty;
    }

    private static double? Mode(IEnumerable<double> values)
    {
        var list = values.Where(x => double.IsFinite(x) && x > 0).ToList();
        if (list.Count == 0) return null;
        return list.GroupBy(x => Math.Round(x, 3))
            .OrderByDescending(x => x.Count()).ThenBy(x => x.Key).First().Key;
    }

    // ── 기하 헬퍼 ────────────────────────────────────────────────────────────────

    /// <summary>이음관도 체인의 일부다 — 독립 부속이지만 관을 통과하는 2포트라 체인을 끊지 않는다(docs/175 § C-4).</summary>
    private static bool IsChainPart(PickedPipePart part)
        => part.Role is PipePartRole.Segment or PipePartRole.Joint
           || (part.Role == PipePartRole.Accessory && part.Connectors.Count >= 2);

    private static Point2D CenterOf(PickedPipePart part)
        => part.Connectors.Count >= 2
            ? Midpoint(part.Connectors[0].Point, part.Connectors[1].Point)
            : part.Connectors.Count == 1 ? part.Connectors[0].Point : part.Origin;

    private static Point2D? DirectionOf(IReadOnlyList<PickedPipePart> parts, ChainLink link)
    {
        var head = parts[link.Part].Connectors[link.HeadConn].Point;
        var tail = parts[link.Part].Connectors[link.TailConn].Point;
        return Normalize(new Point2D(tail.X - head.X, tail.Y - head.Y));
    }

    private static Point2D JointPoint(IReadOnlyList<PickedPipePart> parts, ChainLink a, ChainLink b)
        => Midpoint(parts[a.Part].Connectors[a.TailConn].Point, parts[b.Part].Connectors[b.HeadConn].Point);

    /// <summary>커넥터가 부재 안쪽(절점 쪽)을 향하는 방향. 바깥 방향의 반대다.</summary>
    private static Point2D? Inward(PickedConnector connector) => Normalize(new Point2D(-connector.Outward.X, -connector.Outward.Y));

    private static Point2D? Normalize(Point2D vector)
    {
        var length = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
        return length <= GeometryTolerance ? null : new Point2D(vector.X / length, vector.Y / length);
    }

    private static double Dot(Point2D a, Point2D b) => a.X * b.X + a.Y * b.Y;

    private static Point2D Midpoint(Point2D a, Point2D b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private static double AngleBetweenDeg(Point2D a, Point2D b)
        => Math.Acos(Math.Clamp(Dot(a, b), -1, 1)) * 180 / Math.PI;

    /// <summary>두 직선(점 + 단위방향)의 교점. 평행하거나 방향을 못 구하면 null.</summary>
    private static Point2D? LineIntersection(Point2D p, Point2D? dp, Point2D q, Point2D? dq)
    {
        if (dp is null || dq is null) return null;
        var cross = dp.X * dq.Y - dp.Y * dq.X;
        if (Math.Abs(cross) <= 1e-6) return null;   // 단위벡터끼리의 외적이므로 sin(각도)다.
        var t = ((q.X - p.X) * dq.Y - (q.Y - p.Y) * dq.X) / cross;
        return new Point2D(p.X + dp.X * t, p.Y + dp.Y * t);
    }

    private static Point2D ProjectOnLine(Point2D point, Point2D origin, Point2D direction)
    {
        var t = (point.X - origin.X) * direction.X + (point.Y - origin.Y) * direction.Y;
        return new Point2D(origin.X + direction.X * t, origin.Y + direction.Y * t);
    }

    private static double ParameterOn(ImportedEdge edge, Point2D point)
        => Math.Clamp(Segment2D.ParameterOnSegment(point, edge.Start, edge.End), 0, 1);

    private static Point2D PointOn(ImportedEdge edge, double t)
        => new(edge.Start.X + (edge.End.X - edge.Start.X) * t, edge.Start.Y + (edge.End.Y - edge.Start.Y) * t);
}
