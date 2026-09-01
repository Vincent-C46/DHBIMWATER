namespace DHBIMWATER.Core.Gis;

/// <summary>정점 DN을 폴리선 누가거리로 조회한다.</summary>
public sealed class AlignmentDiameterProfile
{
    private readonly IReadOnlyList<double> _distances;
    private readonly IReadOnlyList<double> _diameters;

    public AlignmentDiameterProfile(PipeAlignment alignment)
    {
        ArgumentNullException.ThrowIfNull(alignment);
        var distances = new double[alignment.Vertices.Count];
        for (var i = 1; i < distances.Length; i++)
            distances[i] = distances[i - 1] + alignment.Vertices[i - 1].DistanceTo(alignment.Vertices[i]);

        var diameters = new double[alignment.Vertices.Count];
        for (var i = 0; i < diameters.Length; i++)
            diameters[i] = DiameterAtVertex(alignment, i);

        _distances = distances;
        _diameters = diameters;
    }

    public double Find(double distanceFromStart)
    {
        if (_diameters.Count == 0) return 0d;
        if (distanceFromStart <= 0d) return _diameters[0];

        var low = 0;
        var high = _distances.Count - 1;
        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            if (_distances[mid] <= distanceFromStart) low = mid + 1;
            else high = mid - 1;
        }
        return _diameters[Math.Clamp(high, 0, _diameters.Count - 1)];
    }

    public static double DiameterAtVertex(PipeAlignment alignment, int vertexIndex)
        => alignment.VertexDiameterMm is { } values
           && vertexIndex >= 0 && vertexIndex < values.Count
           && values[vertexIndex] is > 0d
            ? values[vertexIndex]!.Value
            : alignment.DiameterMm;
}
