using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Services;
using Xunit;

namespace DHBIMWATER.UI.Tests.Services;

/// <summary>재생성 절차(docs/320 § C): 확인, 같은 구조물만 삭제, 새 부재만 표시.</summary>
public class StructureRegenerationTests
{
    private sealed class FakeRepo : IStructureModelRepo
    {
        public Dictionary<string, List<long>> Structures { get; } = new();
        public List<long> Addin { get; } = new();
        public List<long> Deleted { get; } = new();
        public List<(long Id, string Structure)> Marked { get; } = new();

        public IReadOnlyList<long> GetAddinElementIds() => Addin.ToList();
        public IReadOnlyList<long> GetStructureElementIds(string structure)
            => Structures.TryGetValue(structure, out var ids) ? ids : new List<long>();
        public void DeleteElements(IReadOnlyCollection<long> elementIds)
        {
            Deleted.AddRange(elementIds);
            Addin.RemoveAll(elementIds.Contains);
        }
        public void MarkStructure(IEnumerable<long> elementIds, string structure)
            => Marked.AddRange(elementIds.Select(id => (id, structure)));
    }

    private sealed class FakeDialog : IDialogService
    {
        public bool Answer { get; init; } = true;
        public int ConfirmCount { get; private set; }
        public string? LastMessage { get; private set; }
        public void Info(string title, string message) { }
        public void Warn(string title, string message) { }
        public bool Confirm(string title, string message)
        {
            ConfirmCount++;
            LastMessage = message;
            return Answer;
        }
    }

    [Fact]
    public void Prepare_NoExisting_DoesNotAsk()
    {
        var dialog = new FakeDialog();
        Assert.NotNull(StructureRegeneration.Prepare(new FakeRepo(), dialog, StructureRegeneration.Reservoir));
        Assert.Equal(0, dialog.ConfirmCount);
    }

    [Fact]
    public void Prepare_Cancelled_ReturnsNullAndDeletesNothing()
    {
        var repo = new FakeRepo();
        repo.Structures[StructureRegeneration.PumpingStation] = new List<long> { 1, 2 };
        var dialog = new FakeDialog { Answer = false };

        Assert.Null(StructureRegeneration.Prepare(repo, dialog, StructureRegeneration.PumpingStation));
        Assert.Empty(repo.Deleted);
        Assert.Contains("2개", dialog.LastMessage);
    }

    [Fact]
    public void DeleteExisting_RemovesOnlySameStructure_AndMarkCreatedOnlyNewIds()
    {
        var repo = new FakeRepo();
        repo.Structures[StructureRegeneration.DischargeBasin] = new List<long> { 1, 2 };
        repo.Structures[StructureRegeneration.PumpingStation] = new List<long> { 3 };
        repo.Addin.AddRange(new long[] { 1, 2, 3, 4 });   // 4 = 다른 기능 부재

        var regeneration = StructureRegeneration.Prepare(repo, new FakeDialog(), StructureRegeneration.DischargeBasin)!;
        regeneration.DeleteExisting();
        repo.Addin.AddRange(new long[] { 10, 11 });
        regeneration.MarkCreated();

        Assert.Equal(new long[] { 1, 2 }, repo.Deleted);
        Assert.Equal(new[] { (10L, StructureRegeneration.DischargeBasin), (11L, StructureRegeneration.DischargeBasin) }, repo.Marked);
    }
}
