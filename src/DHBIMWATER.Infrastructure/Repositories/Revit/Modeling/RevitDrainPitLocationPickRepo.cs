using Autodesk.Revit.UI;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Geometry;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling;

public sealed class RevitDrainPitLocationPickRepo : IDrainPitLocationPickRepo
{
    private readonly Func<UIDocument?> _uiDoc;
    public RevitDrainPitLocationPickRepo(Func<UIDocument?> uiDoc) => _uiDoc = uiDoc;

    public Point2D? PickLocation()
    {
        var uiDoc = _uiDoc();
        if (uiDoc == null) return null;
        try
        {
            var point = uiDoc.Selection.PickPoint("드레인 피트 중심 위치를 지정하세요");
            return new Point2D(UC.FtToMm(point.X), UC.FtToMm(point.Y));
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return null; }
    }
}
