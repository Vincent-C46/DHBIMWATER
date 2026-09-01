using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Infrastructure.Repositories.Mock;

public sealed class MockDrainPitVoidCommandRepo : IDrainPitVoidCommandRepo
{
    public string? CreateDrainPitVoid(int hostElementId, DrainPitDefinition definition) => null;
}
