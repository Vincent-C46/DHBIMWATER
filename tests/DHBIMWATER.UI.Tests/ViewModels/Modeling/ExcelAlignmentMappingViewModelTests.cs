using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.UI.ViewModels.Modeling;
using Xunit;

namespace DHBIMWATER.UI.Tests.ViewModels.Modeling;

public sealed class ExcelAlignmentMappingViewModelTests
{
    [Fact]
    public void CheckingSheet_FiltersPreviewAndGuessesCoordinateColumns()
    {
        var vm = new ExcelAlignmentMappingViewModel(new StubReader(), new StubDialog(), "sample.xlsx");

        Assert.Empty(vm.CheckedSheets);
        Assert.Null(vm.PreviewSheet);

        vm.Sheets[0].IsChecked = true;

        Assert.Single(vm.CheckedSheets);
        Assert.Same(vm.Sheets[0], vm.PreviewSheet);
        Assert.Equal(0, vm.XColumn?.Index);
        Assert.Equal(1, vm.YColumn?.Index);
        Assert.Equal(2, vm.ZColumn?.Index);
        Assert.Equal(3, vm.StationColumn?.Index);
        Assert.Equal(5, vm.BendKindColumn?.Index);
    }

    [Fact]
    public void RefreshPreview_PreservesUserSelectedColumn()
    {
        var vm = new ExcelAlignmentMappingViewModel(new StubReader(), new StubDialog(), "sample.xlsx");
        vm.Sheets[0].IsChecked = true;
        vm.XColumn = vm.ColumnOptions.Single(x => x.Index == 4);

        vm.DataStartRow = 3;

        Assert.Equal(4, vm.XColumn?.Index);
    }

    [Fact]
    public void UncheckingLastSheet_ClearsPreviewSelection()
    {
        var vm = new ExcelAlignmentMappingViewModel(new StubReader(), new StubDialog(), "sample.xlsx");
        vm.Sheets[0].IsChecked = true;

        vm.Sheets[0].IsChecked = false;

        Assert.Empty(vm.CheckedSheets);
        Assert.Null(vm.PreviewSheet);
        Assert.False(vm.CanConfirm);
    }

    [Fact]
    public void 밸브실모드_XY를_추정하면_기타열을_접어둔다()
    {
        var vm = new ExcelAlignmentMappingViewModel(new StubReader(), new StubDialog(), "sample.xlsx") { IsValveRoomMode = true };

        vm.Sheets[0].IsChecked = true;

        Assert.False(vm.IsOtherColumnsExpanded);
        Assert.Equal("표고 (관중심) *", vm.ZLabel);
    }

    [Fact]
    public void 밸브실모드_Y를_못_찾으면_기타열을_펼친다()
    {
        var vm = new ExcelAlignmentMappingViewModel(new StubReader([" East ", "북쪽", "표 고", "측점", "사용자 X", "곡관유형"]), new StubDialog(), "sample.xlsx") { IsValveRoomMode = true };

        vm.Sheets[0].IsChecked = true;

        Assert.Null(vm.YColumn);
        Assert.True(vm.IsOtherColumnsExpanded);
    }

    private sealed class StubReader(string?[]? header = null) : IExcelAlignmentSourceReader
    {
        public bool CanRead(string filePath) => true;
        public IReadOnlyList<string> GetSheetNames(string filePath) => ["관로1", "관로2"];
        public IReadOnlyList<IReadOnlyList<string?>> PreviewRows(string filePath, string sheetName, int maxRows) =>
        [
            header ?? [" East ", "NORTH", "표 고", "측점", "사용자 X", "곡관유형"],
            ["1", "2", "3", "0+000", "4", ""],
            ["5", "6", "7", "0+010", "8", "45˚"]
        ];
        public ShapefileReadResult Read(string filePath) => throw new NotSupportedException();
        public ShapefileReadResult Read(string filePath, ExcelAlignmentMapping mapping) => throw new NotSupportedException();
        public AlignmentProfileReadResult ReadProfile(string filePath, ExcelAlignmentMapping mapping) => throw new NotSupportedException();
        public ValveRoomListReadResult ReadValveRoomList(string filePath, ExcelAlignmentMapping mapping, int? cumulativeDistanceColumn = null) => throw new NotSupportedException();
    }

    private sealed class StubDialog : IDialogService
    {
        public void Info(string title, string message) { }
        public void Warn(string title, string message) { }
        public bool Confirm(string title, string message) => true;
    }
}
