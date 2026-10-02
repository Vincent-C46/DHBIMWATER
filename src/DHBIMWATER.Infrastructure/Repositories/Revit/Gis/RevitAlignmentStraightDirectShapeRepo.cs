using Autodesk.Revit.DB;
using System.IO;
using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.Infrastructure.Helpers;
using DHBIMWATER.Shared.Diagnostics;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;
using DHBIMWATER.Shared.Errors;
using DHBIMWATER.Core.Piping;
using PipeCountRules = DHBIMWATER.Core.Piping.PipeCountRules;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Gis;

/// <summary>직관을 OD·두께 기반 원통 DirectShape로 배치.</summary>
internal sealed class RevitAlignmentStraightDirectShapeRepo : IAlignmentStraightDirectShapeRepo
{
    private const double JoinToleranceM = 1e-6;
    private const double OversizedOuterDiameterMm = 3000d;
    private const int MaxWarningExamples = 5;
    // 150도 마이터의 반각 75도. 더 큰 기울기는 압출 길이 폭주 위험.
    private static readonly double MaxCutSlope = Math.Tan(StraightJointCutPlane.MaxMiterDeflectionDeg / 2d * Math.PI / 180d);
    private readonly Func<Document?> _doc;
    private readonly IProjectLocationCoordinateTransformRepo _coordinateTransform;
    private readonly IAlignmentSnapshotStore? _snapshotStore;
    public RevitAlignmentStraightDirectShapeRepo(Func<Document?> doc, IProjectLocationCoordinateTransformRepo coordinateTransform,
        IAlignmentSnapshotStore? snapshotStore = null)
    {
        _doc = doc;
        _coordinateTransform = coordinateTransform;
        _snapshotStore = snapshotStore;
    }

