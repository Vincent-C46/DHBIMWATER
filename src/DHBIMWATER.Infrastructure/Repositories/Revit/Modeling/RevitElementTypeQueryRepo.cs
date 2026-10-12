using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation.Provider;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling
{
    internal class RevitElementTypeQueryRepo : IElementTypeQueryRepo
    {
        private readonly Func<Document?> _docProvider;

        public RevitElementTypeQueryRepo(Func<Document?> docProvider)
        {
            _docProvider = docProvider ?? throw new ArgumentNullException(nameof(docProvider));
        }
        public IEnumerable<string> GetBeamTypeNames()
        {
            var doc = _docProvider();
            if (doc is null) return Enumerable.Empty<string>();

            try
            {
                var beamTypes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsElementType()
                    .Cast<FamilySymbol>();

                var rcBeamTypes = beamTypes.Where(fs => IsNotSteelMaterial(doc, fs))
                    .Select(fs => fs.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Distinct()
                    .OrderBy(n => n);

                return rcBeamTypes;
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        }

        /// <summary>구조 재료가 콘크리트인 보 유형명. 판정 기준은 RevitElementTypeCommandRepo.FindOrCreateBeamType과 같은
        /// Family.StructuralMaterialType이다(docs/156 § B-2).
        /// <para>결과를 <see cref="GetBeamTypeNames"/>로 한 번 더 거르는 이유: 그쪽은 재료명 기반 스틸 배제 필터라 기준이 다르다.
        /// 콤보 목록에 없는 이름이 기본 선택으로 들어가면 ComboBox가 빈칸이 되므로 부분집합을 강제한다.</para></summary>
        public IEnumerable<string> GetConcreteBeamTypeNames()
        {
            var doc = _docProvider();
            if (doc is null) return Enumerable.Empty<string>();

            try
            {
                var listed = new HashSet<string>(GetBeamTypeNames(), StringComparer.Ordinal);
                return ConcreteSymbolNames(doc, BuiltInCategory.OST_StructuralFraming).Where(listed.Contains).ToList();
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        }

        /// <summary>구조 재료가 콘크리트인 기둥 유형명. <see cref="GetColumnTypeNames"/>는 재료를 가리지 않으므로
        /// 여기서 걸러낸 결과가 그 부분집합이 된다(docs/156 § B-2).</summary>
        public IEnumerable<string> GetConcreteColumnTypeNames()
        {
            var doc = _docProvider();
            if (doc is null) return Enumerable.Empty<string>();

            try { return ConcreteSymbolNames(doc, BuiltInCategory.OST_StructuralColumns).ToList(); }
            catch { return Enumerable.Empty<string>(); }
        }

        /// <summary>유형에 지정된 구조 재료 이름이 스틸이 아닌지. 재료가 지정돼 있지 않으면 true다.
        /// <para>패밀리 구조재료 유형(<c>Family.StructuralMaterialType</c>)과는 별개 기준이라 둘을 함께 봐야 한다 —
        /// 패밀리가 미지정이어도 유형 재료가 강철일 수 있다(docs/156 § D-1).</para></summary>
        private static bool IsNotSteelMaterial(Document doc, FamilySymbol fs)
        {
            var matParam = fs.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);
            if (matParam == null) return true;
            if (matParam.AsElementId() == ElementId.InvalidElementId) return true;
            var mat = doc.GetElement(matParam.AsElementId()) as Material;
            if (mat != null && (mat.Name.Contains("강철") || mat.Name.Contains("스틸") || mat.Name.Contains("steel")))
                return false;
            return true;
        }

        /// <summary>카테고리 안에서 패밀리 구조 재료가 콘크리트인 유형명. 정렬·중복 제거 규칙은 GetColumnTypeNames와 같다.
        /// Cast가 아니라 OfType을 쓰는 이유는 GetFamilySymbolNames(아래 주석)와 같다.</summary>
        private static IEnumerable<string> ConcreteSymbolNames(Document doc, BuiltInCategory category)
            => new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .WhereElementIsElementType()
                .OfType<FamilySymbol>()
                .Where(fs => fs.Category != null && fs.Category.Id.Value == (int)category)
                .Where(fs => fs.Family?.StructuralMaterialType == StructuralMaterialType.Concrete)
                .Select(fs => fs.Name)
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .OrderBy(n => n);

        public IEnumerable<string> GetAdaptiveComponentTypeNames()
        {
            var doc = _docProvider(); if (doc is null) return Enumerable.Empty<string>();
            try { return new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().Where(x => AdaptiveComponentFamilyUtils.IsAdaptiveComponentFamily(x.Family)).Select(x => $"{x.Family.Name} : {x.Name}").Distinct().OrderBy(x => x).ToList(); }
            catch { return Enumerable.Empty<string>(); }
        }
        /// <summary>선택된 패밀리 유형의 실제 카테고리. 적응형 패밀리는 일반 모델이 아닐 수 있어
        /// 공유 매개변수 바인딩 대상을 정적 목록이 아니라 여기서 실측한다(docs/141).</summary>
        public int? GetFamilyTypeCategoryId(string familyTypeName)
        {
            var doc = _docProvider();
            if (doc is null || string.IsNullOrWhiteSpace(familyTypeName)) return null;
            var separator = familyTypeName.LastIndexOf(" : ", StringComparison.Ordinal);
            if (separator <= 0 || separator >= familyTypeName.Length - 3) return null;
            var familyName = familyTypeName.Substring(0, separator);
            var typeName = familyTypeName.Substring(separator + 3);
            try
            {
                var symbol = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                    .FirstOrDefault(x => x.Family.Name == familyName && x.Name == typeName);
                return symbol?.Category is { } category ? (int)category.Id.Value : null;
            }
            catch { return null; }
        }

        // 패밀리 문서에서 읽은 인스턴스 파라미터명(전체·실수형)과 Adaptive Point 개수는 FamilyParameterCache(정적, 문서 단위)에 둔다(docs/258 § A).
        // 종전에는 이 인스턴스 필드였고 repo가 Transient라 창을 열 때마다 EditFamily를 9~12회 다시 했다.
        // 키는 그대로 "문서Hash|패밀리명"이고, 패밀리 재로드·문서 닫기 이벤트와 InvalidateFamilyCaches가 비운다.

        // Adaptive Point 개수도 패밀리 단위 값이라 같은 공유 캐시에 둔다. 파라미터명과 같은 EditFamily 한 번에 채운다(docs/226 § B).
        // 모델리스 창(밸브실)은 창이 떠 있는 동안 패밀리가 재로드될 수 있다 — 재로드 이벤트가 그 패밀리 항목을 지우고,
        // 명시적 새로고침은 InvalidateFamilyCaches가 전부 비운다(docs/154 § A, docs/258 § A).

        /// <summary>
        /// 가변 패밀리 인스턴스에 쓸 수 있는 파라미터명.
        /// 인스턴스 전용 파라미터는 FamilySymbol에서 조회되지 않아 다음 순서로 근거를 고른다(docs/138 § C).
        /// ① 패밀리 문서를 열어 FamilyManager의 인스턴스 파라미터를 읽고 캐시한다 — 패밀리가 실제로 가진 이름만 나온다
        /// ② 패밀리 문서를 열 수 없거나(인플레이스 등) 인스턴스 파라미터가 없으면 같은 <b>패밀리</b>의 배치된 인스턴스
        ///    (파라미터 집합은 유형이 아니라 패밀리 단위라 형제 유형도 유효하다)
        /// ③ 인스턴스도 하나 없으면 유형 파라미터
        /// <para>①을 1순위로 둔 이유: 배치된 인스턴스의 <c>Parameters</c>에는 프로젝트가 카테고리에 바인딩한
        /// 공유 매개변수("O0201 - 관경", "허용굴곡각도" 등)까지 섞여 들어와, 패밀리가 갖고 있지도 않은 이름이
        /// 후보 목록과 자동추정 결과를 차지한다.</para>
        /// </summary>
        public IEnumerable<string> GetAdaptiveInstanceParameterNames(string familyTypeName)
        {
            var doc = _docProvider();
            if (doc is null || string.IsNullOrWhiteSpace(familyTypeName)) return Enumerable.Empty<string>();
            var separator = familyTypeName.LastIndexOf(" : ", StringComparison.Ordinal);
            if (separator <= 0 || separator >= familyTypeName.Length - 3) return Enumerable.Empty<string>();
            var familyName = familyTypeName.Substring(0, separator);
            var typeName = familyTypeName.Substring(separator + 3);
            try
            {
                // 유형 조회 사전(docs/258 § C) — 조회마다 FamilySymbol 전체를 훑지 않는다.
                var symbol = FamilyParameterCache.FindSymbol(doc, familyName, typeName);
                if (symbol is null) return Enumerable.Empty<string>();

                // 패밀리 문서를 못 열면(인플레이스 등) 빈 목록이 오므로 그대로 아래 폴백으로 내려간다.
                var familyParameterNames = GetFamilyInstanceParameterNames(doc, symbol.Family);
                if (familyParameterNames.Count > 0) return familyParameterNames.OrderBy(n => n).ToList();

                // 폴백: 배치된 인스턴스 → 유형. 유형 파라미터만으로는 OD·두께 같은 인스턴스 전용 항목이 빠진다.
                var sampleInstance = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                    .FirstOrDefault(x => x.Symbol.Family.Id == symbol.Family.Id);
                return NamesOf(sampleInstance ?? (Element)symbol);
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        }

        private static List<string> NamesOf(Element source)
            => source.Parameters.Cast<Parameter>().Select(p => p.Definition?.Name).Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList()!;

        /// <summary>패밀리 문서를 열어 인스턴스 파라미터명을 읽는다. 실패(인플레이스·편집 불가)하면 빈 목록이다.</summary>
        private IReadOnlyList<string> GetFamilyInstanceParameterNames(Document doc, Family family)
        {
            if (FamilyParameterCache.TryGetNames(FamilyParameterCache.Key(doc, family.Name), out var cached)) return cached;
            return ReadFamilyDocument(doc, family).Names;
        }

        /// <summary>
        /// 패밀리 문서를 <b>한 번</b> 열어 인스턴스 파라미터명과 Adaptive Point 개수를 함께 읽고 두 캐시를 모두 채운다(docs/226 § B).
        /// 두 값을 따로 열면 관종 전환 한 번에 EditFamily가 4회(곡관·직관 × 2) 돌아 강관 첫 전환이 느렸다.
        /// 실패(인플레이스·편집 불가)하면 종전 두 함수의 catch 결과와 같이 빈 목록 · -1이다.
        /// </summary>
        private (IReadOnlyList<string> Names, int PointCount) ReadFamilyDocument(Document doc, Family family)
        {
            var key = FamilyParameterCache.Key(doc, family.Name);
            if (FamilyParameterCache.TryGetNames(key, out var cachedNames) && FamilyParameterCache.TryGetPointCount(key, out var cachedCount))
                return (cachedNames, cachedCount);

            IReadOnlyList<string> names = Array.Empty<string>();
            IReadOnlyList<string> numericNames = Array.Empty<string>();
            var count = -1;
            try
            {
                var familyDocument = doc.EditFamily(family);
                try
                {
                    var instanceParameters = familyDocument.FamilyManager.Parameters.Cast<FamilyParameter>().Where(p => p.IsInstance).ToList();
                    names = instanceParameters.Select(p => p.Definition?.Name)
                        .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()!;
                    // 실수형만 따로(docs/233 § B-1). 같은 문서 열기에서 함께 읽어 EditFamily를 늘리지 않는다.
                    numericNames = instanceParameters.Where(p => p.StorageType == StorageType.Double).Select(p => p.Definition?.Name)
                        .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()!;
                    count = new FilteredElementCollector(familyDocument).OfCategory(BuiltInCategory.OST_AdaptivePoints).WhereElementIsNotElementType().GetElementCount();
                }
                finally { familyDocument.Close(false); }
            }
            catch
            {
                // 실패(인플레이스·편집 불가·API 컨텍스트 밖)는 캐시하지 않는다 — 한 번 실패한 빈 목록이 다음 무효화까지
                // 남아 매개변수 자동 추정이 계속 틀어졌다(docs/238 § E). 다음 호출이 다시 시도한다.
                return (Array.Empty<string>(), -1);
            }

            FamilyParameterCache.SetFamilyDocument(key, names, numericNames, count);
            return (names, count);
        }
        public int GetAdaptiveBendPointCount(string familyTypeName)
        {
            var doc = _docProvider(); if (doc is null || string.IsNullOrWhiteSpace(familyTypeName)) return -1;
            var separator = familyTypeName.LastIndexOf(" : ", StringComparison.Ordinal); if (separator <= 0 || separator >= familyTypeName.Length - 3) return -1;
            var familyName = familyTypeName[..separator]; var typeName = familyTypeName[(separator + 3)..];

            // 점 개수는 패밀리 단위 값이라 형제 유형끼리 캐시를 공유한다(EditFamily 반복 방지).
            var key = FamilyParameterCache.Key(doc, familyName);
            if (FamilyParameterCache.TryGetPointCount(key, out var cached)) return cached;

            var count = -1;
            try
            {
                var symbol = FamilyParameterCache.FindSymbol(doc, familyName, typeName);   // docs/258 § C
                if (symbol is null) return -1; // 유형이 없는 경우는 "확인 불가"라 캐시하지 않는다(다른 유형명으로는 조회될 수 있다).
                // 파라미터명과 같은 EditFamily 한 번에 읽는다(docs/226 § B). ReadFamilyDocument가 두 캐시를 모두 채운다.
                count = ReadFamilyDocument(doc, symbol.Family).PointCount;
            }
            catch { count = -1; }

            FamilyParameterCache.SetPointCount(key, count);
            return count;
        }
        public IEnumerable<string> GetBeamInstanceParameterNames(string beamTypeName)
        {
            var doc = _docProvider();
            if (doc is null || string.IsNullOrWhiteSpace(beamTypeName)) return Enumerable.Empty<string>();

            try
            {
                var symbol = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming).WhereElementIsElementType()
                    .Cast<FamilySymbol>().FirstOrDefault(x => x.Name == beamTypeName);
                if (symbol is null) return Enumerable.Empty<string>();

                // 인스턴스 전용 파라미터는 유형 자체에서는 조회할 수 없어, 이미 배치된 인스턴스가 있으면 그걸 우선 쓴다.
                // TODO: 배치된 인스턴스가 하나도 없는 유형은 유형 파라미터만 후보로 보여준다(인스턴스 전용 파라미터 누락 가능).
                var sampleInstance = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming).WhereElementIsNotElementType()
                    .Cast<FamilyInstance>().FirstOrDefault(x => x.Symbol.Id == symbol.Id);
                var source = (Element?)sampleInstance ?? symbol;
                return source.Parameters.Cast<Parameter>().Select(p => p.Definition?.Name).Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList()!;
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        }
        public IEnumerable<string> GetPipingSystemTypeNames() => GetNames(typeof(PipingSystemType));
        public IEnumerable<string> GetPipeTypeNames() => GetNames(typeof(PipeType));
        public IEnumerable<string> GetLevelNames()
        {
            var doc = _docProvider(); if (doc is null) return Enumerable.Empty<string>();
            try { return new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(x => x.Elevation).Select(x => x.Name).ToList(); }
            catch { return Enumerable.Empty<string>(); }
        }
        public IReadOnlyList<LevelInfo> GetLevels()
        {
            var doc = _docProvider(); if (doc is null) return [];
            try
            {
                return new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(level => level.Elevation)
                    .Select(level => new LevelInfo(level.Id.Value, level.Name, UC.FtToMm(level.Elevation)))
                    .ToList();
            }
            catch { return []; }
        }
        public IEnumerable<string> GetPipeAccessoryTypeNames() => GetFamilySymbolNames(BuiltInCategory.OST_PipeAccessory);
        public IEnumerable<string> GetPipeFittingTypeNames() => GetFamilySymbolNames(BuiltInCategory.OST_PipeFitting);

        /// <summary>조회 로직은 카테고리와 무관하므로 Adaptive용 구현을 그대로 쓴다.
        /// 호출부 의도를 드러내려고 이름만 분리해 둔다(중복 구현하지 않는다).</summary>
        public IEnumerable<string> GetPipeAccessoryInstanceParameterNames(string familyTypeName)
            => GetAdaptiveInstanceParameterNames(familyTypeName);

        /// <summary>실수형(Double) 인스턴스 파라미터명만(docs/233 § B-1). 조회·폴백 순서는 <see cref="GetAdaptiveInstanceParameterNames"/>와 같다 —
        /// 패밀리 문서(같은 EditFamily에서 함께 채운 캐시) → 배치된 인스턴스 → 유형. 폴백은 StorageType으로 거른다.</summary>
        public IEnumerable<string> GetPipeAccessoryNumericInstanceParameterNames(string familyTypeName)
        {
            var doc = _docProvider();
            if (doc is null || string.IsNullOrWhiteSpace(familyTypeName)) return Enumerable.Empty<string>();
            var separator = familyTypeName.LastIndexOf(" : ", StringComparison.Ordinal);
            if (separator <= 0 || separator >= familyTypeName.Length - 3) return Enumerable.Empty<string>();
            var familyName = familyTypeName.Substring(0, separator);
            var typeName = familyTypeName.Substring(separator + 3);
            try
            {
                // 유형 조회 사전(docs/258 § C) — 조회마다 FamilySymbol 전체를 훑지 않는다.
                var symbol = FamilyParameterCache.FindSymbol(doc, familyName, typeName);
                if (symbol is null) return Enumerable.Empty<string>();

                // 이름 캐시가 비어 있지 않으면(패밀리 문서를 열 수 있었으면) 실수형 캐시도 같은 호출에서 채워져 있다.
                if (GetFamilyInstanceParameterNames(doc, symbol.Family).Count > 0
                    && FamilyParameterCache.TryGetNumericNames(FamilyParameterCache.Key(doc, symbol.Family.Name), out var numeric))
                    return numeric.OrderBy(n => n).ToList();

                var sampleInstance = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                    .FirstOrDefault(x => x.Symbol.Family.Id == symbol.Family.Id);
                return NumericNamesOf(sampleInstance ?? (Element)symbol);
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        }

        /// <summary>유형 매개변수 중 길이 계열(mm 표시 가능 스펙)만 이름·값(mm)으로(docs/233 § B-2). 캐시하지 않는다 — FamilySymbol 조회는 EditFamily와 달리 가볍다.
        /// <para>mm 스펙 판정은 RevitPipeSegmentCommandRepo.IsMillimeterSpec(:1355)과 같은 규칙이다(배치 경계가 "길이 계열"로 받아들이는 범위와 일치시킨다).</para></summary>
        public IReadOnlyList<DHBIMWATER.Application.DTOs.Revit.PipeAccessoryTypeParameterDto> GetPipeAccessoryTypeLengthParameters(string familyTypeName)
        {
            var doc = _docProvider();
            if (doc is null || string.IsNullOrWhiteSpace(familyTypeName)) return Array.Empty<DHBIMWATER.Application.DTOs.Revit.PipeAccessoryTypeParameterDto>();
            var separator = familyTypeName.LastIndexOf(" : ", StringComparison.Ordinal);
            if (separator <= 0 || separator >= familyTypeName.Length - 3) return Array.Empty<DHBIMWATER.Application.DTOs.Revit.PipeAccessoryTypeParameterDto>();
            var familyName = familyTypeName.Substring(0, separator);
            var typeName = familyTypeName.Substring(separator + 3);
            try
            {
                // 유형 조회 사전(docs/258 § C) — 조회마다 FamilySymbol 전체를 훑지 않는다.
                var symbol = FamilyParameterCache.FindSymbol(doc, familyName, typeName);
                if (symbol is null) return Array.Empty<DHBIMWATER.Application.DTOs.Revit.PipeAccessoryTypeParameterDto>();

                return symbol.Parameters.Cast<Parameter>()
                    .Where(p => p.StorageType == StorageType.Double && !string.IsNullOrWhiteSpace(p.Definition?.Name)
                                && IsMillimeterSpec(p.Definition.GetDataType()))
                    .GroupBy(p => p.Definition.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                    .OrderBy(p => p.Definition.Name)
                    .Select(p => new DHBIMWATER.Application.DTOs.Revit.PipeAccessoryTypeParameterDto(p.Definition.Name, UC.FtToMm(p.AsDouble())))
                    .ToList();
            }
            catch
            {
                return Array.Empty<DHBIMWATER.Application.DTOs.Revit.PipeAccessoryTypeParameterDto>();
            }
        }

        private static bool IsMillimeterSpec(ForgeTypeId dataType)
            => UnitUtils.IsMeasurableSpec(dataType)
               && UnitUtils.GetValidUnits(dataType).Any(x => x.Equals(UnitTypeId.Millimeters));

        private static List<string> NumericNamesOf(Element source)
            => source.Parameters.Cast<Parameter>().Where(p => p.StorageType == StorageType.Double).Select(p => p.Definition?.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList()!;
        public IEnumerable<string> GetGenericModelTypeNames() => GetFamilySymbolNames(BuiltInCategory.OST_GenericModel);

        /// <summary>보·헌치 유형 콤보 공용 목록. 재료로 거르지 않는 이유는 인터페이스 주석 참조(docs/159 § 0-2).
        /// 조회 규칙(카테고리 선별·중복 제거·정렬)은 다른 "패밀리명 : 타입명" 목록과 같아 GetFamilySymbolNames에 위임한다.</summary>
        public IEnumerable<string> GetStructuralFramingFamilyTypeNames()
            => GetFamilySymbolNames(BuiltInCategory.OST_StructuralFraming);

        /// <summary>밸브실 기둥 유형 콤보 전용. GetColumnTypeNames와 달리 패밀리명을 함께 내보낸다(docs/159 § A-2).</summary>
        public IEnumerable<string> GetColumnFamilyTypeNames()
            => GetFamilySymbolNames(BuiltInCategory.OST_StructuralColumns);

        /// <summary>구조 프레임 유형의 단면 폭(mm). 제수밸브실 상부 헌치를 보 폭만큼 끊는 데 쓴다(docs/162 § D-2).
        /// <para>구분자 파싱은 RevitElementTypeCommandRepo.FindStructuralFramingSymbolByFamilyType과 같은 LastIndexOf(" : ")다 —
        /// 타입명 자체에 " : "가 들어갈 수 있다. 구분자가 없으면 타입명 단독으로 본다(하위호환).</para>
        /// <para>단면폭 파라미터는 패밀리마다 다르다. 내장 단면 파라미터를 먼저 보고, 없으면 FindOrCreateBeamType(:168)이
        /// 쓰는 관례명 "b"/"B"를 본다. 어느 쪽도 없으면 0을 돌려 호출부가 절단을 건너뛰게 한다.</para></summary>
        public double GetStructuralFramingSectionWidthMm(string familyTypeName)
        {
            var doc = _docProvider();
            if (doc is null || string.IsNullOrWhiteSpace(familyTypeName)) return 0;

            try
            {
                var separator = familyTypeName.LastIndexOf(" : ", StringComparison.Ordinal);
                var hasFamily = separator > 0 && separator < familyTypeName.Length - 3;
                var familyName = hasFamily ? familyTypeName.Substring(0, separator) : null;
                var typeName = hasFamily ? familyTypeName.Substring(separator + 3) : familyTypeName;

                var symbol = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).WhereElementIsElementType()
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(fs => fs.Category != null
                        && fs.Category.Id.Value == (int)BuiltInCategory.OST_StructuralFraming
                        && fs.Name == typeName
                        && (familyName is null || fs.Family.Name == familyName));
                if (symbol is null) return 0;

                var width = symbol.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH)
                            ?? symbol.LookupParameter("b")
                            ?? symbol.LookupParameter("B");
                return width is null ? 0 : UC.FtToMm(width.AsDouble());
            }
            catch { return 0; }
        }

        /// <summary>구조 기둥 유형의 단면 치수(mm). 제수밸브실 상부 보를 기둥 자리에서 끊는 데 쓴다(docs/202 § B).
        /// <para>기둥은 RevitColumnCommandRepo.CreateColumn이 회전 없이 배치하므로 로컬 b/h가 그대로 프로젝트 X/Y와
        /// 나란하다. 구분자 파싱은 GetStructuralFramingSectionWidthMm과 같다.</para>
        /// <para>파라미터명은 패밀리마다 다르다. 내장 단면 파라미터를 먼저 보고, 없으면 관례명 b/h(한글 폭/깊이)를 본다.
        /// 어느 쪽도 없으면 그 축만 0을 돌려 호출부가 절단을 건너뛰게 한다.</para></summary>
        public (double WidthX, double DepthY) GetStructuralColumnSectionSizeMm(string familyTypeName)
        {
            var doc = _docProvider();
            if (doc is null || string.IsNullOrWhiteSpace(familyTypeName)) return (0, 0);

            try
            {
                var separator = familyTypeName.LastIndexOf(" : ", StringComparison.Ordinal);
                var hasFamily = separator > 0 && separator < familyTypeName.Length - 3;
                var familyName = hasFamily ? familyTypeName.Substring(0, separator) : null;
                var typeName = hasFamily ? familyTypeName.Substring(separator + 3) : familyTypeName;

                var symbol = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).WhereElementIsElementType()
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(fs => fs.Category != null
                        && fs.Category.Id.Value == (int)BuiltInCategory.OST_StructuralColumns
                        && fs.Name == typeName
                        && (familyName is null || fs.Family.Name == familyName));
                if (symbol is null) return (0, 0);

                var width = symbol.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH)
                            ?? symbol.LookupParameter("b")
                            ?? symbol.LookupParameter("B")
                            ?? symbol.LookupParameter("폭");
                var depth = symbol.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT)
                            ?? symbol.LookupParameter("h")
                            ?? symbol.LookupParameter("H")
                            ?? symbol.LookupParameter("깊이");
                return (width is null ? 0 : UC.FtToMm(width.AsDouble()),
                        depth is null ? 0 : UC.FtToMm(depth.AsDouble()));
            }
            catch { return (0, 0); }
        }

        /// <summary>패밀리 문서를 열어 얻은 캐시와 유형 조회 사전을 비운다. 공유 캐시라 다른 창의 다음 조회도 다시 읽는다(docs/258 § A).</summary>
        public void InvalidateFamilyCaches() => FamilyParameterCache.Clear();

        private IEnumerable<string> GetFamilySymbolNames(BuiltInCategory category)
        {
            var doc = _docProvider(); if (doc is null) return Enumerable.Empty<string>();
            try
            {
                // OfCategory(...).Cast<FamilySymbol>()는 카테고리에 FamilySymbol이 아닌 ElementType이 섞이면
                // InvalidCastException으로 조용히 빈 목록을 반환한다. GetColumnTypeNames 등과 같은
                // OfClass(FamilySymbol) 선先필터 방식으로 통일해 그 위험을 없앤다.
                return new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).WhereElementIsElementType()
                    .Cast<FamilySymbol>()
                    .Where(x => x.Category != null && x.Category.Id.Value == (int)category)
                    .Select(x => $"{x.Family.Name} : {x.Name}").Distinct().OrderBy(x => x).ToList();
            }
            catch { return Enumerable.Empty<string>(); }
        }
        private IEnumerable<string> GetNames(Type type)
        {
            var doc = _docProvider(); if (doc is null) return Enumerable.Empty<string>();
            try { return new FilteredElementCollector(doc).OfClass(type).WhereElementIsElementType().Cast<ElementType>().Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x).ToList(); }
            catch { return Enumerable.Empty<string>(); }
        }
        public IEnumerable<string> GetColumnTypeNames()
        {
            var doc = _docProvider();
            if (doc is null) return Enumerable.Empty<string>();

            try
            {
                var collector = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .WhereElementIsElementType();

                return collector
                    .Cast<FamilySymbol>()
                    .Where(fs => fs.Category != null && fs.Category.Id.Value == (int)BuiltInCategory.OST_StructuralColumns)
                    .Select(fs => fs.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        }
        public IEnumerable<string> GetFoundationTypeNames()
        {
            var doc = _docProvider();
            if (doc is null) return Enumerable.Empty<string>();

            try
            {
                var collector = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .WhereElementIsElementType();

                return collector
                    .Cast<FamilySymbol>()
                    .Where(fs => fs.Category != null && fs.Category.Id.Value == (int)BuiltInCategory.OST_StructuralFoundation)
                    .Select(fs => fs.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        }
        public IEnumerable<string> GetSlabTypeNames()
        {
            var doc = _docProvider();
            if (doc is null) return Enumerable.Empty<string>();

            try
            {
                var collector = new FilteredElementCollector(doc)
                    .OfClass(typeof(FloorType))
                    .WhereElementIsElementType();

                return collector
                    .Cast<FloorType>()
                    .Select(ft => ft.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        }
        public IEnumerable<string> GetWallTypeNames()
        {
            var doc = _docProvider();
            if (doc is null) return Enumerable.Empty<string>();

            try
            {
                var collector = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .WhereElementIsElementType();

                return collector
                    .Cast<WallType>()
                    .Where(wt => wt.Kind != WallKind.Curtain)
                    .Select(wt => wt.Name)
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        }
    }
}
