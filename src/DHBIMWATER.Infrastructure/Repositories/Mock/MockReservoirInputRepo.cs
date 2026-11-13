using DHBIMWATER.Application.Interfaces;

namespace DHBIMWATER.Infrastructure.Repositories.Mock;

/// <summary>Revit 없이 동작하는 메모리 보관용 구현. Sandbox/테스트 환경 전용.</summary>
public sealed class MockReservoirInputRepo : IReservoirInputRepo
{
    private static string? _stored;

    public string? Load() => _stored;

    public void Save(string json) => _stored = json;
}
