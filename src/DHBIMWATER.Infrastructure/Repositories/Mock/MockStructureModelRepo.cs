using DHBIMWATER.Application.Interfaces;

namespace DHBIMWATER.Infrastructure.Repositories.Mock;

public sealed class MockStructureModelRepo : IStructureModelRepo
{
    public IReadOnlyList<long> GetAddinElementIds() => Array.Empty<long>();
    public IReadOnlyList<long> GetStructureElementIds(string structure) => Array.Empty<long>();
    public void DeleteElements(IReadOnlyCollection<long> elementIds) { }
    public void MarkStructure(IEnumerable<long> elementIds, string structure) { }
}
