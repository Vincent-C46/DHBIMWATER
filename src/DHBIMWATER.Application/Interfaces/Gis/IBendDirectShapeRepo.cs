using DHBIMWATER.Application.DTOs.Gis;

namespace DHBIMWATER.Application.Interfaces.Gis;

public interface IBendDirectShapeRepo
{
    AdaptiveBendPlacementResult Place(
        IReadOnlyList<AdaptiveBendPlacementPlan> plans,
        AlignmentPlacementOrigin origin,
        PipeInfoParameterContext? info = null,
        IProgress<PipeAlignmentProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
