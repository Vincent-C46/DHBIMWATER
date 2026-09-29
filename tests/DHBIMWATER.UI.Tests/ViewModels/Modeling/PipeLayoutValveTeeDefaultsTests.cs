using System.Collections.Specialized;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.UI.ViewModels.Modeling;
using Xunit;

namespace DHBIMWATER.UI.Tests.ViewModels.Modeling;

/// <summary>docs/232 — 밸브부관 A/B 기본값 완전일치, 콤보별 컬렉션 분리(갱신 Clear로 다른 칸이 비지 않음),
/// 수평·수직분기 자동 추정, B `부관_구경` 우선, 바이패스 밸브 직경 파라미터 추정·기억.</summary>
public sealed class PipeLayoutValveTeeDefaultsTests
{
    private const string TeeFamily = "DH_특수_밸브부관 : 1650A";
    private const string BendFamily = "DH_특수_밸브부관(D형) : 300A";
    private const string GateValve = "DH_게이트 밸브 : 300A";
    private const string ButterflyValve = "DH_버터플라이 밸브 : 1650A";

    [Fact]
    public void Defaults_PreferExactFamilyNames_OverLetterRule()
    {
        var vm = CreateViewModel();

        // 두 이름 모두 `A`/`B` 글자가 없다 — 종전 규칙이면 A는 비고 B는 첫 항목(A 패밀리)이 된다(docs/232 § 1-1).
        Assert.Equal(TeeFamily, vm.ValveTeeFamilyTypeName);
        Assert.Equal(BendFamily, vm.ValveBendFamilyTypeName);
        Assert.Equal("주관_구경", vm.ValveTeeMainDiameterParameterName);
        Assert.Equal("수평분기_구경", vm.ValveTeeBranchDiameterParameterName);
        // B는 `부관_구경` 완전일치 — 일반 규칙("구경" 포함 첫 이름)이면 정렬상 앞선 `주관_구경`이 잡힌다(§ A-3).
        Assert.Equal("부관_구경", vm.ValveBendDiameterParameterName);
    }

    [Fact]
    public void HiddenFlagParameters_AreGuessedWithoutUi()
    {
        var vm = CreateViewModel();

        Assert.Equal("수평분기", vm.ValveTeeHorizontalBranchParameterName);
        Assert.Equal("수직분기", vm.ValveTeeVerticalBranchParameterName);
        // 저장 단위에도 들어간다 — 배치가 예/아니오를 구동한다.
        Assert.Equal("수평분기", vm.ToFamilySettings().ValveTeeHorizontalBranchParameterName);
    }

