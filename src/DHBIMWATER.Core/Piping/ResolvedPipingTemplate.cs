using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Core.Piping;

/// <summary>해석된 직선 1본(캔버스 mm).</summary>
public sealed record ResolvedSegment(Point2D Start, Point2D End, double DiameterMm);

/// <summary>외곽·관경을 넣어 풀어낸 프리셋. VM은 이것을 그대로 그래프에 옮긴다(docs/172 § B-2).</summary>
public sealed record ResolvedPipingTemplate(IReadOnlyList<PipePort> Ports, IReadOnlyList<ResolvedSegment> Segments);
