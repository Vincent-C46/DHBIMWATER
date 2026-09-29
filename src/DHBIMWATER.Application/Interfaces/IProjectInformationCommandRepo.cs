namespace DHBIMWATER.Application.Interfaces;

/// <summary>Revit "프로젝트 정보"에 텍스트 매개변수 값을 기록한다.
/// 바인딩 생성은 <c>ISharedParameterRepository.EnsureParameters</c>가 맡고, 여기서는 값만 쓴다.</summary>
public interface IProjectInformationCommandRepo
{
    /// <summary>매개변수가 없거나 읽기 전용이면 그 항목만 건너뛴다(예외를 던지지 않는다).
    /// 호출부의 Transaction 안에서 돌아야 한다.</summary>
    void SetTextParameters(IReadOnlyDictionary<string, string> values);
}
