namespace DHBIMWATER.Application.DTOs.Revit.ValveRoom;

/// <summary>공기밸브실 본관 관통 Void 입력. 모든 길이 치수는 mm다.</summary>
/// <param name="MainPipeDiameter">본관 호칭지름 DN.</param>
/// <param name="VoidDiameter">Void 지름(= 관 외경 + 여유, MainPipeVoidSizing). null이면 DN(종전 동작).</param>
public record AirValveRoomSpecDto(double MainPipeDiameter, double FoundationTopToPipeCenterDepth, string VoidAxis, double? VoidDiameter = null)
{
    /// <summary>실제 Void 지름.</summary>
    public double EffectiveVoidDiameter => VoidDiameter is > 0 ? VoidDiameter.Value : MainPipeDiameter;
}
