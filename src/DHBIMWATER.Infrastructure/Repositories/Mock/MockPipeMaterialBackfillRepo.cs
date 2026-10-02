using DHBIMWATER.Application.Interfaces;

namespace DHBIMWATER.Infrastructure.Repositories.Mock;

public sealed class MockPipeMaterialBackfillRepo : IPipeMaterialBackfillRepo
{
    public PipeMaterialBackfillScan ScanCandidates() => new(0, false);
    public PipeMaterialBackfillResult Backfill(DHBIMWATER.Core.Piping.BendSettings catalog) => new(0, 0, [], 0, 0);
}
