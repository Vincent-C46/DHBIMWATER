namespace DHBIMWATER.Core.Piping;

/// <summary>연결 패스 입력 — 부재 한 개(docs/266 § 1-1).</summary>
/// <param name="Id">ElementId 값. 귀속 순서의 마지막 비교 키다.</param>
/// <param name="IsValve">배관 밸브류(<c>OST_PipeAccessory</c>). 밸브와 닿은 플랜지 연결은 0이다(R-5).</param>
/// <param name="PartName">`DH_Part` 또는 부재명. 관(직관·단관) 여부와 자체 절단 수를 여기서 정한다(<see cref="PipeCountRules"/>).</param>
/// <param name="Composition">`DH_접합구성`(`1S1F`). 소켓·플랜지 단부 수이고 나머지 단부는 평단(스피곳)이다.</param>
/// <param name="DiameterMm">부재 관경(주관). 귀속 동률 비교와 슬롯 규격 판정에 쓴다.</param>
/// <param name="FlangedWhenNoComposition">구성이 비었을 때 모든 단부를 플랜지로 볼지(밸브부관처럼 구성을 적지 않는 플랜지 이형관, R-7).</param>
public sealed record PipeJointMemberInput(long Id, bool IsValve, string? PartName, string? Composition, double DiameterMm,
    bool FlangedWhenNoComposition = false);

/// <summary>연결 패스 입력 — 단부 한 개.</summary>
/// <param name="Index">부재 안 단부 순번. 짝 지정(<paramref name="PartnerIndex"/>)이 이 값을 가리킨다.</param>
/// <param name="PartnerMemberId">모델에서 실제로 연결된 상대 부재. 없으면 위치 일치로 짝을 찾는다.</param>
public sealed record PipeJointEndInput(long MemberId, int Index, double XMm, double YMm, double ZMm, double DiameterMm,
    long? PartnerMemberId = null, int? PartnerIndex = null);

/// <summary>연결 패스 결과.</summary>
/// <param name="FlangeSlots">부재별 플랜지 슬롯. 규격이 빈 문자열이면 요소 관경이다. 플랜지가 없는 부재(밸브 제외)도 빈 목록으로 들어 있다 — 옛 플랜지 슬롯을 지우는 데 쓴다.</param>
/// <param name="CutCounts">밸브실 문서에서 부재별 절단 수(자체 + 유입부). 밸브실 문서가 아니면 비어 있다 — 절단은 배치가 정한다.</param>
public sealed record PipeJointPassResult(
    IReadOnlyDictionary<long, IReadOnlyList<PipeJointSlotValue>> FlangeSlots,
    IReadOnlyDictionary<long, int> CutCounts,
    int UnpairedFlangeCount, int UnresolvedEndCount, int AmbiguousEndCount,
    IReadOnlyList<string> Warnings);

/// <summary>
/// 문서 전체 부재의 단부 연결을 보고 연결에 의존하는 개수를 정한다(docs/266 § 1). Revit 타입을 쓰지 않는다.
/// <para><b>플랜지</b>: 플랜지 ↔ 플랜지 연결 1건 = 1개소를 한쪽 부재에 귀속(R-5). 밸브와 닿으면 0.
/// 짝 없는 플랜지는 밸브실 문서에서 1(경고), 그 밖 문서에서 0(경고).</para>
/// <para><b>유입부 절단</b>: 밸브실 문서에서 짝 없는 소켓 단부 1곳 = 그 부재 절단 +1(R-6). 짝 없는 평단은 0(유출부).</para>
/// <para>부재 하나만 보고는 정할 수 없는 값이라 부재별 기록(<c>PipeJointCountWriter.Write</c>)과 분리했다. 재기록할 때마다
/// 같은 결과가 나오도록 귀속 순서를 결정적으로 둔다 — 관 > 이형관, 관경 큰 쪽, ElementId 작은 쪽.</para>
/// </summary>
public static class PipeJointConnectionPass
{
    /// <summary>단부 형식. <see cref="Unknown"/>은 섞인 부재라 단부별로 가르지 못한 경우다(docs/266 § 1-2).</summary>
    private enum PipeJointEndKind { Unknown, Flange, Socket, Plain }

    /// <summary>위치 일치 허용오차(mm). 배치 경로의 커넥터 연결 허용오차와 같다(RevitPipeSegmentCommandRepo.ConnectionToleranceMm).</summary>
    public const double DefaultToleranceMm = 1.0;

    /// <summary>단부 관경이 부재 관경과 이만큼 이상 다르면 슬롯 규격을 따로 적는다(T형관 분기 `D600`).</summary>
    private const double SpecDiameterToleranceMm = 0.5;