    [Fact]
    public void ChangingValveBendFamily_KeepsUserPickedValveTeeParameters()
    {
        var vm = CreateViewModel();
        // WPF Selector 흉내: 컬렉션 Reset이 오면 그 콤보에 바인딩된 선택을 null로 밀어 넣는다(docs/232 § 1-2).
        vm.ValveTeeMainParameterNames.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) vm.ValveTeeMainDiameterParameterName = null; };
        vm.ValveTeeBranchParameterNames.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) vm.ValveTeeBranchDiameterParameterName = null; };
        vm.ValveTeeMainDiameterParameterName = "공칭_구경";
        vm.ValveTeeBranchDiameterParameterName = "DN";

        vm.ValveBendFamilyTypeName = "곡관90 : DN100";

        // 컬렉션이 콤보마다 따로라 자기 Reset 뒤에 자기 값만 복원하면 된다. 공유 컬렉션이면 뒤 칸의 Reset이 앞 칸을 지운다.
        Assert.Equal("공칭_구경", vm.ValveTeeMainDiameterParameterName);
        Assert.Equal("DN", vm.ValveTeeBranchDiameterParameterName);
        Assert.Equal("수평분기", vm.ValveTeeHorizontalBranchParameterName);
    }

    [Fact]
    public void ReducerLargeAndSmall_UseSeparateCollections()
    {
        var vm = CreateViewModel();
        Assert.NotSame(vm.ReducerLargeParameterNames, vm.ReducerSmallParameterNames);
    }

    [Fact]
    public void BypassValve_DiameterParameter_IsGuessedForItsOwnFamily_AndRemembered()
    {
        var vm = CreateViewModel();
        // 4번 섹션은 버터플라이, 바이패스는 게이트 — 서로 다른 패밀리다.
        vm.SelectedValveFamilyName = "DH_버터플라이 밸브";
        vm.BypassValveFamilyName = "DH_게이트 밸브";

        Assert.Equal(GateValve, vm.BypassValveTypeName);
        Assert.Contains("호칭지름", vm.BypassValveParameterNames);
        Assert.Equal("호칭지름", vm.BypassValveDiameterParameterName);
        // 배치 경계가 패밀리명으로 찾는 사전에 들어간다 — 종전에는 없어서 4번 섹션 값을 빌렸다(docs/232 § 1-3).
        Assert.Equal("호칭지름", vm.ToFamilySettings().ValveDiameterParameters["DH_게이트 밸브"]);

        vm.BypassValveDiameterParameterName = "사용자지정";
        Assert.Equal("사용자지정", vm.ToFamilySettings().ValveDiameterParameters["DH_게이트 밸브"]);

        // 저장값을 다시 적용하면 콤보가 그 값을 따른다.
        var other = CreateViewModel();
        other.BypassValveFamilyName = "DH_게이트 밸브";
        other.ApplyFamilySettings(vm.ToFamilySettings());
        Assert.Equal("사용자지정", other.BypassValveDiameterParameterName);
    }

    private static PipeLayoutViewModel CreateViewModel() => new(new StubRepo());

    private sealed class StubRepo : IElementTypeQueryRepo
    {
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
        public IEnumerable<string> GetPipeAccessoryTypeNames() => [ButterflyValve, GateValve];
        public IEnumerable<string> GetPipeFittingTypeNames() => ["직관 : DN100", "곡관90 : DN100", "T형 : DN100", TeeFamily, BendFamily];
        public IEnumerable<string> GetPipeAccessoryInstanceParameterNames(string familyTypeName)
            => familyTypeName switch
            {
                // 이름순 정렬을 흉내 낸다 — B에서 `주관_구경`이 `부관_구경`보다 앞에 오게 둔다(§ A-3 검증).
                BendFamily => ["DN", "공칭_구경", "주관_구경", "부관_구경"],
                TeeFamily => ["DN", "공칭_구경", "수직분기", "수평분기", "수평분기_구경", "주관_구경"],
                // DN은 넣지 않는다 — 영문 약어 완전일치(ND/DN/NB)가 키워드보다 앞서 `호칭지름` 추정을 가린다.
                // `주석 축척 사용`은 실제 오류에 나온 이름 — 후보에 있어도 추정이 고르지 않아야 한다.
                // `사용자지정`은 사용자가 손으로 바꾸는 경우를 검증할 후보다(직경 키워드가 없어 추정에 걸리지 않는다).
                GateValve => ["주석 축척 사용", "호칭지름", "사용자지정"],
                ButterflyValve => ["직경"],
                _ => ["길이", "DN"],
            };
        public IEnumerable<string> GetGenericModelTypeNames() => [];
        public IEnumerable<string> GetFoundationTypeNames() => [];
        public IEnumerable<string> GetBeamInstanceParameterNames(string beamTypeName) => [];
        public double GetStructuralFramingSectionWidthMm(string familyTypeName) => 400;
        public (double WidthX, double DepthY) GetStructuralColumnSectionSizeMm(string familyTypeName) => (400, 400);
        public IEnumerable<string> GetPipeAccessoryNumericInstanceParameterNames(string familyTypeName) => GetPipeAccessoryInstanceParameterNames(familyTypeName);
        public IReadOnlyList<DHBIMWATER.Application.DTOs.Revit.PipeAccessoryTypeParameterDto> GetPipeAccessoryTypeLengthParameters(string familyTypeName) => [];
        public void InvalidateFamilyCaches() { }
    }
}
