namespace DHBIMWATER.Application.DTOs.Revit.ValveRoom;

/// <summary>드레인 피트 패밀리 배치 결과(docs/153 § E-2).</summary>
/// <param name="InstanceId">배치된 패밀리 인스턴스 Id.</param>
/// <param name="IsVoidCutting">"로드할 때 보이드로 절단"이 켜진 Void 패밀리인지. false면 절단을 건너뛴 실물 부재다.</param>
/// <param name="Warning">절단 경고. 없으면 null.</param>
public record DrainPitPlacementResult(int InstanceId, bool IsVoidCutting, string? Warning);
