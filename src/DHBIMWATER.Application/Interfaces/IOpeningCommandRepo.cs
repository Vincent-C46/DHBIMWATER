using DHBIMWATER.Core.Structures;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DHBIMWATER.Application.Interfaces
{
    public interface IOpeningCommandRepo
    {
        /// <summary>
        /// 사각 슬래브 개구부. 만든 요소 ID를 돌려주고, <b>만들지 못하면 0</b>이다
        /// (호스트 슬래브·레벨·패밀리를 못 찾은 경우). 호출부가 실패를 알 수 있어야 해서 void 에서 바꿨다.
        /// </summary>
        int CreateSlabOpening(RectangularSlabOpeningDefinition openingDef);
        void CreateSlabOpening(CircularSlabOpeningDefinition openingDef);
        void CreateWallOpening(RectangularWallOpeningDefinition openingDef);
        void CreateWallOpening(CircularWallOpeningDefinition openingDef);
    }
}
