using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Core.Gis;

namespace DHBIMWATER.Application.Interfaces.Gis;

public interface IAlignmentSnapshotStore
{
    /// <summary>선형을 모델 안 스냅샷으로 저장하고 Id를 돌려준다. 실패하면 빈 문자열과 경고 문구를 돌린다.
    /// 열린 트랜잭션 안에서 호출한다.</summary>
    string EnsureSnapshot(PipeAlignment alignment, AlignmentStationProfile profile, AlignmentPlacementOrigin origin, out string warning);
}
