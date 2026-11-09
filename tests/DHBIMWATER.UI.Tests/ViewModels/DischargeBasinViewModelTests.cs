using DHBIMWATER.Application.DTOs.Revit.DischargeBasin;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.UseCases.AutoGenerator;
using DHBIMWATER.UI.ViewModels.Modeling;
using Xunit;

namespace DHBIMWATER.UI.Tests.ViewModels;

/// <summary>
/// 토출조 ViewModel 배선 검증.
/// 계산 자체는 DischargeBasinGeometryCalculatorTests 가 맡고, 여기서는
/// 펌프장 연동 / 권고안 덮어쓰기 전파 / 되돌리기 / 유형별 가시성이 화면 레벨에서 도는지 본다.
/// </summary>
public class DischargeBasinViewModelTests
{
    private sealed class StubDialogService : IDialogService
    {
        public List<(string Title, string Message)> Infos { get; } = new();
        public List<(string Title, string Message)> Warns { get; } = new();

        public void Info(string title, string message) => Infos.Add((title, message));
        public void Warn(string title, string message) => Warns.Add((title, message));
        public bool Confirm(string title, string message) => true;
    }

    /// <summary>토출조 생성 UseCase 스텁. 실제 Revit 호출 없이 호출 여부만 본다.</summary>
    private sealed class StubCreateUseCase : ICreateDischargeBasinUseCase
    {
        public string? BlockingReason { get; set; }
        public List<(DischargeBasinInputDto Input, DischargeBasinGeometryDto Geometry)> Executed { get; } = new();

        public string? GetBlockingReason(DischargeBasinInputDto input, DischargeBasinGeometryDto geometry)
            => BlockingReason;
        public void Execute(DischargeBasinInputDto input, DischargeBasinGeometryDto geometry)
            => Executed.Add((input, geometry));
    }

    /// <summary>엑셀 예시(좌우측 · 단차미적용)와 같은 조건으로 맞춘다.</summary>
    private static DischargeBasinViewModel CreateReady(out StubDialogService dialog)
        => CreateReady(out dialog, out _);

    private static DischargeBasinViewModel CreateReady(out StubDialogService dialog, out StubCreateUseCase useCase)
    {
        dialog = new StubDialogService();
        useCase = new StubCreateUseCase();
        var vm = new DischargeBasinViewModel(dialog, useCase)
        {
            TA = 9.4,
            ON = 2,
            OB = 2000,
            OH = 2000,
            OD = 2000,
        };
        vm.UpdatePumpReference(d: 1500, n: 4, pumpHwl: 5, h3: 1200, h7: 1000, b8PlusT5: 5000);
        return vm;
    }

    private static DischargeBasinValueRow Row(DischargeBasinViewModel vm, string key)
        => vm.ProfileRows.Concat(vm.PlanRows).Concat(vm.OpeningRows).Single(r => r.Key == key);

    [Fact]
    public void WithoutPumpReference_CannotCreateAndRowsAreEmpty()
    {
        var vm = new DischargeBasinViewModel(new StubDialogService(), new StubCreateUseCase());

        Assert.False(vm.CanCreate);
        Assert.NotEmpty(vm.ValidationMessage);
        Assert.All(vm.ProfileRows, r => Assert.Null(r.Value));
        Assert.All(vm.PlanRows, r => Assert.Null(r.Value));
    }

    [Fact]
    public void PumpReference_FillsRecommendationsWithExcelValues()
    {
        var vm = CreateReady(out _);

        Assert.True(vm.CanCreate);
        Assert.Equal(string.Empty, vm.ValidationMessage);

        Assert.Equal(10.8, Row(vm, "TB").Value);
        Assert.Equal(400, Row(vm, "TT1").Value);
        Assert.Equal(500, Row(vm, "TT2").Value);
        Assert.Equal(3800, Row(vm, "TB1").Value);
        Assert.Equal(8000, Row(vm, "TB2").Value);
        Assert.Equal(19500, Row(vm, "TLT").Value);
        Assert.Equal(2300, Row(vm, "TOB1").Value);
    }

    [Fact]
    public void PumpReferenceChange_RecalculatesRecommendations()
    {
        var vm = CreateReady(out _);
        Assert.Equal(3800, Row(vm, "TB1").Value);

        // 펌프 직경을 바꾸면 TB1 = ROUNDUP(2.5D/100)*100 이 따라 바뀐다.
        vm.UpdatePumpReference(d: 2000, n: 4, pumpHwl: 5, h3: 1200, h7: 1000, b8PlusT5: 5000);

        Assert.Equal(5000, Row(vm, "TB1").Value);
    }

    [Fact]
    public void FillingRecommendations_DoesNotMarkRowsAsEdited()
    {
        var vm = CreateReady(out _);

        // 계산 결과 반영이 "사용자 수정"으로 잡히면 권고안이 더 이상 갱신되지 않는다.
        Assert.All(vm.ProfileRows, r => Assert.False(r.IsEdited));
        Assert.All(vm.PlanRows, r => Assert.False(r.IsEdited));
        Assert.All(vm.OpeningRows, r => Assert.False(r.IsEdited));
    }

