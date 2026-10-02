using DHBIMWATER.Application.DTOs.Revit.ValveRoom;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Infrastructure.Repositories.Mock;

public sealed class MockDrainPitVoidCommandRepo : IDrainPitVoidCommandRepo
{
    public DrainPitPlacementResult CreateDrainPitVoid(IReadOnlyList<int> hostElementIds, long levelId,
        DrainPitDefinition definition, DrainPitVoidFamilyMappingDto mapping,
        double foundationThickness, double plainConcreteThickness) => new(0, true, null);
}
