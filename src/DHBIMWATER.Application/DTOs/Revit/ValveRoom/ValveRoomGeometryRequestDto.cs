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
    /// <summary>상부슬래브 하단 헌치 배치 여부.</summary>
    public bool HasUpperHaunch { get; init; }
    /// <summary>기초 상단 헌치 배치 여부.</summary>
    public bool HasLowerHaunch { get; init; }
    /// <summary>헌치 구조 프레임 유형("패밀리명 : 타입명"). 비어 있으면 이름 기반 자동 탐색(FindHaunchBeamSymbol)을 쓴다.</summary>
    public string HaunchTypeName { get; init; } = string.Empty;
    /// <summary>제수밸브실 보의 단면 폭(mm). UseCase가 Revit 유형에서 실측해 채운다 — UI 입력이 아니다.
    /// 상부 헌치를 보에 맞춰 끊는 데 쓰며, 0이면(조회 실패·보 없음) 끊지 않는다(docs/162 § D-4).</summary>
    public double SluiceBeamWidth { get; init; }
    /// <summary>제수밸브실 기둥의 단면 치수(mm, X/Y축). UseCase가 Revit 유형에서 실측해 채운다 — UI 입력이 아니다.
    /// 상부 보를 기둥 자리에서 끊는 데 쓰며, 0이면(조회 실패·기둥 없음) 끊지 않는다(docs/202 § C).</summary>
    public double SluiceColumnWidthX { get; init; }
    /// <inheritdoc cref="SluiceColumnWidthX"/>
    public double SluiceColumnDepthY { get; init; }
}
