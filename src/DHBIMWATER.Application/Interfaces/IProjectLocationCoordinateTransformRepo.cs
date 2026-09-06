using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Application.Interfaces;

/// <summary>공유좌표를 활성 Revit 문서의 모델 좌표로 일괄 변환한다.</summary>
public interface IProjectLocationCoordinateTransformRepo
{
    IReadOnlyList<ProjectLocationModelPoint> ConvertSharedToModel(
        IReadOnlyList<Point3D> points,
        IReadOnlyList<double> zOffsetsM);
}
