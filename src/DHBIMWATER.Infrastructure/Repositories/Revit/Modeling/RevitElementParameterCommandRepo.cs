using Autodesk.Revit.DB;
using DHBIMWATER.Application.Interfaces;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling;

/// <summary>생성이 끝난 요소들에 같은 텍스트 매개변수 값을 찍는다. Transaction은 호출부(UseCase)가 연다.</summary>
public sealed class RevitElementParameterCommandRepo : IElementParameterCommandRepo
{
    private readonly Func<Document?> _doc;
    public RevitElementParameterCommandRepo(Func<Document?> doc) => _doc = doc;

    public void SetTextParameter(IReadOnlyList<int> elementIds, string parameterName, string value)
    {
        if (elementIds.Count == 0 || string.IsNullOrWhiteSpace(parameterName)) return;
        var doc = _doc();
        if (doc == null) return;

        foreach (var id in elementIds.Distinct())
        {
            // 바인딩이 없으면 LookupParameter가 null이다. 값 하나 때문에 배치 트랜잭션을 깨지 않고 건너뛴다.
            var parameter = doc.GetElement(new ElementId(id))?.LookupParameter(parameterName);
            if (parameter == null || parameter.IsReadOnly) continue;
            parameter.Set(value);
        }
    }
}
