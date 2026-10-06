using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.UI.ViewModels.Modeling;
using Xunit;

namespace DHBIMWATER.UI.Tests.ViewModels.Modeling;

/// <summary>PipeLayout 관 등급 콤보(docs/260 § C) — 직관·단관 단위중량 조회와 `DH_관종등급`의 원천.</summary>
public sealed class PipeLayoutPipeKindTests
{
    [Fact]
    public void 기본은_주철_상수1종관이고_주철_등급만_보인다()
    {
        var vm = new PipeLayoutViewModel(new StubTypeRepo());

        Assert.Equal(PipeKindCatalog.Water1, vm.PipeKindLabel);
        Assert.Equal(PipeKindCatalog.DuctileIronAll, vm.PipeKindLabels);
    }

    [Fact]
    public void 강관으로_바꾸면_자동_항목과_STWW_등급이_보이고_자동이_선택된다()
    {
        var vm = new PipeLayoutViewModel(new StubTypeRepo());

        vm.PipeMaterial = PipeMaterial.Steel;

        Assert.Equal(PipeLayoutViewModel.AutoSteelGradeLabel, vm.PipeKindLabel);
        Assert.Equal(new[] { PipeLayoutViewModel.AutoSteelGradeLabel }.Concat(PipeKindCatalog.SteelAll), vm.PipeKindLabels);
        Assert.Equal(string.Empty, vm.ToFamilySettings().PipeKind);
    }

    [Fact]
    public void 관종_토글로_강관을_고르면_등급도_자동으로_바뀐다()
    {
        // 창의 관종 토글은 IsSteelSelected에 바인딩된다(docs/263 § C, 실검증 2026-10-02: 등급이 상수 1종관으로 남아 강관 직관 무게가 비었다).
        var vm = new PipeLayoutViewModel(new StubTypeRepo());

        vm.IsSteelSelected = true;

        Assert.Equal(PipeLayoutViewModel.AutoSteelGradeLabel, vm.PipeKindLabel);
        Assert.Equal(string.Empty, vm.ToFamilySettings().PipeKind);
    }

    [Fact]
    public void 목록에_없는_등급은_무시한다()
    {
        var vm = new PipeLayoutViewModel(new StubTypeRepo());

        vm.PipeKindLabel = PipeKindCatalog.Steel400A;

        Assert.Equal(PipeKindCatalog.Water1, vm.PipeKindLabel);
    }

    [Fact]
    public void 저장값을_적용하고_다시_저장하면_같은_등급이다()
    {
        var vm = new PipeLayoutViewModel(new StubTypeRepo());
        vm.PipeKindLabel = PipeKindCatalog.Water3;

        var saved = vm.ToFamilySettings();
        var other = new PipeLayoutViewModel(new StubTypeRepo());
        other.ApplyFamilySettings(saved, fromStorage: true);

        Assert.Equal(PipeKindCatalog.Water3, other.PipeKindLabel);
        Assert.Equal(PipeKindCatalog.Water3, other.ToFamilySettings().PipeKind);
    }

    [Fact]
    public void 옛_저장본은_현재_등급을_유지한다()
    {
        var vm = new PipeLayoutViewModel(new StubTypeRepo());

        vm.ApplyFamilySettings(PipeLayoutFamilySettings.Empty with { SegmentFamilyTypeName = "직관 : DN100" }, fromStorage: true);

        Assert.Equal(PipeKindCatalog.Water1, vm.PipeKindLabel);
    }

    private sealed class StubTypeRepo : IElementTypeQueryRepo
    {
        public IEnumerable<string> GetPipeAccessoryTypeNames() => [];
        public IEnumerable<string> GetPipeFittingTypeNames() => ["직관 : DN100", "곡관90 : DN100", "T형 : DN100"];
        public IEnumerable<string> GetPipeAccessoryInstanceParameterNames(string familyTypeName) => [];
        public IEnumerable<string> GetSlabTypeNames() => [];
        public IEnumerable<string> GetWallTypeNames() => [];
        public IEnumerable<string> GetColumnTypeNames() => [];
        public IEnumerable<string> GetColumnFamilyTypeNames() => [];
        public IEnumerable<string> GetBeamTypeNames() => [];
        public IEnumerable<string> GetConcreteColumnTypeNames() => [];
        public IEnumerable<string> GetConcreteBeamTypeNames() => [];
        public IEnumerable<string> GetStructuralFramingFamilyTypeNames() => [];
        public IEnumerable<string> GetAdaptiveComponentTypeNames() => [];
        public IEnumerable<string> GetAdaptiveInstanceParameterNames(string familyTypeName) => [];
        public int GetAdaptiveBendPointCount(string familyTypeName) => -1;
        public int? GetFamilyTypeCategoryId(string familyTypeName) => null;
        public IEnumerable<string> GetPipingSystemTypeNames() => ["급수"];
        public IEnumerable<string> GetPipeTypeNames() => ["배관"];
        public IEnumerable<string> GetLevelNames() => ["1층"];
        public IEnumerable<string> GetGenericModelTypeNames() => [];
        public IEnumerable<string> GetFoundationTypeNames() => [];
        public IEnumerable<string> GetBeamInstanceParameterNames(string beamTypeName) => [];
        public double GetStructuralFramingSectionWidthMm(string familyTypeName) => 400;
        public (double WidthX, double DepthY) GetStructuralColumnSectionSizeMm(string familyTypeName) => (400, 400);
        public IEnumerable<string> GetPipeAccessoryNumericInstanceParameterNames(string familyTypeName) => [];
        public IReadOnlyList<DHBIMWATER.Application.DTOs.Revit.PipeAccessoryTypeParameterDto> GetPipeAccessoryTypeLengthParameters(string familyTypeName) => [];
        public void InvalidateFamilyCaches() { }
    }
}
