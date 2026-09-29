using Autodesk.Revit.DB;
using System.IO;
using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.Infrastructure.Helpers;
using DHBIMWATER.Shared.Diagnostics;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Gis;

/// <summary>
/// 관로 직관을 2점 가변(Adaptive Component) 패밀리로 배치한다.
/// 구조 프레이밍(빔) 배치를 대체한 것으로(2026-08-18), 곡관 5점 가변과 같은 표현 체계를 이룬다.
/// 유형 복제 없이 인스턴스 파라미터로 호칭지름·제원과 진행방향 회전을 기록한다.
/// </summary>
internal sealed class RevitAlignmentStraightPlacementRepo : IAlignmentStraightPlacementRepo
{
    /// <summary>세그먼트마다 Regenerate하면 수천 개 배치에서 매우 느려져, 이 개수마다 한 번씩만 호출한다.</summary>
    private const int RegenerateBatchSize = 200;

    /// <summary>
    /// docs/197 § A-3 — Create가 암묵적 재생성을 강제하는지 판정하는 프로브 표본 수. 앞쪽 몇 개만 잰다.
    /// TODO: docs/197 원인 규명 후 프로브(06a0p·06a4p)와 함께 제거할 것.
    /// </summary>
    private const int RegenerationProbeCount = 3;

