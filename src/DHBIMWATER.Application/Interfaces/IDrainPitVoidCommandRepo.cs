using DHBIMWATER.Application.DTOs.Revit.ValveRoom;
using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Application.Interfaces;

public interface IDrainPitVoidCommandRepo
{
    /// <summary>일반모델 Void를 기초 상단 EL에 배치하고 모든 호스트를 절단한다. 실패는 경고로 반환한다.</summary>
    /// <param name="hostElementIds">기초와 버림콘크리트 절단 대상.</param>
    /// <param name="levelId">기초 상부 참조 레벨.</param>
    string? CreateDrainPitVoid(IReadOnlyList<int> hostElementIds, long levelId,
        DrainPitDefinition definition, DrainPitVoidFamilyMappingDto mapping);
}
