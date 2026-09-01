using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Core.Gis;

public sealed class AlignmentVertexNodeLookup
{
    private readonly Dictionary<(long X, long Y, long Z), List<NetworkNode>> _cells = new();
    private readonly double _tolerance;
    private readonly double _cell;

    public AlignmentVertexNodeLookup(IReadOnlyList<NetworkNode> nodes, double tolerance)
    {
        _tolerance = tolerance;
        _cell = tolerance;
        foreach (var node in nodes)
        {
            var key = Key(node.Position);
            if (!_cells.TryGetValue(key, out var bucket)) _cells[key] = bucket = new List<NetworkNode>();
            bucket.Add(node);
        }
    }

    public int? Find(Point3D point)
    {
        var (x, y, z) = Key(point);
        NetworkNode? best = null;
        var bestDistance = double.MaxValue;
        for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
                for (var dz = -1; dz <= 1; dz++)
                {
                    if (!_cells.TryGetValue((x + dx, y + dy, z + dz), out var bucket)) continue;
                    foreach (var node in bucket)
                    {
                        var distance = node.Position.DistanceTo(point);
                        if (distance <= _tolerance && distance < bestDistance) { best = node; bestDistance = distance; }
                    }
                }
        return best?.Id;
    }

    private (long X, long Y, long Z) Key(Point3D point)
        => ((long)Math.Floor(point.X / _cell), (long)Math.Floor(point.Y / _cell), (long)Math.Floor(point.Z / _cell));
}
