using Autodesk.Revit.DB;
using DHBIMWATER.Core.Structures;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling;

/// <summary>
/// 콘크리트 재료 찾기·만들기 공용 로직(docs/248 § A, docs/250 § A). 벽·바닥·보·기둥(<see cref="RevitElementTypeCommandRepo"/>)과
/// 계단(<see cref="RevitStairCommandRepo"/>)이 같이 쓴다. Transaction은 열지 않는다 — 호출부 트랜잭션 안에서 부른다.
/// </summary>
internal static class ConcreteMaterialFactory
{
    // 복제 원본에서 제외할 조적 재료·유형 이름 키워드(대소문자 무시).
    private static readonly string[] MasonryKeywords = { "블록", "블럭", "벽돌", "조적", "masonry", "block", "brick", "cmu" };

    /// <summary>이름("{골재}-{강도}-{슬럼프}")이 같은 재료가 있으면 강도만 맞춰 재사용하고, 없으면 새로 만든다. 실패하면 InvalidElementId.</summary>
    public static ElementId FindOrCreate(Document doc, ConcreteSpec concrete)
    {
        var allMaterials = new FilteredElementCollector(doc)
            .OfClass(typeof(Material))
            .Cast<Material>()
            .ToList();

        var strength = UnitUtils.ConvertToInternalUnits(concrete.CompressiveStrength, UnitTypeId.Megapascals);

        var existing = allMaterials.FirstOrDefault(m =>
            m.Name.Equals(concrete.MaterialName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            // 이전(구조자산 미반영) 버전에서 생성돼 남아있는 재료일 수 있으므로, 강도가 다르면 갱신.
            // 패턴은 건드리지 않는다 — 기존 재료는 그대로 둔다는 사용자 결정(docs/250 § 1).
            ApplyConcreteCompression(doc, existing, concrete.MaterialName, strength);
            return existing.Id;
        }

        // 이름만 보고 첫 재료를 고르면 "콘크리트 블록/벽돌" 같은 조적 재료가 원본이 돼
        // 벽돌 패턴·조적 구조자산이 복제됐다(docs/248 § A). 조적 키워드를 빼고 구조자산 Concrete를 우선한다.
        var candidates = allMaterials.Where(m => !IsMasonryName(m.Name)).ToList();
        var baseMaterial =
            candidates.FirstOrDefault(m => HasConcreteAsset(doc, m) && HasConcreteName(m.Name)) ??
            candidates.FirstOrDefault(m => HasConcreteAsset(doc, m)) ??
            candidates.FirstOrDefault(m => HasConcreteName(m.Name));

        if (baseMaterial == null)
            return CreateConcreteMaterial(doc, concrete.MaterialName, strength);

        if (baseMaterial.Duplicate(concrete.MaterialName) is not Material newMat)
            return ElementId.InvalidElementId;
        // 원본이 무엇이든 블록 등 패턴이 따라오지 않게 비운다(docs/250 § B).
        ClearPatterns(newMat);
        newMat.get_Parameter(BuiltInParameter.PHY_MATERIAL_PARAM_CONCRETE_COMPRESSION)?.Set(strength);
        ApplyConcreteCompression(doc, newMat, concrete.MaterialName, strength);

        return newMat.Id;
    }

    public static bool IsMasonryName(string name) =>
        MasonryKeywords.Any(k => name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);

    public static bool HasConcreteName(string name) =>
        name.IndexOf("concrete", StringComparison.OrdinalIgnoreCase) >= 0 || name.Contains("콘크리트");

    public static bool HasConcreteAsset(Document doc, Material material) =>
        doc.GetElement(material.StructuralAssetId) is PropertySetElement pse &&
        pse.GetStructuralAsset()?.StructuralAssetClass == StructuralAssetClass.Concrete;

    public static bool IsConcreteMaterial(Document doc, Material material) =>
        !IsMasonryName(material.Name) && (HasConcreteAsset(doc, material) || HasConcreteName(material.Name));

    /// <summary>표면 전경·배경, 절단 전경·배경 패턴을 모두 "없음"으로(docs/250 § B).</summary>
    private static void ClearPatterns(Material material)
    {
        material.SurfaceForegroundPatternId = ElementId.InvalidElementId;
        material.SurfaceBackgroundPatternId = ElementId.InvalidElementId;
        material.CutForegroundPatternId = ElementId.InvalidElementId;
        material.CutBackgroundPatternId = ElementId.InvalidElementId;
    }

    // 복제할 콘크리트 재료가 없을 때: 빈 재료를 만들고 Concrete 구조자산을 붙인다(docs/248 § A).
    private static ElementId CreateConcreteMaterial(Document doc, string materialName, double strengthInternal)
    {
        if (doc.GetElement(Material.Create(doc, materialName)) is not Material material)
            return ElementId.InvalidElementId;
        ClearPatterns(material);
        material.MaterialClass = "콘크리트";

        var asset = new StructuralAsset($"{materialName} - 구조자산", StructuralAssetClass.Concrete)
        {
            ConcreteCompression = strengthInternal,
        };
        material.StructuralAssetId = PropertySetElement.Create(doc, asset).Id;

        return material.Id;
    }

    // 재료 이름뿐 아니라 구조자산(StructuralAsset)에도 강도 반영.
    // Duplicate() 직후에는 원본과 구조자산(PropertySetElement)을 공유하므로,
    // 값이 다를 때만 별도 자산으로 복제해서 재료에 연결한다(공유 중인 원본/라이브러리 자산을 직접 수정하지 않음).
    // ConcreteCompression은 StructuralAssetClass가 Concrete인 자산에만 유효(그 외 설정 시 예외 발생).
    private static void ApplyConcreteCompression(Document doc, Material material, string materialName, double strengthInternal)
    {
        if (doc.GetElement(material.StructuralAssetId) is not PropertySetElement pse) return;

        var asset = pse.GetStructuralAsset();
        if (asset == null || asset.StructuralAssetClass != StructuralAssetClass.Concrete) return;
        if (Math.Abs(asset.ConcreteCompression - strengthInternal) < 1e-9) return;

        asset.Name = $"{materialName} - 구조자산";
        asset.ConcreteCompression = strengthInternal;
        var newPse = PropertySetElement.Create(doc, asset);
        material.StructuralAssetId = newPse.Id;
    }
}
