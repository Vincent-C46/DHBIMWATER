namespace DHBIMWATER.Core.Piping;

/// <summary>배관재료표 원자료 한 건. 배치된 인스턴스 1개에서 읽은 값 그대로다(docs/131 § B).</summary>
public sealed record PipeMaterialPart(string JunctionId, int ItemNumber, string Name, string Spec, string Remark = "", long SymbolTypeId = 0, string? TypeImagePath = null);

/// <summary>배관재료표 한 행. 같은 (번호·명칭·규격·비고)를 묶어 수량을 센 결과다.</summary>
public sealed record PipeMaterialRow(int ItemNumber, string Name, string Spec, int Count, string Remark = "", long SymbolTypeId = 0, string? TypeImagePath = null);

/// <summary>
/// 배관재료표 행 집계(docs/133). 일람표는 Revit이 알아서 묶어 주지만,
/// 드래프팅뷰는 표를 직접 그리므로 같은 묶음을 여기서 만들어야 한다.
/// <para>묶는 기준은 docs/131 가정 B와 같다 — 같은 명칭·같은 규격은 같은 부재번호를 갖는다.
/// 여기에 비고까지 더한다(docs/141 § B-7): 일람표는 <c>DH_비고</c>가 필드라 값이 다르면 이미 행이 갈리므로,
/// 드래프팅뷰가 비고를 무시하고 묶으면 같은 격점을 그린 두 표의 행 수가 어긋난다.</para>
/// </summary>
public static class PipeMaterialTable
{
    /// <summary><paramref name="junctionId"/>가 비어 있으면 격점번호가 비어 있는 부재를 모은다
    /// (일람표의 "미지정" 표와 같은 규칙).</summary>
    public static IReadOnlyList<PipeMaterialRow> Build(IReadOnlyList<PipeMaterialPart> parts, string junctionId)
    {
        var wantsUnassigned = string.IsNullOrWhiteSpace(junctionId);
        return parts
            .Where(x => wantsUnassigned
                ? string.IsNullOrWhiteSpace(x.JunctionId)
                : string.Equals(x.JunctionId, junctionId, StringComparison.Ordinal))
            .GroupBy(x => (x.ItemNumber, x.Name, x.Spec, x.Remark))
            .Select(g => new PipeMaterialRow(g.Key.ItemNumber, g.Key.Name, g.Key.Spec, g.Count(), g.Key.Remark, g.First().SymbolTypeId, g.First().TypeImagePath))
            // 번호가 1차 정렬이다. 번호가 같은데 명칭·규격이 다른 경우는 채번이 어긋난 것이지만,
            // 표를 못 그리게 막을 이유는 없으므로 명칭·규격·비고로 안정 정렬만 해 둔다.
            .OrderBy(x => x.ItemNumber)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ThenBy(x => x.Spec, StringComparer.Ordinal)
            .ThenBy(x => x.Remark, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>격점번호가 비어 있지 않은 값 목록(중복 없음, 최초 등장 순서).</summary>
    public static IReadOnlyList<string> JunctionIds(IReadOnlyList<PipeMaterialPart> parts)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part.JunctionId)) continue;
            if (seen.Add(part.JunctionId)) result.Add(part.JunctionId);
        }
        return result;
    }

    public static int UnassignedCount(IReadOnlyList<PipeMaterialPart> parts)
        => parts.Count(x => string.IsNullOrWhiteSpace(x.JunctionId));
}
