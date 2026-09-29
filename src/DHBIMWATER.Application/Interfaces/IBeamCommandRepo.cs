using DHBIMWATER.Core.Structures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DHBIMWATER.Application.Interfaces
{
    public interface IBeamCommandRepo
    {
        /// <param name="joinTargetSlabId">VR-B 보가 결합할 상부슬래브 ElementId(docs/207 § C). 0이면 종전처럼 문서에서 겹치는 Floor를 찾는다.
        /// 배수지·펌프장 호출부는 넘기지 않으므로 동작이 바뀌지 않는다.</param>
        public int CreateBeam(BeamDefinition beamDef, long levelId, int beamTypeId, int joinTargetSlabId = 0);
        /// <summary>받은 헌치들 중 솔리드가 닿거나 겹치는 쌍을 형상 결합한다(코너 4곳·이토실 중간벽 T접합부).
        /// <b>레벨 그룹 분리는 호출부 책임</b>이다 — 같은 레벨끼리만 결합하려면 레벨별로 나눠 호출한다(docs/162 § C-4).</summary>
        void JoinHaunches(IReadOnlyList<int> haunchIds);
        /// <summary>밸브실 보(VR-B) 상단 맞춤·상부슬래브 결합에서 모아 둔 경고를 꺼내고 비운다(docs/238 § C).
        /// 종전에는 트랜잭션 도중 TaskDialog로 띄웠다. 호출부가 완료 대화상자에 함께 보여준다. 기본 구현(Mock 등)은 경고가 없다.</summary>
        IReadOnlyList<string> TakeWarnings() => Array.Empty<string>();
    }
}
