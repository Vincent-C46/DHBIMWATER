namespace DHBIMWATER.Core.Structures;

/// <summary>밸브실 제원표 1벌. 내장 표준표(<see cref="ValveRoomStandardDimensionCatalog.Default"/>) 또는 문서에 저장된 사용자 편집본(docs/312).
/// <para>불변. 편집은 행 목록을 새로 만들어 새 표로 바꾼다.</para></summary>
public sealed class ValveRoomDimensionTable
{
    private const double DiameterTolerance = 0.5;

    public ValveRoomDimensionTable(IEnumerable<ValveRoomStandardDimension> entries)
        => Entries = entries.ToList();

    public IReadOnlyList<ValveRoomStandardDimension> Entries { get; }

    /// <summary>표가 있는 유형 이름. 표 순서 유지.</summary>
    public IReadOnlyList<string> RoomKinds => Entries.Select(x => x.RoomKind).Distinct().ToList();

    /// <summary>유형의 비고 형식 목록. 표 순서라 첫 값이 기본값. 형식 없는 유형이면 빈 목록.</summary>
    public IReadOnlyList<string> Variants(string? roomKind)
        => Entries.Where(x => x.RoomKind == roomKind && x.Variant is not null).Select(x => x.Variant!).Distinct().ToList();

    /// <summary>유형 + 관경으로 제원 1행을 찾는다. 없으면 null(타이밸브실·표 밖 관경).
    /// <para>요청 형식에 그 관경이 없으면 같은 유형의 다른 형식으로 넘어간다 — 제수 A형은 DN350까지라
    /// DN400 이상은 B형뿐이다. 실제 적용 형식은 결과의 <see cref="ValveRoomStandardDimension.Variant"/>로 확인.</para></summary>
    public ValveRoomStandardDimension? Find(string? roomKind, double diameterMm, string? variant = null)
    {
        if (string.IsNullOrWhiteSpace(roomKind) || !double.IsFinite(diameterMm)) return null;
        var matches = Entries.Where(x => x.RoomKind == roomKind && Math.Abs(x.DiameterMm - diameterMm) <= DiameterTolerance).ToList();
        if (matches.Count == 0) return null;
        return matches.FirstOrDefault(x => x.Variant == variant) ?? matches[0];
    }

    /// <summary>저장 전 검사. 문제 있으면 사용자 안내 문장, 없으면 null.
    /// 치수 0 이하, 같은 유형·형식·관경 중복을 막는다.</summary>
    public string? Validate()
    {
        foreach (var row in Entries)
        {
            if (string.IsNullOrWhiteSpace(row.RoomKind))
                return "유형이 빈 행이 있습니다.";
            if (!(row.DiameterMm > 0) || !(row.LengthMm > 0) || !(row.WidthMm > 0) || !(row.HeightMm > 0))
                return $"{row.RoomKind} DN{row.DiameterMm:0}: 관경·길이·폭·높이는 0보다 커야 합니다.";
        }

        var duplicate = Entries
            .GroupBy(x => (x.RoomKind, x.Variant, Diameter: Math.Round(x.DiameterMm, 1)))
            .FirstOrDefault(g => g.Count() > 1);
        return duplicate is null
            ? null
            : $"{duplicate.Key.RoomKind}{(duplicate.Key.Variant is null ? string.Empty : " " + duplicate.Key.Variant)} DN{duplicate.Key.Diameter:0} 행이 {duplicate.Count()}개 있습니다. 하나만 남기세요.";
    }
}
