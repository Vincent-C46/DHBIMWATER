using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Core.Piping;

/// <summary>배관 생성 중 접점 연결 실패 지점. 좌표는 캔버스 기준(mm).</summary>
public sealed record PipeConnectionFailure(
    Point2D CanvasPoint, double ElevationMm, string Role, string FamilyTypeName, long ElementId, string Detail);
