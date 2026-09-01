using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Core.Gis;

public static class AlignmentVertexDirection
{
    public static Vector3D? SkippingNear(IReadOnlyList<Point3D> vertices, int index, int step, double tolerance)
    {
        for (var i = index + step; i >= 0 && i < vertices.Count; i += step)
        {
            if (vertices[index].DistanceTo(vertices[i]) <= tolerance) continue;
            var vector = new Vector3D(vertices[i].X - vertices[index].X, vertices[i].Y - vertices[index].Y, vertices[i].Z - vertices[index].Z);
            return vector.Length <= 1e-9 ? null : vector.Normalize();
        }
        return null;
    }
}
