namespace DHBIMWATER.Core.Piping;

/// <summary>내장 프리셋 카탈로그. 제수·공기·이토는 K-water 표준도(docs/01.*.pdf) 판독값이다(2026-10-28).
/// 사용자가 유형별 밸브·분기 구성을 확정하면 <b>이 목록만</b> 바꾼다 — 코드 구조는 그대로다.</summary>
public static class ValveRoomPipingTemplateCatalog
{
    public static IReadOnlyList<ValveRoomPipingTemplate> BuiltIn { get; } =
    [
        // K-water 표준도 이토밸브실(3.0×2.5, D900~1200, 2026-10-28). 외곽은 밸브실 + 중간벽 + 방류실 전체로 잡힘.
        // 이토관은 중간벽을 지나 방류실 쪽 면에서 끝남(표준도 이토밸브 유출부). 방류관(높이 다름)은 그리지 않음(사용자 결정).
        // 끝점은 피킹한 중간벽에서 찾음(EndAtInteriorWall) — 중간벽 위치는 밸브실 창에서 바뀌므로 비율로 고정하지 않음.
        // 중간벽 없으면 x 0.5(창 기본 = 중간벽 정중앙)에서 끝. y 0.45 = 관경별 (W1+D/2)/W 0.42(DN900)~0.48(DN1200)의 중간값.
        // 슬루스 T 0.35 = 표준도 1140 / 관 길이 3300.
        new("이토밸브실", "이토밸브실 표준도(이토관+슬루스)",
            Ports: [new(PortFlow.In, OutlineSide.Left, 0.45, "IN")],
            Segments: [new(TemplatePoint.Port("IN"), TemplatePoint.Relative(0.5, 0.45), EndAtInteriorWall: true)],
            Valves: [new(0, 0.35, ValveRole.Sluice)]),
        // K-water 표준도 공기밸브실(2.0×2.0, D1500~1600, 2026-10-28). 중앙 수직 공기밸브는 프리셋 범위 밖 — 수직분기(docs/301)로 직접 넣음.
        // 외곽에 본관 관통 Void가 있으면 이 직관 대신 ForMainPipeVoid를 씀(2026-10-29). 이건 Void 없을 때 폴백.
        Straight(AirValveRoom),
        // 타이밸브실(2026-11-09 사용자 지정). IN 2개·OUT 2개, 위·아래 두 본관을 가운데 연결관으로 이은 H(I자 눕힘) 모양.
        // 위·아래 본관은 연결관 T 절점(x 0.5)에서 두 선으로 나눔 — 같은 좌표 끝점이 한 절점으로 붙어 T가 됨.
        // 바이패스 세트 3개(위·아래 본관, 연결관), 세트마다 밸브부관 사이에 버터플라이밸브 2개.
        // 위·아래 = T 절점 중심 세트(T에 붙은 본관 선 끝 t 1, docs/273 § C) + T 양쪽 밸브 1개씩.
        //   밸브는 T에서 고정 300mm(TieValveOffsetMm) — 세트 안 밸브는 T 커넥터부터 붙여 배치됨(docs/297 § G).
        //   Revit은 T에서 세트 반폭(최소 500mm, BypassSet.EstimatedHalfSpanMm) 안 밸브만 세트 안으로 봄 → 비율이면 큰 밸브실에서 빠짐.
        // 연결관 = 중앙 세트(t 0.5) + 그 안 밸브 2개(t 0.45 / 0.55, 각 A 쪽으로 붙음).
        // 바이패스 쪽: 위 본관 +1(위, 바깥), 아래 본관 -1(아래, 바깥), 연결관 +1(진행 방향 왼쪽 = 도면 왼쪽).
        // 세트 3개 모두 바이패스 밸브 2개(V2 = V1, 단관 300, docs/322) — 1개 세트는 T 몸통 보정 안 돼 A가 T와 겹쳐 빠졌음.
        // 연결관이 짧으면 Revit이 실측 최소 길이로 그 세트만 건너뜀(사유에 필요한 T–T 거리).
        // TODO: 외곽이 이토밸브실 형상이라 중간벽이 있음. 연결관 x 0.5가 중간벽과 겹치면 캔버스에서 손으로 옮김
        // 실측 벽 여유(docs/323)가 있으면 PipeLayout이 TieWithWallClearance로 바꿔 씀. 이건 실측 없을 때 폴백(0.25 / 0.75).
        Tie(0.25),
        // K-water 표준도 제수밸브실(7.0×5.0, D1600~1650, 2026-10-28). IN2는 표준도에 없어 뺌(사용자 결정).
        // y 0.55 = (W1 709 + W4 1247 + D/2 800)/5000. 본관 밸브(버터플라이) = 우회 분기 두 점 정중앙 x 0.443.
        // 부제수밸브 우회관 = 바이패스 세트(+1 = 진행 방향 왼쪽 = 도면 상단 W2 측). 분기 간격·우회 관경은 세트가 핸드북으로 정함.
        // 분기점 수직 공기밸브 2개는 프리셋 범위 밖 — 수직분기(docs/301)로 직접 넣음. 신축관(x 0.77~0.88)도 미포함.
        new("제수밸브실", "제수밸브실 표준도(본관 밸브+부제수 바이패스)",
            Ports:
            [
                new(PortFlow.In, OutlineSide.Left, 0.55, "IN"),
                new(PortFlow.Out, OutlineSide.Right, 0.55, "OUT"),
            ],
            Segments: [new(TemplatePoint.Port("IN"), TemplatePoint.Port("OUT"))],
            Valves: [new(0, 0.443, ValveRole.Other)],
            BypassSets: [new(0, 0.443, +1)]),
    ];

