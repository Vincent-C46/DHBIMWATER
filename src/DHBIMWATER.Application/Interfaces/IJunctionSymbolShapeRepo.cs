using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Application.Interfaces;

public interface IJunctionSymbolShapeRepo
{
    JunctionSymbolShapeReadResult Read(IReadOnlyList<long> elementIds);
}
