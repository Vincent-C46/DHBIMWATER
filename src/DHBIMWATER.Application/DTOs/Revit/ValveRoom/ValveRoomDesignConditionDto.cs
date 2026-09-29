namespace DHBIMWATER.Application.DTOs.Revit.ValveRoom;

/// <summary>밸브실 타입 및 기준 표고.</summary>
public record ValveRoomDesignConditionDto
(
    string RoomType,
    /// <summary>기초 상부 EL(m). 부재 표고 계산의 기준이며 내부원점 표고는 항상 0이다.</summary>
    double FoundationTopEl
)
{
    /// <summary>형상 분기용 유형. 기본값은 <see cref="RoomType"/>과 같고, 타이밸브실만 "이토밸브실"로 정규화해 넘긴다.
    /// RoomType은 DH_Zone에 기록될 실제 이름이라 그대로 둔다(docs/142 § 1, docs/155 § C).</summary>
    public string GeometryType { get; init; } = RoomType;
}