    public AlignmentStraightPlacementResult PlaceAlong(IReadOnlyList<PipeAlignment> alignments, string straightFamilyTypeName, double intervalM, AlignmentPlacementOrigin origin,
        StraightPipeSpecTable specs, IReadOnlyList<IReadOnlyList<VertexTrim>>? trims = null, string? diameterParameterName = null,
        string? outerDiameterParameterName = null, string? thicknessParameterName = null, PipeInfoParameterContext? info = null,
        IProgress<PipeAlignmentProgress>? progress = null, IReadOnlyList<IReadOnlySet<int>>? stepVertices = null, CancellationToken cancellationToken = default)
    {
        var doc = _doc() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        var minLengthFt = doc.Application.ShortCurveTolerance;
        var placed = new List<(DirectShape Shape, PipeAlignment Alignment, double DiameterMm, StraightPipeSpec? Spec, AlignmentSampleSegment Segment)>();
        var specCache = new Dictionary<(string PipeKind, double DiameterMm), StraightPipeSpec?>();
        var missingSpecs = new HashSet<(string PipeKind, double DiameterMm)>();
        var abandonedMiters = new HashSet<MiterWarning>();
        var oversizedOuterDiameters = new HashSet<(string PipeKind, double DiameterMm, double OuterDiameterMm)>();
        var cappedCutExtensions = 0;
        var sampled = alignments.Select((alignment, index) => AlignmentIntervalSampler.SampleSegments(
            alignment, intervalM, trims is not null && index < trims.Count ? trims[index] : null,
            stepVertices is not null && index < stepVertices.Count ? stepVertices[index] : null)).ToList();
        var total = sampled.Sum(x => x.Count);
        // 재질은 관종별(DCIP/SP)이고 필요할 때만 조회한다(docs/212 § B).
        var materialIds = new Dictionary<PipeMaterial, ElementId>();
        progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingStraights, 0, total));

        for (var index = 0; index < alignments.Count; index++)
        {
            var alignment = alignments[index];
            var diameterProfile = new AlignmentDiameterProfile(alignment);
            var segments = sampled[index];
            var sourcePoints = segments.SelectMany(segment => new[] { segment.Start, segment.End }).ToArray();
            var zOffsetsM = segments.SelectMany(segment =>
            {
                var zOffsetM = origin.GetZOffsetM(alignment.PipeKind, diameterProfile.Find(segment.DistanceFromStart));
                return new[] { zOffsetM, zOffsetM };
            }).ToArray();
            var converted = ProjectLocationCoordinateConverter.Convert(_coordinateTransform, sourcePoints, zOffsetsM);
            for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();   // docs/209 § A — 사용자 취소
                var segment = segments[segmentIndex];
                var diameterMm = diameterProfile.Find(segment.DistanceFromStart);
                var specKey = (alignment.PipeKind, diameterMm);
                if (!specCache.TryGetValue(specKey, out var spec))
                    specCache[specKey] = spec = specs.Find(alignment.PipeKind, diameterMm);
                if (spec is null) missingSpecs.Add(specKey);
                var outerDiameterMm = spec?.OuterDiameterMm ?? diameterMm;

                var zOffsetM = origin.GetZOffsetM(alignment.PipeKind, diameterMm);
                var start = converted[segmentIndex * 2];
                var end = converted[segmentIndex * 2 + 1];
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
                        var cutNormals = CutNormals(segments, converted, segmentIndex, minLengthFt);
                        if (cutNormals.StartMiterAbandoned)
                            abandonedMiters.Add(new MiterWarning(AlignmentIdOf(alignment), segment.Start));

                        var cut = PreparePipeSolidCut(start, end, cutNormals.Start, cutNormals.End, outerDiameterMm);
                        startExtensionFt = cut.StartExtensionFt;
                        endExtensionFt = cut.EndExtensionFt;
                        cappedCutExtensions += cut.CappedExtensionCount;
                        var solid = BuildPipeSolid(start, end, cut, outerDiameterMm, spec?.ThicknessMm ?? 0d, PipeSolidFactory.MaterialIdFor(doc, materialIds, alignment.PipeKind));
                        shape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                        shape.SetShape(new GeometryObject[] { solid });
                        shape.Name = $"직관_{alignment.PipeKind}_{diameterMm:0}";
                    }
                }
                catch (Exception ex)
                {
                    if (ex is ArgumentException outOfRange &&
                        (outOfRange.ParamName == "origin" ||
                         outOfRange.Message.Contains("design limits", StringComparison.OrdinalIgnoreCase)))
                        throw new DhbimwaterException(ErrorCodes.PA_TooFarFromOrigin,
                            "DirectShape 생성 실패: 내부원점으로부터 너무 멀리 떨어진 곳에 모델링을 시도했습니다.", ex);

                    throw new DhbimwaterException(ErrorCodes.PA_StraightDirectShapeFailed,
                        $"직관 DirectShape 생성 실패: {AlignmentIdOf(alignment)}, segmentIndex={segmentIndex}, " +
                        $"원본 좌표(m) {FormatPoint(segment.Start)} ~ {FormatPoint(segment.End)}, 적용 외경={outerDiameterMm:0.###}mm, " +
                        $"startExtension={FormatExtension(startExtensionFt)}ft, endExtension={FormatExtension(endExtensionFt)}ft, " +
                        $"내부예외={ex.GetType().Name}: {ex.Message}", ex);
                }
                // zOffsetM은 형상 배치에서 이미 반영했다. 파라미터 기록부는 docs/214 이후 쓰지 않는다(ZOffset 태그 폐지).
                placed.Add((shape, alignment, diameterMm, spec, segment));
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
            // 선형 스냅샷(_snapshotStore.EnsureSnapshot)은 `AlignmentId` 태그를 위한 것이었다. DirectShape를
            // 측점편집에서 뺀 뒤(docs/214) 소비자가 없어 생성하지 않는다. 저장소 필드·생성자 인자는 DI 등록을
            // 건드리지 않기 위해 남겨 뒀다 — 가변 패밀리 경로는 지금도 스냅샷을 쓴다.
            foreach (var (shape, alignment, diameterMm, spec, segment) in placed)
            {
                if (info is not { Enabled: true }) continue;

                if (!ReferenceEquals(alignment, previousAlignment))
                {
                    previousAlignment = alignment;
                    stationProfile = new AlignmentStationProfile(alignment);
                }

                var lengthM = segment.Start.DistanceTo(segment.End);
                var startStationM = stationProfile!.StationAt(segment.DistanceFromStart);
                var endStationM = stationProfile.StationAt(segment.DistanceFromStart + lengthM);
                writer.Text(shape, PipeAlignmentParameters.Addin, PipeAlignmentParameters.AddinValue);
                // 6 m 정척 − 1 mm 미만 토막은 단관(부설 1 · 절단 1), 이상은 직관(부설 0 · 절단 0)이다(docs/266 R-1·R-6).
                var segmentCounts = PipeCountRules.ForPlannedSegment(lengthM * 1000d);
                writer.Text(shape, PipeAlignmentParameters.Part, segmentCounts.PartName);
                writer.Text(shape, PipeAlignmentParameters.NominalDiameter, PipeAlignmentParameters.DiameterText(diameterMm));
                writer.Text(shape, PipeAlignmentParameters.Material, MaterialCodeOf(alignment.PipeKind));
                writer.Text(shape, PipeAlignmentParameters.Grade, alignment.PipeKind);
                writer.YesNo(shape, PipeAlignmentParameters.NightWork, info.IsNightWork);   // docs/141 § C-4
                writer.Text(shape, PipeAlignmentParameters.ElevationDatum, PipeAlignmentParameters.Label(info.ZDatum));
                writer.LengthM(shape, PipeAlignmentParameters.Length, lengthM);
                // 무게 = 단위중량 × 연장 + 소켓 무게(docs/178 § A-2). 가변 패밀리 경로와 같은 규칙이다.
                if (StraightPipeWeight.TotalKg(spec, lengthM, info.JointType) is { } weightKg)
                {
                    writer.MassPerLengthKgPerM(shape, PipeAlignmentParameters.UnitWeight, spec!.UnitWeightKgPerM);
                    writer.MassKg(shape, PipeAlignmentParameters.Weight, weightKg);
                }
                // 패밀리 배치 경로와 같은 규칙이다 — 직관도 슬롯 1벌(규격 비움 · 1개소). 인스턴스 하나가 1본 = 접합 1개소다(docs/186 § 5, 2026-09-18 개정).
                writer.JointSlots(shape, PipeKindCatalog.MaterialOf(alignment.PipeKind), info.BendConnection,
                    new[] { (Spec: string.Empty, Count: 1) }, info.UseRestraintGland);
                writer.Integer(shape, PipeAlignmentParameters.LayingCount, segmentCounts.LayingCount);
                writer.Integer(shape, PipeAlignmentParameters.CutCount, segmentCounts.CutCount);
                writer.JointComposition(shape, info.BendConnection, endCount: 2, socketEnds: 1);   // 직관은 소켓 한쪽 → `1S`(docs/199 § 2)
                // DirectShape는 측점편집 대상이 아니다(docs/214, 2026-09-22 사용자 확정). 갱신용 태그
                // (SourceFile·Profile·ZOffset·Flip·VerticalBasis·ShapeKind·AlignmentId·SourceKind·SourceOptions)를
                // 적지 않으므로 수량산출 대상 수집(ShapeKind == "Cylinder")에서 자동으로 빠진다.
                // 수량산출이 읽는 셋(구간·측점시작·측점끝)만 남긴다.
                writer.Text(shape, PlacementStationParameters.Alignment, alignment.RecordNumber);
                writer.StationM(shape, PlacementStationParameters.StaStart, startStationM);
                writer.StationM(shape, PlacementStationParameters.StaEnd, endStationM);
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
        var loops = new List<CurveLoop> { PipeSolidFactory.CircleLoop(extrusionStart, cut.RadiusFt, xAxis, yAxis) };
        var innerRadiusMm = outerDiameterMm / 2d - thicknessMm;
        if (thicknessMm > 0d && innerRadiusMm > 0.01)
            loops.Add(PipeSolidFactory.CircleLoop(extrusionStart, UC.MmToFt(innerRadiusMm), xAxis, yAxis));
        var options = new SolidOptions(materialId, ElementId.InvalidElementId);
        var solid = GeometryCreationUtilities.CreateExtrusionGeometry(loops, axis,
            start.DistanceTo(end) + cut.StartExtensionFt + cut.EndExtensionFt, options);
        solid = BooleanOperationsUtils.CutWithHalfSpace(solid, Plane.CreateByNormalAndOrigin(cut.StartNormal, start));
        return BooleanOperationsUtils.CutWithHalfSpace(solid, Plane.CreateByNormalAndOrigin(-cut.EndNormal, end));
    }

    private static (DHBIMWATER.Core.Geometry.Vector3D Start, DHBIMWATER.Core.Geometry.Vector3D End,
        bool StartMiterAbandoned, bool EndMiterAbandoned) CutNormals(
        IReadOnlyList<AlignmentSampleSegment> segments, IReadOnlyList<XYZ> convertedPoints, int index, double minLengthFt)
    {
        var own = DirectionOf(convertedPoints, index);
        // 길이가 0에 가까워 배치하지 않은 이웃은 마이터 대상이 아니다 — 그 방향을 정규화하면 NaN 법선이 된다(docs/209 § G).
        var hasPrevious = index > 0
            && !segments[index - 1].IsDatumStep && !segments[index].IsDatumStep
            && IsPlaced(convertedPoints, index - 1, minLengthFt)
            && segments[index - 1].End.DistanceTo(segments[index].Start) <= JoinToleranceM;
        var previous = hasPrevious ? DirectionOf(convertedPoints, index - 1) : own;
        var startMiterAbandoned = hasPrevious && StraightJointCutPlane.ExceedsMiterLimit(
            previous, own, StraightJointCutPlane.MaxMiterDeflectionDeg);
        var start = hasPrevious && !startMiterAbandoned
            ? StraightJointCutPlane.Bisect(previous, own)
            : own;
        var hasNext = index + 1 < segments.Count
            && !segments[index + 1].IsDatumStep && !segments[index].IsDatumStep
            && IsPlaced(convertedPoints, index + 1, minLengthFt)
            && segments[index].End.DistanceTo(segments[index + 1].Start) <= JoinToleranceM;
        var next = hasNext ? DirectionOf(convertedPoints, index + 1) : own;
        var endMiterAbandoned = hasNext && StraightJointCutPlane.ExceedsMiterLimit(
            own, next, StraightJointCutPlane.MaxMiterDeflectionDeg);
        var end = hasNext && !endMiterAbandoned
            ? StraightJointCutPlane.Bisect(own, next)
            : own;
        return (start, end, startMiterAbandoned, endMiterAbandoned);
    }

    private static bool IsPlaced(IReadOnlyList<XYZ> convertedPoints, int segmentIndex, double minLengthFt)
        => convertedPoints[segmentIndex * 2].DistanceTo(convertedPoints[segmentIndex * 2 + 1]) >= minLengthFt;

    private static DHBIMWATER.Core.Geometry.Vector3D DirectionOf(IReadOnlyList<XYZ> convertedPoints, int segmentIndex)
        => ToVector3D(convertedPoints[segmentIndex * 2 + 1] - convertedPoints[segmentIndex * 2]).Normalize();

    private static XYZ OrientToAxis(XYZ normal, XYZ axis)
        => normal.DotProduct(axis) >= 0d ? normal : -normal;

    private static double CutExtension(XYZ axis, XYZ normal, double radiusFt, out bool capped)
    {
        var rawSlope = StraightJointCutPlane.CutSlope(ToVector3D(axis), ToVector3D(normal), double.MaxValue);
        capped = rawSlope > MaxCutSlope;
        var slope = Math.Min(rawSlope, MaxCutSlope);
        return radiusFt * slope + UC.MmToFt(1d);
    }

    private static XYZ ToXyz(DHBIMWATER.Core.Geometry.Vector3D vector) => new(vector.X, vector.Y, vector.Z);
    private static DHBIMWATER.Core.Geometry.Vector3D ToVector3D(XYZ vector) => new(vector.X, vector.Y, vector.Z);

    private static string FormatPoint(DHBIMWATER.Core.Geometry.Point3D point)
        => FormattableString.Invariant($"({point.X:0.000}, {point.Y:0.000}, {point.Z:0.000})");

    private static string FormatExtension(double extensionFt)
        => double.IsFinite(extensionFt) ? extensionFt.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "계산 실패";

    private static string AlignmentIdOf(PipeAlignment alignment) => $"{Path.GetFileNameWithoutExtension(alignment.SourceFile)}#{alignment.RecordNumber}";
    private static string MaterialCodeOf(string pipeKind) => PipeKindCatalog.MaterialOf(pipeKind) is { } material ? PipeAlignmentParameters.Code(material) : string.Empty;

    private sealed record MiterWarning(string AlignmentId, DHBIMWATER.Core.Geometry.Point3D Point);
    private readonly record struct PipeSolidCut(XYZ Axis, XYZ StartNormal, XYZ EndNormal, double RadiusFt,
        double StartExtensionFt, double EndExtensionFt, int CappedExtensionCount);
}
