namespace DHBIMWATER.Core.Piping;

// 세트별 펼침. StepMm 부호 = 방향(음수 = 반대), 0 = 펼치지 않음. 단위 실제 mm(심볼오프셋 값, 축척 무관)
public sealed record JunctionSetOffset(long SetId, JunctionExpandAxis Axis, double StepMm);

public static class JunctionSetOffsets
{
    public static JunctionSetOffset For(IReadOnlyList<JunctionSetOffset>? sets, long setId,
        JunctionExpandAxis defaultAxis, double defaultStepMm)
        => sets?.FirstOrDefault(x => x.SetId == setId) ?? new JunctionSetOffset(setId, defaultAxis, defaultStepMm);
}

public sealed record JunctionPreviewLink(long A, long B);

// 번호 태그 방향(docs/296). 자동 = 4방향 중 겹침 적은 쪽
public enum JunctionTagSide { Auto, Right, Top, Left, Bottom }

// 번호 태그 머리·지시선 끝. 도면 mm, 뷰 오른쪽·위쪽 축 (docs/296)
public sealed record JunctionTagPlacement(long ElementId, double HeadX, double HeadY, double LeaderX, double LeaderY);

// 좌표 단위 = 도면 mm, X = 뷰 오른쪽, Y = 뷰 위쪽. Leader = 지시선 끝(부재 중심선 위 = 부재 위치)
public sealed record JunctionPreviewPlacement(JunctionPreviewItem Item, double X, double Y,
    double MinX, double MinY, double MaxX, double MaxY, double TagX, double TagY,
    double LeaderX = 0, double LeaderY = 0);
// SetId 0 = 주관(수평 부재 사이). 세트 중심선은 세트 Id
public sealed record JunctionPreviewSegment(double X1, double Y1, double X2, double Y2, long SetId = 0);
public sealed record JunctionPreviewDimension(JunctionPreviewPlacement First, JunctionPreviewPlacement Second,
    JunctionExpandAxis Axis, double StepMm);
public sealed record JunctionPreviewScene(IReadOnlyList<JunctionPreviewPlacement> Placements,
    IReadOnlyList<JunctionPreviewSegment> CenterLines, IReadOnlyList<JunctionPreviewDimension> Dimensions,
    IReadOnlyList<string> Warnings);

public static class JunctionPreviewLayout
{
    // 종전 태그 규칙(부재 위치 − 4.5mm). 이전 버전이 만든 태그 소유 판정에만 씀(docs/296 § C)
    public const double TagGapPaperMm = 4.5;
    // 기호 범위 바깥 끝 ~ 번호 원 가장자리 간격 = 지시선 보이는 길이(docs/296)
    public const double TagClearancePaperMm = 3;
    // 자동 배치 충돌 판정 여유
    private const double TagMarginPaperMm = 0.3;
    // 기호선 없는 부재의 대체 기호 반폭
    public const double FallbackHalfPaperMm = 2.2;
    // 번호 원 반지름(미리보기 20px ÷ 8px/mm). 원끼리 겹침 판정에 씀
    public const double TagRadiusPaperMm = 1.25;
    private const double OverlapTolerance = 0.1;

    public static JunctionPreviewScene Build(IReadOnlyList<JunctionPreviewItem> items,
        IReadOnlyList<JunctionPreviewLink> links, IReadOnlyList<JunctionSetOffset> sets, int viewScale,
        bool expand = true, JunctionTagSide tagSide = JunctionTagSide.Auto)
    {
        var setById = sets.ToDictionary(x => x.SetId);
        var placements = PlaceTags(items.Select(item => Place(item, setById, viewScale, expand)).ToList(), tagSide);
        var byId = placements.GroupBy(x => x.Item.ElementId).ToDictionary(x => x.Key, x => x.First());

        // 세트 뿌리 = 순서 1 부재와 이어진 수평 부재
        var roots = new Dictionary<long, JunctionPreviewPlacement>();
        foreach (var first in placements.Where(x => x.Item.SetId != 0 && x.Item.Order == 1))
        {
            var root = links.Where(x => x.A == first.Item.ElementId || x.B == first.Item.ElementId)
                .Select(x => x.A == first.Item.ElementId ? x.B : x.A)
                .Select(id => byId.GetValueOrDefault(id)).FirstOrDefault(x => x is { Item.IsVertical: false });
            if (root is not null) roots[first.Item.SetId] = root;
        }

        var centerLines = new List<JunctionPreviewSegment>();
        foreach (var link in links.Select(x => x.A < x.B ? x : new JunctionPreviewLink(x.B, x.A)).Distinct())
        {
            if (byId.GetValueOrDefault(link.A) is { Item.IsVertical: false } a &&
                byId.GetValueOrDefault(link.B) is { Item.IsVertical: false } b)
                centerLines.Add(new JunctionPreviewSegment(a.X, a.Y, b.X, b.Y));
        }

        var dimensions = new List<JunctionPreviewDimension>();
        foreach (var set in sets)
        {
            var members = placements.Where(x => x.Item.SetId == set.SetId && x.Item.Order is not null && x.Item.Connected)
                .OrderBy(x => x.Item.Order).ToList();
            if (members.Count == 0 || !expand || set.StepMm == 0) continue;
            if (roots.TryGetValue(set.SetId, out var root))
                centerLines.Add(new JunctionPreviewSegment(root.X, root.Y, members[^1].X, members[^1].Y, set.SetId));
            if (members.Count >= 2)
                dimensions.Add(new JunctionPreviewDimension(members[0], members[1], set.Axis, set.StepMm));
        }

        return new JunctionPreviewScene(placements, centerLines, dimensions,
            Overlaps(placements, sets, roots));
    }