    private readonly Func<Document?> _doc;
    private readonly IProjectLocationCoordinateTransformRepo _coordinateTransform;
    private readonly IAlignmentSnapshotStore? _snapshotStore;
    public RevitAlignmentStraightPlacementRepo(Func<Document?> doc, IProjectLocationCoordinateTransformRepo coordinateTransform,
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
        // docs/197 § A-1 — 같은 데이터인데 실행마다 06a1/06d가 수십~수백 배 차이 난다.
        // 문서 누적 규모가 원인인지 가르려면 실행 시점의 문서 크기가 로그에 남아야 한다.
        if (PlacementProfiler.IsActive)
        {
            using var documentScaleScope = PlacementProfiler.Step("00a 문서 규모 집계");
            PlacementProfiler.Count("문서 요소 수(배치 전)",
                new FilteredElementCollector(doc).WhereElementIsNotElementType().GetElementCount());
            PlacementProfiler.Count("일반모델 인스턴스 수(배치 전)",
                new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_GenericModel)
                    .WhereElementIsNotElementType().GetElementCount());
        }
        var symbol = ResolveSymbol(doc, straightFamilyTypeName);
        // Revit 짧은 커브 허용치(약 0.00256ft ≈ 0.78mm)보다 짧은 세그먼트는 배치 의미가 없어 건너뛴다.
        var minLengthFt = doc.Application.ShortCurveTolerance;
        // docs/198 — 파라미터는 인스턴스를 만든 직후, 좌표 설정과 같은 자리에서 쓴다.
        // 예전처럼 배치를 다 끝낸 뒤 몰아 쓰면 형상 구동 파라미터(DN/OD/두께·rot_*)가 바뀌어
        // 최종 Regenerate가 전량을 다시 푼다(2026-09-21 실측: 574개 × 181.6ms = 104초).
        var placed = new List<(FamilyInstance Instance, PipeAlignment Alignment, double DiameterMm, StraightPipeSpec? Spec, AlignmentSampleSegment Segment, double ZOffsetM)>();
        var specCache = new Dictionary<(string PipeKind, double DiameterMm), StraightPipeSpec?>();
        // (등급, DN)이 규격표에 없으면 종전엔 OD·두께를 안 쓰고 경고도 없어 패밀리 기본 OD가 남았다(2026-09-22).
        // DirectShape 경로처럼 경고하고, OD는 곡관 경로처럼 같은 관종의 다른 등급 행에서 빌린다.
        var missingSpecs = new HashSet<(string PipeKind, double DiameterMm)>();
        var pending = 0;
        // 총계용·배치용으로 두 번 샘플링하던 것을 한 번으로(docs/209 § F). DirectShape 리포와 같은 모양이다.
        var sampled = alignments.Select((alignment, index) => AlignmentIntervalSampler.SampleSegments(
            alignment, intervalM, trims is not null && index < trims.Count ? trims[index] : null,
            stepVertices is not null && index < stepVertices.Count ? stepVertices[index] : null)).ToList();
        var total = sampled.Sum(x => x.Count);
        progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingStraights, 0, total));

        // 패밀리별로 한 번만 경고하면 충분하다 — 세그먼트마다 같은 누락 메시지가 반복되면 오히려 안 읽힌다.
        var missingParameters = new HashSet<string>();
        var writer = new PipeParameterWriter();
        var snapshotWarnings = new HashSet<string>();

        // SampleSegments로 intervalM(6m)마다 끊어 시작/끝점을 잇는다.
        // 곡관이 들어가는 정점에서는 그 몸통 자리(t, B형은 하류쪽 t+s)만큼 직관을 만들지 않는다.
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

            // docs/198 § 3-1 — 배치 대상 세그먼트를 먼저 추린다. 회전값은 "실제로 배치되는 이웃 세그먼트"를 참조하므로
            // (짧아서 건너뛴 세그먼트는 이웃이 아니다) 파라미터를 생성 직후에 쓰려면 이웃을 미리 알아야 한다.
            var placeable = new List<(AlignmentSampleSegment Segment, XYZ Start, XYZ End, double DiameterMm, StraightPipeSpec? Spec, double ZOffsetM)>();
            for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                var candidate = segments[segmentIndex];
                var candidateDiameterMm = diameterProfile.Find(candidate.DistanceFromStart);
                var specKey = (alignment.PipeKind, candidateDiameterMm);
                if (!specCache.TryGetValue(specKey, out var candidateSpec))
                    specCache[specKey] = candidateSpec = specs.Find(alignment.PipeKind, candidateDiameterMm);
                if (candidateSpec is null) missingSpecs.Add(specKey);
                var candidateStart = converted[segmentIndex * 2];
                var candidateEnd = converted[segmentIndex * 2 + 1];
                if (candidateStart.DistanceTo(candidateEnd) < minLengthFt) continue;
                placeable.Add((candidate, candidateStart, candidateEnd, candidateDiameterMm, candidateSpec,
                    origin.GetZOffsetM(alignment.PipeKind, candidateDiameterMm)));
            }
            if (placeable.Count == 0) continue;

            // 선형 단위 상태(측점 프로파일·스냅샷·직전 세그먼트)는 선형마다 새로 잡는다.
            // 종전 06c 루프의 "선형이 바뀌면 초기화"(ReferenceEquals 비교)와 같은 의미다.
            var cursor = new AlignmentWriteCursor(new AlignmentStationProfile(alignment));

            for (var j = 0; j < placeable.Count; j++)
            {
                cancellationToken.ThrowIfCancellationRequested();   // docs/209 § A — 사용자 취소
                var (segment, start, end, diameterMm, spec, zOffsetM) = placeable[j];

                FamilyInstance instance;
                IList<ElementId> pointIds;
                // 06a는 세 호출로 쪼개 계측한다. GetInstancePlacementPointElementRefIds가 암묵적 문서 재생성을
                // 강제하는지(= 명시적 Regenerate 배치가 무력화되는지) 확인하기 위한 것이다.
                using (PlacementProfiler.Step("06a1 직관 Create"))
                using (PlacementProfiler.StepBucketed("06a1 직관 Create", placed.Count))
                    instance = AdaptiveComponentInstanceUtils.CreateAdaptiveComponentInstance(doc, symbol);
                // docs/197 § A-3 — Create 직후 즉시 명시 Regenerate. 0에 가까우면 Create가 이미 재생성한 것이다.
                if (placed.Count < RegenerationProbeCount)
                    using (PlacementProfiler.Step("06a0p Create직후 Regenerate 프로브")) doc.Regenerate();
                using (PlacementProfiler.Step("06a2 직관 GetPointIds"))
                using (PlacementProfiler.StepBucketed("06a2 직관 GetPointIds", placed.Count))
                    pointIds = AdaptiveComponentInstanceUtils.GetInstancePlacementPointElementRefIds(instance);
                if (pointIds.Count != 2)
                    throw new DhbimwaterException(ErrorCodes.PA_AdaptivePointCount, $"직관 패밀리 '{straightFamilyTypeName}'의 Adaptive Point가 2개가 아닙니다({pointIds.Count}개).");
                using (PlacementProfiler.Step("06a3 직관 좌표 설정"))
                using (PlacementProfiler.StepBucketed("06a3 직관 좌표 설정", placed.Count))
                {
                    ((ReferencePoint)doc.GetElement(pointIds[0])).Position = start;
                    ((ReferencePoint)doc.GetElement(pointIds[1])).Position = end;
                }
                // docs/197 § A-3 — 좌표만 바뀐 인스턴스 1개를 푸는 한계비용. 06d 총합 ÷ 인스턴스 수와 비교한다.
                if (placed.Count < RegenerationProbeCount)
                    using (PlacementProfiler.Step("06a4p 좌표설정후 Regenerate 프로브")) doc.Regenerate();
                // docs/198 § 3-2 — 파라미터를 여기서 쓴다. 좌표와 같은 dirty 구간에 묶여 다음 Create의
                // 암묵적 재생성 한 번으로 함께 풀린다(종전에는 06d에서 전량이 한 번 더 풀렸다).
                using (PlacementProfiler.Step("06c 직관 파라미터 기록"))
                using (PlacementProfiler.StepBucketed("06c 직관 파라미터 기록", placed.Count))
                    WriteParameters(instance, alignment, segment, diameterMm, spec, zOffsetM,
                        j + 1 < placeable.Count ? placeable[j + 1].Segment : null, cursor);
                placed.Add((instance, alignment, diameterMm, spec, segment, zOffsetM));
                // 진행률은 Regenerate 주기와 무관하게 세그먼트 1개마다 보고한다.
                // (UI는 전용 STA 스레드라 Revit 스레드를 막지 않고, 생성 1건이 100ms 이상이라 보고 빈도도 낮다)
                progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingStraights, placed.Count, total));
                if (++pending >= RegenerateBatchSize)
                {
                    using (PlacementProfiler.Step("06b 직관 Regenerate")) doc.Regenerate();
                    pending = 0;
                }
            }
        }
        if (pending > 0) { using (PlacementProfiler.Step("06b 직관 Regenerate")) doc.Regenerate(); }
        progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlacingStraights, total, total));
        PlacementProfiler.Count("직관 인스턴스", placed.Count);

        // docs/198 § 3-2 — 종전 06c 후속 루프의 본문을 그대로 옮긴 것이다. 내용은 바뀌지 않았고,
        // 호출 시점만 "배치가 다 끝난 뒤"에서 "인스턴스를 만든 직후"로 당겼다.
        void WriteParameters(FamilyInstance instance, PipeAlignment alignment, AlignmentSampleSegment segment,
            double diameterMm, StraightPipeSpec? spec, double zOffsetM, AlignmentSampleSegment? nextSegment,
            AlignmentWriteCursor cursor)
        {
            Point3D? nextStart = null;
            Vector3D? nextOwnDirection = null;
            var nextIsDatumStep = false;
            if (nextSegment is { } next)
            {
                nextStart = next.Start;
                nextIsDatumStep = next.IsDatumStep;
                nextOwnDirection = new Vector3D(
                    next.End.X - next.Start.X,
                    next.End.Y - next.Start.Y,
                    next.End.Z - next.Start.Z);
            }
            var (startDirection, endDirection, ownDirection) = StraightSegmentOrientation.Resolve(
                segment.Start, segment.End, cursor.PreviousEnd, cursor.PreviousOwnDirection, nextStart, nextOwnDirection,
                cursor.PreviousIsDatumStep, nextIsDatumStep, segment.IsDatumStep);

            if (diameterParameterName is not null) SetParameter(instance, diameterParameterName, diameterMm, missingParameters);
            var outerDiameterMm = spec?.OuterDiameterMm
                ?? specs.FindOuterDiameterByDiameter(diameterMm, PipeKindCatalog.MaterialOf(alignment.PipeKind) ?? PipeMaterial.DuctileIron)?.OuterDiameterMm;
            if (outerDiameterMm is { } od && outerDiameterParameterName is not null) SetParameter(instance, outerDiameterParameterName, od, missingParameters);
            if (spec is not null && thicknessParameterName is not null) SetParameter(instance, thicknessParameterName, spec.ThicknessMm, missingParameters);
            if (!TrySetRotations(instance, startDirection, endDirection, origin.TrueNorthClockwiseDegrees)) missingParameters.Add("rot_XY_n/rot_XZ_n");
            cursor.PreviousEnd = segment.End;
            cursor.PreviousOwnDirection = ownDirection;
            cursor.PreviousIsDatumStep = segment.IsDatumStep;

            if (info is not { Enabled: true }) return;

            if (!cursor.SnapshotResolved)
            {
                cursor.SnapshotResolved = true;
                string snapshotWarning;
                if (_snapshotStore is null)
                {
                    cursor.SnapshotId = string.Empty;
                    snapshotWarning = "연동 선형 스냅샷 저장소가 등록되지 않아 측점 편집 스냅샷을 기록하지 못했습니다.";
                }
                else
                {
                    cursor.SnapshotId = _snapshotStore.EnsureSnapshot(alignment, cursor.StationProfile, origin, out snapshotWarning);
                }
                if (!string.IsNullOrWhiteSpace(snapshotWarning)) snapshotWarnings.Add(snapshotWarning);
            }

            var alignmentSnapshotId = cursor.SnapshotId;
            var stationProfile = cursor.StationProfile;
            var lengthM = segment.Start.DistanceTo(segment.End);
            var startStationM = stationProfile.StationAt(segment.DistanceFromStart);
            var endStationM = stationProfile.StationAt(segment.DistanceFromStart + lengthM);
            writer.Text(instance, PipeAlignmentParameters.Addin, PipeAlignmentParameters.AddinValue);
            writer.Text(instance, PipeAlignmentParameters.Part, PipeAlignmentParameters.StraightPartValue);
            writer.Text(instance, PipeAlignmentParameters.NominalDiameter, PipeAlignmentParameters.DiameterText(diameterMm));
            writer.Text(instance, PipeAlignmentParameters.Material, MaterialCodeOf(alignment.PipeKind));
            writer.Text(instance, PipeAlignmentParameters.Grade, alignment.PipeKind);
            writer.YesNo(instance, PipeAlignmentParameters.NightWork, info.IsNightWork);   // docs/141 § C-4
            writer.Text(instance, PipeAlignmentParameters.ElevationDatum, PipeAlignmentParameters.Label(info.ZDatum));
            writer.LengthM(instance, PipeAlignmentParameters.Length, lengthM);
            // 무게 = 단위중량 × 연장 + 소켓 무게(docs/178 § A-2). 규격표에 M무게가 없는 관종은 두 칸 다 비워 둔다.
            if (StraightPipeWeight.TotalKg(spec, lengthM, info.JointType) is { } weightKg)
            {
                writer.Number(instance, PipeAlignmentParameters.UnitWeight, spec!.UnitWeightKgPerM);
                writer.Number(instance, PipeAlignmentParameters.Weight, weightKg);
            }
            // 직관도 접합 슬롯 1벌을 적는다(docs/186 § 5, 2026-09-18 개정). 산출서 「강관 직관 용접합 및 부설 196본」은
            // 엑셀이 연장 ÷ 정척으로 역산한 값이고, BIM은 정척으로 잘린 인스턴스 하나가 곧 1본 = 접합 1개소다.
            // 규격은 비운다(요소 관경). 부설은 슬롯이 아니라 인스턴스가 만드는 행이라 DHBoost가 `DH_부설수`를 읽지 않는다.
            writer.JointSlots(instance, PipeKindCatalog.MaterialOf(alignment.PipeKind), info.BendConnection,
                new[] { (Spec: string.Empty, Count: 1) }, info.UseRestraintGland);
            writer.Integer(instance, PipeAlignmentParameters.LayingCount, 0);
            // 접합 구성은 개소와 별개로 부재 형상을 적는다. 직관은 소켓이 한쪽뿐이라 A/B형과 무관하게 `1S`(docs/199 § 2).
            writer.JointComposition(instance, info.BendConnection, endCount: 2, socketEnds: 1);
            writer.Text(instance, PlacementStationParameters.SourceFile, alignment.SourceFile);
            writer.Text(instance, PlacementStationParameters.Alignment, alignment.RecordNumber);
            writer.Text(instance, PlacementStationParameters.Profile, string.Empty);
            writer.Number(instance, PlacementStationParameters.StaStart, startStationM);
            writer.Number(instance, PlacementStationParameters.StaEnd, endStationM);
            writer.Number(instance, PlacementStationParameters.ZOffset, zOffsetM);
            writer.YesNo(instance, PlacementStationParameters.Flip, false);
            writer.Text(instance, PlacementStationParameters.VerticalBasis, "GlobalZ");
            if (!string.IsNullOrWhiteSpace(alignmentSnapshotId))
            {
                writer.Text(instance, PlacementStationParameters.AlignmentId, alignmentSnapshotId);
                writer.Text(instance, PlacementStationParameters.SourceKind, "Pipe");
                writer.Text(instance, PlacementStationParameters.SourceOptions, string.Empty);
            }
        }

        var warnings = new List<string>();
        warnings.AddRange(snapshotWarnings);
        if (missingSpecs.Count > 0)
        {
            var values = string.Join(", ", missingSpecs.OrderBy(x => x.PipeKind).ThenBy(x => x.DiameterMm)
                .Select(x => $"{x.PipeKind}/DN{x.DiameterMm:0.##}"));
            warnings.Add($"직관 제원이 없어 관종/DN {missingSpecs.Count}건은 두께를 쓰지 않았고 외경은 같은 관종의 다른 등급 값(없으면 패밀리 기본값)으로 두었습니다: {values}");
        }
        var missingRotation = missingParameters.Remove("rot_XY_n/rot_XZ_n");
        if (missingParameters.Count > 0)
            warnings.Add($"직관 패밀리 '{straightFamilyTypeName}'에 {string.Join(", ", missingParameters.OrderBy(x => x))} 파라미터가 없거나 읽기전용이라 값을 설정하지 못했습니다.");
        if (missingRotation)
            warnings.Add($"직관 패밀리 '{straightFamilyTypeName}'에 rot_XY_n/rot_XZ_n 각도 파라미터가 없어 진행방향 회전을 적용하지 못했습니다.");
        if (writer.InvalidTypes.Count > 0)
            warnings.Add($"프로젝트 매개변수 {string.Join(", ", writer.InvalidTypes.OrderBy(x => x))}의 타입이 배치 태그 계약과 달라 값을 건너뛰었습니다.");
        var missingProjectParameters = writer.Missing.Except(writer.InvalidTypes).OrderBy(x => x).ToList();
        if (missingProjectParameters.Count > 0)
            warnings.Add($"프로젝트 매개변수 {string.Join(", ", missingProjectParameters)}를 직관 인스턴스에 기록하지 못했습니다(바인딩 실패 또는 읽기전용).");
        PlacementProfiler.Count("06d 대상 인스턴스 수", placed.Count);   // docs/197 § A-5
        if (placed.Count > 0) { using (PlacementProfiler.Step("06d 직관 최종 Regenerate")) doc.Regenerate(); }
        var outerDiameters = placed
            .Where(x => x.Spec is not null)
            .GroupBy(x => (x.Alignment.PipeKind, x.DiameterMm))
            .ToDictionary(x => x.Key, x => x.First().Spec!.OuterDiameterMm);
        return new AlignmentStraightPlacementResult(placed.Count, warnings, outerDiameters);
    }

    /// <summary>"패밀리명 : 타입명" 문자열로 FamilySymbol을 찾는다. 표기는 곡관 목록(GetAdaptiveComponentTypeNames)과 같다.</summary>
    private static FamilySymbol ResolveSymbol(Document doc, string familyTypeName)
    {
        if (string.IsNullOrWhiteSpace(familyTypeName)) throw new DhbimwaterException(ErrorCodes.PA_SelectionRequired, "직관 패밀리를 선택하세요.");
        var separator = familyTypeName.LastIndexOf(" : ", StringComparison.Ordinal);
        if (separator <= 0 || separator >= familyTypeName.Length - 3)
            throw new DhbimwaterException(ErrorCodes.PA_FamilyNameFormat, $"직관 패밀리 표기가 '패밀리명 : 타입명' 형식이 아닙니다: {familyTypeName}");
        var familyName = familyTypeName[..separator];
        var typeName = familyTypeName[(separator + 3)..];

        var symbol = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .FirstOrDefault(x => x.Family.Name == familyName && x.Name == typeName)
            ?? throw new DhbimwaterException(ErrorCodes.PA_StraightFamilyNotFound, $"직관 패밀리를 찾을 수 없습니다: {familyName} - {typeName}");
        if (!AdaptiveComponentFamilyUtils.IsAdaptiveComponentFamily(symbol.Family))
            throw new DhbimwaterException(ErrorCodes.PA_NotAdaptiveFamily, $"'{familyTypeName}'은 가변(Adaptive Component) 패밀리가 아닙니다.");
        if (!symbol.IsActive) symbol.Activate();
        return symbol;
    }

    private static void SetParameter(FamilyInstance instance, string name, double valueMm, HashSet<string> missingParameters)
    {
        if (!AdaptiveParameterWriter.TryWrite(instance, name, valueMm.ToString("0.##"), UC.MmToFt(valueMm), (int)Math.Round(valueMm))) missingParameters.Add(name);
    }

    private static bool TrySetRotations(FamilyInstance instance, Vector3D startDirection, Vector3D endDirection, double trueNorthDeg)
    {
        var (startXy, startXz) = BendOrientation.Compute(startDirection);
        var (endXy, endXz) = BendOrientation.Compute(endDirection);
        var numbered = TrySetAngle(instance, "rot_XY_1", BendOrientation.ApplyProjectNorth(startXy, trueNorthDeg))
            && TrySetAngle(instance, "rot_XY_2", BendOrientation.ApplyProjectNorth(endXy, trueNorthDeg))
            && TrySetAngle(instance, "rot_XZ_1", startXz) && TrySetAngle(instance, "rot_XZ_2", endXz);
        // 번호 없는 옛 패밀리는 점별 분리 불가. 끝쪽 방향만 쓴다.
        return numbered || (TrySetAngle(instance, "rot_XY", BendOrientation.ApplyProjectNorth(endXy, trueNorthDeg))
            && TrySetAngle(instance, "rot_XZ", endXz));
    }

    private static bool TrySetAngle(FamilyInstance instance, string name, double degrees)
    {
        var parameter = instance.LookupParameter(name);
        return parameter is { IsReadOnly: false, StorageType: StorageType.Double }
            && parameter.Definition.GetDataType().Equals(SpecTypeId.Angle)
            && parameter.Set(UC.DegToRad(degrees));
    }

    /// <summary>
    /// 선형 하나를 배치하는 동안 이어지는 상태(측점 프로파일·스냅샷 ID·직전 세그먼트).
    /// docs/198 — 파라미터를 생성 루프 안에서 쓰려고 종전 06c 루프의 선형 단위 지역변수를 옮겨 담은 것이다.
    /// </summary>
    private sealed class AlignmentWriteCursor
    {
        public AlignmentWriteCursor(AlignmentStationProfile stationProfile) => StationProfile = stationProfile;
        public AlignmentStationProfile StationProfile { get; }
        public string SnapshotId { get; set; } = string.Empty;
        public bool SnapshotResolved { get; set; }
        public Point3D? PreviousEnd { get; set; }
        public Vector3D? PreviousOwnDirection { get; set; }
        public bool PreviousIsDatumStep { get; set; }
    }

    private static string AlignmentIdOf(PipeAlignment alignment) => $"{Path.GetFileNameWithoutExtension(alignment.SourceFile)}#{alignment.RecordNumber}";
    private static string MaterialCodeOf(string pipeKind) => PipeKindCatalog.MaterialOf(pipeKind) is { } material ? PipeAlignmentParameters.Code(material) : string.Empty;
}
