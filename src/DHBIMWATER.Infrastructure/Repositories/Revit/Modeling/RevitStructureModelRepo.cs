using Autodesk.Revit.DB;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Services;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling;

/// <summary>구조물 단위 부재 조회·삭제·표시(docs/320 § C). Transaction은 호출부(UseCase)가 연다.</summary>
public sealed class RevitStructureModelRepo : IStructureModelRepo
{
    private const string AddinParameter = "DH_Addin";
    private const string AddinValue = "DHBIMWATER";

    private readonly Func<Document?> _doc;
    public RevitStructureModelRepo(Func<Document?> doc) => _doc = doc;

    public IReadOnlyList<long> GetAddinElementIds()
    {
        var doc = _doc();
        var addin = doc is null ? null : TextEqualsFilter(doc, AddinParameter, AddinValue);
        return addin is null ? Array.Empty<long>() : Collect(doc!, addin);
    }

    public IReadOnlyList<long> GetStructureElementIds(string structure)
    {
        var doc = _doc();
        if (doc is null) return Array.Empty<long>();
        var addin = TextEqualsFilter(doc, AddinParameter, AddinValue);
        var marked = TextEqualsFilter(doc, StructureRegeneration.ParameterName, structure);
        // 바인딩 없으면 표시된 부재도 없음
        return addin is null || marked is null ? Array.Empty<long>() : Collect(doc, new LogicalAndFilter(addin, marked));
    }

    public void DeleteElements(IReadOnlyCollection<long> elementIds)
    {
        var doc = _doc();
        if (doc is null) return;
        var ids = elementIds.Select(id => new ElementId(id)).Where(id => doc.GetElement(id) is not null).ToList();
        if (ids.Count > 0) doc.Delete(ids);
    }

    public void MarkStructure(IEnumerable<long> elementIds, string structure)
    {
        var doc = _doc();
        if (doc is null) return;
        foreach (var id in elementIds)
        {
            var parameter = doc.GetElement(new ElementId(id))?.LookupParameter(StructureRegeneration.ParameterName);
            if (parameter is null || parameter.IsReadOnly) continue;
            parameter.Set(structure);
        }
    }

    private static IReadOnlyList<long> Collect(Document doc, ElementFilter filter)
        => new FilteredElementCollector(doc).WhereElementIsNotElementType().WherePasses(filter)
            .ToElementIds().Select(id => id.Value).ToList();

    /// <summary>이름으로 찾은 공유 매개변수의 값 일치 필터. 같은 이름 여러 개면 OR. 없으면 null.</summary>
    private static ElementFilter? TextEqualsFilter(Document doc, string parameterName, string value)
    {
        var filters = new FilteredElementCollector(doc).OfClass(typeof(SharedParameterElement))
            .Where(e => e.Name == parameterName)
            .Select(e => (ElementFilter)new ElementParameterFilter(ParameterFilterRuleFactory.CreateEqualsRule(e.Id, value)))
            .ToList();
        return filters.Count switch
        {
            0 => null,
            1 => filters[0],
            _ => new LogicalOrFilter(filters),
        };
    }
}
