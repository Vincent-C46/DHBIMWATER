using DHBIMWATER.Application.DTOs.Documentation;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.UseCases.Documentation;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Shared.Errors;
using Xunit;

namespace DHBIMWATER.UI.Tests.UseCases.Documentation;

public sealed class BuildJunctionDetailPreviewUseCaseTests
{
    [Fact]
    public void Execute_ReadsSlotsAndOrphans_ThenAlwaysRollsBack()
    {
        var transaction = new TestTransaction();
        var shapes = new TestShapeRepo();

        var result = Create(transaction, shapes).Execute();

        // 수평 부재까지 전부 읽음. 순서 = 수평·고아 먼저(번호순), 그다음 세트 순서
        Assert.Equal([1L, 3L, 2L], shapes.ElementIds);
        Assert.Equal(["Begin", "Rollback"], transaction.Events);
        Assert.DoesNotContain("Commit", transaction.Events);
        Assert.Single(result.ShapeDiagnostics!);
        Assert.Equal("반응함", Assert.Single(result.ShapeDiagnostics!).OffsetResponse);
    }

    [Fact]
    public void Execute_ReadFailure_ContinuesWithFallbackAndDiagnostic()
    {
        var transaction = new TestTransaction();
        var result = Create(transaction, new TestShapeRepo { Exception = new InvalidOperationException("읽기 실패") }).Execute();

        Assert.All(Assert.Single(result.Junctions).Items, item => Assert.Null(item.Shape));
        Assert.Equal(3, result.ShapeDiagnostics!.Count);
        Assert.All(result.ShapeDiagnostics, item => Assert.Contains("읽기 실패", item.Note));
        Assert.Contains("Rollback", transaction.Events);
        Assert.DoesNotContain("Commit", transaction.Events);
    }

    [Fact]
    public void Execute_MarksHorizontalSetAndOrphan_AndLinksPartners()
    {
        var junction = Assert.Single(Create(new TestTransaction(), new TestShapeRepo()).Execute().Junctions);
        var items = junction.Items.ToDictionary(x => x.ElementId);

        Assert.False(items[1].IsVertical);
        Assert.Null(items[1].Order);
        Assert.Equal((1, 2L), (items[2].Order, items[2].SetId));
        Assert.True(items[2].IsVertical);
        Assert.False(items[3].Connected);
        Assert.Equal([new JunctionPreviewLink(1, 2)], junction.Links);
    }

    [Fact]
    public void Execute_NoActiveDocument_IsRethrown()
    {
        var transaction = new TestTransaction { BeginException = new DhbimwaterException(ErrorCodes.NoActiveDocument, "문서 없음") };

        Assert.Throws<DhbimwaterException>(() => Create(transaction, new TestShapeRepo()).Execute());
        Assert.Contains("Rollback", transaction.Events);
        Assert.DoesNotContain("Commit", transaction.Events);
    }

    private static BuildJunctionDetailPreviewUseCase Create(TestTransaction transaction, TestShapeRepo shapes)
        => new(new TestJunctionRepo(), new TestDraftingRepo(), transaction, shapes);

    private sealed class TestJunctionRepo : IJunctionDetailRepo
    {
        private static readonly JunctionDetailGroup Group = new("J1", [
            new JunctionNode(1, [new(1, 0, 0, null), new(0, 0, 1, 2)]),
            new JunctionNode(2, [new(0, 0, -1, 1)]),
            new JunctionNode(3, [new(0, 0, 1, null)])]);
        public IReadOnlyList<JunctionDetailGroup> GetJunctions() => [Group];
        public IReadOnlyList<JunctionPreviewItem> GetPreviewItems(JunctionDetailGroup group) => [
            Item(1), Item(2), Item(3)];
        public string GetRoomType() => "";
        public IReadOnlyList<JunctionDetailTagType> GetTagTypes() => [];
        public JunctionDetailViewChange CreateOrUpdateView(JunctionDetailGroup group, IReadOnlyList<VerticalSlot> slots, JunctionDetailViewOptions options) => new(true, []);
        public IReadOnlyList<string> ResetOffsets(JunctionDetailGroup group) => [];
        private static JunctionPreviewItem Item(long id) => new(id, (int)id, $"부재{id}", "",
            JunctionSymbolKind.Generic, null, new(1, 0, 0), new(0, 1, 0), new(0, 0, 1),
            new(0, 1, 0), 0);
    }

    private sealed class TestDraftingRepo : IPipeMaterialDraftingRepo
    {
        public IReadOnlyList<PipeMaterialPart> GetParts() => [];
        public IReadOnlySet<string> GetExistingViewNames() => new HashSet<string>();
        public void CreateTableView(string viewName, string title, IReadOnlyList<PipeMaterialRow> rows, bool includeTypeImage = true) { }
        public bool RedrawTableView(string viewName, string title, IReadOnlyList<PipeMaterialRow> rows, bool includeTypeImage = true) => false;
    }

    private sealed class TestShapeRepo : IJunctionSymbolShapeRepo
    {
        public IReadOnlyList<long> ElementIds { get; private set; } = [];
        public Exception? Exception { get; init; }
        public JunctionSymbolShapeReadResult Read(IReadOnlyList<long> elementIds)
        {
            ElementIds = elementIds;
            if (Exception is not null) throw Exception;
            var diagnostic = new JunctionSymbolShapeDiagnostic(2, "부재2", 1, 2, 10, 20, "반응함", "GetInstanceGeometry");
            return new(new Dictionary<long, JunctionSymbolShape>(), [diagnostic]);
        }
    }

    private sealed class TestTransaction : ITransactionContext
    {
        public List<string> Events { get; } = [];
        public Exception? BeginException { get; init; }
        public IReadOnlyList<string> SuppressedWarnings => [];
        public void Begin(string name, bool suppressWarnings = false)
        {
            Events.Add("Begin");
            if (BeginException is not null) throw BeginException;
        }
        public void Commit() => Events.Add("Commit");
        public void Rollback() => Events.Add("Rollback");
        public void Dispose() { }
    }
}
