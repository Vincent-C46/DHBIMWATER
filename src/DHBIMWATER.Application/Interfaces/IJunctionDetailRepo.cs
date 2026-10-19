using DHBIMWATER.Core.Piping;
using DHBIMWATER.Application.DTOs.Documentation;

namespace DHBIMWATER.Application.Interfaces;

public sealed record JunctionDetailGroup(string JunctionId, IReadOnlyList<JunctionNode> Nodes,
    IReadOnlyList<string>? Warnings = null);
public sealed record JunctionDetailViewChange(bool Created, IReadOnlyList<string> Warnings);
public sealed record JunctionDetailTagType(long Id, string Name);

public interface IJunctionDetailRepo
{
    IReadOnlyList<JunctionDetailGroup> GetJunctions();
    IReadOnlyList<JunctionPreviewItem> GetPreviewItems(JunctionDetailGroup group);
    string GetRoomType();
    IReadOnlyList<JunctionDetailTagType> GetTagTypes();
    JunctionDetailViewChange CreateOrUpdateView(JunctionDetailGroup group, IReadOnlyList<VerticalSlot> slots, JunctionDetailViewOptions options);
    IReadOnlyList<string> ResetOffsets(JunctionDetailGroup group);
    // 부재 둘러싼 밸브실 4면 벽(미리보기 축). 못 찾으면 Room null + 이유 (docs/293, docs/299)
    JunctionValveRoomLookup GetValveRoom(JunctionDetailGroup group) => new(null, null);
}