    public static PipeJointPassResult Run(IReadOnlyList<PipeJointMemberInput> members, IReadOnlyList<PipeJointEndInput> ends,
        bool isValveRoom, double toleranceMm = DefaultToleranceMm)
    {
        var memberById = members.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
        var nodes = ends.Where(x => memberById.ContainsKey(x.MemberId))
            .Select(x => new Node(x)).ToList();
        var nodeByKey = nodes.GroupBy(x => (x.End.MemberId, x.End.Index)).ToDictionary(g => g.Key, g => g.First());

        var ambiguous = Pair(nodes, nodeByKey, toleranceMm);
        AssignKinds(nodes, memberById);

        var flange = members.Where(x => !x.IsValve).GroupBy(x => x.Id).ToDictionary(g => g.Key, _ => new List<(string Spec, int Count)>());
        var inlet = new Dictionary<long, int>();
        var unpairedFlange = 0;
        var unresolved = 0;

        foreach (var node in nodes.OrderBy(x => x.End.MemberId).ThenBy(x => x.End.Index))
        {
            var member = memberById[node.End.MemberId];
            if (node.Partner is { } partner)
            {
                // 한 연결은 두 단부에서 한 번씩 보이므로 키가 작은 쪽에서만 센다.
                if (Compare(node, partner) > 0) continue;
                if (node.Kind is PipeJointEndKind.Unknown || partner.Kind is PipeJointEndKind.Unknown)
                {
                    if (node.Kind == PipeJointEndKind.Flange || partner.Kind == PipeJointEndKind.Flange || IsMixed(member) || IsMixed(memberById[partner.End.MemberId]))
                        unresolved++;
                    continue;
                }
                if (node.Kind != PipeJointEndKind.Flange || partner.Kind != PipeJointEndKind.Flange) continue;
                var other = memberById[partner.End.MemberId];
                if (member.IsValve || other.IsValve) continue;   // 밸브와 닿은 연결은 밸브 접합 및 부설 품에 들어 있다(R-5)
                var owner = Owner(member, other);
                var ownerEnd = owner.Id == member.Id ? node.End : partner.End;
                flange[owner.Id].Add((SpecFor(owner, ownerEnd), 1));
                continue;
            }

            if (node.IsAmbiguous || member.IsValve) continue;
            switch (node.Kind)
            {
                case PipeJointEndKind.Flange:
                    unpairedFlange++;
                    if (isValveRoom) flange[member.Id].Add((SpecFor(member, node.End), 1));
                    break;
                case PipeJointEndKind.Socket when isValveRoom:
                    inlet[member.Id] = inlet.GetValueOrDefault(member.Id) + 1;
                    break;
                case PipeJointEndKind.Unknown:
                    unresolved++;
                    break;
            }
        }

        var cuts = new Dictionary<long, int>();
        if (isValveRoom)
            foreach (var member in members.Where(x => !x.IsValve))
                cuts[member.Id] = PipeCountRules.BaseCutCount(member.PartName, member.Composition) + inlet.GetValueOrDefault(member.Id);

        var warnings = new List<string>();
        if (unpairedFlange > 0)
            warnings.Add(isValveRoom
                ? $"짝 없는 플랜지 {unpairedFlange}곳을 1개소씩 기록했습니다 — 구조물·막힌 끝이면 수량을 확인하세요."
                : $"짝 없는 플랜지 {unpairedFlange}곳은 접합으로 세지 않았습니다 — 구조물·막힌 끝이면 수량을 확인하세요.");
        if (unresolved > 0)
            warnings.Add($"소켓·플랜지가 섞인 부재끼리 맞닿아 단부 형식을 정하지 못한 곳 {unresolved}곳은 플랜지 접합을 0으로 두었습니다. DH_접합구성을 확인하세요.");
        if (ambiguous > 0)
            warnings.Add($"단부 세 개 이상이 한 점에 겹친 곳 {ambiguous}곳은 짝을 정하지 못해 플랜지·유입부 수량에서 뺐습니다.");

        return new PipeJointPassResult(
            flange.ToDictionary(x => x.Key, x => (IReadOnlyList<PipeJointSlotValue>)Merge(x.Value)),
            cuts, unpairedFlange, unresolved, ambiguous, warnings);
    }

