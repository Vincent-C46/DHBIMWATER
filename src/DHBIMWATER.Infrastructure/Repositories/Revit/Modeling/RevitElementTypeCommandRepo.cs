using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Structures;
using System;
using System.Linq;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling
{
    internal class RevitElementTypeCommandRepo : IElementTypeCommandRepo
    {
        private readonly Func<Document?> _doc;

        public RevitElementTypeCommandRepo(Func<Document?> doc)
        {
            _doc = doc;
        }

        // 재료 찾기·만들기는 계단과 같이 쓰도록 ConcreteMaterialFactory로 옮겼다(docs/250 § A).
        public int FindOrCreateConcreteMaterial(ConcreteSpec concrete)
        {
            var doc = _doc();
            if (doc == null) return 0;
            var id = ConcreteMaterialFactory.FindOrCreate(doc, concrete);
            return id == ElementId.InvalidElementId ? 0 : (int)id.Value;
        }

        public int FindOrCreateSlabType(FloorTypeSpec spec, int materialId)
        {
            var doc = _doc();
            if (doc == null) return 0;

            var name = spec.Name;

            var floorType = new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .FirstOrDefault(ft => ft.Name == name);

            if (floorType == null)
            {
                var baseFloorType = new FilteredElementCollector(doc)
                    .OfClass(typeof(FloorType))
                    .Cast<FloorType>()
                    .FirstOrDefault(ft => ft.GetCompoundStructure() != null);

                if (baseFloorType == null)
                {
                    TaskDialog.Show("Error", "적절한 복제 대상 FloorType이 없습니다");
                    return 0;
                }

                floorType = baseFloorType.Duplicate(name) as FloorType;
                if (floorType == null) return 0;
            }

            var cs = floorType.GetCompoundStructure();
            var structureLayer = new CompoundStructureLayer(UC.MmToFt(spec.Thickness), MaterialFunctionAssignment.Structure, new ElementId((long)materialId));
            cs.SetLayers(new List<CompoundStructureLayer> { structureLayer });
            cs.SetNumberOfShellLayers(ShellLayerType.Exterior, 0);
            cs.SetNumberOfShellLayers(ShellLayerType.Interior, 0);
            floorType.SetCompoundStructure(cs);

            return (int)floorType.Id.Value;
        }
        public int FindOrCreateWallType(WallTypeSpec spec, int materialId)
        {
            var doc = _doc();
            if (doc == null) return 0;

            var name = spec.Name;

            var wallType = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .FirstOrDefault(wt => wt.Name == name);

            if (wallType == null)
            {
                // 첫 복합 구조 유형을 쓰면 벽돌·조적 벽이 원본이 될 수 있다(docs/248 § B).
                // 기본 벽 중 콘크리트 구조 레이어 → 조적 이름 아님 → 아무 기본 벽 순으로 고른다.
                var basicWallTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .Where(wt => wt.Kind == WallKind.Basic && wt.GetCompoundStructure() != null)
                    .ToList();
                var baseWallType =
                    basicWallTypes.FirstOrDefault(wt => !ConcreteMaterialFactory.IsMasonryName(wt.Name) && HasConcreteStructureLayer(doc, wt)) ??
                    basicWallTypes.FirstOrDefault(wt => !ConcreteMaterialFactory.IsMasonryName(wt.Name)) ??
                    basicWallTypes.FirstOrDefault();

                if (baseWallType == null)
                {
                    TaskDialog.Show("Error", "적절한 복제 대상 WallType이 없습니다");
                    return 0;
                }

                wallType = baseWallType.Duplicate(name) as WallType;
                if (wallType == null) return 0;

                // 원본의 개략 축척 채우기 패턴(벽돌 해치 등)을 물려받지 않도록 새 복제분만 비운다.
                var coarseFill = wallType.get_Parameter(BuiltInParameter.COARSE_SCALE_FILL_PATTERN_ID_PARAM);
                if (coarseFill != null && !coarseFill.IsReadOnly)
                    coarseFill.Set(ElementId.InvalidElementId);
            }

            var cs = wallType.GetCompoundStructure();
            var structureLayer = new CompoundStructureLayer(UC.MmToFt(spec.Thickness), MaterialFunctionAssignment.Structure, new ElementId((long)materialId));
            cs.SetLayers(new List<CompoundStructureLayer> { structureLayer });
            cs.SetNumberOfShellLayers(ShellLayerType.Exterior, 0);
            cs.SetNumberOfShellLayers(ShellLayerType.Interior, 0);
            wallType.SetCompoundStructure(cs);

            return (int)wallType.Id.Value;
        }

        // 구조 레이어(없으면 첫 레이어)의 재료가 콘크리트인지.
        private static bool HasConcreteStructureLayer(Document doc, WallType wallType)
        {
            var cs = wallType.GetCompoundStructure();
            if (cs == null || cs.LayerCount == 0) return false;
            var index = cs.StructuralMaterialIndex >= 0 ? cs.StructuralMaterialIndex : 0;
            return doc.GetElement(cs.GetMaterialId(index)) is Material material && ConcreteMaterialFactory.IsConcreteMaterial(doc, material);
        }

        public int FindOrCreateBeamType(BeamTypeSpec spec)
        {
            var doc = _doc();
            if (doc == null) return 0;

            var allBeamTypes = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .OfClass(typeof(FamilySymbol))
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .Where(fs => fs.Family.StructuralMaterialType == StructuralMaterialType.Concrete)
                .ToList();

            // Find
            var existing = allBeamTypes.FirstOrDefault(b => b.Name.Contains(spec.Name));
            if (existing != null) return (int)existing.Id.Value;

            //Create
            var baseType = allBeamTypes.FirstOrDefault(fs => fs.LookupParameter("b") != null && fs.LookupParameter("h") != null);

            if (baseType == null) {
                TaskDialog.Show("Error", "적절한 복제 대상 Beam type이 없습니다.");
                return 0;
            }

            var newType = baseType.Duplicate(spec.Name) as FamilySymbol;
            newType.LookupParameter("b")?.Set(UC.MmToFt(spec.Width));
            newType.LookupParameter("h")?.Set(UC.MmToFt(spec.Height));

            if (!newType.IsActive)
            {
                newType.Activate();
                doc.Regenerate();
            }

            return (int)newType.Id.Value;
        }

        public int FindBeamSymbol(string typeName) => FindFamilySymbol(BuiltInCategory.OST_StructuralFraming, fs => fs.Name == typeName);

        /// <summary>"패밀리명 : 타입명"으로 구조 프레임 유형을 찾는다. 헌치 콤보가 패밀리명까지 담게 되면서(docs/156 § D-1)
        /// 타입명 완전일치인 <see cref="FindBeamSymbol"/>로는 매칭되지 않기 때문이다.
        /// <para>구분자 파싱 규칙은 RevitElementTypeQueryRepo.GetFamilyTypeCategoryId와 같은 LastIndexOf(" : ")다 —
        /// 타입명 자체에 " : "가 들어갈 수 있다. 구분자가 없으면 예전 타입명 단독 값으로 보고 FindBeamSymbol로 넘긴다.</para></summary>
        public int FindStructuralFramingSymbolByFamilyType(string familyTypeName)
        {
            if (string.IsNullOrWhiteSpace(familyTypeName)) return 0;
            var separator = familyTypeName.LastIndexOf(" : ", StringComparison.Ordinal);
            if (separator <= 0 || separator >= familyTypeName.Length - 3) return FindBeamSymbol(familyTypeName);
            var familyName = familyTypeName.Substring(0, separator);
            var typeName = familyTypeName.Substring(separator + 3);
            return FindFamilySymbol(BuiltInCategory.OST_StructuralFraming, fs => fs.FamilyName == familyName && fs.Name == typeName);
        }

        public int FindHaunchBeamSymbol() => FindFamilySymbol(BuiltInCategory.OST_StructuralFraming,
            fs => fs.Name.Contains("헌치") || fs.Name.Contains("haunch") || fs.FamilyName.Contains("헌치") || fs.FamilyName.Contains("haunch"));

        public int FindColumnSymbol(string typeName) => FindFamilySymbol(BuiltInCategory.OST_StructuralColumns, fs => fs.Name == typeName);

        /// <summary>기둥 콤보가 패밀리명까지 담게 되면서(docs/159 § B-1) 타입명 완전일치인 <see cref="FindColumnSymbol"/>로는
        /// 매칭되지 않기 때문이다. 구분자 파싱 규칙은 <see cref="FindStructuralFramingSymbolByFamilyType"/>과 같다.</summary>
        public int FindColumnSymbolByFamilyType(string familyTypeName)
        {
            if (string.IsNullOrWhiteSpace(familyTypeName)) return 0;
            var separator = familyTypeName.LastIndexOf(" : ", StringComparison.Ordinal);
            if (separator <= 0 || separator >= familyTypeName.Length - 3) return FindColumnSymbol(familyTypeName);
            var familyName = familyTypeName.Substring(0, separator);
            var typeName = familyTypeName.Substring(separator + 3);
            return FindFamilySymbol(BuiltInCategory.OST_StructuralColumns, fs => fs.FamilyName == familyName && fs.Name == typeName);
        }

        public int FindGenericModelSymbol(string symbolName) => FindFamilySymbol(BuiltInCategory.OST_GenericModel, fs => fs.Name.Contains(symbolName));

        // 보·기둥·독립기초 유형의 구조 재료 지정(docs/249 § C). 인스턴스 Id를 받으면 그 유형에 적용한다.
        // 유형 매개변수라 같은 유형을 쓰는 문서 전체 인스턴스가 바뀐다 — 사용자 결정(docs/249 § 0).
        public bool ApplyStructuralMaterial(int elementOrTypeId, int materialId)
        {
            var doc = _doc();
            if (doc == null || elementOrTypeId == 0 || materialId == 0) return false;

            var element = doc.GetElement(new ElementId((long)elementOrTypeId));
            var type = element is FamilyInstance instance ? instance.Symbol : element as ElementType;
            var param = type?.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);
            if (param == null || param.IsReadOnly || param.StorageType != StorageType.ElementId)
            {
                Logging.LogManager.Logger.Warn($"CONCRETE_MATERIAL 구조 재료를 지정할 수 없습니다: 유형 '{type?.Name ?? elementOrTypeId.ToString()}'");
                return false;
            }

            var materialElementId = new ElementId((long)materialId);
            if (param.AsElementId() != materialElementId)
                param.Set(materialElementId);
            return true;
        }

        private int FindFamilySymbol(BuiltInCategory category, Func<FamilySymbol, bool> predicate)
        {
            var doc = _doc();
            if (doc == null) return 0;
            return new FilteredElementCollector(doc)
                .OfCategory(category)
                .WhereElementIsElementType()
                .OfType<FamilySymbol>()
                .FirstOrDefault(predicate)?.Id.Value is long id ? (int)id : 0;
        }
    }
}
