using Autodesk.Revit.DB;
using DHBIMWATER.Core.Gis;

namespace DHBIMWATER.Infrastructure.Helpers;

internal static class PipeSolidFactory
{
    private const string DcipMaterialName = "DCIP";
    private const string SteelMaterialName = "SP";

    /// <summary>관종별 Revit 재질(docs/212 § B). 주철관 "DCIP", 강관 "SP". 그 외 관종은 규격 자료가 없어 배치되지 않지만 방어적으로 코드명을 쓴다.</summary>
    internal static ElementId FindOrCreateMaterial(Document doc, PipeMaterial material)
    {
        var name = material switch
        {
            PipeMaterial.Steel => SteelMaterialName,
            PipeMaterial.DuctileIron => DcipMaterialName,
            _ => PipeAlignmentParameters.Code(material)
        };
        var existing = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>()
            .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        return existing?.Id ?? Material.Create(doc, name);
    }

    /// <summary>등급 문자열 → 재질 ElementId. 관종 종류 수(최대 2)만큼만 재질을 조회한다. 등급을 모르면 주철관으로 본다.</summary>
    internal static ElementId MaterialIdFor(Document doc, IDictionary<PipeMaterial, ElementId> cache, string? pipeKind)
    {
        var material = PipeKindCatalog.MaterialOf(pipeKind) ?? PipeMaterial.DuctileIron;
        if (!cache.TryGetValue(material, out var id)) cache[material] = id = FindOrCreateMaterial(doc, material);
        return id;
    }

    internal static CurveLoop CircleLoop(XYZ center, double radiusFt, XYZ xAxis, XYZ yAxis)
    {
        var loop = new CurveLoop();
        loop.Append(Arc.Create(center, radiusFt, 0, Math.PI, xAxis, yAxis));
        loop.Append(Arc.Create(center, radiusFt, Math.PI, 2 * Math.PI, xAxis, yAxis));
        return loop;
    }
}
