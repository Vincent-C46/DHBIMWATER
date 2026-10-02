using DHBIMWATER.Application.DTOs.Revit.ValveRoom;
using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Application.Interfaces;

// TODO: Void 전용이 아니므로 IDrainPitPlacementCommandRepo로 개명 검토(docs/153 § E-2).
public interface IDrainPitVoidCommandRepo
{
    /// <summary>일반모델 패밀리를 기초 상단 EL에 배치한다. Void 패밀리("로드할 때 보이드로 절단" 켜짐)면 호스트를 절단하고,
    /// 실물 피트 패밀리면 절단 없이 배치만 한다. 절단 실패는 경고로 반환한다(docs/153 § E).</summary>
    /// <param name="hostElementIds">기초와 버림콘크리트 절단 대상. 실물 패밀리면 쓰이지 않는다.</param>
    /// <param name="levelId">기초 상부 참조 레벨.</param>
    /// <param name="foundationThickness">밸브실 기초 두께(mm). 패밀리 인스턴스 매개변수 '밸브실_바닥슬라브두께'에 기록한다.</param>
    /// <param name="plainConcreteThickness">버림콘크리트 두께(mm). 패밀리 인스턴스 매개변수 '드레인피트_버림콘크리트_두께'에 기록한다.</param>
    DrainPitPlacementResult CreateDrainPitVoid(IReadOnlyList<int> hostElementIds, long levelId,
        DrainPitDefinition definition, DrainPitVoidFamilyMappingDto mapping,
        double foundationThickness, double plainConcreteThickness);
}
