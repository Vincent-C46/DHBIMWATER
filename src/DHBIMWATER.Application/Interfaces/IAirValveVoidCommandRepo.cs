using DHBIMWATER.Application.DTOs.Revit.ValveRoom;

namespace DHBIMWATER.Application.Interfaces;

/// <summary>공기밸브실 독립기초의 면에 Void 패밀리를 호스팅한다.</summary>
public interface IAirValveVoidCommandRepo
{
    void CreateAirValveFoundationVoid(int foundationElementId, AirValveVoidPlacementDefinition definition);
    /// <summary>Void 절단 실패 경고를 꺼내고 비운다(docs/238 § C). 종전에는 트랜잭션 도중 TaskDialog로 띄웠다. 기본 구현은 경고가 없다.</summary>
    IReadOnlyList<string> TakeWarnings() => Array.Empty<string>();
}
