using DHBIMWATER.Core.Structures;
using DHBIMWATER.UI.ViewModels.Modeling;
using Xunit;

namespace DHBIMWATER.UI.Tests.ViewModels.Modeling;

/// <summary>밸브실 제원표 편집 창(docs/312) 검증.</summary>
public class ValveRoomDimensionCatalogViewModelTests
{
    [Fact]
    public void 처음_유형으로_고른_유형의_행만_보인다()
    {
        var vm = new ValveRoomDimensionCatalogViewModel(ValveRoomStandardDimensionCatalog.Default, "이토밸브실");

        Assert.Equal("이토밸브실", vm.SelectedRoomKind);
        Assert.All(vm.VisibleRows, x => Assert.Equal("이토밸브실", x.RoomKind));
        Assert.Contains(vm.VisibleRows, x => x.DiameterMm == 1200);
    }

    [Fact]
    public void 값을_고치면_표에_반영된다()
    {
        var vm = new ValveRoomDimensionCatalogViewModel(ValveRoomStandardDimensionCatalog.Default, "이토밸브실");
        vm.VisibleRows.Single(x => x.DiameterMm == 1200).LengthMm = 3300;

        var entry = vm.ToTable().Find("이토밸브실", 1200);

        Assert.Equal(3300d, entry!.LengthMm);
        // 다른 유형 행은 그대로 남는다.
        Assert.Equal(ValveRoomStandardDimensionCatalog.Entries.Count, vm.ToTable().Entries.Count);
    }

    [Fact]
    public void 행을_추가하고_삭제할_수_있다()
    {
        var vm = new ValveRoomDimensionCatalogViewModel(ValveRoomStandardDimensionCatalog.Default, "공기밸브실");
        var before = vm.VisibleRows.Count;

        vm.AddRowCommand.Execute(null);
        var added = vm.SelectedRow!;
        added.DiameterMm = 3200;
        Assert.Equal(before + 1, vm.VisibleRows.Count);
        Assert.NotNull(vm.ToTable().Find("공기밸브실", 3200));

        vm.SelectedRow = vm.VisibleRows.First();
        vm.DeleteRowCommand.Execute(null);
        Assert.Equal(before, vm.VisibleRows.Count);
    }

    [Fact]
    public void 새_행_관경을_비워두면_확인이_막힌다()
    {
        var vm = new ValveRoomDimensionCatalogViewModel(ValveRoomStandardDimensionCatalog.Default, "공기밸브실");
        bool? closed = null;
        vm.CloseAction = x => closed = x;

        vm.AddRowCommand.Execute(null);
        vm.ConfirmCommand.Execute(null);

        Assert.Null(closed);
        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public void 표준값_복원하면_내장_표로_돌아간다()
    {
        var vm = new ValveRoomDimensionCatalogViewModel(ValveRoomStandardDimensionCatalog.Default, "이토밸브실");
        vm.VisibleRows.Single(x => x.DiameterMm == 1200).LengthMm = 9999;

        vm.ResetCommand.Execute(null);

        Assert.Equal(3000d, vm.ToTable().Find("이토밸브실", 1200)!.LengthMm);
    }
}
