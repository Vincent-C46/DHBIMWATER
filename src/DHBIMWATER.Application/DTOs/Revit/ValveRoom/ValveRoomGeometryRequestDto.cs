using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Application.DTOs.Revit.ValveRoom;

/// <summary>ValveRoomGeometryCalculator 입력. RoomType에 따라 MudSpec/SluiceSpec 중 하나만 채워진다.</summary>
public record ValveRoomGeometryRequestDto
(
    ValveRoomDesignConditionDto DesignConditionDto,
    ValveRoomPlanSpecDto PlanSpecDto,
    ValveRoomProfileSpecDto ProfileSpecDto,
    MudValveRoomSpecDto? MudSpec,
    SluiceValveRoomSpecDto? SluiceSpec
)
{
    /// <summary>드레인 피트 목록. 비어 있으면 피트를 만들지 않는다.</summary>
    public IReadOnlyList<DrainPitDefinition> DrainPits { get; init; } = Array.Empty<DrainPitDefinition>();
}
