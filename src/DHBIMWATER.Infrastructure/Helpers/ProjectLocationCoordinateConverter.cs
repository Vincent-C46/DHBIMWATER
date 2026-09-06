using Autodesk.Revit.DB;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Infrastructure.Helpers;

internal static class ProjectLocationCoordinateConverter
{
    public static XYZ[] Convert(IProjectLocationCoordinateTransformRepo transform,
        IReadOnlyList<Point3D> points, IReadOnlyList<double> zOffsetsM)
        => transform.ConvertSharedToModel(points, zOffsetsM)
            .Select(point => new XYZ(point.XFeet, point.YFeet, point.ZFeet))
            .ToArray();
}
