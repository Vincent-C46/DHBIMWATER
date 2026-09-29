using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Application.Interfaces
{
    /// <summary>
    /// Revit 뷰에서 밸브실 외곽 벽체와 기둥을 사용자에게 직접 피킹시켜 레이아웃 외곽을 만든다(docs/184).
    /// 반환 좌표는 <b>Revit 프로젝트 좌표 mm</b>이며, 캔버스 좌표 변환은 호출측(ViewModel)이 담당한다.
    /// </summary>
    public interface IValveRoomOutlinePickRepo
    {
        /// <summary>
        /// 사용자가 아무것도 고르지 않고 ESC로 취소하면 null을 반환한다.
        /// 기둥만 골랐다면 <see cref="ValveRoomOutline.Walls"/>가 빈 외곽(= <c>IsEmpty</c>)을 돌려주므로,
        /// 호출측이 "취소"와 "벽을 안 골랐다"를 구분할 수 있다(docs/184 § C-5).
        /// </summary>
        ValveRoomOutline? PickExistingObjects();

        /// <summary>저장된 ElementId로 외곽을 복원한다(docs/204 § C-3). 사용자에게 피킹을 요구하지 않는다.
        /// 삭제된 부재는 제외하고 남은 것만으로 재구성하며, 제외된 개수는 <paramref name="missingCount"/>로 돌려준다.</summary>
        ValveRoomOutline? ResolveExistingObjects(IReadOnlyList<long> elementIds, out int missingCount);
    }
}
