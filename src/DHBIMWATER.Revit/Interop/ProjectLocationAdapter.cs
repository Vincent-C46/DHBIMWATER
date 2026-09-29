using Autodesk.Revit.DB;
using DHBoost.Revit.Interop;
using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Revit.Interop;

internal sealed class ProjectLocationAdapter : IProjectLocationCommandRepo,
    IProjectLocationQueryRepo, IProjectLocationCoordinateTransformRepo
{
    private const int ExpectedContractVersion = 1;
    private readonly Func<Document?> _document;

    public ProjectLocationAdapter(Func<Document?> document) => _document = document;

    public void SetInternalOriginSharedPosition(double eastWestMeters, double northSouthMeters,
        double trueNorthToProjectNorthClockwiseDegrees)
        => SetOrigin(eastWestMeters, northSouthMeters, trueNorthToProjectNorthClockwiseDegrees);

    public void SetInternalOriginSharedPositionPreservingAngle(double eastWestMeters, double northSouthMeters)
        => SetOrigin(eastWestMeters, northSouthMeters, null);

    public (double EastWestMeters, double NorthSouthMeters) GetProjectBasePointSharedPosition()
    {
        var document = GetDocument();
        if (!ProjectLocationInterop.TryGetOriginSharedPosition(document, out var eastingM, out var northingM, out _, out var error))
            throw new DhbimwaterException(ErrorCodes.BasePointQueryFailed, $"기준점 조회 실패: {error}");
        return (eastingM, northingM);
    }

    public double GetTrueNorthToProjectNorthClockwiseDegrees()
    {
        var document = GetDocument();
        if (!ProjectLocationInterop.TryGetOriginSharedPosition(document, out _, out _, out var angleDeg, out var error))
            throw new DhbimwaterException(ErrorCodes.TrueNorthQueryFailed, $"진북각 조회 실패: {error}");
        return angleDeg;
    }

    public IReadOnlyList<ProjectLocationModelPoint> ConvertSharedToModel(IReadOnlyList<Point3D> points,
        IReadOnlyList<double> zOffsetsM)
    {
        if (points.Count != zOffsetsM.Count)
            throw new DhbimwaterException(ErrorCodes.CoordinateCountMismatch, "좌표와 표고 보정값 개수가 다릅니다.");

        var document = GetDocument();
        var eastingsM = points.Select(point => point.X).ToArray();
        var northingsM = points.Select(point => point.Y).ToArray();
        var elevationsM = points.Select((point, index) => point.Z + zOffsetsM[index]).ToArray();
        var converted = ProjectLocationInterop.ConvertSharedToModel(document, eastingsM, northingsM, elevationsM, out var error);
        if (!string.IsNullOrWhiteSpace(error) || converted.Length != points.Count)
            throw new DhbimwaterException(ErrorCodes.SharedCoordinateConvertFailed, $"공유좌표 변환 실패: {error}");
        return converted.Select(point => new ProjectLocationModelPoint(point.X, point.Y, point.Z)).ToArray();
    }

    private void SetOrigin(double eastWestMeters, double northSouthMeters, double? trueNorthClockwiseDeg)
    {
        var document = GetDocument();
        if (!ProjectLocationInterop.SetOriginSharedPosition(document, eastWestMeters, northSouthMeters,
                trueNorthClockwiseDeg, out var error))
            throw new DhbimwaterException(ErrorCodes.BasePointSetFailed, $"기준점 설정 실패: {error}");
    }

    private Document GetDocument()
    {
        InteropInitializer.EnsureInitialized();
        if (ProjectLocationInterop.ContractVersion != ExpectedContractVersion)
            throw new DhbimwaterException(ErrorCodes.DhBoostContractMismatch,
                $"dll 계약 버전이 맞지 않습니다(v{ProjectLocationInterop.ContractVersion}, 필요 v{ExpectedContractVersion}).");
        return _document() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
    }
}
