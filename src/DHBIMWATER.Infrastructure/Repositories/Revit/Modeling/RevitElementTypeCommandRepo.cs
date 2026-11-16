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

            // 콘크리트 보만. 헌치 패밀리도 콘크리트 프레임이라 찾기·복제 원본 둘 다에서 뺌 — 안 빼면 보가 헌치 단면으로 생김(docs/325 § C)
            var allBeamTypes = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .OfClass(typeof(FamilySymbol))
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .Where(fs => fs.Family.StructuralMaterialType == StructuralMaterialType.Concrete && !IsHaunchFamilyOrType(fs))
                .ToList();

            // Find. 이름 정확히 일치 — 부분일치면 "600 x 800"이 "1600 x 800"을 잡음.
            // 같은 유형 이름이 여러 패밀리에 있을 수 있어 사각형 보 패밀리 것 우선
            // 크기 매개변수(b·h) 있는 것만 — 이름만 같고 b·h 없는 유형(다른 패밀리)은 크기 못 맞춤 → 아래에서 새로 복제
            var existing = allBeamTypes.Where(b => b.Name == spec.Name && HasSizeParams(b, BeamWidthParam, BeamHeightParam))
                .OrderByDescending(IsRectangularFamily).FirstOrDefault();
            if (existing != null)
            {
                // 이름은 "폭 x 높이"인데 실제 b·h가 다른 유형(예전 코드·수동 수정) → 이름대로 고침. 안 고치면 GB1·GH1 바꿔도 모델 크기 그대로
                SyncSize(existing, BeamWidthParam, spec.Width, BeamHeightParam, spec.Height);
                return (int)existing.Id.Value;
            }

            //Create. 콘크리트 사각형 보(b·h 유형 매개변수) 복제 후 b·h 수정. 패밀리 이름에 "사각형"/"Rectangular" 든 것 우선
            var baseType = PickSizedBase(allBeamTypes, BeamWidthParam, BeamHeightParam);

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

        /// <summary>기둥 유형 찾기·만들기(docs/325 § C). 이름 정확히 일치로 찾고, 없으면 콘크리트 사각형 기둥(b·h) 복제 후 b·h 수정.
        /// 원본 없으면 0 — UseCase가 생성 중단.</summary>
        public int FindOrCreateColumnType(ColumnTypeSpec spec)
        {
            var doc = _doc();
            if (doc == null) return 0;

            var candidates = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .OfClass(typeof(FamilySymbol))
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .Where(fs => fs.Family.StructuralMaterialType == StructuralMaterialType.Concrete)
                .ToList();

            var existing = candidates.FirstOrDefault(fs => fs.Name == spec.Name);
            if (existing != null) return (int)existing.Id.Value;

            var baseType = PickSizedBase(candidates, ColumnWidthParam, ColumnDepthParam);
            return baseType == null ? 0 : DuplicateSized(doc, baseType, spec.Name, ColumnWidthParam, spec.B, ColumnDepthParam, spec.H);
        }

        /// <summary>헌치 유형 찾기·만들기(docs/325 § C). 후보 = 패밀리 이름에 "헌치"/"haunch"(대소문자 무시) 든 구조 프레임.
        /// 이름 정확히 일치로 찾고, 없으면 크기 매개변수 있는 첫 헌치 유형 복제 후 크기 수정. 원본 없으면 0.</summary>
        public int FindOrCreateHaunchType(BeamTypeSpec spec)
        {
            var doc = _doc();
            if (doc == null) return 0;

            var candidates = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .OfClass(typeof(FamilySymbol))
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .Where(IsHaunchFamily)
                .ToList();

            var existing = candidates.FirstOrDefault(fs => fs.Name == spec.Name);
            if (existing != null) return (int)existing.Id.Value;

            var baseType = candidates.FirstOrDefault(fs => fs.LookupParameter(HaunchWidthParam) != null && fs.LookupParameter(HaunchHeightParam) != null);
            return baseType == null ? 0 : DuplicateSized(doc, baseType, spec.Name, HaunchWidthParam, spec.Width, HaunchHeightParam, spec.Height);
        }

        // 크기 매개변수 이름(유형 매개변수). 헌치는 패밀리 실제 이름 확인 필요 — 지금은 보와 같은 b·h 가정(docs/325 § C-2)
        private const string BeamWidthParam = "b";
        private const string BeamHeightParam = "h";
        private const string ColumnWidthParam = "b";
        private const string ColumnDepthParam = "h";
        private const string HaunchWidthParam = "b";
        private const string HaunchHeightParam = "h";

        /// <summary>패밀리 이름에 "헌치" 또는 "haunch"(대소문자 무시) 포함. 헌치 복제 후보 고를 때 씀.
        /// FamilyName과 Family.Name 둘 다 봄(같은 값이지만 한쪽이 비는 경우 대비).</summary>
        private static bool IsHaunchFamily(FamilySymbol fs)
            => HasHaunchWord(fs.FamilyName) || HasHaunchWord(fs.Family?.Name);

        /// <summary>보 찾기·복제 원본에서 뺄 대상. 패밀리 이름 또는 유형 이름에 "헌치"/"haunch" —
        /// 예전 코드가 헌치 패밀리에 "500 x 700" 같은 보 이름 유형을 만들었을 수 있어 패밀리 이름이 기준, 유형 이름은 추가 안전망.</summary>
        private static bool IsHaunchFamilyOrType(FamilySymbol fs)
            => IsHaunchFamily(fs) || HasHaunchWord(fs.Name);

        private static bool HasHaunchWord(string? name)
            => !string.IsNullOrEmpty(name) && (name!.Contains("헌치") || name.IndexOf("haunch", StringComparison.OrdinalIgnoreCase) >= 0);

        private static bool IsRectangularFamily(FamilySymbol fs)
            => fs.FamilyName.Contains("사각형") || fs.FamilyName.IndexOf("rectangular", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>크기 매개변수 둘 다 있는 복제 원본. 패밀리 이름에 "사각형"/"Rectangular" 든 것 우선, 없으면 첫 후보.</summary>
        private static FamilySymbol? PickSizedBase(System.Collections.Generic.IEnumerable<FamilySymbol> candidates, string widthParam, string heightParam)
        {
            var sized = candidates.Where(fs => fs.LookupParameter(widthParam) != null && fs.LookupParameter(heightParam) != null).ToList();
            return sized.FirstOrDefault(IsRectangularFamily) ?? sized.FirstOrDefault();
        }

        private static bool HasSizeParams(FamilySymbol fs, string widthParam, string heightParam)
            => fs.LookupParameter(widthParam) != null && fs.LookupParameter(heightParam) != null;

        /// <summary>크기 매개변수가 지정값(mm)과 0.5mm 넘게 다르면 지정값으로 씀. 쓰기 막힌 매개변수는 건너뜀.</summary>
        private static void SyncSize(FamilySymbol fs, string widthParam, double width, string heightParam, double height)
        {
            Sync(fs.LookupParameter(widthParam), width);
            Sync(fs.LookupParameter(heightParam), height);

            static void Sync(Parameter? parameter, double mm)
            {
                if (parameter is null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double) return;
                if (Math.Abs(UC.FtToMm(parameter.AsDouble()) - mm) > 0.5) parameter.Set(UC.MmToFt(mm));
            }
        }

        /// <summary>유형 복제 후 두 크기 매개변수(mm) 씀. 같은 이름은 찾기 단계에서 걸러져 Duplicate 이름 충돌 없음.</summary>
        private static int DuplicateSized(Document doc, FamilySymbol baseType, string name, string widthParam, double width, string heightParam, double height)
        {
            if (baseType.Duplicate(name) is not FamilySymbol newType) return 0;
            newType.LookupParameter(widthParam)?.Set(UC.MmToFt(width));
            newType.LookupParameter(heightParam)?.Set(UC.MmToFt(height));
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
