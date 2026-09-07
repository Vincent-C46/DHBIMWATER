using Autodesk.Revit.DB;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

/// <summary>
/// 배관재료표 원자료를 모으는 공용 경로. 일람표(<see cref="RevitPipeMaterialScheduleRepo"/>)와
/// 드래프팅뷰(<see cref="RevitPipeMaterialDraftingRepo"/>)가 **같은 부재 집합**을 봐야 하므로 한곳에 둔다.
/// </summary>
internal static class PipeMaterialPartCollector
{
    /// <summary>DHBIMWATER가 만든 배관 밸브류 인스턴스만 고른다.
    /// 사용자가 직접 놓은 부속이나 링크 요소까지 세면 재료표 수량이 틀어진다.</summary>
    public static IEnumerable<FamilyInstance> AddinInstances(Document document)
        => new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_PipeAccessory)
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .Where(x => string.Equals(
                x.LookupParameter(ValvePipingParameters.Addin)?.AsString(),
                ValvePipingParameters.AddinValue, StringComparison.Ordinal));

    public static IReadOnlyList<PipeMaterialPart> Read(Document document)
        => AddinInstances(document)
            .Select(x => new PipeMaterialPart(
                x.LookupParameter(ValvePipingParameters.JunctionId)?.AsString() ?? string.Empty,
                x.LookupParameter(ValvePipingParameters.ItemNumber)?.AsInteger() ?? 0,
                x.LookupParameter(ValvePipingParameters.ItemName)?.AsString() ?? string.Empty,
                x.LookupParameter(ValvePipingParameters.ItemSpec)?.AsString() ?? string.Empty))
            .ToList();
}
