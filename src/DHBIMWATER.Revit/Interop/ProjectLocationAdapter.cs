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
    {
        // 원점 공유좌표만 바꾸면 내부원점에서 떨어진 PBP는 옛 자리에 남아 입력값과 다른 값을 보인다(2026-10-23).
        // PBP를 먼저 내부원점으로 옮기고 원점을 설정 — PBP 표시값 = 입력값.
        MoveProjectBasePointToInternalOrigin();
        SetOrigin(eastWestMeters, northSouthMeters, trueNorthToProjectNorthClockwiseDegrees);
    }

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

    /// <summary>PBP가 내부원점에 없으면 옮긴다. 모델·공유좌표는 안 바뀜. 트랜잭션은 호출 UseCase가 연다.</summary>
    private void MoveProjectBasePointToInternalOrigin()
    {
        var document = GetDocument();
        var basePoint = BasePoint.GetProjectBasePoint(document);
        if (basePoint is null) return;
        var offset = basePoint.Position;
        if (offset.IsAlmostEqualTo(XYZ.Zero)) return;

        // 고정된 PBP는 이동 전에 풀고 끝나면 되돌린다.
        var wasPinned = basePoint.Pinned;
        try
        {
            if (wasPinned) basePoint.Pinned = false;
            ElementTransformUtils.MoveElement(document, basePoint.Id, offset.Negate());
        }
        catch (Exception ex)
        {
            throw new DhbimwaterException(ErrorCodes.BasePointSetFailed, $"프로젝트 기준점을 내부원점으로 옮기지 못했습니다: {ex.Message}");
        }
        finally
        {
            if (wasPinned) basePoint.Pinned = true;
        }
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
