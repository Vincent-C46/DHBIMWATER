using DHBIMWATER.Core.Piping;
using DHBIMWATER.UI.ViewModels.Modeling;
using Xunit;

namespace DHBIMWATER.UI.Tests.ViewModels.Modeling;

/// <summary>수직분기 입면 창(docs/301 § D).</summary>
public sealed class VerticalBranchViewModelTests
{
    private const string Gate200 = "DH_제수밸브 : DN200";
    private const string Gate100 = "DH_제수밸브 : DN100";
    private const string Air200 = "DH_공기밸브 : DN200";

    private static VerticalBranchViewModel Create(params string[] names)
        => new(new VerticalBranchStart(584213, "DH_밸브부관A : 300x200", 200, -2050), names);

    [Fact]
    public void Default_HasOnePipe_AndCanPlace()
    {
        var vm = Create(Gate200);
        var part = Assert.Single(vm.Parts);
        Assert.True(part.IsPipe);
        Assert.Equal(VerticalBranchViewModel.DefaultPipeLengthMm, part.LengthMm);
        Assert.True(vm.CanPlace);
        Assert.False(vm.HasError);
    }

    [Fact]
    public void AddOptions_FilterByStartDiameter_UntilShowAll()
    {
        var vm = Create(Gate200, Gate100, Air200);
        Assert.Equal(VerticalBranchViewModel.PipeOption, vm.AddOptions[0]);
        Assert.Equal([Air200, Gate200], vm.AddOptions.Skip(1).OrderBy(x => x, StringComparer.Ordinal));
        Assert.DoesNotContain(Gate100, vm.AddOptions);

        vm.ShowAllDiameters = true;
        Assert.Contains(Gate100, vm.AddOptions);
    }

    [Fact]
    public void FamilyWithoutDiameterMatch_ShowsAllItsTypes()
    {
        // 공기밸브 유형명 DN이 본관과 다르거나 없어도 목록에 남아야 함.
        const string Air75 = "DH_공기밸브 : DN75";
        const string AirPlain = "DH_공기밸브 : 표준";
        var vm = Create(Gate200, Gate100, Air75, AirPlain);
        Assert.False(vm.ShowAllDiameters);
        Assert.Contains(Air75, vm.AddOptions);
        Assert.Contains(AirPlain, vm.AddOptions);
        Assert.DoesNotContain(Gate100, vm.AddOptions);
    }

    [Fact]
    public void NoDiameterMatch_ShowsAllByDefault()
    {
        var vm = Create(Gate100);
        Assert.True(vm.ShowAllDiameters);
        Assert.Contains(Gate100, vm.AddOptions);
    }

    [Fact]
    public void ShortPipe_BlocksPlace()
    {
        var vm = Create(Gate200);
        vm.Parts[0].LengthMm = 5;
        Assert.False(vm.CanPlace);
        Assert.True(vm.HasError);
        Assert.Contains("#1 단관", vm.ErrorText);
    }

    [Fact]
    public void AddMoveRemove_KeepOrder()
    {
        var vm = Create(Gate200, Air200);
        vm.SelectedAddOption = Gate200;
        vm.AddCommand.Execute(null);
        vm.SelectedAddOption = Air200;
        vm.AddCommand.Execute(null);
        Assert.Equal(["", Gate200, Air200], vm.ToParts().Select(x => x.FamilyTypeName));

        vm.MoveTowardStartCommand.Execute(vm.Parts[1]);
        Assert.Equal([Gate200, "", Air200], vm.ToParts().Select(x => x.FamilyTypeName));

        vm.RemoveCommand.Execute(vm.Parts[0]);
        Assert.Equal(["", Air200], vm.ToParts().Select(x => x.FamilyTypeName));
    }

    [Fact]
    public void PartsTopDown_ShowsEndFirst_AndFollowsMoves()
    {
        var vm = Create(Gate200, Air200);
        vm.SelectedAddOption = Gate200;
        vm.AddCommand.Execute(null);
        vm.SelectedAddOption = Air200;
        vm.AddCommand.Execute(null);
        // 목록 맨 위 = 끝(공기밸브), 맨 아래 = 시작 쪽 단관.
        Assert.Equal([Air200, Gate200, ""], vm.PartsTopDown.Select(x => x.FamilyTypeName));

        // ▲(끝 쪽으로) = 목록에서 위로.
        vm.MoveTowardEndCommand.Execute(vm.Parts[0]);
        Assert.Equal([Air200, "", Gate200], vm.PartsTopDown.Select(x => x.FamilyTypeName));
    }

    [Fact]
    public void Preview_StacksFromBottom()
    {
        var vm = Create(Gate200);
        vm.SelectedAddOption = Gate200;
        vm.AddCommand.Execute(null);

        Assert.Equal(2, vm.PreviewItems.Count);
        var first = vm.PreviewItems[0];
        Assert.True(first.IsPipe);
        Assert.Equal(VerticalBranchViewModel.PreviewHeight, first.Top + first.Height, 6);
        // 다음 부품은 바로 위에 붙음.
        Assert.Equal(first.Top, vm.PreviewItems[1].Top + vm.PreviewItems[1].Height, 6);
    }

    [Fact]
    public void Place_ClosesConfirmed()
    {
        var vm = Create(Gate200);
        bool? closed = null;
        vm.CloseAction = x => closed = x;
        vm.PlaceCommand.Execute(null);
        Assert.True(closed);
        Assert.True(vm.Confirmed);
    }
}
