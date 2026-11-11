using DHBIMWATER.Core.Structures;

namespace DHBIMWATER.Application.Interfaces
{
    public interface IGenericModelCommandRepo
    {
        int PlaceInstance(GenericModelPlacementDefinition def, long levelId, int symbolId);

        /// <summary>PlaceInstance 중 쌓인 경고(1건 = 1줄, 중복 제거)를 가져가고 비운다. 호출부가 경고창 1번에 보여줌.</summary>
        IReadOnlyList<string> TakeWarnings();
    }
}
