using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Interfaces.Gis;

namespace DHBIMWATER.Infrastructure.Repositories.Mock;

public sealed class MockReducerDirectShapeRepo : IReducerDirectShapeRepo
{
    public ReducerPlacementResult Place(IReadOnlyList<ReducerDirectShapePlacementPlan> plans, AlignmentPlacementOrigin origin,
        PipeInfoParameterContext? info = null, IProgress<PipeAlignmentProgress>? progress = null)
    {
        progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingReducers, plans.Count, plans.Count));
        return new ReducerPlacementResult(plans.Count, Array.Empty<string>());
    }
}
