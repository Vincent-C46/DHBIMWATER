using DHBIMWATER.Application.DTOs.Documentation;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.UseCases.Documentation;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.UI.ViewModels.Documentation;
using Xunit;

namespace DHBIMWATER.UI.Tests.ViewModels.Documentation;

public sealed class JunctionDetailViewModelTests
{
    [Fact]
    public void Defaults_MatchDocumentedOptions_AndDisableUnavailableInputs()
    {
        var vm = Create();

        Assert.True(vm.CreateSchedule);
        Assert.True(vm.CreateDrafting);
        Assert.True(vm.CreatePlan);
        Assert.False(vm.Backfill);
        Assert.True(vm.ExpandVertical);
        Assert.Equal(8, vm.StepPaperMm);
        Assert.Equal(50, vm.ViewScale);
        Assert.True(vm.PlaceTags);
        Assert.False(vm.IsBackfillEnabled);
        Assert.False(vm.CanPlaceTags);
    }

    [Fact]
    public void OffsetPreview_RecalculatesScaleAndCollapsesToZero()
    {
        var vm = Create();
        vm.StepPaperMm = 7.5;
        vm.ViewScale = 20;
        Assert.Equal("+150", Assert.Single(vm.OffsetRows).WorldUpMm);

        vm.ExpandVertical = false;
        Assert.Equal("0", Assert.Single(vm.OffsetRows).WorldUpMm);
        Assert.Equal("—", Assert.Single(vm.OffsetRows).Order);
    }

    [Fact]
    public void DisconnectedVerticalItem_HasNoOrderAndZeroOffset()
    {
        var vm = Create(connected: false);
        var row = Assert.Single(vm.OffsetRows);
        Assert.Equal("—", row.Order);
        Assert.Equal("0", row.WorldUpMm);
        Assert.Equal("연결 끊김", row.Status);
    }

    [Fact]
    public void TypeImageOption_ShowsOrHidesRevitImagePathInTablePreview()
    {
        var vm = Create();
        Assert.Equal("sample.png", Assert.Single(vm.TableRows).TypeImagePath);
        vm.UseTypeImage = false;
        Assert.Null(Assert.Single(vm.TableRows).TypeImagePath);
    }

    [Fact]
    public void RunAvailability_RequiresAnOutputOrUsableBackfill()
    {
        var vm = Create();
        Assert.True(vm.CanRun);
        vm.CreateSchedule = false;
        vm.CreateDrafting = false;
        vm.CreatePlan = false;
        Assert.False(vm.CanRun);
    }

    private static JunctionDetailViewModel Create(bool connected = true)
    {
        var repo = new PreviewRepo(connected);
        var useCase = new BuildJunctionDetailPreviewUseCase(repo, new PartsRepo());
        var backfill = new BackfillPipeMaterialParametersUseCase(null!, new BackfillRepo(), null!, null!);
        return new JunctionDetailViewModel(useCase, null!,
            new CreateJunctionDetailViewUseCase(null!, repo), backfill);
    }

    private sealed class PreviewRepo(bool connected) : IJunctionDetailRepo
    {
        public IReadOnlyList<JunctionDetailGroup> GetJunctions() =>
        [
            new("GV-01", [
                new JunctionNode(1, [new(1, 0, 0, null), new(0, 0, 1, 2)]),
                new JunctionNode(2, connected ? [new(0, 0, -1, 1)] : [new(0, 0, -1, 1), new(0, 0, 1, null)])])
        ];

        public IReadOnlyList<JunctionPreviewItem> GetPreviewItems(JunctionDetailGroup group) =>
        [new(2, 3, "단관", "DN100 L=300", JunctionSymbolKind.Pipe, 1,
            new(1, 0, 0), new(0, 1, 0), new(0, 0, 1), new(0, 1, 0), 0, connected)];
        public string GetRoomType() => "공기밸브실";
        public IReadOnlyList<JunctionDetailTagType> GetTagTypes() => [];
        public JunctionDetailViewChange CreateOrUpdateView(JunctionDetailGroup group, IReadOnlyList<VerticalSlot> slots,
            JunctionDetailViewOptions options) => new(true, []);
        public IReadOnlyList<string> ResetOffsets(JunctionDetailGroup group) => [];
    }

    private sealed class PartsRepo : IPipeMaterialDraftingRepo
    {
        private static readonly PipeMaterialPart[] Parts =
        [new("GV-01", 3, "단관", "DN100 L=300", TypeImagePath: "sample.png")];
        public IReadOnlyList<PipeMaterialPart> GetParts() => Parts;
        public IReadOnlySet<string> GetExistingViewNames() => new HashSet<string>();
        public void CreateTableView(string viewName, string title, IReadOnlyList<PipeMaterialRow> rows, bool includeTypeImage = true) { }
        public bool RedrawTableView(string viewName, string title, IReadOnlyList<PipeMaterialRow> rows, bool includeTypeImage = true) => false;
    }

    private sealed class BackfillRepo : IPipeMaterialBackfillRepo
    {
        public PipeMaterialBackfillScan ScanCandidates() => new(0, false);
        public PipeMaterialBackfillResult Backfill(BendSettings catalog) => new(0, 0, []);
    }
}
