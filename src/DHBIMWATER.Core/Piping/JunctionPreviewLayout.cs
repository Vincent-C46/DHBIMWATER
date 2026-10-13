namespace DHBIMWATER.Core.Piping;

// 세트별 펼침. StepPaperMm 부호 = 방향(음수 = 반대), 0 = 펼치지 않음. 단위 도면 mm
public sealed record JunctionSetOffset(long SetId, JunctionExpandAxis Axis, double StepPaperMm);

public static class JunctionSetOffsets
{
    public static JunctionSetOffset For(IReadOnlyList<JunctionSetOffset>? sets, long setId,
        JunctionExpandAxis defaultAxis, double defaultStepPaperMm)
        => sets?.FirstOrDefault(x => x.SetId == setId) ?? new JunctionSetOffset(setId, defaultAxis, defaultStepPaperMm);
}

public sealed record JunctionPreviewLink(long A, long B);

// 좌표 단위 = 도면 mm, X = 뷰 오른쪽, Y = 뷰 위쪽
public sealed record JunctionPreviewPlacement(JunctionPreviewItem Item, double X, double Y,
    double MinX, double MinY, double MaxX, double MaxY, double TagX, double TagY);
// SetId 0 = 주관(수평 부재 사이). 세트 중심선은 세트 Id
public sealed record JunctionPreviewSegment(double X1, double Y1, double X2, double Y2, long SetId = 0);
public sealed record JunctionPreviewDimension(JunctionPreviewPlacement First, JunctionPreviewPlacement Second,
    JunctionExpandAxis Axis, double StepPaperMm);
public sealed record JunctionPreviewScene(IReadOnlyList<JunctionPreviewPlacement> Placements,
    IReadOnlyList<JunctionPreviewSegment> CenterLines, IReadOnlyList<JunctionPreviewDimension> Dimensions,
    IReadOnlyList<string> Warnings);

public static class JunctionPreviewLayout
{
    // Revit 태그 머리 위치와 같은 값 (RevitJunctionDetailRepo.PlaceNumberTags)
    public const double TagGapPaperMm = 4.5;
    // 기호선 없는 부재의 대체 기호 반폭
    public const double FallbackHalfPaperMm = 2.2;
    // 번호 원 반지름(미리보기 20px ÷ 8px/mm). 원끼리 겹침 판정에 씀
    public const double TagRadiusPaperMm = 1.25;
    private const double OverlapTolerance = 0.1;

    public static JunctionPreviewScene Build(IReadOnlyList<JunctionPreviewItem> items,
        IReadOnlyList<JunctionPreviewLink> links, IReadOnlyList<JunctionSetOffset> sets, int viewScale,
        bool expand = true)
    {
        var setById = sets.ToDictionary(x => x.SetId);
        var placements = items.Select(item => Place(item, setById, viewScale, expand)).ToList();
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
            if (members.Count == 0 || !expand || set.StepPaperMm == 0) continue;
            if (roots.TryGetValue(set.SetId, out var root))
                centerLines.Add(new JunctionPreviewSegment(root.X, root.Y, members[^1].X, members[^1].Y, set.SetId));
            if (members.Count >= 2)
                dimensions.Add(new JunctionPreviewDimension(members[0], members[1], set.Axis, set.StepPaperMm));
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
            var distance = order * set.StepPaperMm;
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

        // 수직 부재: Y축 펼침이면 왼쪽, X축 펼침이면 아래. 수평 부재: 아래
        var left = item.IsVertical && axis == JunctionExpandAxis.Y;
        return new JunctionPreviewPlacement(item, x, y, minX, minY, maxX, maxY,
            left ? x - TagGapPaperMm : x, left ? y : y - TagGapPaperMm);
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
