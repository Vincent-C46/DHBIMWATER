using DHBIMWATER.Application.Interfaces;

namespace DHBIMWATER.Infrastructure.Repositories.Mock;

public sealed class MockJunctionDetailSheetRepo : IJunctionDetailSheetRepo
{
    public JunctionDetailSheetChange CreateOrUpdateSheet(JunctionDetailSheetRequest request)
        => new(JunctionDetailSheetStatus.Created, []);
}
