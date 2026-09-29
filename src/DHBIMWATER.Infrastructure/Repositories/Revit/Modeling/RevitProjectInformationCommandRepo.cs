using Autodesk.Revit.DB;
using DHBIMWATER.Application.Interfaces;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling;

/// <summary>프로젝트 정보 요소(ProjectInfo)에 텍스트 매개변수 값을 기록한다. Transaction은 호출부(UseCase)가 연다.</summary>
public sealed class RevitProjectInformationCommandRepo : IProjectInformationCommandRepo
{
    private readonly Func<Document?> _doc;
    public RevitProjectInformationCommandRepo(Func<Document?> doc) => _doc = doc;

    public void SetTextParameters(IReadOnlyDictionary<string, string> values)
    {
        if (values.Count == 0) return;
        var projectInfo = _doc()?.ProjectInformation;
        if (projectInfo == null) return;

        foreach (var (name, value) in values)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            // 바인딩이 없으면 null이다. 값 하나 때문에 생성 트랜잭션을 깨지 않고 건너뛴다.
            var parameter = projectInfo.LookupParameter(name);
            if (parameter == null || parameter.IsReadOnly) continue;
            parameter.Set(value ?? string.Empty);
        }
    }
}
