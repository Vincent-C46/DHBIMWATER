namespace DHBIMWATER.Core.Gis;

public sealed class AlignmentStationProfile
{
    private const double DegenerateEpsilon = 1e-9;
    private const double MismatchToleranceM = 0.5;
    private readonly double[] _distances;
    private readonly IReadOnlyList<(double DistanceM, double StationM)> _sourceStations;

    public AlignmentStationProfile(PipeAlignment alignment)
    {
        ArgumentNullException.ThrowIfNull(alignment);

        _distances = new double[alignment.Vertices.Count];
        for (var index = 1; index < alignment.Vertices.Count; index++)
        {
            var length = alignment.Vertices[index - 1].DistanceTo(alignment.Vertices[index]);
            _distances[index] = _distances[index - 1] + (length > DegenerateEpsilon ? length : 0d);
        }

        _sourceStations = alignment.VertexStationM?
            .Take(Math.Min(alignment.VertexStationM.Count, _distances.Length))
            .Select((station, index) => (Index: index, Station: station))
            .Where(x => x.Station is { } value && double.IsFinite(value))
            .Select(x => (_distances[x.Index], x.Station!.Value))
            .ToList()
            ?? [];

        HasSourceStation = _sourceStations.Count >= 2;
        if (!HasSourceStation) return;

        for (var index = 1; index < _sourceStations.Count; index++)
        {
            var previous = _sourceStations[index - 1];
            var current = _sourceStations[index];
            if (Math.Abs((current.StationM - previous.StationM) - (current.DistanceM - previous.DistanceM)) > MismatchToleranceM)
                MismatchCount++;
        }
    }

    public bool HasSourceStation { get; }
    public int MismatchCount { get; }

    public double StationAt(double distanceFromStartM)
    {
        if (!HasSourceStation) return distanceFromStartM;

        var anchor = _sourceStations[0];
        foreach (var source in _sourceStations)
        {
            if (source.DistanceM > distanceFromStartM) break;
            anchor = source;
        }
        return anchor.StationM + (distanceFromStartM - anchor.DistanceM);
    }

    public double DistanceAtVertex(int vertexIndex)
    {
        if ((uint)vertexIndex >= (uint)_distances.Length) throw new ArgumentOutOfRangeException(nameof(vertexIndex));
        return _distances[vertexIndex];
    }
}
