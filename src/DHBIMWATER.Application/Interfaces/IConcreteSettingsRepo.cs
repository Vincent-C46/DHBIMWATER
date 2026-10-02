using DHBIMWATER.Core.Structures;

namespace DHBIMWATER.Application.Interfaces;

/// <summary>구조물 공용 콘크리트 강도 설정의 문서 저장소(docs/249 § B). 선례: <c>IPipeLayoutFamilySettingsRepo</c>.</summary>
public interface IConcreteSettingsRepo
{
    /// <summary>저장된 설정이 없으면 null. 기본값(<see cref="ConcreteSettings.Default"/>) 대체는 호출부가 한다.</summary>
    ConcreteSettings? Load();

    /// <summary>트랜잭션은 열지 않는다. 반드시 UseCase의 트랜잭션 안에서 호출된다.</summary>
    void Save(ConcreteSettings settings);
}
