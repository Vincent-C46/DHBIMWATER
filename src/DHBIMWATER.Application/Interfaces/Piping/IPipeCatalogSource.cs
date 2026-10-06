using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Application.Interfaces.Piping;

public interface IPipeCatalog { }

public sealed record PipeProjectTablesResult(StraightPipeSpecTable StraightPipes,
    BendFittingCatalog Fittings, JointDeflectionTable JointDeflections, string Source);

public sealed record PipeTableMigrationResult(bool Imported, bool Skipped, string Error);

public interface IPipeCatalogSource
{
    bool TryEnsureAvailable(out string error);
    PipeProjectTablesResult LoadProjectTables();
    IPipeCatalog OpenCatalog();
    bool ShowEditor(PipeMaterial? material);
    PipeTableMigrationResult MigrateLegacyTables(BendSettings legacy);
}
