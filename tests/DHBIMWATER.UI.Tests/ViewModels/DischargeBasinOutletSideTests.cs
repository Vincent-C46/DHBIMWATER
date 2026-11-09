using DHBIMWATER.Application.DTOs.Revit.DischargeBasin;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.UseCases.AutoGenerator;
using DHBIMWATER.UI.ViewModels.Modeling;
using Xunit;

namespace DHBIMWATER.UI.Tests.ViewModels;

/// <summary>토출 좌우 선택, 모델리스 위임, 도해 강조(docs/320).</summary>
public class DischargeBasinOutletSideTests
{
    private sealed class StubDialog : IDialogService
    {
        public void Info(string title, string message) { }
        public void Warn(string title, string message) { }
        public bool Confirm(string title, string message) => true;
    }

    private sealed class StubUseCase : ICreateDischargeBasinUseCase
    {
        public DischargeBasinInputDto? Input { get; private set; }
        public string? GetBlockingReason(DischargeBasinInputDto input, DischargeBasinGeometryDto geometry) => null;
        public void Execute(DischargeBasinInputDto input, DischargeBasinGeometryDto geometry) => Input = input;
    }

    private static DischargeBasinViewModel Create(StubUseCase useCase)
    {
        var vm = new DischargeBasinViewModel(new StubDialog(), useCase);
        vm.UpdatePumpReference(d: 1500, n: 4, pumpHwl: 5, h3: 1200, h7: 1000, b8PlusT5: 5000);
        vm.TA = 9.4;
        vm.Separation = 3;
        vm.ON = 2;
        vm.OD = 1000;
        vm.OB = 1000;
        vm.OH = 1000;
        return vm;
    }

    [Theory]
    [InlineData(DischargeBasinViewModel.PositionLeft, DischargeOutletPosition.Left, "좌측")]
    [InlineData(DischargeBasinViewModel.PositionRight, DischargeOutletPosition.Right, "우측")]
    public void Position_MapsToInputAndImage(string label, DischargeOutletPosition expected, string imagePart)
    {
        var useCase = new StubUseCase();
        var vm = Create(useCase);
        vm.SelectedPosition = label;
        vm.CreateDischargeBasinCommand.Execute(null);

        Assert.Equal(expected, useCase.Input?.Position);
        Assert.Contains(imagePart, vm.PlanImagePath);
    }

    [Fact]
    public void Create_RunsOnlyInsideRevitContextDelegate()
    {
        var useCase = new StubUseCase();
        var vm = Create(useCase);
        Action? pending = null;
        vm.RunInRevitContext = action => pending = action;

        vm.CreateDischargeBasinCommand.Execute(null);
        Assert.Null(useCase.Input);

        pending!();
        Assert.NotNull(useCase.Input);
    }

    [Fact]
    public void Highlight_FollowsFocusAndSide()
    {
        var vm = Create(new StubUseCase());
        Assert.Null(vm.PlanHighlightGeometry);

        vm.SetHint("TB2");
        var right = vm.PlanHighlightGeometry;
        Assert.NotNull(right);

        vm.SelectedPosition = DischargeBasinViewModel.PositionLeft;
        Assert.NotEqual(right, vm.PlanHighlightGeometry);

        vm.SelectedPosition = DischargeBasinViewModel.PositionCenter;
        Assert.NotNull(vm.PlanHighlightGeometry);

        vm.SetHint("Separation");
        Assert.Null(vm.PlanHighlightGeometry);

        vm.SetHint("RefD");
        Assert.NotNull(vm.PlanHighlightGeometry);
        Assert.NotNull(vm.ProfileHighlightGeometry);

        vm.SetHint("TOL2");
        Assert.NotNull(vm.OpeningHighlightGeometry);

        vm.ClearFocusedParameter();
        Assert.Null(vm.OpeningHighlightGeometry);
    }
}
