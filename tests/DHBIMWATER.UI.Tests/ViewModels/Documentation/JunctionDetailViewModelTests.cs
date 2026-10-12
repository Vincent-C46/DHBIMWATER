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
        Assert.Equal("150", Assert.Single(vm.OffsetRows).WorldUpMm);

        vm.ExpandVertical = false;
        Assert.Equal("0", Assert.Single(vm.OffsetRows).WorldUpMm);
        Assert.Equal("—", Assert.Single(vm.OffsetRows).Order);
    }

    [Fact]
    public void StepSlider_MovesPreviewPoint_AndStretchesValveRoomBox()
    {
        var vm = Create();
        vm.StepPaperMm = 5;
        var nearY = Assert.Single(vm.PreviewPoints).Y;
        var nearBoxTop = vm.PreviewBoxTop;

        vm.StepPaperMm = 10;
        var farY = Assert.Single(vm.PreviewPoints).Y;

        Assert.Equal(40, nearY - farY);   // 간격 5mm 차이 × 화면 8px/mm
        Assert.True(vm.PreviewBoxTop < nearBoxTop);
        Assert.True(vm.PreviewStackTop < 337);
        Assert.False(vm.IsPreviewDimVisible);   // 부재 1개면 간격 치수선이 없다
    }

    [Fact]
    public void CollapsedPreview_KeepsAllPointsOnTheMainLine()
    {
        var vm = Create();
        vm.ExpandVertical = false;
        Assert.Equal(370, Assert.Single(vm.PreviewPoints).Y);
        Assert.Equal(285, vm.PreviewBoxTop);
        Assert.False(vm.IsPreviewDimVisible);
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

    [Fact]
    public void PlanOnly_WithManualParts_CanRunWithoutMaterialRows()
    {
        var vm = Create(material: false);
        vm.CreateSchedule = false;
        vm.CreateDrafting = false;
        Assert.True(vm.CanRun);
        vm.CreateSchedule = true;
        Assert.False(vm.CanRun);
    }

    [Fact]
    public void ExpandDirection_ChangesPreviewAndLocalOffset()
    {
        var vm = Create();
        Assert.True(vm.ExpandY);
        Assert.True(vm.ExpandPositive);
        Assert.Equal(306, Assert.Single(vm.PreviewPoints).Y);
        vm.ExpandPositive = false;
        Assert.Equal(434, Assert.Single(vm.PreviewPoints).Y);
        Assert.Equal("-400", Assert.Single(vm.OffsetRows).LocalYmm);
        vm.ExpandX = true;
        Assert.Equal(206, Assert.Single(vm.PreviewPoints).X);
        Assert.Equal("-400", Assert.Single(vm.OffsetRows).LocalXmm);
        vm.ExpandVertical = false;
        Assert.Equal(270, Assert.Single(vm.PreviewPoints).X);
    }

    [Fact]
    public void ShapePreview_ScalesWithViewScale_AndUsesFallbackWhenMissing()
    {
        var shape = JunctionSymbolShape.FromPolylines([
            (IReadOnlyList<ShapePoint>)[new(-50, -50), new(50, -50), new(50, 50), new(-50, 50), new(-50, -50)]
        ]);
        var vm = Create(shape: shape);
        var geometry = Assert.Single(vm.PreviewPoints).ShapeGeometry!;
        Assert.Equal(16, geometry.Bounds.Width, 6);
        Assert.Equal("기호선을 읽은 부재 1/1개 · 오프셋 반응 1개", vm.ShapeDiagnosticSummary);

        vm.ViewScale = 100;
        Assert.Equal(8, Assert.Single(vm.PreviewPoints).ShapeGeometry!.Bounds.Width, 6);
        Assert.False(Assert.Single(Create().PreviewPoints).HasShape);

        var shapeRepo = new EmptyShapeRepo(shape);
        var onceVm = Create(shape: shape, shapeRepo: shapeRepo);
        onceVm.ViewScale = 20;
        onceVm.ExpandX = true;
        onceVm.ExpandPositive = false;
        Assert.Equal(1, shapeRepo.ReadCount);
    }

    private static JunctionDetailViewModel Create(bool connected = true, bool material = true,
        JunctionSymbolShape? shape = null, EmptyShapeRepo? shapeRepo = null)
    {
        var repo = new PreviewRepo(connected);
        var useCase = new BuildJunctionDetailPreviewUseCase(repo, new PartsRepo(material), new PreviewTransaction(),
            shapeRepo ?? new EmptyShapeRepo(shape));
        var backfill = new BackfillPipeMaterialParametersUseCase(null!, new BackfillRepo(), null!, null!);
        return new JunctionDetailViewModel(useCase, null!,
            new CreateJunctionDetailViewUseCase(null!, repo), backfill);
    }

    private sealed class PreviewRepo(bool connected) : IJunctionDetailRepo
    {
        public IReadOnlyList<JunctionDetailGroup> GetJunctions() =>
        [
            new("GV-01", [
                new JunctionNode(1, connected ? [new(1, 0, 0, null), new(0, 0, 1, 2)] : [new(1, 0, 0, null)]),
                new JunctionNode(2, connected ? [new(0, 0, -1, 1)] : [new(0, 0, -1, null), new(0, 0, 1, null)])])
        ];

        public IReadOnlyList<JunctionPreviewItem> GetPreviewItems(JunctionDetailGroup group) =>
        [new(2, 3, "단관", "DN100 L=300", JunctionSymbolKind.Pipe, 1,
            new(1, 0, 0), new(0, 1, 0), new(0, 0, 1), new(0, 1, 0), 0, connected, new(1, 0, 0))];
        public string GetRoomType() => "공기밸브실";
        public IReadOnlyList<JunctionDetailTagType> GetTagTypes() => [];
        public JunctionDetailViewChange CreateOrUpdateView(JunctionDetailGroup group, IReadOnlyList<VerticalSlot> slots,
            JunctionDetailViewOptions options) => new(true, []);
        public IReadOnlyList<string> ResetOffsets(JunctionDetailGroup group) => [];
    }

    private sealed class PartsRepo(bool material) : IPipeMaterialDraftingRepo
    {
        private static readonly PipeMaterialPart[] Parts =
        [new("GV-01", 3, "단관", "DN100 L=300", TypeImagePath: "sample.png")];
        public IReadOnlyList<PipeMaterialPart> GetParts() => material ? Parts : [];
        public IReadOnlySet<string> GetExistingViewNames() => new HashSet<string>();
        public void CreateTableView(string viewName, string title, IReadOnlyList<PipeMaterialRow> rows, bool includeTypeImage = true) { }
        public bool RedrawTableView(string viewName, string title, IReadOnlyList<PipeMaterialRow> rows, bool includeTypeImage = true) => false;
    }

    private sealed class BackfillRepo : IPipeMaterialBackfillRepo
    {
        public PipeMaterialBackfillScan ScanCandidates() => new(0, false);
        public PipeMaterialBackfillResult Backfill(BendSettings catalog) => new(0, 0, []);
    }

    private sealed class EmptyShapeRepo(JunctionSymbolShape? shape) : IJunctionSymbolShapeRepo
    {
        public int ReadCount { get; private set; }
        public JunctionSymbolShapeReadResult Read(IReadOnlyList<long> elementIds)
        {
            ReadCount++;
            return new(shape is null ? new Dictionary<long, JunctionSymbolShape>() : new Dictionary<long, JunctionSymbolShape> { [2] = shape },
                shape is null ? [] : [new JunctionSymbolShapeDiagnostic(2, "단관", 4, shape.Polylines.Sum(x => x.Count),
                    shape.MaxX - shape.MinX, shape.MaxY - shape.MinY, "반응함", "GetInstanceGeometry")]);
        }
    }

    private sealed class PreviewTransaction : ITransactionContext
    {
        public IReadOnlyList<string> SuppressedWarnings => [];
        public void Begin(string name, bool suppressWarnings = false) { }
        public void Commit() => throw new InvalidOperationException("미리보기는 커밋하지 않습니다.");
        public void Rollback() { }
        public void Dispose() { }
    }
}
