using Autodesk.Revit.DB;
using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Core.Geometry;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling;

public sealed class RevitPipeAlignmentQueryRepo : IPipeAlignmentQueryRepo
{
    private readonly Func<Document?> _doc;
    public RevitPipeAlignmentQueryRepo(Func<Document?> doc) => _doc = doc;

    public IReadOnlyList<PipeAlignmentQueryResult> GetByElementIds(IReadOnlyList<int> elementIds)
    {
        var doc = _doc() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        var results = new List<PipeAlignmentQueryResult>();
        foreach (var id in elementIds)
        {
            if (doc.GetElement(new ElementId(id)) is not DirectShape shape) continue;
            var lines = shape.get_Geometry(new Options())?.OfType<Line>().ToList();
            if (lines is null || lines.Count == 0) continue;

            var vertices = new List<Point3D> { ToPoint3D(lines[0].GetEndPoint(0)) };
            vertices.AddRange(lines.Select(line => ToPoint3D(line.GetEndPoint(1))));

            var kind = shape.LookupParameter("DH_Part")?.AsString() ?? string.Empty;
            // TODO: DH_구경은 폐지되고 DH_호칭지름(텍스트 "DN300")으로 대체됐다(docs/155 § G).
            // 이 조회 경로는 IPipeAlignmentQueryRepo가 DI 등록만 되고 주입되는 곳이 없어 현재 미사용이다.
            var diameterFt = shape.LookupParameter("DH_구경")?.AsDouble() ?? 0;
            results.Add(new PipeAlignmentQueryResult((int)shape.Id.Value, vertices, kind, UC.FtToMm(diameterFt)));
        }
        return results;
    }

    private static Point3D ToPoint3D(XYZ p) => new(UC.FtToMm(p.X), UC.FtToMm(p.Y), UC.FtToMm(p.Z));
}
