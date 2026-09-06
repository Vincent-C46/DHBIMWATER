namespace DHBIMWATER.Application.DTOs.Revit.ValveRoom;

/// <summary>드레인 피트 Void 패밀리 선택 및 매개변수 매핑. 밸브실 전체에서 공용(피트별 개별 매핑 아님).</summary>
public record DrainPitVoidFamilyMappingDto
{
    /// <summary>"패밀리명 : 타입명" 형식. IElementTypeQueryRepo.GetGenericModelTypeNames() 반환값과 동일 형식.</summary>
    public string FamilyTypeName { get; init; } = string.Empty;
    public string LengthParameterName { get; init; } = string.Empty;
    public string WidthParameterName { get; init; } = string.Empty;
    public string DepthParameterName { get; init; } = string.Empty;
}
