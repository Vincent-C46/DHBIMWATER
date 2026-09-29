namespace DHBIMWATER.Core.Piping;

/// <summary>내장 프리셋 카탈로그. 1차는 전부 직관만이다(docs/172 § 0-3 Q1).
/// 사용자가 유형별 밸브·분기 구성을 확정하면 <b>이 목록만</b> 바꾼다 — 코드 구조는 그대로다.</summary>
public static class ValveRoomPipingTemplateCatalog
{
    public static IReadOnlyList<ValveRoomPipingTemplate> BuiltIn { get; } =
    [
        Straight("이토밸브실"),
        Straight("공기밸브실"),
        // 자리표시자(docs/172 § 0-4 A7) — 타이밸브실 배관 구성 미확정.
        Straight("타이밸브실"),
        // IN 2개는 제수밸브실이다(2026-09-15 사용자 수정 — 종전 이토밸브실에 두었던 것을 옮김). 배관 구성은 미확정 자리표시자.
        new("제수밸브실", "제수밸브실 기본(직관)",
            Ports:
            [
                new(PortFlow.In, OutlineSide.Left, 0.5, "IN"),
                new(PortFlow.In, OutlineSide.Left, 0.75, "IN2"),
                new(PortFlow.Out, OutlineSide.Right, 0.5, "OUT"),
            ],
            Segments:
            [
                new(TemplatePoint.Port("IN"), TemplatePoint.Port("OUT")),
                new(TemplatePoint.Port("IN2"), TemplatePoint.Relative(1.0, 0.75)),   // 우측 벽면까지. 포트 없음
            ],
            Valves: []),
    ];

    /// <summary>유형의 기본 프리셋. 없으면 null — 적용 버튼이 비활성된다.</summary>
    public static ValveRoomPipingTemplate? Default(string? zone)
        => string.IsNullOrWhiteSpace(zone) ? null
         : BuiltIn.FirstOrDefault(x => string.Equals(x.Zone, zone, StringComparison.Ordinal));

    /// <summary>IN(좌, 중앙) → OUT(우, 중앙) 직관 1본.</summary>
    private static ValveRoomPipingTemplate Straight(string zone) => new(zone, $"{zone} 기본(직관)",
        [new(PortFlow.In, OutlineSide.Left, 0.5, "IN"), new(PortFlow.Out, OutlineSide.Right, 0.5, "OUT")],
        [new(TemplatePoint.Port("IN"), TemplatePoint.Port("OUT"))],
        []);
}
