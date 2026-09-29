using DHBIMWATER.Application.DTOs.Revit.DischargeBasin;

namespace DHBIMWATER.Application.UseCases.AutoGenerator;

/// <summary>
/// 토출조 모델 생성. ViewModel 이 Revit 의존 없이 테스트될 수 있도록 UseCase 를 인터페이스로 끊어 둔다
/// (Application/UseCases/Sheets/IPumpingStationUseCase 와 같은 방식).
/// </summary>
public interface ICreateDischargeBasinUseCase
{
    /// <summary>
    /// 생성이 막혀 있으면 그 사유를, 만들 수 있으면 null 을 돌려준다.
    /// 유형(단차)뿐 아니라 산출된 제원(예: 중앙부 토출의 TL3 ≤ 0)까지 본다.
    /// </summary>
    string? GetBlockingReason(DischargeBasinInputDto input, DischargeBasinGeometryDto geometry);

    /// <summary>Transaction 을 열어 레벨·슬래브·벽체·개구부를 만든다.</summary>
    void Execute(DischargeBasinInputDto input, DischargeBasinGeometryDto geometry);
}
