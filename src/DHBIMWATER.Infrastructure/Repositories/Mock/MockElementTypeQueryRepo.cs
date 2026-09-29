using DHBIMWATER.Application.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DHBIMWATER.Infrastructure.Repositories.Mock
{
    internal class MockElementTypeQueryRepo : IElementTypeQueryRepo
    {
        public IEnumerable<string> GetBeamTypeNames()
        {
            return new List<string>
            {
                "Mock 보 타입 1 - 300x600",
                "Mock 보 타입 2 - 400x700",
                "Mock 보 타입 3 - 500x800",
                "Mock 보 타입 4 - 600x900"
            };
        }
        /// <summary>Mock 보는 전부 콘크리트로 본다.</summary>
        public IEnumerable<string> GetConcreteBeamTypeNames() => GetBeamTypeNames();
        /// <summary>Mock 기둥은 전부 콘크리트로 본다.</summary>
        public IEnumerable<string> GetConcreteColumnTypeNames() => GetColumnTypeNames();
        public IEnumerable<string> GetStructuralFramingFamilyTypeNames() => new[]
        {
            "DH_헌치 : 헌치 300x300",
            "DH_헌치 : 헌치 400x400",
            "Mock 강재보 패밀리 : H-300x150",   // 재료 필터를 걸지 않는다는 것을 드러내는 항목(docs/159 § A-2)
            "Mock 보 패밀리 : Mock 보 타입 1 - 300x600"
        };
        /// <summary>밸브실 기둥 콤보용 "패밀리명 : 타입명" 목록(docs/159 § A-3).</summary>
        public IEnumerable<string> GetColumnFamilyTypeNames()
            => GetColumnTypeNames().Select(n => $"Mock 기둥 패밀리 : {n}");
        public IEnumerable<string> GetAdaptiveComponentTypeNames() => new[] { "Mock 5점 곡관 : DN100", "Mock 5점 곡관 : DN200", "Mock 3점 곡관 : DN300" };
        /// <summary>Mock 적응형 패밀리는 일반 모델(OST_GenericModel = -2000151)로 본다.</summary>
        public int? GetFamilyTypeCategoryId(string familyTypeName) => -2000151;
        public int GetAdaptiveBendPointCount(string familyTypeName) => familyTypeName.StartsWith("Mock 5점 곡관", StringComparison.Ordinal) ? 5 : familyTypeName.StartsWith("Mock 3점 곡관", StringComparison.Ordinal) ? 3 : -1;

        public IEnumerable<string> GetColumnTypeNames()
        {
            return new List<string>
            {
                "Mock 기둥 타입 1 - 400x400",
                "Mock 기둥 타입 2 - 500x500",
                "Mock 기둥 타입 3 - 600x600",
                "Mock 기둥 타입 4 - 700x700"
            };
        }
        public IEnumerable<string> GetBeamInstanceParameterNames(string beamTypeName) => new[] { "직경", "관종", "주석", "설명" };
        /// <summary>Mock 구조 프레임은 단면 폭 400mm로 본다(docs/162 § D-3).</summary>
        public double GetStructuralFramingSectionWidthMm(string familyTypeName) => string.IsNullOrWhiteSpace(familyTypeName) ? 0 : 400;
        /// <summary>Mock 구조 기둥은 단면 400×400mm로 본다(docs/202 § B').</summary>
        public (double WidthX, double DepthY) GetStructuralColumnSectionSizeMm(string familyTypeName) =>
            string.IsNullOrWhiteSpace(familyTypeName) ? (0, 0) : (400, 400);
        public IEnumerable<string> GetAdaptiveInstanceParameterNames(string familyTypeName) => new[] { "DN", "관종", "주석", "설명" };
        public IEnumerable<string> GetPipingSystemTypeNames() => new[] { "Mock 파이프 시스템 1 - 급수" };
        public IEnumerable<string> GetPipeTypeNames() => new[] { "Mock PipeType 1" };
        public IEnumerable<string> GetLevelNames() => new[] { "Level 1" };
        public IEnumerable<string> GetPipeAccessoryTypeNames() => new[]
        {
            "Mock 밸브 : DN100", "Mock 밸브 : DN200", "Mock 플랜지 : DN100"
        };
        public IEnumerable<string> GetPipeFittingTypeNames() => new[]
        {
            "Mock 직관 : DN100", "Mock 단관 : DN100", "Mock 곡관90 : DN100", "Mock 곡관45 : DN100", "Mock T형 : DN100"
        };
        public IEnumerable<string> GetPipeAccessoryInstanceParameterNames(string familyTypeName) => new[] { "길이", "DN", "주석", "설명" };
        /// <summary>실수형만(docs/233 § B-1). Mock에서는 "주석"·"설명"을 문자열로 본다.</summary>
        public IEnumerable<string> GetPipeAccessoryNumericInstanceParameterNames(string familyTypeName) => new[] { "길이", "DN" };
        /// <summary>유형 길이 매개변수(docs/233 § B-2). 유형명의 DN을 그대로 값으로 돌려 준다.</summary>
        public IReadOnlyList<DHBIMWATER.Application.DTOs.Revit.PipeAccessoryTypeParameterDto> GetPipeAccessoryTypeLengthParameters(string familyTypeName)
        {
            var dn = DHBIMWATER.Core.Piping.NominalDiameterParser.Parse(familyTypeName ?? string.Empty);
            return dn is null
                ? Array.Empty<DHBIMWATER.Application.DTOs.Revit.PipeAccessoryTypeParameterDto>()
                : new[] { new DHBIMWATER.Application.DTOs.Revit.PipeAccessoryTypeParameterDto("Nominal pipe size", dn.Value) };
        }
        public IEnumerable<string> GetGenericModelTypeNames() => new[] { "Mock 일반모델 밸브 : DN100", "Mock 일반모델 밸브 : DN200" };
        /// <summary>Mock은 캐시를 두지 않으므로 할 일이 없다.</summary>
        public void InvalidateFamilyCaches() { }

        public IEnumerable<string> GetFoundationTypeNames()
        {
            return new List<string>
            {
                "Mock 기초 타입 1 - THK200",
                "Mock 기초 타입 2 - THK250",
                "Mock 기초 타입 3 - THK300",
                "Mock 기초 타입 4 - THK350"
            };
        }

        public IEnumerable<string> GetSlabTypeNames()
        {
            return new List<string>
            {
                "Mock 슬래브 타입 1 - THK150",
                "Mock 슬래브 타입 2 - THK180",
                "Mock 슬래브 타입 3 - THK200",
                "Mock 슬래브 타입 4 - THK250",
                "Mock 슬래브 타입 5 - THK300"
            };
        }

        public IEnumerable<string> GetWallTypeNames()
        {
            return new List<string>
            {
                "Mock 벽체 타입 1 - THK200",
                "Mock 벽체 타입 2 - THK250",
                "Mock 벽체 타입 3 - THK300",
                "Mock 벽체 타입 4 - THK350"
            };
        }
    }
}