    /// <summary>유형의 기본 프리셋. 없으면 null — 적용 버튼이 비활성된다.</summary>
    public static ValveRoomPipingTemplate? Default(string? zone)
        => string.IsNullOrWhiteSpace(zone) ? null
         : BuiltIn.FirstOrDefault(x => string.Equals(x.Zone, zone, StringComparison.Ordinal));

    /// <summary>타이밸브실 이름. 실측 벽 여유 분기(docs/323)에 씀.</summary>
    public const string TieValveRoom = "타이밸브실";

    /// <summary>타이밸브실 — 위·아래 본관을 내벽에서 <paramref name="wallClearanceMm"/>(실측 c, docs/323) 떨어뜨림.
    /// 외곽이 비었거나 c가 폭 절반 이상이면 null(폴백 = <see cref="Default"/>의 비율 배치).</summary>
    public static ValveRoomPipingTemplate? TieWithWallClearance(ValveRoomOutline outline, double wallClearanceMm)
    {
        if (outline.IsEmpty || outline.HeightMm <= 0 || !double.IsFinite(wallClearanceMm) || wallClearanceMm <= 0) return null;
        var low = wallClearanceMm / outline.HeightMm;
        return low < 0.5 ? Tie(low) : null;
    }

    /// <summary>타이밸브실 H 배치. <paramref name="low"/> = 아래 본관 y 비율, 위 본관 = 1 − low. 포트 비율도 같은 값(왼·오른 변 길이 = 외곽 높이).</summary>
    private static ValveRoomPipingTemplate Tie(double low)
    {
        var high = 1 - low;
        return new(TieValveRoom, "타이밸브실(IN 2·OUT 2, 버터플라이+바이패스)",
            Ports:
            [
                new(PortFlow.In, OutlineSide.Left, high, "IN"),
                new(PortFlow.In, OutlineSide.Left, low, "IN2"),
                new(PortFlow.Out, OutlineSide.Right, high, "OUT"),
                new(PortFlow.Out, OutlineSide.Right, low, "OUT2"),
            ],
            Segments:
            [
                new(TemplatePoint.Port("IN"), TemplatePoint.Relative(0.5, high)),
                new(TemplatePoint.Relative(0.5, high), TemplatePoint.Port("OUT")),
                new(TemplatePoint.Port("IN2"), TemplatePoint.Relative(0.5, low)),
                new(TemplatePoint.Relative(0.5, low), TemplatePoint.Port("OUT2")),
                new(TemplatePoint.Relative(0.5, low), TemplatePoint.Relative(0.5, high)),
            ],
            Valves:
            [
                new(0, 0.9, ValveRole.Butterfly, OffsetMm: -TieValveOffsetMm), new(1, 0.1, ValveRole.Butterfly, OffsetMm: TieValveOffsetMm),
                new(2, 0.9, ValveRole.Butterfly, OffsetMm: -TieValveOffsetMm), new(3, 0.1, ValveRole.Butterfly, OffsetMm: TieValveOffsetMm),
                new(4, 0.45, ValveRole.Butterfly), new(4, 0.55, ValveRole.Butterfly),
            ],
            BypassSets: [new(0, 1.0, +1, TwoValves: true), new(2, 1.0, -1, TwoValves: true), new(4, 0.5, +1, TwoValves: true)]);
    }
    /// <summary>타이밸브실 T 양쪽 버터플라이밸브의 T 절점 거리(mm). 세트 반폭 최소 500 안쪽.</summary>
    private const double TieValveOffsetMm = 300;

