using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Core.Piping;

/// <summary>프리셋(비율 좌표)을 실제 외곽에 맞춰 캔버스 mm 좌표로 푼다(docs/172 § B-4). Revit·UI 무관.</summary>
public static class ValveRoomPipingTemplateResolver
{
    /// <param name="outline">캔버스 좌표 외곽(기준점을 뺀 것 — VM의 _canvasOutline).</param>
    /// <param name="fromFarEdge">VM의 ArrowFromTop. 포트 <see cref="PipePort.OffsetMm"/> 환산에만 쓴다 —
    /// 선 좌표는 이 값과 무관하게 같아야 한다(테스트로 고정).</param>
    /// <param name="defaultDiameterMm">기본 관경. <see cref="TemplateSegment.DiameterRatio"/>가 null인 선에 그대로 쓴다.</param>
    public static ResolvedPipingTemplate Resolve(ValveRoomPipingTemplate template, ValveRoomOutline outline, bool fromFarEdge, double defaultDiameterMm)
    {
        if (outline.IsEmpty) throw new DhbimwaterException(ErrorCodes.PL_PresetNeedsOutline, "외곽이 없으면 프리셋을 적용할 수 없습니다.");
        if (template.Valves.Count > 0) throw new DhbimwaterException(ErrorCodes.PL_PresetValveUnsupported, "밸브 프리셋은 아직 지원하지 않습니다(docs/172 § 0-5).");   // 코드 연결(docs/240 § B)

        // 1) 포트: 비율(하단/좌측 기준) → 이격 mm. fromFarEdge면 반대 모서리 기준값으로 환산해 접점이 같은 자리에 오게 한다.
        var ports = template.Ports.Select(p =>
        {
            var length = outline.SideLengthOf(p.Side);
            var fromNear = p.OffsetRatio * length;
            var offset = fromFarEdge ? length - fromNear : fromNear;
            return PipePort.Create(p.Flow, p.Side, Math.Round(offset, 1), p.Label);
        }).ToList();
        // 라벨 중복은 카탈로그 오류다. ToDictionary가 예외를 내므로 테스트로 잡는다.
        var anchors = ports.ToDictionary(p => p.Label, p => outline.AnchorOf(p, fromFarEdge), StringComparer.Ordinal);

        // 2) 선: 포트 참조는 접점 그대로(= IN/OUT 자동 연결), Relative는 경계상자 비율.
        var segments = template.Segments.Select(s => new ResolvedSegment(
            ToPoint(s.Start), ToPoint(s.End),
            s.DiameterRatio is { } r ? defaultDiameterMm * r : defaultDiameterMm)).ToList();
        return new ResolvedPipingTemplate(ports, segments);

        Point2D ToPoint(TemplatePoint p) => p.Kind switch
        {
            TemplatePointKind.Port => anchors.TryGetValue(p.PortLabel ?? string.Empty, out var a) ? a
                : throw new DhbimwaterException(ErrorCodes.PL_PresetPortMissing, $"프리셋이 참조한 포트 '{p.PortLabel}'이 없습니다."),
            _ => new Point2D(outline.MinX + outline.WidthMm * p.RatioX, outline.MinY + outline.HeightMm * p.RatioY),
        };
    }
}
