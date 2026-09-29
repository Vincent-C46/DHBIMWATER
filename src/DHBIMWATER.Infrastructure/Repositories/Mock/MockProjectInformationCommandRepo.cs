using DHBIMWATER.Application.Interfaces;

namespace DHBIMWATER.Infrastructure.Repositories.Mock;

public sealed class MockProjectInformationCommandRepo : IProjectInformationCommandRepo
{
    public void SetTextParameters(IReadOnlyDictionary<string, string> values) { }
}
