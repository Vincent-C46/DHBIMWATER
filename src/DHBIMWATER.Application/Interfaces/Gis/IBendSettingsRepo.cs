using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Application.Interfaces.Gis;

public interface IBendSettingsRepo
{
    /// <summary>저장된 설정이 없으면 null. 기본값 대체는 호출부(UseCase/VM)가 결정한다.</summary>
    BendSettings? Load();

    /// <summary>트랜잭션은 열지 않는다. 반드시 UseCase의 트랜잭션 안에서 호출된다.</summary>
    void Save(BendSettings settings);

    /// <summary>배치 설정만 저장한다. 표 칸은 기존 값을 그대로 둔다(저장본이 없으면 "[]"). 트랜잭션 안에서 부른다.</summary>
    void SavePlacementSettings(BendSettings settings);
}
