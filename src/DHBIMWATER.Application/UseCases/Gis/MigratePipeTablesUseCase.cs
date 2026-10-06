using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Application.Interfaces.Piping;

namespace DHBIMWATER.Application.UseCases.Gis;

public sealed class MigratePipeTablesUseCase
{
    private readonly ITransactionContext _transaction;
    private readonly IBendSettingsRepo _legacy;
    private readonly IPipeCatalogSource _catalog;

    public MigratePipeTablesUseCase(ITransactionContext transaction, IBendSettingsRepo legacy, IPipeCatalogSource catalog)
    { _transaction = transaction; _legacy = legacy; _catalog = catalog; }

    public PipeTableMigrationResult Execute()
    {
        var settings = _legacy.Load();
        if (settings is null || settings.StraightPipes.Entries.Count == 0
            && settings.Fittings.Entries.Count == 0 && settings.JointDeflections.Entries.Count == 0)
            return new PipeTableMigrationResult(false, true, string.Empty);

        using (_transaction)
        {
            try
            {
                _transaction.Begin("규격표 이관");
                var result = _catalog.MigrateLegacyTables(settings);
                if (result.Skipped) { _transaction.Rollback(); return result; }
                if (!result.Imported) throw new InvalidOperationException(result.Error);
                _legacy.Save(settings);
                _transaction.Commit();
                return result;
            }
            catch (Exception ex)
            {
                _transaction.Rollback();
                return new PipeTableMigrationResult(false, false, ex.Message);
            }
        }
    }
}
