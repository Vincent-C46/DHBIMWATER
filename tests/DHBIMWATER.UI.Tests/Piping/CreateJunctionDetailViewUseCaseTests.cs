using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.DTOs.Documentation;
using DHBIMWATER.Application.UseCases.Documentation;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.UI.Tests.Piping;

public sealed class CreateJunctionDetailViewUseCaseTests
{
    [Fact]
    public void Execute_ContinuesAfterOneJunctionFails()
    {
        var repo = new FakeRepo { FailSecond = true };
        var transaction = new FakeTransaction();

        var result = new CreateJunctionDetailViewUseCase(transaction, repo).Execute();

        Assert.Single(result.Created);
        Assert.Single(result.Skipped);
        Assert.True(transaction.Committed);
        Assert.Equal([new VerticalSlot(2, 1, 2)], repo.FirstSlots);
    }

    [Fact]
    public void Execute_ResetOnly_DoesNotCreateViews()
    {
        var repo = new FakeRepo();
        var result = new CreateJunctionDetailViewUseCase(new FakeTransaction(), repo).Execute(resetOnly: true);

        Assert.Equal(2, result.Reset.Count);
        Assert.Empty(result.Created);
        Assert.Equal(2, repo.ResetCalls);
        Assert.Equal(0, repo.CreateCalls);
    }

    [Fact]
    public void Execute_CollapsedOptions_PassesNoSlotsAndUsesRequestedScale()
    {
        var repo = new FakeRepo();
        var options = new JunctionDetailViewOptions(false, 6.5, 20, false, 0);

        var result = new CreateJunctionDetailViewUseCase(new FakeTransaction(), repo).Execute(options);

        Assert.Empty(repo.FirstSlots);
        Assert.Equal(options, repo.LastOptions);
        Assert.Equal(2, result.Created.Count);
    }

    [Fact]
    public void Execute_NoTargets_ReportsWhyNoViewWasCreated()
    {
        var repo = new FakeRepo { Empty = true };
        var result = new CreateJunctionDetailViewUseCase(new FakeTransaction(), repo).Execute();
        Assert.Empty(result.Created);
        Assert.Contains(result.Warnings, x => x.Contains("부재가 없어"));
    }

    [Fact]
    public void Execute_HorizontalOnly_StillCreatesView()
    {
        var repo = new FakeRepo { HorizontalOnly = true };
        var result = new CreateJunctionDetailViewUseCase(new FakeTransaction(), repo).Execute();
        Assert.Equal(2, result.Created.Count);
        Assert.Empty(repo.FirstSlots);
    }

    private sealed class FakeRepo : IJunctionDetailRepo
    {
        public bool FailSecond { get; init; }
        public bool Empty { get; init; }
        public bool HorizontalOnly { get; init; }
        public int CreateCalls { get; private set; }
        public int ResetCalls { get; private set; }
        public IReadOnlyList<VerticalSlot> FirstSlots { get; private set; } = [];
        public JunctionDetailViewOptions? LastOptions { get; private set; }

        public IReadOnlyList<JunctionDetailGroup> GetJunctions() => Empty ? [] :
        [
            new("J1", HorizontalOnly
                ? [new JunctionNode(1, [new(1, 0, 0, 2)]), new JunctionNode(2, [new(-1, 0, 0, 1)])]
                : [new JunctionNode(1, [new(1, 0, 0, null), new(0, 0, 1, 2)]),
                    new JunctionNode(2, [new(0, 0, -1, 1)])]),
            new("J2", [new JunctionNode(3, [new(1, 0, 0, null), new(0, 0, 1, 4)]),
                new JunctionNode(4, [new(0, 0, -1, 3)])])
        ];

        public IReadOnlyList<JunctionPreviewItem> GetPreviewItems(JunctionDetailGroup group) => [];
        public string GetRoomType() => string.Empty;
        public IReadOnlyList<JunctionDetailTagType> GetTagTypes() => [];

        public JunctionDetailViewChange CreateOrUpdateView(JunctionDetailGroup group, IReadOnlyList<VerticalSlot> slots, JunctionDetailViewOptions options)
        {
            CreateCalls++;
            LastOptions = options;
            if (group.JunctionId == "J2" && FailSecond) throw new InvalidOperationException("뷰 실패");
            if (group.JunctionId == "J1") FirstSlots = slots;
            return new(true, []);
        }

        public IReadOnlyList<string> ResetOffsets(JunctionDetailGroup group)
        {
            ResetCalls++;
            return [];
        }
    }

    private sealed class FakeTransaction : ITransactionContext
    {
        public bool Committed { get; private set; }
        public IReadOnlyList<string> SuppressedWarnings => [];
        public void Begin(string name, bool suppressWarnings = false) { }
        public void Commit() => Committed = true;
        public void Rollback() { }
        public void Dispose() { }
    }
}