    /// <summary>모델 연결을 먼저 짝으로 쓰고, 남은 단부는 위치 일치로 짝을 찾는다. 한 점에 셋 이상 겹치면 짝을 정하지 않는다.</summary>
    /// <returns>짝을 정하지 못한 겹친 단부 수.</returns>
    private static int Pair(List<Node> nodes, Dictionary<(long, int), Node> byKey, double toleranceMm)
    {
        foreach (var node in nodes)
        {
            if (node.Partner is not null || node.End.PartnerMemberId is not { } partnerId || node.End.PartnerIndex is not { } partnerIndex) continue;
            if (partnerId == node.End.MemberId || !byKey.TryGetValue((partnerId, partnerIndex), out var partner) || partner.Partner is not null) continue;
            node.Partner = partner;
            partner.Partner = node;
        }

        var open = nodes.Where(x => x.Partner is null).ToList();
        // 허용오차 크기 격자 칸으로 나눠 이웃 27칸만 본다 — 계획관로 문서는 단부가 수천 개라 전수 비교(O(n²))를 피한다.
        var cellSize = Math.Max(toleranceMm, 1e-6);
        (long, long, long) CellOf(PipeJointEndInput e) => ((long)Math.Floor(e.XMm / cellSize), (long)Math.Floor(e.YMm / cellSize), (long)Math.Floor(e.ZMm / cellSize));
        var grid = open.GroupBy(x => CellOf(x.End)).ToDictionary(g => g.Key, g => g.ToList());
        var candidates = open.ToDictionary(x => x, x =>
        {
            var (cx, cy, cz) = CellOf(x.End);
            var found = new List<Node>();
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            for (var dz = -1; dz <= 1; dz++)
                if (grid.TryGetValue((cx + dx, cy + dy, cz + dz), out var cell))
                    found.AddRange(cell.Where(y => !ReferenceEquals(x, y) && y.End.MemberId != x.End.MemberId && Distance(x.End, y.End) <= toleranceMm));
            return found;
        });
        var ambiguous = 0;
        foreach (var node in open.OrderBy(x => x.End.MemberId).ThenBy(x => x.End.Index))
        {
            if (node.Partner is not null) continue;
            var list = candidates[node];
            if (list.Count == 0) continue;
            if (list.Count == 1 && candidates[list[0]].Count == 1 && list[0].Partner is null)
            {
                node.Partner = list[0];
                list[0].Partner = node;
                continue;
            }
            node.IsAmbiguous = true;
            ambiguous++;
        }
        return ambiguous;
    }

    /// <summary>단부 형식을 정한다(docs/266 § 1-2). 부재의 단부 풀(플랜지 F · 소켓 S · 나머지 평단)이 한 종류뿐이면 그대로,
    /// 섞였으면 상대 단부에서 유도한다 — 플랜지 ↔ 플랜지, 소켓 ↔ 평단. 풀에 하나만 남으면 나머지 단부를 채운다.
    /// 밸브류는 플랜지로 본다(밸브실 밸브는 플랜지 접속이다). 끝까지 못 정하면 <see cref="PipeJointEndKind.Unknown"/>.</summary>
    private static void AssignKinds(List<Node> nodes, Dictionary<long, PipeJointMemberInput> members)
    {
        var pools = new Dictionary<long, Dictionary<PipeJointEndKind, int>>();
        foreach (var group in nodes.GroupBy(x => x.End.MemberId))
        {
            var member = members[group.Key];
            var count = group.Count();
            Dictionary<PipeJointEndKind, int> pool;
            var ends = EndsOf(member);
            if (member.IsValve)
                pool = new() { [PipeJointEndKind.Flange] = count };
            else
            {
                var flange = Math.Clamp(ends.Flange, 0, count);
                var socket = Math.Clamp(ends.Socket, 0, count - flange);
                pool = new()
                {
                    [PipeJointEndKind.Flange] = flange,
                    [PipeJointEndKind.Socket] = socket,
                    [PipeJointEndKind.Plain] = count - flange - socket,
                };
            }
            pools[group.Key] = pool;
        }

        bool changed;
        do
        {
            changed = false;
            foreach (var group in nodes.Where(x => x.Kind == PipeJointEndKind.Unknown).GroupBy(x => x.End.MemberId).ToList())
            {
                var pool = pools[group.Key];
                var kinds = pool.Where(x => x.Value > 0).Select(x => x.Key).ToList();
                if (kinds.Count == 1)
                {
                    foreach (var node in group) Take(node, kinds[0], pool);
                    changed = true;
                    continue;
                }
                foreach (var node in group)
                {
                    if (node.Partner is not { Kind: not PipeJointEndKind.Unknown } partner) continue;
                    var wanted = Complement(partner.Kind);
                    if (pool.GetValueOrDefault(wanted) <= 0) continue;
                    Take(node, wanted, pool);
                    changed = true;
                }
            }
        } while (changed);

        // 남은 단부가 전부 짝 없는 단부면 어느 단부가 어느 형식인지는 결과에 영향이 없다(짝 없는 플랜지·소켓 수만 센다).
        // 풀에 남은 대로 채운다 — 양 끝이 다 열린 `1F 단관`이 이 경우다.
        foreach (var group in nodes.Where(x => x.Kind == PipeJointEndKind.Unknown).GroupBy(x => x.End.MemberId).ToList())
        {
            if (group.Any(x => x.Partner is not null || x.IsAmbiguous)) continue;
            var pool = pools[group.Key];
            foreach (var node in group.OrderBy(x => x.End.Index))
            {
                var kind = pool.Where(x => x.Value > 0).Select(x => x.Key).OrderBy(x => x).FirstOrDefault();
                if (kind == PipeJointEndKind.Unknown) break;
                Take(node, kind, pool);
            }
        }

        static void Take(Node node, PipeJointEndKind kind, Dictionary<PipeJointEndKind, int> pool)
        {
            if (node.Kind != PipeJointEndKind.Unknown || pool.GetValueOrDefault(kind) <= 0) return;
            node.Kind = kind;
            pool[kind]--;
        }
    }

