using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Interfaces.Gis;

namespace DHBIMWATER.Infrastructure.Repositories.Mock;

public sealed class MockBendDirectShapeRepo : IBendDirectShapeRepo
{
    public AdaptiveBendPlacementResult Place(IReadOnlyList<AdaptiveBendPlacementPlan> plans, AlignmentPlacementOrigin origin, PipeInfoParameterContext? info = null, IProgress<PipeAlignmentProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingBends, plans.Count, plans.Count));
        return new AdaptiveBendPlacementResult(plans.Count, Array.Empty<string>());
    }
}
