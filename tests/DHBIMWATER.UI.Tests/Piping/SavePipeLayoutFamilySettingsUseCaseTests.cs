using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.UseCases.AutoGenerator;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.UI.Tests.Piping;

/// <summary>PipeLayout 패밀리 설정 저장 UseCase(docs/175 § E-2). 트랜잭션 안에서 Save가 불리고, 실패하면 롤백한다.</summary>
public sealed class SavePipeLayoutFamilySettingsUseCaseTests
{
    [Fact]
    public void Execute_SavesInsideTransaction()
    {
        var transaction = new StubTransaction();
        var repo = new StubRepo();
        var settings = new PipeLayoutFamilySettings(SegmentFamilyTypeName: "DH_직관 : DN100", LengthParameterName: "길이");

        new SavePipeLayoutFamilySettingsUseCase(transaction, repo).Execute(settings);

        Assert.Same(settings, repo.Saved);
        Assert.Equal(["Begin", "Commit", "Dispose"], transaction.Log);
    }

    [Fact]
    public void Execute_RollsBack_WhenRepoThrows()
    {
        var transaction = new StubTransaction();
        var repo = new StubRepo { Throw = true };

        Assert.Throws<InvalidOperationException>(() =>
            new SavePipeLayoutFamilySettingsUseCase(transaction, repo).Execute(PipeLayoutFamilySettings.Empty));

        Assert.Equal(["Begin", "Rollback", "Dispose"], transaction.Log);
    }

    private sealed class StubTransaction : ITransactionContext
    {
        public List<string> Log { get; } = [];
        public void Begin(string name, bool suppressWarnings = false) => Log.Add("Begin");
        public void Commit() => Log.Add("Commit");
        public void Rollback() => Log.Add("Rollback");
        public IReadOnlyList<string> SuppressedWarnings => Array.Empty<string>();
        public void Dispose() => Log.Add("Dispose");
    }

    private sealed class StubRepo : IPipeLayoutFamilySettingsRepo
    {
        public bool Throw { get; init; }
        public PipeLayoutFamilySettings? Saved { get; private set; }
        public PipeLayoutFamilySettings? Load() => Saved;
        public void Save(PipeLayoutFamilySettings settings)
        {
            if (Throw) throw new InvalidOperationException("저장 실패");
            Saved = settings;
        }
    }
}