    [Fact]
    public void EditingRecommendation_MarksItAndPropagatesToDependents()
    {
        var vm = CreateReady(out _);

        Row(vm, "TT1").Value = 600;

        Assert.True(Row(vm, "TT1").IsEdited);
        Assert.Equal(600, Row(vm, "TT1").Value);
        Assert.Equal(700, Row(vm, "TT2").Value);   // TT2 = TT1 + 100
        Assert.Equal(700, Row(vm, "TB3").Value);   // TB3 = TT2
        Assert.Equal(600, Row(vm, "TCB1").Value);  // TCB1 = TT1
        Assert.False(Row(vm, "TT2").IsEdited);     // 전파된 항목은 수정으로 잡히지 않는다
    }

    [Fact]
    public void EditedRecommendation_SurvivesInputChange()
    {
        var vm = CreateReady(out _);
        Row(vm, "TT1").Value = 600;

        vm.TA = 9.6;

        Assert.Equal(600, Row(vm, "TT1").Value);
        Assert.True(Row(vm, "TT1").IsEdited);
    }

    [Fact]
    public void RevertProfile_RestoresComputedValues()
    {
        var vm = CreateReady(out _);
        Row(vm, "TT1").Value = 600;
        Assert.Equal(700, Row(vm, "TT2").Value);

        vm.RevertProfileCommand.Execute(null);

        Assert.Equal(400, Row(vm, "TT1").Value);
        Assert.Equal(500, Row(vm, "TT2").Value);
        Assert.False(Row(vm, "TT1").IsEdited);
    }

    [Fact]
    public void RevertPlan_LeavesProfileEditsAlone()
    {
        var vm = CreateReady(out _);
        Row(vm, "TT1").Value = 600;   // 종단
        Row(vm, "TL1").Value = 2300;  // 평면

        vm.RevertPlanCommand.Execute(null);

        Assert.True(Row(vm, "TT1").IsEdited);
        Assert.Equal(600, Row(vm, "TT1").Value);
        Assert.False(Row(vm, "TL1").IsEdited);
        Assert.Equal(1500, Row(vm, "TL1").Value);
    }

    [Fact]
    public void Position_TogglesTypeSpecificRows()
    {
        var vm = CreateReady(out _);

        Assert.True(Row(vm, "Theta2").IsVisible);
        Assert.True(Row(vm, "TB5").IsVisible);
        Assert.False(Row(vm, "TL3").IsVisible);

        vm.SelectedPosition = DischargeBasinViewModel.PositionCenter;

        Assert.False(Row(vm, "Theta2").IsVisible);
        Assert.False(Row(vm, "TB5").IsVisible);
        Assert.True(Row(vm, "TL3").IsVisible);
        Assert.True(Row(vm, "TL4").IsVisible);
    }

    [Fact]
    public void Step_TogglesStepRows()
    {
        var vm = CreateReady(out _);

        Assert.False(Row(vm, "TH4").IsVisible);
        Assert.Null(Row(vm, "TB6").Value);

        vm.SelectedStep = DischargeBasinViewModel.StepApplied;

        Assert.True(Row(vm, "TH4").IsVisible);
        Assert.Equal(2200, Row(vm, "TH4").Value);
        Assert.Equal(3700, Row(vm, "TB6").Value);
    }

    [Fact]
    public void Conduit_SwitchesImageAndSpacing()
    {
        var vm = CreateReady(out _);

        Assert.Contains("우측-암거", vm.PlanImagePath);
        Assert.Equal(300, Row(vm, "OT").Value);

        vm.SelectedConduit = DischargeBasinViewModel.ConduitPipe;

        Assert.Contains("우측-PIPE", vm.PlanImagePath);
        Assert.Equal(0, Row(vm, "OT").Value);
        Assert.Equal(1000, Row(vm, "O1").Value);

        vm.SelectedPosition = DischargeBasinViewModel.PositionCenter;
        Assert.Contains("중앙부", vm.PlanImagePath);
    }

    [Fact]
    public void ProfileImage_FollowsStep()
    {
        var vm = CreateReady(out _);

        Assert.Contains("단차미적용", vm.ProfileImagePath);

        vm.SelectedStep = DischargeBasinViewModel.StepApplied;

        Assert.Contains("단차적용", vm.ProfileImagePath);
    }

    [Fact]
    public void SetHint_FillsFormulaAndBasisFromRow()
    {
        var vm = CreateReady(out _);

        vm.SetHint("TL1");

        Assert.Contains("TL1", vm.HintTitle);
        Assert.Contains("ROUNDUP", vm.HintFormula);
        // 산식근거는 1.5D인데 수식은 1.0D — 확인 필요 항목이라 ⚠ 문구가 붙어 있어야 한다.
        Assert.Contains("⚠", vm.HintDescription);
    }

