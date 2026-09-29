using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Application.Interfaces;

/// <summary>PipeLayout 패밀리·파라미터 매핑의 프로젝트 저장소(docs/175 § E-2). 선례: <c>IBendSettingsRepo</c>.</summary>
public interface IPipeLayoutFamilySettingsRepo
{
    /// <summary>저장된 설정이 없으면 null. 기본값(이름 추정) 대체는 호출부(ViewModel)가 결정한다.</summary>
    PipeLayoutFamilySettings? Load();

    /// <summary>트랜잭션은 열지 않는다. 반드시 UseCase의 트랜잭션 안에서 호출된다.</summary>
    void Save(PipeLayoutFamilySettings settings);
}
