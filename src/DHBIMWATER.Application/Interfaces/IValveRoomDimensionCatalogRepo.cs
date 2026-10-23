using DHBIMWATER.Core.Structures;

namespace DHBIMWATER.Application.Interfaces;

/// <summary>사용자가 편집한 밸브실 제원표의 문서 저장소(docs/312). 선례: <see cref="IConcreteSettingsRepo"/>.</summary>
public interface IValveRoomDimensionCatalogRepo
{
    /// <summary>저장된 표가 없으면 null. 내장 표준표 대체는 호출부가 한다.</summary>
    ValveRoomDimensionTable? Load();

    /// <summary>트랜잭션은 열지 않는다. 반드시 UseCase의 트랜잭션 안에서 호출된다.</summary>
    void Save(ValveRoomDimensionTable table);
}