    /// <summary>
    /// 이격거리는 제원 수식에 안 쓰이지만 <b>배치 원점</b>을 정한다(확인필요사항 B-1 해소).
    /// 힌트가 그 사실과 기준면을 알려 줘야 사용자가 값을 제대로 넣는다.
    /// </summary>
    [Fact]
    public void SetHint_SupportsInputFields()
    {
        var vm = CreateReady(out _);

        vm.SetHint("Separation");

        Assert.Contains("이격거리", vm.HintTitle);
        Assert.Contains("벽체 외면", vm.HintDescription);
        Assert.Contains("배치 원점", vm.HintDescription);
    }

    [Fact]
    public void Create_WithoutPumpReference_WarnsInsteadOfRunningUseCase()
    {
        var dialog = new StubDialogService();
        var useCase = new StubCreateUseCase();
        var vm = new DischargeBasinViewModel(dialog, useCase);

        vm.CreateDischargeBasinCommand.Execute(null);

        Assert.Single(dialog.Warns);
        Assert.Empty(useCase.Executed);
    }

    [Fact]
    public void Create_PassesInputAndGeometryToUseCase()
    {
        var vm = CreateReady(out _, out var useCase);

        vm.CreateDischargeBasinCommand.Execute(null);

        var (input, geometry) = Assert.Single(useCase.Executed);
        Assert.Equal(DischargeOutletPosition.Right, input.Position);
        Assert.Equal(DischargeStepType.None, input.Step);
        Assert.Equal(3800, geometry.TB1);
        Assert.Equal(8000, geometry.TB2);
        Assert.Equal(19500, geometry.TLT);
    }

    [Fact]
    public void Create_AppliesEditedRecommendationsToGeometry()
    {
        var vm = CreateReady(out _, out var useCase);
        Row(vm, "TT1").Value = 600;

        vm.CreateDischargeBasinCommand.Execute(null);

        // 덮어쓴 권고안이 생성 형상에도 그대로 실려야 한다.
        var (_, geometry) = Assert.Single(useCase.Executed);
        Assert.Equal(600, geometry.TT1);
        Assert.Equal(700, geometry.TT2);
    }

    [Fact]
    public void Create_BlockedType_WarnsBeforeRunningUseCase()
    {
        var vm = CreateReady(out var dialog, out var useCase);
        useCase.BlockingReason = "단차 적용 형상은 아직 생성할 수 없습니다.";
        vm.SelectedStep = DischargeBasinViewModel.StepApplied;

        vm.CreateDischargeBasinCommand.Execute(null);

        Assert.Single(dialog.Warns);
        Assert.Empty(useCase.Executed);
    }

    [Fact]
    public void PrimaryRows_ContainConfirmedKeysInSourceOrder()
    {
        var vm = CreateReady(out _);

        Assert.Equal(new[] { "TB", "TF", "TT1", "TT2", "TT3" }, vm.PrimaryProfileRows.Select(r => r.Key));
        Assert.Equal(new[] { "TB1", "TB2", "TLT" }, vm.PrimaryPlanRows.Select(r => r.Key));
    }

    [Fact]
    public void PrimaryAndDetailRows_CoverSourceRowsWithoutDuplicates()
    {
        var vm = CreateReady(out _);

        var profile = vm.PrimaryProfileRows.Concat(vm.DetailProfileRows).ToList();
        var plan = vm.PrimaryPlanRows.Concat(vm.DetailPlanRows).ToList();

        Assert.Equal(vm.ProfileRows.Count, profile.Count);
        // θ 파생값은 계산 전용이라 화면 행에서 뺀다.
        string[] hidden = { "Theta1", "Theta2", "TB4", "TB5", "TT4", "TT5", "TL5", "TL6", "TL7", "TL8" };
        var shownPlan = vm.PlanRows.Where(r => !hidden.Contains(r.Key)).ToList();
        Assert.Equal(shownPlan.Count, plan.Count);
        // 같은 객체여야 재계산·덮어쓰기 표시가 화면 행에 반영된다.
        Assert.All(vm.ProfileRows, r => Assert.Contains(profile, x => ReferenceEquals(x, r)));
        Assert.All(shownPlan, r => Assert.Contains(plan, x => ReferenceEquals(x, r)));
    }

    [Theory]
    [InlineData("TA")]
    [InlineData("Separation")]
    [InlineData("ON")]
    [InlineData("OB")]
    [InlineData("OH")]
    [InlineData("OD")]
    [InlineData("RefD")]
    [InlineData("RefN")]
    [InlineData("RefPumpHwl")]
    [InlineData("RefH3")]
    [InlineData("RefH7")]
    [InlineData("RefB8PlusT5")]
    public void InputHintTexts_ProvidesTooltipForDesignInputs(string key)
    {
        var vm = CreateReady(out _);

        Assert.False(string.IsNullOrWhiteSpace(vm.InputHintTexts[key]));
    }
}
