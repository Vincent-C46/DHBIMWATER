namespace DHBIMWATER.Application.Interfaces;

/// <summary>이미 만들어진 요소에 텍스트 매개변수를 일괄 기록한다.
/// 생성 Repo마다 인자를 늘리지 않고 UseCase에서 한 번에 처리하려는 용도다(docs/142 § 4).</summary>
public interface IElementParameterCommandRepo
{
    /// <summary>매개변수가 없거나 읽기 전용인 요소는 건너뛴다(예외를 던지지 않는다).
    /// 호출부의 Transaction 안에서 돌아야 한다.</summary>
    void SetTextParameter(IReadOnlyList<int> elementIds, string parameterName, string value);
}
