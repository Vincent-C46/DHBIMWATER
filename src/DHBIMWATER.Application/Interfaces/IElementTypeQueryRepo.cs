using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DHBIMWATER.Application.Interfaces
{
    public interface IElementTypeQueryRepo
    {
        IEnumerable<string> GetSlabTypeNames();
        IEnumerable<string> GetWallTypeNames();
        IEnumerable<string> GetColumnTypeNames();
        /// <summary>구조 기둥(OST_StructuralColumns) 유형 전체를 "패밀리명 : 타입명"으로 반환한다.
        /// 밸브실 기둥 유형 콤보 전용 — 패밀리가 달라도 유형명이 같을 수 있어 구분이 필요하다(docs/159 § A-2).
        /// <para><see cref="GetColumnTypeNames"/>(유형명 단독)는 배수지 창이 계속 쓰므로 남겨 둔다.</para></summary>
        IEnumerable<string> GetColumnFamilyTypeNames();
        IEnumerable<string> GetBeamTypeNames();
        /// <summary>구조 재료가 콘크리트인 기둥 유형명. <see cref="GetColumnTypeNames"/>의 부분집합이며 정렬 규칙도 같다.
        /// 판정 기준은 Family.StructuralMaterialType == Concrete로, FindOrCreateBeamType과 같다(docs/156 § 0-2).</summary>
        IEnumerable<string> GetConcreteColumnTypeNames();
        /// <summary>구조 재료가 콘크리트인 보 유형명. <see cref="GetBeamTypeNames"/>의 부분집합임이 보장돼야 한다 —
        /// 목록에 없는 이름이 기본 선택으로 들어가면 ComboBox가 빈칸이 된다(docs/156 § B-2).</summary>
        IEnumerable<string> GetConcreteBeamTypeNames();
        /// <summary>구조 프레임(OST_StructuralFraming) 유형 전체를 "패밀리명 : 타입명"으로 반환한다.
        /// <b>재료로 거르지 않는다</b> — 구조 프레임 패밀리 템플릿의 기본 재료 유형이 강철이라, 콘크리트로 바꾸지 않은
        /// DH_헌치 같은 패밀리가 목록에서 통째로 사라졌기 때문이다(docs/159 § 0-2).
        /// <para>밸브실의 <b>보 유형·헌치 유형 콤보가 공용</b>한다. 재료 판정은 목록이 아니라 기본값 자동 선택
        /// (<see cref="GetConcreteBeamTypeNames"/>)에서만 쓴다.</para></summary>
        IEnumerable<string> GetStructuralFramingFamilyTypeNames();
        /// <summary>프로젝트에 로드된 Adaptive Component 패밀리의 타입명 목록. "패밀리명 : 타입명" 형식으로 반환한다. 직관(2점)·곱관(5점) 목록이 공용한다.</summary>
        IEnumerable<string> GetAdaptiveComponentTypeNames();
        /// <summary>familyTypeName("패밀리명 : 타입명") 인스턴스에 값을 쓸 수 있는 파라미터명 목록. 직경·관종 매핑 후보로 쓴다.</summary>
        IEnumerable<string> GetAdaptiveInstanceParameterNames(string familyTypeName);
        /// <summary>familyTypeName("패밀리명 : 타입명")이 가진 Adaptive Point 개수. 확인할 수 없으면 -1.</summary>
        int GetAdaptiveBendPointCount(string familyTypeName);
        /// <summary>familyTypeName("패밀리명 : 타입명")이 속한 카테고리의 <c>BuiltInCategory</c> 정수값. 확인할 수 없으면 null.
        /// <para>적응형 패밀리 목록(<see cref="GetAdaptiveComponentTypeNames"/>)은 카테고리를 가리지 않으므로,
        /// 공유 매개변수를 붙일 카테고리를 배치 직전에 실측할 때 쓴다(docs/141).</para></summary>
        int? GetFamilyTypeCategoryId(string familyTypeName);
        IEnumerable<string> GetPipingSystemTypeNames();
        IEnumerable<string> GetPipeTypeNames();
        IEnumerable<string> GetLevelNames();
        /// <summary>프로젝트에 로드된 배관부속(Pipe Accessory) 패밀리의 타입명 목록. "패밀리명 : 타입명" 형식으로 반환한다.</summary>
        IEnumerable<string> GetPipeAccessoryTypeNames();
        /// <summary>프로젝트에 로드된 배관 부속류(Pipe Fitting) 패밀리의 타입명 목록. "패밀리명 : 타입명" 형식으로 반환한다.
        /// PipeLayout의 직관·단관·곡관·T형 콤보 소스로 쓴다.</summary>
        IEnumerable<string> GetPipeFittingTypeNames();
        /// <summary>배관부속 familyTypeName("패밀리명 : 타입명") 인스턴스에 값을 쓸 수 있는 파라미터명 목록.
        /// PipeLayout의 단관 길이 파라미터 후보로 쓰며 카테고리와 무관하게 동작한다.</summary>
        IEnumerable<string> GetPipeAccessoryInstanceParameterNames(string familyTypeName);
        /// <summary>배관부속 인스턴스 파라미터 중 <b>실수형(Double)</b>만. 밸브 직경 콤보 후보 전용이다(docs/233 § B-1) —
        /// 예/아니오(`주석 축척 사용`)·문자열 파라미터가 직경으로 선택돼 배치 사전 검사에서 세트가 통째로 건너뛰어지는 것을 막는다.
        /// 다른 콤보(길이·각도·예/아니오)는 종전 <see cref="GetPipeAccessoryInstanceParameterNames"/>를 그대로 쓴다.</summary>
        IEnumerable<string> GetPipeAccessoryNumericInstanceParameterNames(string familyTypeName);
        /// <summary>배관부속 유형("패밀리명 : 타입명")의 <b>유형 매개변수</b> 중 길이 계열(mm 표시 가능 스펙)만 이름·값(mm)으로(docs/233 § B-2).
        /// 값은 확인용이며 쓰지 않는다. 유형을 못 찾거나 문서가 없으면 빈 목록.</summary>
        IReadOnlyList<DTOs.Revit.PipeAccessoryTypeParameterDto> GetPipeAccessoryTypeLengthParameters(string familyTypeName);
        /// <summary>프로젝트에 로드된 일반모델(Generic Model) 패밀리의 타입명 목록. "패밀리명 : 타입명" 형식으로 반환한다.</summary>
        IEnumerable<string> GetGenericModelTypeNames();
        IEnumerable<string> GetFoundationTypeNames();
        /// <summary>선택된 빔 유형에 배치 가능한 인스턴스 파라미터명 목록. 직경·관종 필드를 패밀리 파라미터에 매핑할 때 후보로 쓴다.</summary>
        IEnumerable<string> GetBeamInstanceParameterNames(string beamTypeName);
        /// <summary>구조 프레임 유형("패밀리명 : 타입명")의 단면 폭(mm). 유형을 못 찾거나 단면폭 파라미터가
        /// 없으면 0. 제수밸브실 상부 헌치를 보 폭만큼 끊는 데 쓴다(docs/162 § B-1).</summary>
        double GetStructuralFramingSectionWidthMm(string familyTypeName);
        /// <summary>구조 기둥 유형("패밀리명 : 타입명")의 단면 치수(mm). WidthX는 패밀리 로컬 X(b),
        /// DepthY는 로컬 Y(h)에 해당한다. 유형을 못 찾거나 파라미터가 없으면 각각 0.
        /// 제수밸브실 상부 보를 기둥 자리에서 끊는 데 쓴다(docs/202 § A).</summary>
        (double WidthX, double DepthY) GetStructuralColumnSectionSizeMm(string familyTypeName);
        /// <summary>EditFamily로 읽어 둔 패밀리 단위 캐시를 버린다. 패밀리가 재로드·편집됐을 때 호출한다(docs/154 § A).
        /// 캐시만 비우고 Revit 조회는 하지 않는다.</summary>
        void InvalidateFamilyCaches();
    }
}
