using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.Infrastructure.Helpers;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

internal sealed class RevitAlignmentPipePlacementRepo : IAlignmentPipePlacementRepo
{
    private readonly Func<Document?> _doc;
    private readonly IProjectLocationCoordinateTransformRepo _coordinateTransform;
    public RevitAlignmentPipePlacementRepo(Func<Document?> doc, IProjectLocationCoordinateTransformRepo coordinateTransform)
    { _doc = doc; _coordinateTransform = coordinateTransform; }
    public int PlaceAlong(IReadOnlyList<PipeAlignment> alignments, string systemName, string pipeTypeName, string? levelName, double intervalM, AlignmentPlacementOrigin origin, CancellationToken cancellationToken = default)
    {
        var doc = _doc() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        var system = new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).Cast<PipingSystemType>().FirstOrDefault(x => x.Name == systemName) ?? throw new DhbimwaterException(ErrorCodes.PA_PipingTypeNotFound, $"파이프 시스템 유형을 찾을 수 없습니다: {systemName}");
        var type = new FilteredElementCollector(doc).OfClass(typeof(PipeType)).Cast<PipeType>().FirstOrDefault(x => x.Name == pipeTypeName) ?? throw new DhbimwaterException(ErrorCodes.PA_PipingTypeNotFound, $"PipeType을 찾을 수 없습니다: {pipeTypeName}");
        var level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault(x => levelName is null || x.Name == levelName) ?? throw new DhbimwaterException(ErrorCodes.PA_PipingTypeNotFound, "레벨을 찾을 수 없습니다.");
        // Revit 짧은 커브 허용치보다 짧은 구간은 Pipe 생성이 불가하므로 건너뛴다 (Beam 리포지토리와 동일 기준).
        var minLengthFt = doc.Application.ShortCurveTolerance;
        var count = 0;
        foreach (var alignment in alignments)
        {
            var zOffsetM = origin.GetZOffsetM(alignment);
            Pipe? previous = null;
            var segments = AlignmentIntervalSampler.SampleSegments(alignment.Vertices, intervalM);
            var sourcePoints = segments.SelectMany(segment => new[] { segment.Start, segment.End }).ToArray();
            var converted = ProjectLocationCoordinateConverter.Convert(_coordinateTransform, sourcePoints,
                Enumerable.Repeat(zOffsetM, sourcePoints.Length).ToArray());
            for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();   // docs/209 § A — 사용자 취소
                var start = converted[segmentIndex * 2];
                var end = converted[segmentIndex * 2 + 1];
                if (start.DistanceTo(end) < minLengthFt) continue;
                var pipe = Pipe.Create(doc, system.Id, type.Id, level.Id, start, end);
                if (alignment.DiameterMm > 0) pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.Set(UC.MmToFt(alignment.DiameterMm));
                // 건너뛴 구간의 previous는 유지해, 다음 파이프를 새 시작점에서 이전 파이프와 연결한다.
                if (previous is not null) Connect(doc, previous, pipe, start);
                previous = pipe; count++;
            }
        }
        return count;
    }
    private static void Connect(Document doc, Pipe first, Pipe second, XYZ point)
    {
        var a = first.ConnectorManager.Connectors.Cast<Connector>().OrderBy(x => x.Origin.DistanceTo(point)).First();
        var b = second.ConnectorManager.Connectors.Cast<Connector>().OrderBy(x => x.Origin.DistanceTo(point)).First();
        try { if (a.CoordinateSystem.BasisZ.IsAlmostEqualTo(b.CoordinateSystem.BasisZ) || a.CoordinateSystem.BasisZ.IsAlmostEqualTo(-b.CoordinateSystem.BasisZ)) doc.Create.NewUnionFitting(a, b); else doc.Create.NewElbowFitting(a, b); }
        catch (Exception ex)
        {
            // PipeType 부속 설정이 맞지 않으면 이 연결부만 생략하고 직선 파이프는 유지한다.
            System.Diagnostics.Debug.WriteLine($"선형 파이프 구간 연결 생략: {ex.Message}");
        }
    }
}
