using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Infrastructure.Repositories.Mock;

public sealed class MockPipeMaterialScheduleRepo : IPipeMaterialScheduleRepo
{
    public IReadOnlyList<PipeMaterialPart> GetParts() => [];
    public IReadOnlySet<string> GetExistingViewNames() => new HashSet<string>();
    public void CreateSchedule(string scheduleName, string junctionId) { }
}
