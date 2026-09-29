using DHBIMWATER.Application.Interfaces;

namespace DHBIMWATER.Infrastructure.Repositories.Mock;

public sealed class MockElementParameterCommandRepo : IElementParameterCommandRepo
{
    public void SetTextParameter(IReadOnlyList<int> elementIds, string parameterName, string value) { }
}
