namespace DHBIMWATER.Core.Piping;

/// <summary>
/// 격점 안 부재번호 채번 규칙(docs/180). 같은 `명칭+규격`은 같은 번호를 받아야
/// 항목화를 끈 일람표에서 행 1개에 번호 1개가 대응한다(docs/131 § B-4).
/// <para>신규 배치(<c>RevitPipeSegmentCommandRepo.RecordMaterialParameters</c>)와
/// 재기록(<c>RevitPipeMaterialBackfillRepo.AssignNumbers</c>)이 <b>같은 번호</b>를 내야 하므로
/// 규칙을 여기 한곳에 둔다 — <see cref="PipeMaterialSpecText"/>를 한곳에 둔 것과 같은 이유다.</para>
/// </summary>
public static class PipeMaterialNumbering
{
    /// <summary>채번 단위. 사용자가 말하는 "동일한 유형·동일한 규격"이 이 짝이다 —
    /// 명칭에 접합 표기(`2F`)가, 규격에 직경·연장이 들어 있다(<see cref="PipeMaterialSpecText"/>).</summary>
    public readonly record struct PartKey(string Name, string Spec);

    /// <summary>한 격점 안에서, 기존 부재의 번호 체계를 이어받아 <paramref name="keys"/>에 번호를 매긴다.</summary>
    /// <param name="existing">같은 격점에 이미 있는(번호가 찍힌) 부재. 호출측이 격점으로 걸러서 넘긴다.</param>
    /// <param name="keys">이번에 번호를 받을 부재 키. 중복이 있어도 되며, <b>등장 순서가 곧 채번 순서</b>다
    /// — 사전순이 아니라 최초 배치 순서로 매긴다(도면을 읽는 순서에 가깝다).</param>
    /// <returns>기존 그룹 + 새 그룹 전부의 번호. 호출측은 이 사전을 그대로 조회해 쓴다.</returns>
    public static IReadOnlyDictionary<PartKey, int> Assign(
        IEnumerable<PipeMaterialPart> existing, IEnumerable<PartKey> keys)
    {
        var numbers = new Dictionary<PartKey, int>();
        var max = 0;

        foreach (var part in existing)
        {
            // 0 이하는 "아직 안 매겨진 값"이다 — 파라미터가 없는 인스턴스도 0으로 읽힌다
            // (PipeMaterialPartCollector.Read의 `?? 0`). 그 값을 그룹 번호로 삼으면 0번 행이 생긴다.
            if (part.ItemNumber <= 0) continue;
            var key = new PartKey(part.Name, part.Spec);
            // 한 그룹에 번호가 둘 이상 찍혀 있으면(사용자가 인스턴스 하나만 고친 경우) 가장 작은 값을 택한다.
            // 수집 순서에 결과가 흔들리지 않게 하기 위해서다.
            if (!numbers.TryGetValue(key, out var assigned) || part.ItemNumber < assigned)
                numbers[key] = part.ItemNumber;
            if (part.ItemNumber > max) max = part.ItemNumber;
        }

        // 빈 번호(1,2,4에서 3)는 메우지 않고 max+1부터 나간다 — 사용자가 고친 번호와 이미 붙인 태그를 건드리지 않기 위해서다.
        var next = max + 1;
        foreach (var key in keys)
            if (!numbers.ContainsKey(key)) numbers[key] = next++;

        return numbers;
    }

    /// <summary>격점번호 동일성. 비어 있는 값(null·공백)끼리는 같은 격점("미지정")으로 본다 —
    /// 일람표·드래프팅뷰의 "미지정" 표와 같은 규칙이다(<see cref="PipeMaterialTable.Build"/>).</summary>
    public static bool SameJunction(string? left, string? right)
        => string.IsNullOrWhiteSpace(left)
            ? string.IsNullOrWhiteSpace(right)
            : string.Equals(left, right, StringComparison.Ordinal);
}
