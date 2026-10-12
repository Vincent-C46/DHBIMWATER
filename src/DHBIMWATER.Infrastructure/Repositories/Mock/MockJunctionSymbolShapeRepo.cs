using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Infrastructure.Repositories.Mock;

internal sealed class MockJunctionSymbolShapeRepo : IJunctionSymbolShapeRepo
{
    public JunctionSymbolShapeReadResult Read(IReadOnlyList<long> elementIds)
        => new(new Dictionary<long, JunctionSymbolShape>(), []);
}
