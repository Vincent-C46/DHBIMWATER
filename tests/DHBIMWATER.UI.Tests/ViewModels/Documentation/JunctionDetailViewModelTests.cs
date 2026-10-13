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
        var set = Assert.Single(vm.OffsetSets);
        Assert.Equal(8, set.StepPaperMm);
        Assert.True(set.IsY);
        Assert.Equal(50, vm.ViewScale);
        Assert.True(vm.PlaceTags);
        Assert.False(vm.IsBackfillEnabled);
        Assert.False(vm.CanPlaceTags);
    }

    [Fact]
    public void OffsetPreview_RecalculatesScaleAndCollapsesToZero()
    {
        var vm = Create();
        vm.OffsetSets[0].StepText = "7.5";
        vm.ViewScale = 20;
        Assert.Equal("150", Assert.Single(vm.OffsetRows).WorldUpMm);

        vm.ExpandVertical = false;
        Assert.Equal("0", Assert.Single(vm.OffsetRows).WorldUpMm);
        Assert.Equal("—", Assert.Single(vm.OffsetRows).Order);
    }

    [Fact]
    public void StepText_AllowsNegativeAndLargeValues_AndKeepsLastValueWhileTyping()
    {
        var vm = Create();
        var set = vm.OffsetSets[0];
        set.StepText = "-";
        Assert.True(set.HasError);
        Assert.Equal(8, set.StepPaperMm);

        set.StepText = "-8";
        Assert.False(set.HasError);
        Assert.Equal("-400", Assert.Single(vm.OffsetRows).WorldUpMm);
        Assert.Equal("-400", Assert.Single(vm.OffsetRows).LocalYmm);

        set.StepText = "50";
        Assert.Equal("2500", Assert.Single(vm.OffsetRows).WorldUpMm);
    }

    [Fact]
    public void Preview_MovesVerticalPartFromRootBySignedStep()
    {
        var vm = Create();
        double Dy() => vm.PreviewPoints.Single(x => x.Item.ElementId == 2).Y - vm.PreviewPoints.Single(x => x.Item.ElementId == 1).Y;
        double Dx() => vm.PreviewPoints.Single(x => x.Item.ElementId == 2).X - vm.PreviewPoints.Single(x => x.Item.ElementId == 1).X;
        Assert.Equal(-64, Dy(), 6);   // 8mm × 8px, 캔버스 Y 아래가 +
        vm.OffsetSets[0].StepText = "-5";
        Assert.Equal(40, Dy(), 6);
        vm.OffsetSets[0].IsX = true;
        Assert.Equal(-40, Dx(), 6);
        Assert.Equal("-250", Assert.Single(vm.OffsetRows).LocalXmm);
        vm.ExpandVertical = false;
        Assert.Equal(0, Dx(), 6);
        Assert.True(vm.PreviewCanvasWidth >= 400);
    }

    [Fact]
    public void ApplyAllSets_SetsAxisAndStepForEverySet()
    {
        var vm = Create();
        vm.BulkAxisX = true;
        vm.BulkStepText = "-6";
        vm.ApplyAllSetsCommand.Execute(null);
        var set = Assert.Single(vm.OffsetSets);
        Assert.True(set.IsX);
        Assert.Equal(-6, set.StepPaperMm);
        Assert.Equal("-6", set.StepText);
        Assert.Equal("-300", Assert.Single(vm.OffsetRows).LocalXmm);
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
    public void ShapePreview_ScalesWithViewScale_AndUsesFallbackWhenMissing()
    {
        var shape = JunctionSymbolShape.FromPolylines([
            (IReadOnlyList<ShapePoint>)[new(-50, -50), new(50, -50), new(50, 50), new(-50, 50), new(-50, -50)]
        ]);
        var vm = Create(shape: shape);
        var geometry = vm.PreviewPoints.Single(x => x.HasShape).ShapeGeometry!;
        Assert.Equal(16, geometry.Bounds.Width, 6);
        Assert.Equal("기호선을 읽은 부재 1/2개 · 오프셋 반응 1개", vm.ShapeDiagnosticSummary);

        vm.ViewScale = 100;
        Assert.Equal(8, vm.PreviewPoints.Single(x => x.HasShape).ShapeGeometry!.Bounds.Width, 6);
        Assert.All(Create().PreviewPoints, x => Assert.False(x.HasShape));

        var shapeRepo = new EmptyShapeRepo(shape);
        var onceVm = Create(shape: shape, shapeRepo: shapeRepo);
        onceVm.ViewScale = 20;
        onceVm.OffsetSets[0].IsX = true;
        onceVm.OffsetSets[0].StepText = "-8";
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
        [new(1, 1, "T관", "DN300×DN100", JunctionSymbolKind.Tee, null,
            new(1, 0, 0), new(0, 1, 0), new(0, 0, 1), new(0, 1, 0), 0, true, new(1, 0, 0)),
         new(2, 3, "단관", "DN100 L=300", JunctionSymbolKind.Pipe, 1,
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
