using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Application.Interfaces
{
    /// <summary>
    /// Revit 뷰에서 이미 모델링된 배관 부속류·밸브류를 피킹해 캔버스 배관망으로 되돌린다(docs/158).
    /// 반환 좌표는 <b>Revit 프로젝트 좌표 mm</b>이며, 캔버스 좌표 변환은 호출측(ViewModel)이 담당한다.
    /// </summary>
    public interface IValveRoomPipingPickRepo
    {
        /// <summary>사용자가 아무것도 고르지 않고 ESC로 취소하면 null을 반환한다.</summary>
        PipeLayoutImport? PickPiping();
    }
}