    /// <summary>공기밸브실 유형명. 추천 배치가 고정 프리셋 대신 Void 자리를 쓰는 유형이다(2026-10-29).</summary>
    public const string AirValveRoom = "공기밸브실";

    /// <summary>공기밸브실 — 기초 본관 관통 Void 자리를 지나는 직관 1본(2026-10-29). 외곽이 비었으면 null.
    /// 끝 = 포트 접점 면(외곽이 기초 외측면 모드면 기초까지, 2026-10-29).
    /// X축 관통 = 좌→우 IN/OUT 포트(Void Y). Y축 = 하→상 선만, 포트 없음 — 캔버스가 Top/Bottom 화살표 미지원(docs/172 § 0-5).
    /// 관경 = Void 지름에서 역산한 DN(기본 관경 대비 비율로 넣음).</summary>
    public static ValveRoomPipingTemplate? ForMainPipeVoid(string zone, ValveRoomOutline outline, MainPipeVoid pipeVoid, double defaultDiameterMm)
    {
        if (outline.IsEmpty || outline.WidthMm <= 0 || outline.HeightMm <= 0 || defaultDiameterMm <= 0) return null;
        var ratio = pipeVoid.AlongX
            ? (pipeVoid.Center.Y - outline.MinY) / outline.HeightMm
            : (pipeVoid.Center.X - outline.MinX) / outline.WidthMm;
        ratio = Math.Clamp(ratio, 0, 1);
        // Y축은 포트가 없어 끝점을 비율로 줌 — 기초 외측면 사용 시 0~1 밖(기초까지 연장).
        var face = outline.PortFaceBounds;
        var bottom = (face.MinY - outline.MinY) / outline.HeightMm;
        var top = (face.MaxY - outline.MinY) / outline.HeightMm;
        var diameterRatio = pipeVoid.NominalDiameterMm / defaultDiameterMm;
        var name = $"{zone} 본관 관통 Void(DN{pipeVoid.NominalDiameterMm:0})";
        return pipeVoid.AlongX
            ? new(zone, name,
                [new(PortFlow.In, OutlineSide.Left, ratio, "IN"), new(PortFlow.Out, OutlineSide.Right, ratio, "OUT")],
                [new(TemplatePoint.Port("IN"), TemplatePoint.Port("OUT"), diameterRatio)],
                [])
            : new(zone, name, [],
                [new(TemplatePoint.Relative(ratio, bottom), TemplatePoint.Relative(ratio, top), diameterRatio)],
                []);
    }

    /// <summary>IN(좌, 중앙) → OUT(우, 중앙) 직관 1본.</summary>
    private static ValveRoomPipingTemplate Straight(string zone) => new(zone, $"{zone} 기본(직관)",
        [new(PortFlow.In, OutlineSide.Left, 0.5, "IN"), new(PortFlow.Out, OutlineSide.Right, 0.5, "OUT")],
        [new(TemplatePoint.Port("IN"), TemplatePoint.Port("OUT"))],
        []);
}
