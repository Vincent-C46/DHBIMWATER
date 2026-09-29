using Autodesk.Revit.DB;
using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.Infrastructure.Helpers;
using System.IO;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling;

public sealed class RevitPipeAlignmentCommandRepo : IPipeAlignmentCommandRepo
{
    private const double MinimumSegmentFeet = 1.0 / 256.0;
    private readonly Func<Document?> _doc;
    private readonly IProjectLocationCoordinateTransformRepo _coordinateTransform;
    public RevitPipeAlignmentCommandRepo(Func<Document?> doc, IProjectLocationCoordinateTransformRepo coordinateTransform)
    { _doc = doc; _coordinateTransform = coordinateTransform; }

    public int DeletePreviews()
    {
        var doc = _doc() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        // 관리 코드 LookupParameter×2를 전 GenericModel DirectShape에 돌리던 경로다(docs/209 § E). Revit 필터로 좁힌다.
        var addin = SharedParameterElement.Lookup(doc, PipeAlignmentParameters.Guids[PipeAlignmentParameters.Addin]);
        var category = SharedParameterElement.Lookup(doc, PipeAlignmentParameters.CategoryGuid);
        if (addin is null || category is null) return 0;   // 바인딩 자체가 없으면 우리 미리보기도 없다.
        var filter = new LogicalAndFilter(
            new ElementParameterFilter(ParameterFilterRuleFactory.CreateEqualsRule(addin.Id, "DHBIMWATER")),
            new ElementParameterFilter(ParameterFilterRuleFactory.CreateEqualsRule(category.Id, "관로")));
        var ids = new FilteredElementCollector(doc)
            .OfClass(typeof(DirectShape))
            .OfCategory(BuiltInCategory.OST_GenericModel)
            .WhereElementIsNotElementType()
            .WherePasses(filter)
            .ToElementIds()
            .ToList();
        if (ids.Count == 0) return 0;
        doc.Delete(ids);
        return ids.Count;
    }

    public PipeAlignmentCreateResult Create(PipeAlignmentCreateDefinition definition, IProgress<PipeAlignmentProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var doc = _doc() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        var created = 0; var skipped = 0; var warnings = new List<string>(); var createdIds = new List<long>();
        progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingStraights, 0, definition.Alignments.Count));
        for (var alignmentIndex = 0; alignmentIndex < definition.Alignments.Count; alignmentIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();   // docs/209 § A — 사용자 취소
            var alignment = definition.Alignments[alignmentIndex];
            var zOffsetM = definition.Origin.GetZOffsetM(alignment);
            if (alignment.Vertices.Count < 2) { warnings.Add($"{Path.GetFileName(alignment.SourceFile)} 레코드 {alignment.RecordNumber}: 정점이 2개 미만이라 건너뛰었습니다."); continue; }
            var points = ProjectLocationCoordinateConverter.Convert(_coordinateTransform, alignment.Vertices,
                Enumerable.Repeat(zOffsetM, alignment.Vertices.Count).ToArray());
            var lines = new List<GeometryObject>();
            for (var i = 1; i < alignment.Vertices.Count; i++)
            {
                var start = points[i - 1];
                var end = points[i];
                if (start.DistanceTo(end) < MinimumSegmentFeet) { skipped++; continue; }
                lines.Add(Line.CreateBound(start, end));
            }
            if (lines.Count == 0) { warnings.Add($"{Path.GetFileName(alignment.SourceFile)} 레코드 {alignment.RecordNumber}: 유효한 구간이 없어 건너뛰었습니다."); continue; }
            var shape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
            shape.SetShape(lines);
            shape.Name = $"관로_{alignment.PipeKind}_{alignment.RecordNumber}";
            SetText(shape, "DH_Addin", "DHBIMWATER");
            SetText(shape, "DH_Category", "관로");
            SetText(shape, "DH_Part", alignment.PipeKind);
            // 미리보기도 최종 배치와 같은 어휘("DN300")를 쓴다. DH_구경은 폐지(docs/155 § G).
            SetText(shape, PipeAlignmentParameters.NominalDiameter, PipeAlignmentParameters.DiameterText(alignment.DiameterMm));
            SetLength(shape, "DH_연장", CalculateLengthMm(alignment.Vertices));
            // DH_시점표고·DH_종점표고는 docs/178에서, DH_레코드번호는 docs/214에서 폐지됐다
            // (레코드번호는 배치 경로의 `Alignment` 태그와 중복이었다).
            created++;
            createdIds.Add(shape.Id.Value);   // 미리보기 직후 현재 뷰 Zoom용(docs/226 § D)
            progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingStraights, alignmentIndex + 1, definition.Alignments.Count));
        }
        progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingStraights, definition.Alignments.Count, definition.Alignments.Count));
        return new PipeAlignmentCreateResult(created, skipped, warnings, createdIds);
    }

    private static double CalculateLengthMm(IReadOnlyList<DHBIMWATER.Core.Geometry.Point3D> vertices) => vertices.Zip(vertices.Skip(1), (a, b) => a.DistanceTo(b)).Sum() * 1000;
    private static void SetText(Element element, string name, string value) => element.LookupParameter(name)?.Set(value ?? string.Empty);
    private static void SetLength(Element element, string name, double millimeters) => element.LookupParameter(name)?.Set(UC.MmToFt(millimeters));
}
