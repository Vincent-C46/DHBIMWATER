using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Core.Gis;

namespace DHBIMWATER.Infrastructure.Repositories.Mock;

internal sealed class MockAlignmentStraightDirectShapeRepo : IAlignmentStraightDirectShapeRepo
{
    public AlignmentStraightPlacementResult PlaceAlong(IReadOnlyList<PipeAlignment> alignments, string straightFamilyTypeName, double intervalM, AlignmentPlacementOrigin origin,
        StraightPipeSpecTable specs, IReadOnlyList<IReadOnlyList<VertexTrim>>? trims = null, string? diameterParameterName = null,
        string? outerDiameterParameterName = null, string? thicknessParameterName = null, PipeInfoParameterContext? info = null,
        IProgress<PipeAlignmentProgress>? progress = null, IReadOnlyList<IReadOnlySet<int>>? stepVertices = null, CancellationToken cancellationToken = default)
    {
        var count = alignments.Select((x, i) => AlignmentIntervalSampler.SampleSegments(
            x, intervalM, trims is not null && i < trims.Count ? trims[i] : null,
            stepVertices is not null && i < stepVertices.Count ? stepVertices[i] : null).Count).Sum();
        progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingStraights, count, count));
        return new AlignmentStraightPlacementResult(count, Array.Empty<string>());
    }
}
