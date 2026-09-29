using Autodesk.Revit.DB;
using DHBIMWATER.Application.Interfaces;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling;

/// <summary>프로젝트 정보 요소(ProjectInfo)에서 텍스트 매개변수를 읽는다.</summary>
public sealed class RevitProjectInformationQueryRepo : IProjectInformationQueryRepo
{
    private readonly Func<Document?> _doc;
    public RevitProjectInformationQueryRepo(Func<Document?> doc) => _doc = doc;

    public string GetTextParameter(string parameterName)
    {
        if (string.IsNullOrWhiteSpace(parameterName)) return string.Empty;
        // 문서가 없거나(창만 띄운 상태) 바인딩 전이면 LookupParameter가 null이다 — 빈 값으로 본다.
        var projectInfo = _doc()?.ProjectInformation;
        return projectInfo?.LookupParameter(parameterName)?.AsString() ?? string.Empty;
    }
}
