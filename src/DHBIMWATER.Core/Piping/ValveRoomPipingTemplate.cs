namespace DHBIMWATER.Core.Piping;

/// <summary>프리셋 포트. <see cref="OffsetRatio"/>는 변 길이에 대한 비율이며
/// Left/Right는 <b>항상 하단(MinY) 기준</b>, Top/Bottom은 좌측(MinX) 기준이다.
/// ArrowFromTop 상태는 해석기가 이격값으로 환산할 때만 반영한다(docs/172 § B-1).</summary>
public sealed record TemplatePort(PortFlow Flow, OutlineSide Side, double OffsetRatio, string Label);

public enum TemplatePointKind { Port, Relative }

/// <summary>선의 끝점. Port면 <see cref="PortLabel"/>의 접점(= IN/OUT 자동 연결), Relative면 경계상자 비율 좌표.</summary>
public sealed record TemplatePoint(TemplatePointKind Kind, string? PortLabel, double RatioX, double RatioY)
{
    public static TemplatePoint Port(string label) => new(TemplatePointKind.Port, label, 0, 0);
    public static TemplatePoint Relative(double rx, double ry) => new(TemplatePointKind.Relative, null, rx, ry);
}

/// <param name="DiameterRatio">기본 관경 대비 비율. null = 기본 관경.</param>
/// <param name="EndAtInteriorWall">true면 Start→End 방향 반직선이 처음 만나는 내부 벽(이토실 중간벽 등)의 먼 면에서 끝냄.
/// 외곽에 내부 벽 없으면 End 그대로(2026-10-28).</param>
public sealed record TemplateSegment(TemplatePoint Start, TemplatePoint End, double? DiameterRatio = null, bool EndAtInteriorWall = false);

/// <summary>밸브 역할. 1차 카탈로그는 비어 있다 — 패밀리 매칭·배치는 후속 지시서(docs/171 § 2-2, docs/172 § 0-5).</summary>
// Butterfly = 추천 배치가 이름에 "버터플라이" 든 패밀리를 고름(2026-11-09). 나머지는 게이트 밸브 패밀리
public enum ValveRole { Sluice, Air, Mud, Check, Other, Butterfly }

// TODO(docs/175): 이음관이 독립 부속이 되어 JointBefore/JointAfter 플래그는 쓰이지 않는다.
//   카탈로그에 밸브가 들어올 때 TemplateFitting(TypeKey) 형태로 교체한다. 호출자가 없어 지금은 그대로 둔다.
public sealed record TemplateValve(int SegmentIndex, double T, ValveRole Role, bool JointBefore = false, bool JointAfter = false);

/// <summary>프리셋 바이패스 세트 1개(2026-10-08). 패밀리는 비워 두고 위치·방향만 정한다 — 유형은 사용자가 고른다.</summary>
/// <param name="Side">+1 = 선 진행 방향(시작→끝)의 왼쪽, -1 = 오른쪽(<see cref="BypassSetSpec.Side"/>).</param>
public sealed record TemplateBypassSet(int SegmentIndex, double T, int Side);

/// <summary>밸브실 유형별 추천 배관 프리셋. 좌표는 전부 외곽 내측 경계상자에 대한 비율(0~1)이라
/// 외곽 크기가 달라도 같은 모양이 나온다.</summary>
/// <param name="Zone">PipeLayoutViewModel.ValveRoomZones 값과 글자 단위로 같아야 한다(docs/161).</param>
public sealed record ValveRoomPipingTemplate(
    string Zone,
    string Name,
    IReadOnlyList<TemplatePort> Ports,
    IReadOnlyList<TemplateSegment> Segments,
    IReadOnlyList<TemplateValve> Valves,
    IReadOnlyList<TemplateBypassSet>? BypassSets = null);
