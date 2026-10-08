using Autodesk.Revit.DB;
using System.IO;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

/// <summary>
/// 배관재료표 원자료를 모으는 공용 경로. 일람표(<see cref="RevitPipeMaterialScheduleRepo"/>)와
/// 드래프팅뷰(<see cref="RevitPipeMaterialDraftingRepo"/>)가 **같은 부재 집합**을 봐야 하므로 한곳에 둔다.
/// </summary>
internal static class PipeMaterialPartCollector
{
    /// <summary>DHBIMWATER가 만든 배관 밸브류·배관 이음쇠 인스턴스만 고른다.
    /// 관·곡관·T형은 배관 이음쇠로 배치되므로 한 카테고리만 훑으면 재료표에서 통째로 빠진다(docs/133).
    /// 사용자가 직접 놓은 부속이나 링크 요소까지 세면 재료표 수량이 틀어진다.</summary>
    public static IEnumerable<FamilyInstance> AddinInstances(Document document)
        => PipingInstances(document)
            .Where(x => string.Equals(
                Get(x, ValvePipingParameters.Addin)?.AsString(),
                ValvePipingParameters.AddinValue, StringComparison.Ordinal));

    /// <summary><c>DH_Addin</c>이 비어 있는 배관 밸브류·이음쇠 인스턴스(재기록 후보, docs/136).
    /// 매개변수 자체가 없는(바인딩 전에 배치된) 인스턴스도 후보다.
    /// <paramref name="scope"/>가 있으면 그 요소로만 좁힌다 — 사용자가 대상을 직접 고른 경우다.
    /// <para><see cref="AddinInstances"/>와 같은 모집단을 봐야 "태그된 것 + 태그 안 된 것 = 전체"가 성립한다.</para></summary>
    public static IEnumerable<FamilyInstance> UntaggedInstances(
        Document document, IReadOnlyCollection<ElementId>? scope = null)
        => PipingInstances(document, scope)
            .Where(x => string.IsNullOrWhiteSpace(Get(x, ValvePipingParameters.Addin)?.AsString()));

    /// <summary>두 수집 경로가 공유하는 모집단 — 배관 밸브류·이음쇠 패밀리 인스턴스.</summary>
    private static IEnumerable<FamilyInstance> PipingInstances(
        Document document, IReadOnlyCollection<ElementId>? scope = null)
        => (scope is { Count: > 0 }
                ? new FilteredElementCollector(document, scope.ToList())
                : new FilteredElementCollector(document))
            .WherePasses(new ElementMulticategoryFilter(new[]
                { BuiltInCategory.OST_PipeAccessory, BuiltInCategory.OST_PipeFitting }))
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>();

    /// <param name="exclude">결과에서 뺄 인스턴스. 채번처럼 "이번에 만든 것 말고 기존 것"만 봐야 할 때 쓴다(docs/180 § C).</param>
    public static IReadOnlyList<PipeMaterialPart> Read(Document document, IReadOnlyCollection<ElementId>? exclude = null)
        => AddinInstances(document)
            .Where(x => exclude is not { Count: > 0 } || !exclude.Contains(x.Id))
            .Select(x =>
            {
                var imageId = x.Symbol?.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_IMAGE)?.AsElementId();
                var imageType = imageId is not null && imageId != ElementId.InvalidElementId
                    ? document.GetElement(imageId) as ImageType : null;
                var imagePath = imageType?.Path;
                if (imageType?.PathType == PathType.Relative && !string.IsNullOrWhiteSpace(document.PathName))
                    imagePath = Path.GetFullPath(imagePath!, Path.GetDirectoryName(document.PathName)!);
                return new PipeMaterialPart(
                    Get(x, ValvePipingParameters.JunctionId)?.AsString() ?? string.Empty,
                    Get(x, ValvePipingParameters.ItemNumber)?.AsInteger() ?? 0,
                    Get(x, ValvePipingParameters.ItemName)?.AsString() ?? string.Empty,
                    Get(x, ValvePipingParameters.ItemSpec)?.AsString() ?? string.Empty,
                    Get(x, ValvePipingParameters.Remark)?.AsString() ?? string.Empty,
                    x.Symbol?.Id.Value ?? 0, imagePath);
            })
            .ToList();

    /// <summary>이름 조회는 요소 파라미터 집합을 선형 탐색한다. GUID 조회가 빠르고 동명 외부 파라미터와도 섞이지 않는다(docs/208 § D-2).
    /// GUID가 다른 옛 바인딩을 위해 이름 조회로 폴백한다.</summary>
    private static Parameter? Get(Element element, string name)
        => (ValvePipingParameters.Guids.TryGetValue(name, out var guid) ? element.get_Parameter(guid) : null)
           ?? element.LookupParameter(name);
}