    private static PipeJointEndKind Complement(PipeJointEndKind kind) => kind switch
    {
        PipeJointEndKind.Flange => PipeJointEndKind.Flange,
        PipeJointEndKind.Socket => PipeJointEndKind.Plain,
        PipeJointEndKind.Plain => PipeJointEndKind.Socket,
        _ => PipeJointEndKind.Unknown,
    };

    private static bool IsMixed(PipeJointMemberInput member)
        => !member.IsValve && EndsOf(member) is { Flange: > 0, Socket: > 0 };

    /// <summary>구성 문자열을 단부 수로 푼다. 비어 있으면 플랜지 이형관만 전부 플랜지(큰 수로 두면 실제 단부 수로 잘린다), 그 밖은 전부 평단.</summary>
    private static PipeJointEnds EndsOf(PipeJointMemberInput member)
    {
        var ends = PipeJointComposition.Decode(member.Composition);
        return ends.IsEmpty && member.FlangedWhenNoComposition ? new PipeJointEnds(0, int.MaxValue / 2) : ends;
    }

    /// <summary>귀속 순서: 관 > 이형관, 관경 큰 쪽, ElementId 작은 쪽(docs/266 § 1-3).</summary>
    private static PipeJointMemberInput Owner(PipeJointMemberInput a, PipeJointMemberInput b)
    {
        var aPipe = PipeCountRules.IsPipe(a.PartName);
        var bPipe = PipeCountRules.IsPipe(b.PartName);
        if (aPipe != bPipe) return aPipe ? a : b;
        if (Math.Abs(a.DiameterMm - b.DiameterMm) > SpecDiameterToleranceMm) return a.DiameterMm > b.DiameterMm ? a : b;
        return a.Id <= b.Id ? a : b;
    }

    /// <summary>접합 지점 관경이 귀속 부재 관경과 다르면 `D{관경}`, 같으면 빈 문자열(요소 관경을 쓴다, docs/186 § 4-3).</summary>
    private static string SpecFor(PipeJointMemberInput owner, PipeJointEndInput end)
        => Math.Abs(end.DiameterMm - owner.DiameterMm) > SpecDiameterToleranceMm ? ValvePipingParameters.DiameterText(end.DiameterMm) : string.Empty;

    private static List<PipeJointSlotValue> Merge(List<(string Spec, int Count)> slots)
        => slots.GroupBy(x => x.Spec, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new PipeJointSlotValue(PipeCountRules.FlangeJointKind, g.Key, g.Sum(x => x.Count)))
            .ToList();

    private static int Compare(Node a, Node b)
        => a.End.MemberId != b.End.MemberId ? a.End.MemberId.CompareTo(b.End.MemberId) : a.End.Index.CompareTo(b.End.Index);

    private static double Distance(PipeJointEndInput a, PipeJointEndInput b)
    {
        var dx = a.XMm - b.XMm; var dy = a.YMm - b.YMm; var dz = a.ZMm - b.ZMm;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private sealed class Node(PipeJointEndInput end)
    {
        public PipeJointEndInput End { get; } = end;
        public Node? Partner { get; set; }
        public PipeJointEndKind Kind { get; set; } = PipeJointEndKind.Unknown;
        public bool IsAmbiguous { get; set; }
    }
}
