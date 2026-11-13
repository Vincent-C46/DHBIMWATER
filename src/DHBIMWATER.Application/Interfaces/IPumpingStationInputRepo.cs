namespace DHBIMWATER.Application.Interfaces;

/// <summary>펌프장 창 입력값(제원)의 문서 저장소. 같은 프로젝트에서 창 다시 열 때 복원용.
/// 값 형식(JSON)은 창(ViewModel)이 정함 — 저장소는 문자열만 보관. 선례: <see cref="IConcreteSettingsRepo"/>.</summary>
public interface IPumpingStationInputRepo
{
    /// <summary>저장본 없으면 null.</summary>
    string? Load();

    /// <summary>트랜잭션 안 엶. 반드시 UseCase 트랜잭션 안에서 호출.</summary>
    void Save(string json);
}
