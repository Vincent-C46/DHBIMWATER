namespace DHBIMWATER.Application.Interfaces;

/// <summary>Revit "프로젝트 정보"의 텍스트 매개변수를 읽는다. Transaction이 필요 없는 조회 전용이다.</summary>
public interface IProjectInformationQueryRepo
{
    /// <summary>매개변수가 없거나 값이 비어 있으면 빈 문자열을 준다(예외를 던지지 않는다).</summary>
    string GetTextParameter(string parameterName);
}
