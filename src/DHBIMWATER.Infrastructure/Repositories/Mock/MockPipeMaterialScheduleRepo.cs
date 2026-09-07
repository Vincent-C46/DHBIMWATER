using DHBIMWATER.Application.Interfaces;

namespace DHBIMWATER.Infrastructure.Repositories.Mock;

public sealed class MockPipeMaterialScheduleRepo : IPipeMaterialScheduleRepo
{
    public PipeMaterialScheduleScan Scan() => new([], 0, 0);
    public bool ScheduleExists(string scheduleName) => false;
    public void CreateSchedule(string scheduleName, string junctionId) { }
}
