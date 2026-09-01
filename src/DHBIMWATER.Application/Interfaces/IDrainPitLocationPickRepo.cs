using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Application.Interfaces;

public interface IDrainPitLocationPickRepo
{
    /// <summary>ESC 취소 시 null. 반환 좌표는 프로젝트 내부좌표 mm이며 Z는 버린다.</summary>
    Point2D? PickLocation();
}
