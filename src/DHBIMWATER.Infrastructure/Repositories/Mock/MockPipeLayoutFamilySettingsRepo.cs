using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Infrastructure.Repositories.Mock;

/// <summary>Revit 없이 동작하는 메모리 보관용 구현(docs/175 § E-3). Sandbox/테스트 환경에서만 쓴다.</summary>
public sealed class MockPipeLayoutFamilySettingsRepo : IPipeLayoutFamilySettingsRepo
{
    private static PipeLayoutFamilySettings? _stored;

    public PipeLayoutFamilySettings? Load() => _stored;

    public void Save(PipeLayoutFamilySettings settings) => _stored = settings;
}
