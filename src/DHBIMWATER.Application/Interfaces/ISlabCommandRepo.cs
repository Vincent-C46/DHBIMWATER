using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Structures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DHBIMWATER.Application.Interfaces
{
    public interface ISlabCommandRepo
    {
        int CreateSlab (SlabDefinition slabDef, long levelId, int slabTypeId);

        /// <summary>슬래브와 교차하는 구조 프레임(보·헌치)·벽을 형상 결합. 실패 쌍은 건너뛰고 경고로 돌려줌(docs/289 § D).</summary>
        IReadOnlyList<string> JoinWithIntersectingFramingAndWalls(int slabId) => [];
    }
}
