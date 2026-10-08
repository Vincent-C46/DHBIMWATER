using DHBIMWATER.Core.Piping;
using DHBIMWATER.Application.DTOs.Documentation;

namespace DHBIMWATER.Application.Interfaces;

public sealed record JunctionDetailGroup(string JunctionId, IReadOnlyList<JunctionNode> Nodes);
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
}
