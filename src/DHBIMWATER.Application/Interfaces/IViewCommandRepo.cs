using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Structures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DHBIMWATER.Application.Interfaces
{
    public interface IViewCommandRepo
    {
        int CreateSectionView(SectionViewDefinition sectionViewDef);
        /// <summary>절단점에서 Up × Right 방향을 바라보는 단면도를 만든다(위 = +Z). 같은 이름의 단면도는 지우고 다시 만든다(docs/246).</summary>
        int CreateSectionView(SectionCutDefinition cut);
        //int UpdateSectionView(SectionViewDefinition sectionViewDef);
    }
}
