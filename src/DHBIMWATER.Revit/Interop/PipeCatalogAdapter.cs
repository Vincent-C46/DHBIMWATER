using Autodesk.Revit.DB;
using DHBoost.Revit.Interop;
using DHBIMWATER.Application.Interfaces.Piping;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Revit.Interop;

internal sealed class PipeCatalogAdapter : IPipeCatalogSource
{
    private const int ExpectedCatalogContractVersion = 3;
    private const int ExpectedRulesContractVersion = 5;
    private readonly Func<Document?> _document;
    private string _installWarning = string.Empty;

    public PipeCatalogAdapter(Func<Document?> document) => _document = document;

    public bool TryEnsureAvailable(out string error)
    {
        try
        {
            InteropInitializer.EnsureInitialized();
            var catalogVersion = PipeCatalogInterop.ContractVersion;
            var rulesVersion = PipeQuantityRulesInterop.ContractVersion;
            if (catalogVersion != ExpectedCatalogContractVersion || rulesVersion != ExpectedRulesContractVersion)
            {
                error = $"규격표 dll이 맞지 않아 실행할 수 없습니다(규격표 계약 v{catalogVersion}/v{ExpectedCatalogContractVersion}, 수량 규칙 계약 v{rulesVersion}/v{ExpectedRulesContractVersion}). DHBIMWATER를 다시 설치하세요.";
                return false;
            }
            if (!PipeCatalog.IsInstalled)
            {
                var warnings = PipeCatalog.Install(PipeCatalogDataMapper.Map(PipeCatalogInterop.GetData(PipeCatalogInterop.LoadDefaults())));
                _installWarning = string.Join(Environment.NewLine, warnings);
                foreach (var warning in warnings) System.Diagnostics.Trace.TraceWarning(warning);
            }
            error = _installWarning;
            return true;
        }
        catch (Exception ex)
        {
            error = $"규격표 dll이 맞지 않아 실행할 수 없습니다(계약 확인 실패: {ex.Message}). DHBIMWATER를 다시 설치하세요.";
            return false;
        }
    }

    public PipeProjectTablesResult LoadProjectTables()
    {
        var catalog = PipeCatalogInterop.Load(RequiredDocument());
        var (straight, fittings, joints) = PipeCatalogDataMapper.ProjectTables(PipeCatalogInterop.GetData(catalog).ProjectTables);
        return new PipeProjectTablesResult(straight, fittings, joints, PipeCatalogInterop.Source(catalog));
    }

    public IPipeCatalog OpenCatalog() => new CatalogHandle(PipeCatalogInterop.Load(RequiredDocument()));

    public bool ShowEditor(PipeMaterial? material)
        => PipeCatalogInterop.ShowEditor(RequiredDocument(), material is null ? null :
            Enum.Parse<PipeMaterialCode>(material.Value.ToString()), BrandCode.DHBIMWATER);

    public PipeTableMigrationResult MigrateLegacyTables(BendSettings legacy)
    {
        var document = RequiredDocument();
        if (PipeCatalogInterop.HasProjectTables(document)) return new PipeTableMigrationResult(false, true, string.Empty);
        if (legacy.StraightPipes.Entries.Count == 0 && legacy.Fittings.Entries.Count == 0
            && legacy.JointDeflections.Entries.Count == 0)
            return new PipeTableMigrationResult(false, true, string.Empty);
        var imported = PipeCatalogInterop.ImportProjectTables(document,
            PipeCatalogDataMapper.ProjectTables(legacy), out var error);
        return new PipeTableMigrationResult(imported, false, error);
    }

    private Document RequiredDocument() => _document() ?? throw new InvalidOperationException("활성 Revit 문서가 없습니다.");

    internal sealed record CatalogHandle(PipeCatalogHandle Handle) : IPipeCatalog;
}
