using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Application.UseCases.Documentation;

/// <summary>부재 원자료에서 격점 스캔 결과를 만든다(docs/208 § D-1).
/// 일람표·도면 UseCase가 같은 규칙으로 대상을 잡도록 한곳에 둔다 — 종전에는 일람표 Repo의 <c>Scan()</c>과
/// 도면 UseCase의 <c>ScanOf</c>가 같은 값을 다른 코드로 만들었다.</summary>
public static class PipeMaterialScan
{
    public static PipeMaterialScheduleScan Of(IReadOnlyList<PipeMaterialPart> parts)
        => new(PipeMaterialTable.JunctionIds(parts), PipeMaterialTable.UnassignedCount(parts), parts.Count);
}
