using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Structures;

namespace DHBIMWATER.Infrastructure.Repositories.Mock;

/// <summary>Revit 없이 동작하는 메모리 보관용 구현(docs/312). Sandbox/테스트 환경에서만 쓴다.</summary>
public sealed class MockValveRoomDimensionCatalogRepo : IValveRoomDimensionCatalogRepo
{
    private static ValveRoomDimensionTable? _stored;

    public ValveRoomDimensionTable? Load() => _stored;

    public void Save(ValveRoomDimensionTable table) => _stored = table;
}
