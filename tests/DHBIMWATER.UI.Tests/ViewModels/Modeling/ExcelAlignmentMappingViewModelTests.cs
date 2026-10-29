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

        vm.DataStartRow = 6;

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
    public void 기본값은_헤더4행_데이터시작5행()
    {
        var vm = new ExcelAlignmentMappingViewModel(new StubReader(), new StubDialog(), "sample.xlsx");

        Assert.Equal(4, vm.HeaderRow);
        Assert.Equal(5, vm.DataStartRow);
    }

    [Fact]
    public void 관로모드_필수열을_다_추정하면_열지정을_접어둔다()
    {
        var vm = new ExcelAlignmentMappingViewModel(new StubReader(), new StubDialog(), "sample.xlsx");

        vm.Sheets[0].IsChecked = true;

        Assert.False(vm.IsColumnMappingExpanded);
    }

    [Fact]
    public void 관로모드_Y를_못_찾으면_열지정을_펼친다()
    {
        var vm = new ExcelAlignmentMappingViewModel(new StubReader([" East ", "북쪽", "표 고", "측점", "사용자 X", "곡관유형"]), new StubDialog(), "sample.xlsx");

        vm.Sheets[0].IsChecked = true;

        Assert.Null(vm.YColumn);
        Assert.True(vm.IsColumnMappingExpanded);
    }

    [Fact]
    public void 밸브실모드_필수열을_다_추정하면_열지정을_접어둔다()
    {
        var vm = new ExcelAlignmentMappingViewModel(
            new StubReader([" East ", "NORTH", "표 고", "측점", "DN", "곡관유형", "밸브실", "원지반"]), new StubDialog(), "sample.xlsx") { IsValveRoomMode = true };

        vm.Sheets[0].IsChecked = true;

        Assert.False(vm.HasMissingRequiredColumn);
        Assert.False(vm.IsColumnMappingExpanded);
        Assert.Equal("표고 (관중심) *", vm.ZLabel);
    }

    [Fact]
    public void 밸브실모드_필수열_하나라도_없으면_열지정을_펼친다()
    {
        // 밸브실 유형·원지반 열 없음.
        var vm = new ExcelAlignmentMappingViewModel(new StubReader(), new StubDialog(), "sample.xlsx") { IsValveRoomMode = true };

        vm.Sheets[0].IsChecked = true;

        Assert.True(vm.IsColumnMappingExpanded);
    }

    private sealed class StubReader(string?[]? header = null) : IExcelAlignmentSourceReader
    {
        public bool CanRead(string filePath) => true;
        public IReadOnlyList<string> GetSheetNames(string filePath) => ["관로1", "관로2"];
        // 관로 정보 보고서 꼴 — 1~3행 제목·빈 행, 4행 헤더, 5행부터 데이터.
        public IReadOnlyList<IReadOnlyList<string?>> PreviewRows(string filePath, string sheetName, int maxRows) =>
        [
            ["관로 정보 보고서"],
            [],
            [],
            header ??[" East ", "NORTH", "표 고", "측점", "사용자 X", "곡관유형"],
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
