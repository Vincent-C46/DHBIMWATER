using Autodesk.Revit.DB;
using System.IO;
using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.Infrastructure.Helpers;
using DHBIMWATER.Shared.Diagnostics;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Gis;

/// <summary>직관을 OD·두께 기반 원통 DirectShape로 배치.</summary>
internal sealed class RevitAlignmentStraightDirectShapeRepo : IAlignmentStraightDirectShapeRepo
{
    private const string DcipMaterialName = "DCIP";
    private const double JoinToleranceM = 1e-6;
    private const double OversizedOuterDiameterMm = 3000d;
    private const int MaxWarningExamples = 5;
    // 150도 마이터의 반각 75도. 더 큰 기울기는 압출 길이 폭주 위험.
    private static readonly double MaxCutSlope = Math.Tan(StraightJointCutPlane.MaxMiterDeflectionDeg / 2d * Math.PI / 180d);
    private readonly Func<Document?> _doc;
    public RevitAlignmentStraightDirectShapeRepo(Func<Document?> doc) => _doc = doc;

    public AlignmentStraightPlacementResult PlaceAlong(IReadOnlyList<PipeAlignment> alignments, string straightFamilyTypeName, double intervalM, AlignmentPlacementOrigin origin,
        StraightPipeSpecTable specs, IReadOnlyList<IReadOnlyList<VertexTrim>>? trims = null, string? diameterParameterName = null,
        string? outerDiameterParameterName = null, string? thicknessParameterName = null, PipeInfoParameterContext? info = null,
        IProgress<PipeAlignmentProgress>? progress = null)
    {
        var doc = _doc() ?? throw new InvalidOperationException("활성 Revit 문서가 없습니다.");
        var minLengthFt = doc.Application.ShortCurveTolerance;
        var basePoint = AlignmentPlacementMapper.GetProjectBasePoint(doc);
        var placed = new List<(DirectShape Shape, PipeAlignment Alignment, double DiameterMm, StraightPipeSpec? Spec, AlignmentSampleSegment Segment, double ZOffsetM)>();
        var specCache = new Dictionary<(string PipeKind, double DiameterMm), StraightPipeSpec?>();
        var missingSpecs = new HashSet<(string PipeKind, double DiameterMm)>();
        var abandonedMiters = new HashSet<MiterWarning>();
        var oversizedOuterDiameters = new HashSet<(string PipeKind, double DiameterMm, double OuterDiameterMm)>();
        var cappedCutExtensions = 0;
        var sampled = alignments.Select((alignment, index) => AlignmentIntervalSampler.SampleSegments(
            alignment, intervalM, trims is not null && index < trims.Count ? trims[index] : null)).ToList();
        var total = sampled.Sum(x => x.Count);
        var materialId = total > 0 ? FindOrCreateDcipMaterial(doc) : ElementId.InvalidElementId;
        progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingStraights, 0, total));

        for (var index = 0; index < alignments.Count; index++)
        {
            var alignment = alignments[index];
            var diameterProfile = new AlignmentDiameterProfile(alignment);
            var segments = sampled[index];
            for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                var segment = segments[segmentIndex];
                var diameterMm = diameterProfile.Find(segment.DistanceFromStart);
                var specKey = (alignment.PipeKind, diameterMm);
                if (!specCache.TryGetValue(specKey, out var spec))
                    specCache[specKey] = spec = specs.Find(alignment.PipeKind, diameterMm);
                if (spec is null) missingSpecs.Add(specKey);
                var outerDiameterMm = spec?.OuterDiameterMm ?? diameterMm;

                var zOffsetM = origin.GetZOffsetM(alignment.PipeKind, diameterMm);
                var start = ToXyz(segment.Start, zOffsetM, origin, basePoint);
                var end = ToXyz(segment.End, zOffsetM, origin, basePoint);
                if (start.DistanceTo(end) < minLengthFt) continue;
                if (outerDiameterMm > OversizedOuterDiameterMm)
                    oversizedOuterDiameters.Add((alignment.PipeKind, diameterMm, outerDiameterMm));

                DirectShape shape;
                var startExtensionFt = double.NaN;
                var endExtensionFt = double.NaN;
                try
                {
                    using (PlacementProfiler.Step("06a 직관 DirectShape 생성"))
                    {
                        var cutNormals = CutNormals(segments, segmentIndex);
                        if (cutNormals.StartMiterAbandoned)
                            abandonedMiters.Add(new MiterWarning(AlignmentIdOf(alignment), segment.Start));

                        var cut = PreparePipeSolidCut(start, end, cutNormals.Start, cutNormals.End, outerDiameterMm);
                        startExtensionFt = cut.StartExtensionFt;
                        endExtensionFt = cut.EndExtensionFt;
                        cappedCutExtensions += cut.CappedExtensionCount;
                        var solid = BuildPipeSolid(start, end, cut, outerDiameterMm, spec?.ThicknessMm ?? 0d, materialId);
                        shape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                        shape.SetShape(new GeometryObject[] { solid });
                        shape.Name = $"직관_{alignment.PipeKind}_{diameterMm:0}";
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"직관 DirectShape 생성 실패: {AlignmentIdOf(alignment)}, segmentIndex={segmentIndex}, " +
                        $"원본 좌표(m) {FormatPoint(segment.Start)} ~ {FormatPoint(segment.End)}, 적용 외경={outerDiameterMm:0.###}mm, " +
                        $"startExtension={FormatExtension(startExtensionFt)}ft, endExtension={FormatExtension(endExtensionFt)}ft", ex);
                }
                placed.Add((shape, alignment, diameterMm, spec, segment, zOffsetM));
                progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingStraights, placed.Count, total));
            }
        }
        progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingStraights, total, total));
        PlacementProfiler.Count("직관 DirectShape", placed.Count);

        var writer = new PipeParameterWriter();
        using (PlacementProfiler.Step("06c 직관 파라미터 기록"))
        {
            PipeAlignment? previousAlignment = null;
            AlignmentStationProfile? stationProfile = null;
            foreach (var (shape, alignment, diameterMm, spec, segment, zOffsetM) in placed)
            {
                if (info is not { Enabled: true }) continue;
                if (!ReferenceEquals(alignment, previousAlignment))
                {
                    previousAlignment = alignment;
                    stationProfile = new AlignmentStationProfile(alignment);
                }

                var lengthM = segment.Start.DistanceTo(segment.End);
                var runM = Math.Sqrt(Math.Pow(segment.End.X - segment.Start.X, 2) + Math.Pow(segment.End.Y - segment.Start.Y, 2));
                var riseM = segment.End.Z - segment.Start.Z;
                var startStationM = stationProfile!.StationAt(segment.DistanceFromStart);
                var endStationM = stationProfile.StationAt(segment.DistanceFromStart + lengthM);
                writer.Text(shape, PipeAlignmentParameters.Addin, PipeAlignmentParameters.AddinValue);
                writer.Text(shape, PipeAlignmentParameters.Part, PipeAlignmentParameters.StraightPartValue);
                writer.Text(shape, PipeAlignmentParameters.AlignmentId, AlignmentIdOf(alignment));
                writer.Text(shape, PipeAlignmentParameters.NominalDiameter, PipeAlignmentParameters.DiameterText(diameterMm));
                writer.Text(shape, PipeAlignmentParameters.Material, MaterialLabelOf(alignment.PipeKind));
                writer.Text(shape, PipeAlignmentParameters.Grade, alignment.PipeKind);
                writer.LengthMm(shape, PipeAlignmentParameters.OuterDiameter, spec?.OuterDiameterMm ?? diameterMm);
                writer.LengthMm(shape, PipeAlignmentParameters.WallThickness, spec?.ThicknessMm ?? 0d);
                writer.Text(shape, PipeAlignmentParameters.JointType, info.JointType);
                writer.Text(shape, PipeAlignmentParameters.ElevationDatum, PipeAlignmentParameters.Label(info.ZDatum));
                writer.Text(shape, PipeAlignmentParameters.SourceFile, Path.GetFileName(alignment.SourceFile));
                writer.Text(shape, PipeAlignmentParameters.RecordNumber, alignment.RecordNumber);
                writer.LengthM(shape, PipeAlignmentParameters.Length, lengthM);
                writer.Number(shape, PipeAlignmentParameters.StartElevation, segment.Start.Z);
                writer.Number(shape, PipeAlignmentParameters.EndElevation, segment.End.Z);
                writer.Number(shape, PipeAlignmentParameters.Slope, runM > 1e-9 ? riseM / runM * 100d : 0d);
                writer.Text(shape, PlacementStationParameters.SourceFile, alignment.SourceFile);
                writer.Text(shape, PlacementStationParameters.Alignment, alignment.RecordNumber);
                writer.Text(shape, PlacementStationParameters.Profile, string.Empty);
                writer.Number(shape, PlacementStationParameters.StaStart, startStationM);
                writer.Number(shape, PlacementStationParameters.StaEnd, endStationM);
                writer.Number(shape, PlacementStationParameters.ZOffset, zOffsetM);
                writer.YesNo(shape, PlacementStationParameters.Flip, false);
                writer.Text(shape, PlacementStationParameters.VerticalBasis, "GlobalZ");
                writer.Text(shape, PlacementStationParameters.ShapeKind, "Cylinder");
            }
        }

        var warnings = new List<string>();
        if (missingSpecs.Count > 0)
        {
            var values = string.Join(", ", missingSpecs.OrderBy(x => x.PipeKind).ThenBy(x => x.DiameterMm)
                .Select(x => $"{x.PipeKind}/DN{x.DiameterMm:0.##}"));
            warnings.Add($"직관 제원이 없어 관종/DN {missingSpecs.Count}건은 호칭지름을 외경으로 간주해 속 찬 원기둥으로 만들었습니다: {values}");
        }
        if (abandonedMiters.Count > 0)
        {
            var examples = string.Join(", ", abandonedMiters.OrderBy(x => x.AlignmentId)
                .ThenBy(x => x.Point.X).ThenBy(x => x.Point.Y).ThenBy(x => x.Point.Z)
                .Take(MaxWarningExamples)
                .Select(x => $"{x.AlignmentId} 원본좌표(m) {FormatPoint(x.Point)}"));
            warnings.Add($"되꺾임 절점 {abandonedMiters.Count}곳(편각 > {StraightJointCutPlane.MaxMiterDeflectionDeg:0}°)은 마이터 절단 대신 관 축 직각 절단을 적용했습니다. 예: {examples}");
        }
        if (cappedCutExtensions > 0)
            warnings.Add($"절단 여유 기울기 상한 tan(75°)을 적용한 절단면 {cappedCutExtensions}건이 있습니다.");
        if (oversizedOuterDiameters.Count > 0)
        {
            var values = string.Join(", ", oversizedOuterDiameters.OrderBy(x => x.PipeKind).ThenBy(x => x.DiameterMm)
                .Select(x => $"{x.PipeKind}/DN{x.DiameterMm:0.##}: OD {x.OuterDiameterMm:0.##}mm"));
            warnings.Add($"적용 외경이 {OversizedOuterDiameterMm:0}mm를 초과한 관종/DN {oversizedOuterDiameters.Count}건이 있습니다: {values}");
        }
        if (writer.InvalidTypes.Count > 0)
            warnings.Add($"프로젝트 매개변수 {string.Join(", ", writer.InvalidTypes.OrderBy(x => x))}의 타입이 배치 태그 계약과 달라 값을 건너뛰었습니다.");
        var missingProjectParameters = writer.Missing.Except(writer.InvalidTypes).OrderBy(x => x).ToList();
        if (missingProjectParameters.Count > 0)
            warnings.Add($"프로젝트 매개변수 {string.Join(", ", missingProjectParameters)}를 직관 DirectShape에 기록하지 못했습니다(바인딩 실패 또는 읽기전용).");

        var outerDiameters = placed
            .Where(x => x.Spec is not null)
            .GroupBy(x => (x.Alignment.PipeKind, x.DiameterMm))
            .ToDictionary(x => x.Key, x => x.First().Spec!.OuterDiameterMm);
        return new AlignmentStraightPlacementResult(placed.Count, warnings, outerDiameters);
    }

    private static PipeSolidCut PreparePipeSolidCut(XYZ start, XYZ end,
        DHBIMWATER.Core.Geometry.Vector3D startCutNormal, DHBIMWATER.Core.Geometry.Vector3D endCutNormal,
        double outerDiameterMm)
    {
        var axis = (end - start).Normalize();
        var startNormal = OrientToAxis(ToXyz(startCutNormal), axis);
        var endNormal = OrientToAxis(ToXyz(endCutNormal), axis);
        var radiusFt = UC.MmToFt(outerDiameterMm / 2d);
        var startExtensionFt = CutExtension(axis, startNormal, radiusFt, out var startCapped);
        var endExtensionFt = CutExtension(axis, endNormal, radiusFt, out var endCapped);
        return new PipeSolidCut(axis, startNormal, endNormal, radiusFt, startExtensionFt, endExtensionFt,
            (startCapped ? 1 : 0) + (endCapped ? 1 : 0));
    }

    private static Solid BuildPipeSolid(XYZ start, XYZ end, PipeSolidCut cut, double outerDiameterMm,
        double thicknessMm, ElementId materialId)
    {
        var extrusionStart = start - cut.Axis * cut.StartExtensionFt;
        var axis = cut.Axis;
        var seed = Math.Abs(axis.Z) > 0.9 ? XYZ.BasisX : XYZ.BasisZ;
        var xAxis = axis.CrossProduct(seed).Normalize();
        var yAxis = axis.CrossProduct(xAxis).Normalize();
        var loops = new List<CurveLoop> { CircleLoop(extrusionStart, cut.RadiusFt, xAxis, yAxis) };
        var innerRadiusMm = outerDiameterMm / 2d - thicknessMm;
        if (thicknessMm > 0d && innerRadiusMm > 0.01)
            loops.Add(CircleLoop(extrusionStart, UC.MmToFt(innerRadiusMm), xAxis, yAxis));
        var options = new SolidOptions(materialId, ElementId.InvalidElementId);
        var solid = GeometryCreationUtilities.CreateExtrusionGeometry(loops, axis,
            start.DistanceTo(end) + cut.StartExtensionFt + cut.EndExtensionFt, options);
        solid = BooleanOperationsUtils.CutWithHalfSpace(solid, Plane.CreateByNormalAndOrigin(cut.StartNormal, start));
        return BooleanOperationsUtils.CutWithHalfSpace(solid, Plane.CreateByNormalAndOrigin(-cut.EndNormal, end));
    }

    private static (DHBIMWATER.Core.Geometry.Vector3D Start, DHBIMWATER.Core.Geometry.Vector3D End,
        bool StartMiterAbandoned, bool EndMiterAbandoned) CutNormals(
        IReadOnlyList<AlignmentSampleSegment> segments, int index)
    {
        var own = DirectionOf(segments[index]);
        var hasPrevious = index > 0 && segments[index - 1].End.DistanceTo(segments[index].Start) <= JoinToleranceM;
        var previous = hasPrevious ? DirectionOf(segments[index - 1]) : own;
        var startMiterAbandoned = hasPrevious && StraightJointCutPlane.ExceedsMiterLimit(
            previous, own, StraightJointCutPlane.MaxMiterDeflectionDeg);
        var start = hasPrevious && !startMiterAbandoned
            ? StraightJointCutPlane.Bisect(previous, own)
            : own;
        var hasNext = index + 1 < segments.Count && segments[index].End.DistanceTo(segments[index + 1].Start) <= JoinToleranceM;
        var next = hasNext ? DirectionOf(segments[index + 1]) : own;
        var endMiterAbandoned = hasNext && StraightJointCutPlane.ExceedsMiterLimit(
            own, next, StraightJointCutPlane.MaxMiterDeflectionDeg);
        var end = hasNext && !endMiterAbandoned
            ? StraightJointCutPlane.Bisect(own, next)
            : own;
        return (start, end, startMiterAbandoned, endMiterAbandoned);
    }

    private static DHBIMWATER.Core.Geometry.Vector3D DirectionOf(AlignmentSampleSegment segment)
        => new DHBIMWATER.Core.Geometry.Vector3D(
            segment.End.X - segment.Start.X,
            segment.End.Y - segment.Start.Y,
            segment.End.Z - segment.Start.Z).Normalize();

    private static XYZ OrientToAxis(XYZ normal, XYZ axis)
        => normal.DotProduct(axis) >= 0d ? normal : -normal;

    private static double CutExtension(XYZ axis, XYZ normal, double radiusFt, out bool capped)
    {
        var rawSlope = StraightJointCutPlane.CutSlope(ToVector3D(axis), ToVector3D(normal), double.MaxValue);
        capped = rawSlope > MaxCutSlope;
        var slope = Math.Min(rawSlope, MaxCutSlope);
        return radiusFt * slope + UC.MmToFt(1d);
    }

    private static ElementId FindOrCreateDcipMaterial(Document doc)
    {
        var existing = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>()
            .FirstOrDefault(x => string.Equals(x.Name, DcipMaterialName, StringComparison.OrdinalIgnoreCase));
        return existing?.Id ?? Material.Create(doc, DcipMaterialName);
    }

    /// <summary>완전한 원 대신 반원 Arc 2개로 폐합.</summary>
    private static CurveLoop CircleLoop(XYZ center, double radiusFt, XYZ xAxis, XYZ yAxis)
    {
        var loop = new CurveLoop();
        loop.Append(Arc.Create(center, radiusFt, 0, Math.PI, xAxis, yAxis));
        loop.Append(Arc.Create(center, radiusFt, Math.PI, 2 * Math.PI, xAxis, yAxis));
        return loop;
    }

    private static XYZ ToXyz(DHBIMWATER.Core.Geometry.Point3D point, double zOffsetM, AlignmentPlacementOrigin origin, XYZ basePoint)
        => AlignmentPlacementMapper.ToXyz(point, zOffsetM, origin.X, origin.Y, basePoint);

    private static XYZ ToXyz(DHBIMWATER.Core.Geometry.Vector3D vector) => new(vector.X, vector.Y, vector.Z);
    private static DHBIMWATER.Core.Geometry.Vector3D ToVector3D(XYZ vector) => new(vector.X, vector.Y, vector.Z);

    private static string FormatPoint(DHBIMWATER.Core.Geometry.Point3D point)
        => FormattableString.Invariant($"({point.X:0.000}, {point.Y:0.000}, {point.Z:0.000})");

    private static string FormatExtension(double extensionFt)
        => double.IsFinite(extensionFt) ? extensionFt.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "계산 실패";

    private static string AlignmentIdOf(PipeAlignment alignment) => $"{Path.GetFileNameWithoutExtension(alignment.SourceFile)}#{alignment.RecordNumber}";
    private static string MaterialLabelOf(string pipeKind) => PipeKindCatalog.MaterialOf(pipeKind) is { } material ? PipeAlignmentParameters.Label(material) : string.Empty;

    private sealed record MiterWarning(string AlignmentId, DHBIMWATER.Core.Geometry.Point3D Point);
    private readonly record struct PipeSolidCut(XYZ Axis, XYZ StartNormal, XYZ EndNormal, double RadiusFt,
        double StartExtensionFt, double EndExtensionFt, int CappedExtensionCount);
}
