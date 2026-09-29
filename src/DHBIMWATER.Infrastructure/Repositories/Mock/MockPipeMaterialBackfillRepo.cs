using DHBIMWATER.Application.Interfaces;

namespace DHBIMWATER.Infrastructure.Repositories.Mock;

public sealed class MockPipeMaterialBackfillRepo : IPipeMaterialBackfillRepo
{
    public PipeMaterialBackfillScan ScanCandidates() => new(0, false);
    public PipeMaterialBackfillResult Backfill() => new(0, 0, []);
}