    private static JunctionPreviewPlacement Place(JunctionPreviewItem item,
        IReadOnlyDictionary<long, JunctionSetOffset> sets, int viewScale, bool expand)
    {
        var x = item.Position.X / viewScale;
        var y = item.Position.Y / viewScale;
        var axis = sets.TryGetValue(item.SetId, out var set) ? set.Axis : JunctionExpandAxis.Y;
        if (expand && set is not null && item.Connected && item.Order is { } order)
        {
            var distance = order * set.StepMm / viewScale; // 실제 mm → 도면 mm
            if (set.Axis == JunctionExpandAxis.X) x += distance;
            else y += distance;
        }

        double minX, minY, maxX, maxY;
        if (item.Shape is { IsRenderable: true } shape)
        {
            minX = x + shape.MinX / viewScale; maxX = x + shape.MaxX / viewScale;
            minY = y + shape.MinY / viewScale; maxY = y + shape.MaxY / viewScale;
        }
        else
        {
            minX = x - FallbackHalfPaperMm; maxX = x + FallbackHalfPaperMm;
            minY = y - FallbackHalfPaperMm; maxY = y + FallbackHalfPaperMm;
        }

        // 태그는 PlaceTags가 정함. 여기선 부재 위치로 둠
        return new JunctionPreviewPlacement(item, x, y, minX, minY, maxX, maxY, x, y, x, y);
    }

    /// <summary>번호 태그 머리 위치(docs/296). 머리 = 방향 쪽 기호 범위 끝 + 간격 + 원 반지름, 부재 위치와 같은 수평/수직선
    /// → 지시선(부재 위치 → 머리)이 직선. 자동은 4방향 중 겹침 최소, 동점이면 격점 바깥쪽 → 우·상·좌·하.</summary>
    private static List<JunctionPreviewPlacement> PlaceTags(List<JunctionPreviewPlacement> placements, JunctionTagSide side)
    {
        var tagged = placements.Where(x => x.Item.ItemNumber > 0).ToList();
        if (tagged.Count == 0) return placements;
        var centerX = placements.Average(x => x.X);
        var centerY = placements.Average(x => x.Y);
        var heads = new List<(double X, double Y)>();
        var result = new List<JunctionPreviewPlacement>(placements.Count);
        foreach (var p in placements)
        {
            if (p.Item.ItemNumber <= 0) { result.Add(p); continue; }
            var candidates = side == JunctionTagSide.Auto ? AutoOrder(p, centerX, centerY) : [side];
            var head = candidates.Select(s => Head(p, s))
                .Select((h, i) => (Head: h, Cost: Cost(p, h, placements, heads), Index: i))
                .OrderBy(x => x.Cost).ThenBy(x => x.Index).First().Head;
            heads.Add(head);
            result.Add(p with { TagX = head.X, TagY = head.Y, LeaderX = p.X, LeaderY = p.Y });
        }
        return result;
    }

    private static readonly JunctionTagSide[] SideOrder =
        [JunctionTagSide.Right, JunctionTagSide.Top, JunctionTagSide.Left, JunctionTagSide.Bottom];

    // 격점 중심에서 바깥쪽 방향 먼저. OrderByDescending은 안정 정렬 → 동점은 우·상·좌·하 순
    private static IReadOnlyList<JunctionTagSide> AutoOrder(JunctionPreviewPlacement p, double centerX, double centerY)
        => SideOrder.OrderByDescending(s => Direction(s).X * (p.X - centerX) + Direction(s).Y * (p.Y - centerY)).ToList();

    private static (double X, double Y) Direction(JunctionTagSide side) => side switch
    {
        JunctionTagSide.Right => (1, 0),
        JunctionTagSide.Top => (0, 1),
        JunctionTagSide.Left => (-1, 0),
        _ => (0, -1),
    };

    private static (double X, double Y) Head(JunctionPreviewPlacement p, JunctionTagSide side)
    {
        const double d = TagClearancePaperMm + TagRadiusPaperMm;
        return side switch
        {
            JunctionTagSide.Right => (Math.Max(p.MaxX, p.X) + d, p.Y),
            JunctionTagSide.Top => (p.X, Math.Max(p.MaxY, p.Y) + d),
            JunctionTagSide.Left => (Math.Min(p.MinX, p.X) - d, p.Y),
            _ => (p.X, Math.Min(p.MinY, p.Y) - d),
        };
    }

