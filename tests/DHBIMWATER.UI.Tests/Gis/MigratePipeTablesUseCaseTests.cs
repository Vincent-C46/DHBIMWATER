using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Application.Interfaces.Piping;
using DHBIMWATER.Application.UseCases.Gis;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.UI.Tests.Gis;

public sealed class MigratePipeTablesUseCaseTests
{
    [Theory]
    [InlineData(true, false, 1, 0, 1)]
    [InlineData(false, false, 0, 1, 0)]
    [InlineData(false, true, 0, 1, 0)]
    public void OldTablesAreClearedOnlyAfterImportSucceeds(bool imported, bool skipped,
        int expectedSaves, int expectedRollbacks, int expectedCommits)
    {
        var legacy = new StubRepo(BendSettings.Default);
        var transaction = new StubTransaction();
        var catalog = new StubCatalog(new PipeTableMigrationResult(imported, skipped,
            imported || skipped ? string.Empty : "가져오기 실패"));

        var result = new MigratePipeTablesUseCase(transaction, legacy, catalog).Execute();

        Assert.Equal(imported, result.Imported);
        Assert.Equal(expectedSaves, legacy.Saves);
        Assert.Equal(expectedRollbacks, transaction.Rollbacks);
        Assert.Equal(expectedCommits, transaction.Commits);
    }

    private sealed class StubRepo(BendSettings settings) : IBendSettingsRepo
    {
        public int Saves { get; private set; }
        public BendSettings? Load() => settings;
        public void Save(BendSettings value) => Saves++;
        public void SavePlacementSettings(BendSettings value) => throw new NotSupportedException();
    }

    private sealed class StubTransaction : ITransactionContext
    {
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }
        public IReadOnlyList<string> SuppressedWarnings => [];
        public void Begin(string name, bool suppressWarnings = false) => Assert.Equal("규격표 이관", name);
        public void Commit() => Commits++;
        public void Rollback() => Rollbacks++;
        public void Dispose() { }
    }

    private sealed class StubCatalog(PipeTableMigrationResult result) : IPipeCatalogSource
    {
        public bool TryEnsureAvailable(out string error) { error = string.Empty; return true; }
        public PipeProjectTablesResult LoadProjectTables() => throw new NotSupportedException();
        public IPipeCatalog OpenCatalog() => throw new NotSupportedException();
        public bool ShowEditor(PipeMaterial? material) => throw new NotSupportedException();
        public PipeTableMigrationResult MigrateLegacyTables(BendSettings legacy) => result;
    }
}
