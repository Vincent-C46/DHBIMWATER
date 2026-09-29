using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Structures;
using DHBIMWATER.Infrastructure.Services.Mock;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace DHBIMWATER.Infrastructure.Repositories.Mock
{
    public class MockBeamCommandRepo : IBeamCommandRepo
    {
        public int CreateBeam(BeamDefinition beamDef, long levelId, int beamTypeId, int joinTargetSlabId = 0)
        {
            var mockDialogService = new MockDialogService();
            mockDialogService.Info("Beam Creation", $"{beamDef.ElementCode} 작성완료");
            return 0;
        }

        /// <summary>Mock에는 결합할 실물 지오메트리가 없다(docs/162 § D-7).</summary>
        public void JoinHaunches(IReadOnlyList<int> haunchIds) { }
    }
}
