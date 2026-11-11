using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Infrastructure.Repositories.Mock;

/// <summary>Revit 없는 Sandbox용(docs/323). 패밀리 실측은 Revit에서만 되므로 항상 실패 — 창은 폭을 지금 값으로 둔다.</summary>
public sealed class MockTieValveRoomProbeRepo : ITieValveRoomProbeRepo
{
    public TieValveRoomProbe Measure(double mainDiameterMm, PipeMaterial material, SteelFittingClass steelClass)
        => throw new InvalidOperationException("타이밸브실 최소 폭 실측은 Revit에서만 됩니다.");
}
