namespace DHBIMWATER.Core.Piping;

/// <summary>밸브실 유형별 격점번호(<c>DH_격점번호</c>) 기본값(docs/177).
/// <para>접두를 ASCII로 두는 이유가 표기 취향이 아니다 — 이 값이 일람표·드래프팅뷰 이름
/// <c>배관재료표_{격점번호}</c>에 그대로 들어가고(CreatePipeMaterialScheduleUseCase.cs:65),
/// 대상 정렬이 <c>StringComparer.OrdinalIgnoreCase</c> 문자열 순서라서다(같은 파일 :64).
/// 연번을 2자리로 0 채움하는 것도 같은 이유 — 한 자리면 <c>GV-1</c> &lt; <c>GV-10</c> &lt; <c>GV-2</c>로 어긋난다.</para></summary>
public static class ValveRoomJunctionIdCatalog
{
    /// <summary>기본값에 붙는 연번. 모델을 읽지 않으므로 항상 1이다(docs/177 확정 2).
    /// 실제 번호는 사용자가 창에서 고친다.</summary>
    private const int DefaultSequence = 1;

    /// <summary>유형 → 접두. 키는 <c>DH_Zone</c>에 찍히는 문자열과 글자 하나까지 같아야 한다
    /// (<c>PipeLayoutViewModel.ValveRoomZones</c>, <c>ValveRoomView.xaml</c> 라디오).</summary>
    // TODO: BV는 나비밸브(Butterfly Valve)와 겹쳐 읽힐 수 있다. 현장 표기가 DV(Drain)나 BO(Blow-off)면 여기만 고친다.
    private static readonly Dictionary<string, string> PrefixByZone = new(StringComparer.Ordinal)
    {
        ["제수밸브실"] = "GV",   // Gate Valve
        ["이토밸브실"] = "BV",   // Blow-off Valve
        ["공기밸브실"] = "AV",   // Air Valve
        ["타이밸브실"] = "TV",   // Tie Valve
    };

    /// <summary>유형의 접두. 유형이 비었거나 표에 없으면 null.</summary>
    public static string? Prefix(string? zone)
        => string.IsNullOrWhiteSpace(zone) ? null
         : PrefixByZone.TryGetValue(zone, out var prefix) ? prefix : null;

    /// <summary>유형의 격점번호 기본값(<c>GV-01</c>). 유형이 비었거나 표에 없으면 빈 문자열 —
    /// 호출부는 이때 기존 입력을 건드리지 않는다.</summary>
    public static string Default(string? zone)
        => Prefix(zone) is { } prefix ? $"{prefix}-{DefaultSequence:00}" : string.Empty;
}