    // 겹침 수 = 번호 원이 덮는 다른 기호 + 이미 놓은 번호 원 + 지시선이 지나는 다른 기호
    private static int Cost(JunctionPreviewPlacement p, (double X, double Y) head,
        IReadOnlyList<JunctionPreviewPlacement> all, IReadOnlyList<(double X, double Y)> heads)
    {
        const double r = TagRadiusPaperMm + TagMarginPaperMm;
        var cost = 0;
        foreach (var q in all)
        {
            if (ReferenceEquals(q, p) || q.Item.ElementId == p.Item.ElementId) continue;
            if (q.MinX < head.X + r && head.X - r < q.MaxX && q.MinY < head.Y + r && head.Y - r < q.MaxY) cost++;
            if (SegmentHitsBox(p.X, p.Y, head.X, head.Y, q)) cost++;
        }
        foreach (var h in heads)
            if (Math.Abs(h.X - head.X) < r * 2 && Math.Abs(h.Y - head.Y) < r * 2) cost++;
        return cost;
    }

    // 지시선은 수평 또는 수직 → 축 정렬 선분·상자 교차. 시작점이 상자 안이면 모든 방향 같으니 세지 않음
    private static bool SegmentHitsBox(double x1, double y1, double x2, double y2, JunctionPreviewPlacement q)
    {
        if (q.MinX <= x1 && x1 <= q.MaxX && q.MinY <= y1 && y1 <= q.MaxY) return false;
        var (minX, maxX) = (Math.Min(x1, x2), Math.Max(x1, x2));
        var (minY, maxY) = (Math.Min(y1, y2), Math.Max(y1, y2));
        // 선분 폭 0이어도 열린 구간 비교로 맞음(양 끝 경계 접촉은 겹침 아님)
        return minX < q.MaxX && q.MinX < maxX && minY < q.MaxY && q.MinY < maxY;
    }

    // 다른 세트끼리, 세트와 주관 부재가 겹치면 경고. 자기 세트 뿌리(T)와는 원래 붙어 있으니 제외
    private static IReadOnlyList<string> Overlaps(IReadOnlyList<JunctionPreviewPlacement> placements,
        IReadOnlyList<JunctionSetOffset> sets, IReadOnlyDictionary<long, JunctionPreviewPlacement> roots)
    {
        var number = sets.Select((x, i) => (x.SetId, i + 1)).ToDictionary(x => x.SetId, x => x.Item2);
        var warnings = new List<string>();
        var candidates = placements.Where(x => x.Item.Connected && (x.Item.SetId == 0 || number.ContainsKey(x.Item.SetId)))
            .ToList();
        for (var i = 0; i < candidates.Count; i++)
        for (var j = i + 1; j < candidates.Count; j++)
        {
            var a = candidates[i];
            var b = candidates[j];
            if (a.Item.SetId == b.Item.SetId) continue;
            if (IsRootOf(a, b, roots) || IsRootOf(b, a, roots)) continue;
            if (!Overlap(a, b)) continue;
            warnings.Add($"{Label(a, number)} · {Label(b, number)} 기호가 겹칩니다");
        }
        // 번호 원끼리 겹침(태그 머리 위치 같은 규칙이라 Revit에서도 겹침)
        var tagged = placements.Where(x => x.Item.ItemNumber > 0).ToList();
        for (var i = 0; i < tagged.Count; i++)
        for (var j = i + 1; j < tagged.Count; j++)
        {
            var a = tagged[i];
            var b = tagged[j];
            if (Math.Abs(a.TagX - b.TagX) < TagRadiusPaperMm * 2 && Math.Abs(a.TagY - b.TagY) < TagRadiusPaperMm * 2)
                warnings.Add($"{Label(a, number)} · {Label(b, number)} 번호 원이 겹칩니다");
        }
        return warnings.Distinct(StringComparer.Ordinal).ToList();
    }

    private static bool IsRootOf(JunctionPreviewPlacement root, JunctionPreviewPlacement member,
        IReadOnlyDictionary<long, JunctionPreviewPlacement> roots)
        => member.Item.SetId != 0 && roots.TryGetValue(member.Item.SetId, out var found) &&
           found.Item.ElementId == root.Item.ElementId;

    private static bool Overlap(JunctionPreviewPlacement a, JunctionPreviewPlacement b)
        => a.MinX + OverlapTolerance < b.MaxX && b.MinX + OverlapTolerance < a.MaxX &&
           a.MinY + OverlapTolerance < b.MaxY && b.MinY + OverlapTolerance < a.MaxY;

    private static string Label(JunctionPreviewPlacement placement, IReadOnlyDictionary<long, int> number)
    {
        var name = placement.Item.ItemNumber > 0 ? $"{placement.Item.ItemNumber}번" : placement.Item.Name;
        return placement.Item.SetId != 0 && number.TryGetValue(placement.Item.SetId, out var n)
            ? $"세트 {n} {name}" : $"주관 {name}";
    }
}
