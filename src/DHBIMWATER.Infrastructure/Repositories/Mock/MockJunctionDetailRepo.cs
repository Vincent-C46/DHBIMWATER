using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Application.DTOs.Documentation;

namespace DHBIMWATER.Infrastructure.Repositories.Mock;

public sealed class MockJunctionDetailRepo : IJunctionDetailRepo
{
    public IReadOnlyList<JunctionDetailGroup> GetJunctions() => [];
    public IReadOnlyList<JunctionPreviewItem> GetPreviewItems(JunctionDetailGroup group) => [];
    public string GetRoomType() => string.Empty;
    public IReadOnlyList<JunctionDetailTagType> GetTagTypes() => [];
    public JunctionDetailViewChange CreateOrUpdateView(JunctionDetailGroup group, IReadOnlyList<VerticalSlot> slots, JunctionDetailViewOptions options)
        => new(true, []);
    public IReadOnlyList<string> ResetOffsets(JunctionDetailGroup group) => [];
}
