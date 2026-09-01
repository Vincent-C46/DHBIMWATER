using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Application.Interfaces;

public interface IDrainPitVoidCommandRepo
{
    /// <summary>절단 실패는 경고 문자열로 반환한다. null이면 성공이다.</summary>
    string? CreateDrainPitVoid(int hostElementId, DrainPitDefinition definition);
}
