using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Infrastructure.Repositories.Mock;

public sealed class MockDrainPitLocationPickRepo : IDrainPitLocationPickRepo
{
    public Point2D? PickLocation() => null;
}
