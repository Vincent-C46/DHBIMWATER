using DHBIMWATER.Application.Interfaces;

namespace DHBIMWATER.Infrastructure.Repositories.Mock;

public sealed class MockPipeJointConnectionRepo : IPipeJointConnectionRepo
{
    public IReadOnlyList<string> RecordConnectionJoints() => [];
}
