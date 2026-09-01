using DHBIMWATER.Application.DTOs.Gis;

namespace DHBIMWATER.Application.Interfaces.Gis;

public interface IReducerDirectShapeRepo
{
    ReducerPlacementResult Place(IReadOnlyList<ReducerDirectShapePlacementPlan> plans,
        AlignmentPlacementOrigin origin, PipeInfoParameterContext? info = null,
        IProgress<PipeAlignmentProgress>? progress = null);
}
