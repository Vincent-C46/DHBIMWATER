using DHBIMWATER.Application.Interfaces;

namespace DHBIMWATER.Infrastructure.Repositories.Mock;

public sealed class MockProjectInformationQueryRepo : IProjectInformationQueryRepo
{
    public string GetTextParameter(string parameterName) => string.Empty;
}
