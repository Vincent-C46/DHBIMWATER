using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Gis;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.Core.Parameters;
using DHBIMWATER.Shared.Diagnostics;

namespace DHBIMWATER.Application.UseCases.Gis;

public sealed class ModelPipeAlignmentUseCase
{
    private readonly ITransactionContext _transaction;
    private readonly AlignmentSourceLoader _loader;
    private readonly IPipeAlignmentCommandRepo _alignmentRepo;
    private readonly IAlignmentStraightPlacementRepo _straightRepo;
    private readonly IAlignmentPipePlacementRepo _pipeRepo;
    private readonly IProjectLocationCommandRepo _projectLocationRepo;
    private readonly ISharedParameterRepository _sharedParameterRepo;
    private readonly BendSettingsProvider _bendSettings;
    private readonly IAdaptiveBendPlacementRepo _adaptiveBendRepo;
    private readonly IBendDirectShapeRepo _bendDirectShapeRepo;
    private readonly IReducerDirectShapeRepo _reducerRepo;
    private readonly IAlignmentStraightDirectShapeRepo _straightDirectShapeRepo;

    public ModelPipeAlignmentUseCase(ITransactionContext transaction, AlignmentSourceLoader loader, IPipeAlignmentCommandRepo alignmentRepo,
        IAlignmentStraightPlacementRepo straightRepo, IAlignmentPipePlacementRepo pipeRepo, IProjectLocationCommandRepo projectLocationRepo,
        ISharedParameterRepository sharedParameterRepo, BendSettingsProvider bendSettings, IAdaptiveBendPlacementRepo adaptiveBendRepo,
        IBendDirectShapeRepo bendDirectShapeRepo, IReducerDirectShapeRepo reducerRepo, IAlignmentStraightDirectShapeRepo straightDirectShapeRepo)
    { _transaction = transaction; _loader = loader; _alignmentRepo = alignmentRepo; _straightRepo = straightRepo; _pipeRepo = pipeRepo; _projectLocationRepo = projectLocationRepo; _sharedParameterRepo = sharedParameterRepo; _bendSettings = bendSettings; _adaptiveBendRepo = adaptiveBendRepo; _bendDirectShapeRepo = bendDirectShapeRepo; _reducerRepo = reducerRepo; _straightDirectShapeRepo = straightDirectShapeRepo; }

    /// <summary>최종 모델 전 Line DirectShape 선형 미리보기 커밋.</summary>
    public PipeAlignmentModelingResult Preview(PipeAlignmentModelingRequest request, IProgress<PipeAlignmentProgress>? progress = null)
    {
        PlacementProfiler.Begin($"미리보기 Line DirectShape / 소스 {request.Files.Count}개");
        try
        {
            progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.Loading, 0, 0));
            AlignmentLoadResult loaded;
            using (PlacementProfiler.Step("01 파일 로드"))
                loaded = _loader.Load(request.Files, warnOnUnknownPipeKinds: false);
            var reference = request.ReferenceX is { } referenceX && request.ReferenceY is { } referenceY
                ? ((double X, double Y)?)(referenceX, referenceY)
                : AlignmentReferencePoint.FromFirstVertex(loaded.Alignments);
            var settings = request.CurrentBendSettings ?? _bendSettings.Load().Settings;
            var origin = new AlignmentPlacementOrigin(reference?.X ?? 0, reference?.Y ?? 0, ZDatum.Centerline, settings.StraightPipes);

            using (_transaction)
            {
                try
                {
                    _transaction.Begin("관로 선형 미리보기", suppressWarnings: true);
                    using (PlacementProfiler.Step("04 미리보기 공유 매개변수 확보"))
                        _sharedParameterRepo.EnsureParameters(PipeAlignmentParameters.Definitions.Concat(GetPreviewOnlyParameterDefinitions()).ToList());
                    if (request.ApplySharedCoordinates)
                        _projectLocationRepo.SetInternalOriginSharedPosition(origin.X, origin.Y, 0);
                    var (count, skipped, warnings) = ToDirectShape(loaded.Alignments, origin, progress);
                    _transaction.Commit();
                    return new PipeAlignmentModelingResult(request.OutputMode, count, skipped,
                        loaded.Warnings.Concat(FindMiterLimitWarnings(loaded.Alignments)).Concat(warnings).ToList());
                }
                catch { _transaction.Rollback(); throw; }
            }
        }
        finally { PlacementProfiler.End(); }
    }

    public PipeAlignmentModelingResult Execute(PipeAlignmentModelingRequest request, IProgress<PipeAlignmentProgress>? progress = null)
    {
        if (request.IntervalMm <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.IntervalMm));
        // 장거리 배치 성능 병목 비중 확정용 단계별 계측 (2026-08-24).
        // 결과 보고서: %LOCALAPPDATA%\DHBIMWATER\Logs\placement-profile.log
        // TODO: 병목이 확정되면 계측 배선을 걷어낼지 결정할 것.
        PlacementProfiler.Begin($"최종 {request.OutputMode} / 간격 {request.IntervalMm:0}mm / 소스 {request.Files.Count}개");
        try { return ExecuteCore(request, progress); }
        finally { PlacementProfiler.End(); }
    }

    private PipeAlignmentModelingResult ExecuteCore(PipeAlignmentModelingRequest request, IProgress<PipeAlignmentProgress>? progress)
    {
        AlignmentLoadResult loaded;
        progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.Loading, 0, 0));
        using (PlacementProfiler.Step("01 파일 로드"))
            loaded = _loader.Load(request.Files);
        // 기준점을 명시하지 않은 호출부에만 자동 산출을 적용한다.
        // (0, 0)을 자동 산출로 대체하면 "원본 좌표 그대로 배치"를 요청할 수 없어 파일마다 오프셋이 달라진다.
        var reference = request.ReferenceX is { } referenceX && request.ReferenceY is { } referenceY
            ? ((double X, double Y)?)(referenceX, referenceY)
            : AlignmentReferencePoint.FromFirstVertex(loaded.Alignments);
        var settingsResolution = request.CurrentBendSettings is null
            ? _bendSettings.Load()
            : new BendSettingsResolution(request.CurrentBendSettings, BendSettingsSource.Project);
        var (settings, settingsSource) = settingsResolution;
        DatumOffsetResult offset;
        using (PlacementProfiler.Step("01b 기준면 보정"))
            offset = AlignmentDatumOffsetter.ToCenterline(loaded.Alignments, request.ZDatum, settings.StraightPipes, request.ReducerBodyLengthMm);
        var alignments = offset.Alignments;
        var stepVertices = offset.DiameterStepVertexIndices;
        var origin = new AlignmentPlacementOrigin(reference?.X ?? 0, reference?.Y ?? 0, ZDatum.Centerline, settings.StraightPipes);
        var infoContext = new PipeInfoParameterContext(settings.ActiveJointType, settings.ApplicationMode, request.ZDatum);
        var elevationWarnings = GetElevationWarnings(alignments, request, settings.StraightPipes);
        // 원통 DirectShape와 Adaptive 모두 같은 곡관 계획 사용. PipingSystem만 Revit 부속 자동 생성.
        BendTrimPlan? bendPlan;
        IReadOnlyList<string> bendWarnings;
        progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlanningBends, 0, 0));
        using (PlacementProfiler.Step("02 곡관 계획(PlanBends)"))
            (bendPlan, bendWarnings) = UsesBends(request.OutputMode)
                ? PlanBends(alignments, stepVertices, request, settings, settingsSource == BendSettingsSource.BuiltInDefault)
                : (null, Array.Empty<string>());
        ReducerTrimPlan? reducerPlan;
        using (PlacementProfiler.Step("02b 이경관 계획"))
            reducerPlan = UsesBends(request.OutputMode)
                ? ReducerTrimPlanner.Plan(alignments,
                    new FixedReducerSpecSource(request.ReducerBodyLengthMm, AlignmentFor(request.ZDatum)), bendPlan,
                    request.SnapToleranceMm / 1000d, stepVertices)
                : null;
        var mergedTrims = MergeTrims(bendPlan, reducerPlan, alignments);

        // 트랜잭션을 열기 전에 카탈로그가 가리키는 곡관 패밀리가 실제로 이 문서에 로드돼 있는지 확인한다.
        // 그렇지 않으면 직관 배치까지 다 끝낸 뒤 곡관 배치 단계에서야 실패해 전체가 롤백된다.
        if (request.OutputMode == PipeAlignmentOutputMode.Adaptive && bendPlan is not null)
        {
            using var bendFamilyScope = PlacementProfiler.Step("03 곡관 패밀리 로드 확인");
            EnsureBendConfiguration(bendPlan, request.BendFamilyTypeName);
            EnsureBendFamilyLoaded(bendPlan, request.BendFamilyTypeName);
        }

        using (_transaction)
        {
            try
            {
                _transaction.Begin(request.OutputMode == PipeAlignmentOutputMode.DirectShape ? "직관·곡관·이경관 DirectShape 배치" : "선형 패밀리 배치", suppressWarnings: true);
                using (PlacementProfiler.Step("04 공유 매개변수 확보"))
                {
                    if (UsesBends(request.OutputMode))
                        _sharedParameterRepo.EnsureParameters(PipeAlignmentParameters.Definitions.Concat(PlacementStationParameters.Definitions).ToList());
                }
                if (request.ApplySharedCoordinates)
                {
                    using var sharedCoordinateScope = PlacementProfiler.Step("05 공유좌표 설정");
                    _projectLocationRepo.SetInternalOriginSharedPosition(origin.X, origin.Y, 0);
                }
                AlignmentStraightPlacementResult? straightResult = null;
                int count;
                int skipped;
                IReadOnlyList<string> repoWarnings;
                using (PlacementProfiler.Step("06 직관/파이프 배치 전체"))
                    (count, skipped, repoWarnings) = request.OutputMode switch
                {
                    PipeAlignmentOutputMode.DirectShape => ToStraight(straightResult = _straightDirectShapeRepo.PlaceAlong(alignments, string.Empty, request.IntervalMm / 1000d, origin, settings.StraightPipes, mergedTrims, info: infoContext, progress: progress), bendWarnings),
                    PipeAlignmentOutputMode.Adaptive => ToStraight(straightResult = _straightRepo.PlaceAlong(alignments, Require(request.StraightFamilyTypeName, "직관 패밀리"), request.IntervalMm / 1000d, origin, settings.StraightPipes, mergedTrims, request.StraightDiameterParameterName, request.StraightOuterDiameterParameterName, request.StraightThicknessParameterName, infoContext, progress), bendWarnings),
                    PipeAlignmentOutputMode.PipingSystem => (_pipeRepo.PlaceAlong(alignments, Require(request.PipingSystemTypeName, "파이프 시스템 유형"), Require(request.PipeTypeName, "PipeType"), request.LevelName, request.IntervalMm / 1000d, origin), 0, (IReadOnlyList<string>)Array.Empty<string>()),
                    _ => throw new ArgumentOutOfRangeException(nameof(request.OutputMode))
                };
                AdaptiveBendPlacementResult bendResult;
                using (PlacementProfiler.Step("07 곡관 배치 전체"))
                    bendResult = bendPlan is null ? new AdaptiveBendPlacementResult(0, Array.Empty<string>())
                        : request.OutputMode == PipeAlignmentOutputMode.Adaptive
                            ? PlaceBendFittings(bendPlan, alignments, origin, request, infoContext, progress)
                            : request.OutputMode == PipeAlignmentOutputMode.DirectShape
                                ? PlaceBendDirectShapes(bendPlan, alignments, origin, request, infoContext, progress)
                                : new AdaptiveBendPlacementResult(0, Array.Empty<string>());
                ReducerPlacementResult reducerResult;
                using (PlacementProfiler.Step("08 이경관 배치 전체"))
                    reducerResult = request.OutputMode is PipeAlignmentOutputMode.DirectShape or PipeAlignmentOutputMode.Adaptive
                        && reducerPlan is not null
                        ? PlaceReducers(reducerPlan, alignments, origin, infoContext, progress)
                        : new ReducerPlacementResult(0, Array.Empty<string>());
                progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.Committing, 0, 0));
                using (PlacementProfiler.Step("09 트랜잭션 Commit")) _transaction.Commit();
                var suppressedWarningSummary = SummarizeSuppressedWarnings(_transaction.SuppressedWarnings);
                return new PipeAlignmentModelingResult(request.OutputMode, count, skipped,
                    loaded.Warnings.Concat(offset.Warnings).Concat(elevationWarnings).Concat(reducerPlan?.Warnings ?? Array.Empty<string>()).Concat(repoWarnings).Concat(bendResult.Warnings).Concat(reducerResult.Warnings)
                        .Concat(FindOuterDiameterMismatches(straightResult, bendResult)).Concat(suppressedWarningSummary).ToList(),
                    bendResult.Count);
            }
            catch { _transaction.Rollback(); throw; }
        }
    }

    private static void EnsureBendConfiguration(BendTrimPlan bendPlan, string? familyTypeName)
    {
        if (bendPlan.Placements.Count == 0) return;
        ParseFamilyType(familyTypeName);
    }

    private void EnsureBendFamilyLoaded(BendTrimPlan bendPlan, string? familyTypeName)
    {
        if (bendPlan.Placements.Count == 0) return;
        var pair = ParseFamilyType(familyTypeName);

        var missing = _adaptiveBendRepo.FindMissingSymbols(new[] { pair });
        if (missing.Count > 0)
            throw new InvalidOperationException($"다음 곡관 패밀리·타입을 문서에서 찾을 수 없습니다(로드 필요): {string.Join(", ", missing.Select(x => $"{x.FamilyName}:{x.TypeName}"))}");
    }

    /// <summary>
    /// 절점 곡관 판정을 폴리선 정점 차감량과 5점 가변 곡관 배치 계획으로 환산한다.
    /// </summary>
    private (BendTrimPlan? Plan, IReadOnlyList<string> Warnings) PlanBends(IReadOnlyList<PipeAlignment> alignments,
        IReadOnlyList<IReadOnlySet<int>> stepVertices, PipeAlignmentModelingRequest request, BendSettings settings, bool usesDefault)
    {
        if (request.SnapToleranceMm <= 0) throw new ArgumentOutOfRangeException(nameof(request.SnapToleranceMm));

        var warnings = new List<string>();
        if (usesDefault) warnings.Add("저장된 관로 규격 설정이 없어 내장 기본값으로 판정했습니다.");
        if (settings.Fittings.NeedsRestore) warnings.Add("저장된 곡관 치수가 현행 규격표와 구조가 다릅니다(중복 행 또는 플랜지곡관 누락). [관·곡관 규격표] 창에서 [기본값 복원] 후 저장하세요.");

        var snapTolerance = request.SnapToleranceMm / 1000d;
        var graphAlignments = CollapseDatumSteps(alignments, stepVertices);
        var graph = PipeNetworkBuilder.Build(graphAlignments, snapTolerance);
        var nodes = PipeNetworkClassifier.Classify(graph);
        var authority = BendAuthorityPolicy.Evaluate(graphAlignments, graph, nodes, settings, snapTolerance);
        var resolutions = authority.Kept;
        var plan = BendTrimPlanner.Plan(alignments, graph, resolutions, snapTolerance, stepVertices);
        warnings.AddRange(authority.Warnings);
        warnings.AddRange(plan.Warnings);

        var skipped = resolutions.Count(x => x.Kind != BendResolutionKind.None && !x.HasFittingSize);
        if (skipped > 0)
            warnings.Add($"곡관 치수가 없어 배치하지 못한 절점 {skipped}곳은 직관을 절점까지 붙였습니다.");

        var exceeded = resolutions.Where(x => !x.IsAcceptable).Select(x => x.NodeId).ToList();
        if (exceeded.Count > 0)
            warnings.Add($"허용 범위 초과 곡관 절점 {exceeded.Count}곳: {string.Join(", ", exceeded)}. 배치 가능한 곡관은 3D 뷰에서 적색 표시합니다.");

        return (plan, warnings);
    }

    private static IReadOnlyList<PipeAlignment> CollapseDatumSteps(IReadOnlyList<PipeAlignment> alignments,
        IReadOnlyList<IReadOnlySet<int>> stepVertices)
    {
        var collapsed = new List<PipeAlignment>(alignments.Count);
        for (var a = 0; a < alignments.Count; a++)
        {
            var steps = stepVertices.ElementAtOrDefault(a);
            if (steps is null || steps.Count == 0) { collapsed.Add(alignments[a]); continue; }
            var vertices = new List<DHBIMWATER.Core.Geometry.Point3D>();
            for (var v = 0; v < alignments[a].Vertices.Count; v++)
            {
                if (steps.Contains(v))
                {
                    var up = alignments[a].Vertices[v - 1];
                    var down = alignments[a].Vertices[v];
                    vertices[^1] = new DHBIMWATER.Core.Geometry.Point3D((up.X + down.X) / 2d, (up.Y + down.Y) / 2d, (up.Z + down.Z) / 2d);
                    continue;
                }
                vertices.Add(alignments[a].Vertices[v]);
            }
            collapsed.Add(alignments[a] with { Vertices = vertices });
        }
        return collapsed;
    }

    /// <summary>선택한 단일 패밀리·타입으로 모든 5점 가변 곡관을 배치한다.</summary>
    private AdaptiveBendPlacementResult PlaceBendFittings(BendTrimPlan plan, IReadOnlyList<PipeAlignment> alignments, AlignmentPlacementOrigin origin, PipeAlignmentModelingRequest request, PipeInfoParameterContext infoContext, IProgress<PipeAlignmentProgress>? progress)
    {
        if (plan.Placements.Count == 0) return new AdaptiveBendPlacementResult(0, Array.Empty<string>());
        var (familyName, typeName) = ParseFamilyType(request.BendFamilyTypeName);
        var (bendPlans, outerDiameterFallbacks) = BuildBendPlans(plan, alignments, origin, request, familyName, typeName, true);
        var result = _adaptiveBendRepo.Place(bendPlans, origin, infoContext, progress);
        return AddOuterDiameterFallbackWarning(result, outerDiameterFallbacks);
    }

    private AdaptiveBendPlacementResult PlaceBendDirectShapes(BendTrimPlan plan, IReadOnlyList<PipeAlignment> alignments, AlignmentPlacementOrigin origin, PipeAlignmentModelingRequest request, PipeInfoParameterContext infoContext, IProgress<PipeAlignmentProgress>? progress)
    {
        var (bendPlans, outerDiameterFallbacks) = BuildBendPlans(plan, alignments, origin, request, string.Empty, string.Empty, false);
        return AddOuterDiameterFallbackWarning(_bendDirectShapeRepo.Place(bendPlans, origin, infoContext, progress), outerDiameterFallbacks);
    }

    private static (IReadOnlyList<AdaptiveBendPlacementPlan> Plans, HashSet<(string PipeKind, double DiameterMm, string SourceKind, double OuterDiameterMm)> Fallbacks) BuildBendPlans(BendTrimPlan plan, IReadOnlyList<PipeAlignment> alignments, AlignmentPlacementOrigin origin, PipeAlignmentModelingRequest request, string familyName, string typeName, bool isAdaptive)
    {
        var fallbacks = new HashSet<(string PipeKind, double DiameterMm, string SourceKind, double OuterDiameterMm)>();
        var profiles = new Dictionary<int, AlignmentStationProfile>();
        var plans = plan.Placements.Select(x =>
        {
            var alignment = alignments[x.AlignmentIndex];
            if (!profiles.TryGetValue(x.AlignmentIndex, out var profile)) profiles[x.AlignmentIndex] = profile = new AlignmentStationProfile(alignment);
            var nodeStationM = profile.StationAt(profile.DistanceAtVertex(x.VertexIndex));
            var nodeStationText = alignment.VertexStationTexts is { } stationTexts && x.VertexIndex >= 0 && x.VertexIndex < stationTexts.Count && !string.IsNullOrWhiteSpace(stationTexts[x.VertexIndex])
                ? stationTexts[x.VertexIndex]!
                : FormatStation(nodeStationM);
            var spec = origin.StraightPipeSpecs.Find(x.PipeKind, x.DiameterMm);
            var outerDiameterMm = spec?.OuterDiameterMm;
            if (outerDiameterMm is null && origin.StraightPipeSpecs.FindOuterDiameterByDiameter(x.DiameterMm) is { } fallback)
            {
                outerDiameterMm = fallback.OuterDiameterMm;
                fallbacks.Add((x.PipeKind, x.DiameterMm, fallback.PipeKind, fallback.OuterDiameterMm));
            }
            return new AdaptiveBendPlacementPlan(x.NodeId, familyName, typeName, x.Points, x.DiameterMm, x.WallThicknessMm,
                origin.GetZOffsetM(x.PipeKind, x.DiameterMm), isAdaptive ? request.BendDiameterParameterName : null, isAdaptive ? request.BendWallThicknessParameterName : null, isAdaptive ? request.BendOuterDiameterParameterName : null, x.IsAcceptable,
                isAdaptive ? x.RotXYDeg : Array.Empty<double>(), isAdaptive ? x.RotXZDeg : Array.Empty<double>(), x.PipeKind, alignment.SourceFile, alignment.RecordNumber, outerDiameterMm,
                x.DeflectionDeg, x.AngleDeg, x.EffectiveAllowableDeg, x.ResidualDeg, x.CenterlineRadiusMm, x.LayingLengthMm, x.Form, x.WeightKg,
                nodeStationM - x.UpstreamLegMm / 1000d, nodeStationM + x.DownstreamLegMm / 1000d, nodeStationText);
        }).ToList();
        return (plans, fallbacks);
    }

    private static AdaptiveBendPlacementResult AddOuterDiameterFallbackWarning(AdaptiveBendPlacementResult result, HashSet<(string PipeKind, double DiameterMm, string SourceKind, double OuterDiameterMm)> outerDiameterFallbacks)
    {
        if (outerDiameterFallbacks.Count == 0) return result;
        // 다른 관종 값을 대신 쓴 것이므로 조용히 넘어가지 않고 어떤 관종에서 가져왔는지 알린다.
        var fallbackNotice = $"직관 제원표에 없는 관종/DN {outerDiameterFallbacks.Count}건은 같은 DN의 다른 관종 외경으로 곡관을 배치했습니다(외경은 관종과 무관합니다): "
            + string.Join(", ", outerDiameterFallbacks
                .OrderBy(x => x.PipeKind).ThenBy(x => x.DiameterMm)
                .Select(x => $"{x.PipeKind}/DN{x.DiameterMm:0.##} ← {x.SourceKind} {x.OuterDiameterMm:0.##}mm"));
        return result with { Warnings = result.Warnings.Prepend(fallbackNotice).ToList() };
    }

    private static string FormatStation(double stationM) => $"{Math.Floor(stationM / 1000d):0}+{stationM % 1000d:000.000}";

    private ReducerPlacementResult PlaceReducers(ReducerTrimPlan plan, IReadOnlyList<PipeAlignment> alignments,
        AlignmentPlacementOrigin origin, PipeInfoParameterContext info, IProgress<PipeAlignmentProgress>? progress)
    {
        var warnings = new HashSet<string>();
        var placements = plan.Placements.Select(x =>
        {
            var alignment = alignments[x.AlignmentIndex];
            var largeDiameter = Math.Max(x.UpstreamDiameterMm, x.DownstreamDiameterMm);
            var smallDiameter = Math.Min(x.UpstreamDiameterMm, x.DownstreamDiameterMm);
            var largeSpec = origin.StraightPipeSpecs.Find(x.PipeKind, largeDiameter);
            var smallSpec = origin.StraightPipeSpecs.Find(x.PipeKind, smallDiameter);
            var largeFallback = origin.StraightPipeSpecs.FindOuterDiameterByDiameter(largeDiameter);
            var smallFallback = origin.StraightPipeSpecs.FindOuterDiameterByDiameter(smallDiameter);
            if (largeSpec is null || smallSpec is null)
                warnings.Add($"직관 제원이 없어 이경관 {x.PipeKind}/DN{largeDiameter:0.##}×{smallDiameter:0.##}을 호칭지름 기준 속 찬 형상으로 배치합니다.");
            return new ReducerDirectShapePlacementPlan(x, alignment.SourceFile, alignment.RecordNumber,
                largeSpec?.OuterDiameterMm ?? largeFallback?.OuterDiameterMm ?? largeDiameter, smallSpec?.OuterDiameterMm ?? smallFallback?.OuterDiameterMm ?? smallDiameter,
                largeSpec?.ThicknessMm ?? 0d, smallSpec?.ThicknessMm ?? 0d, origin.GetZOffsetM(x.PipeKind, largeDiameter));
        }).ToList();
        var result = _reducerRepo.Place(placements, origin, info, progress);
        return result with { Warnings = warnings.Concat(result.Warnings).ToList() };
    }

    private static IReadOnlyList<IReadOnlyList<VertexTrim>> MergeTrims(BendTrimPlan? bendPlan, ReducerTrimPlan? reducerPlan,
        IReadOnlyList<PipeAlignment> alignments)
    {
        var merged = new List<IReadOnlyList<VertexTrim>>(alignments.Count);
        for (var i = 0; i < alignments.Count; i++)
        {
            var values = new VertexTrim[alignments[i].Vertices.Count];
            Array.Fill(values, VertexTrim.None);
            var bends = bendPlan?.Plans.FirstOrDefault(x => x.AlignmentIndex == i)?.Trims;
            var reducers = reducerPlan?.Trims.ElementAtOrDefault(i);
            for (var v = 0; v < values.Length; v++)
                values[v] = bends is not null && v < bends.Count && !bends[v].IsEmpty
                    ? bends[v]
                    : reducers is not null && v < reducers.Count ? reducers[v] : VertexTrim.None;
            merged.Add(values);
        }
        return merged;
    }

    private static ReducerAlignment AlignmentFor(ZDatum datum)
    {
        // TODO: 제원표 확정 시 동심/편심 표준품 구분에 사용. 현재 형상은 폴리선 분할점에서 편심을 얻는다.
        return datum == ZDatum.Centerline ? ReducerAlignment.Concentric : ReducerAlignment.Eccentric;
    }

    private static (string FamilyName, string TypeName) ParseFamilyType(string? familyTypeName)
    {
        if (string.IsNullOrWhiteSpace(familyTypeName))
            throw new InvalidOperationException("배치할 곡관이 있습니다. 곡관 패밀리·유형을 선택하세요.");
        var separator = familyTypeName.LastIndexOf(" : ", StringComparison.Ordinal);
        if (separator <= 0 || separator >= familyTypeName.Length - 3)
            throw new InvalidOperationException($"곡관 패밀리·유형 표기가 '패밀리명 : 유형명' 형식이 아닙니다: {familyTypeName}");
        return (familyTypeName[..separator], familyTypeName[(separator + 3)..]);
    }

    private (int Count, int Skipped, IReadOnlyList<string> Warnings) ToDirectShape(IReadOnlyList<PipeAlignment> alignments, AlignmentPlacementOrigin origin, IProgress<PipeAlignmentProgress>? progress)
    { var result = _alignmentRepo.Create(new PipeAlignmentCreateDefinition(alignments, origin), progress); return (result.CreatedCount, result.SkippedSegments, result.Warnings); }
    private static IReadOnlyList<string> GetElevationWarnings(IReadOnlyList<PipeAlignment> alignments, PipeAlignmentModelingRequest request, StraightPipeSpecTable specs)
    {
        var missing = alignments
            .Where(x => specs.Find(x.PipeKind, x.DiameterMm) is null)
            .Select(x => (x.PipeKind, x.DiameterMm))
            .Distinct()
            .ToList();
        if (missing.Count == 0) return Array.Empty<string>();

        var values = string.Join(", ", missing.Select(x => $"{x.PipeKind}/DN{x.DiameterMm:0.##}"));
        if (PipeElevationDatum.NeedsSpec(request.ZDatum))
            return new[] { $"직관 제원이 없어 관종/DN {missing.Count}건은 호칭지름을 외경으로 간주해 높이를 보정했습니다{(UsesBends(request.OutputMode) ? "(OD·두께 정보도 기록하지 못했습니다)" : string.Empty)}: {values}" };
        return UsesBends(request.OutputMode)
            ? new[] { $"직관 제원이 없어 OD·두께 인스턴스 매개변수를 기록하지 못한 관종/DN {missing.Count}건: {values}" }
            : Array.Empty<string>();
    }
    /// <summary>직관 배치 결과를 스위치 공통 형태로 맞춘다. 직관 배치에는 건너뛴 세그먼트 개념이 없어 Skipped는 0이다.</summary>
    private static (int Count, int Skipped, IReadOnlyList<string> Warnings) ToStraight(AlignmentStraightPlacementResult result, IReadOnlyList<string> bendWarnings)
        => (result.Count, 0, bendWarnings.Concat(result.Warnings).ToList());
    private static IReadOnlyList<string> FindOuterDiameterMismatches(AlignmentStraightPlacementResult? straight, AdaptiveBendPlacementResult bends)
    {
        if (straight?.OuterDiametersMm is null || bends.OuterDiametersMm is null) return Array.Empty<string>();
        var mismatches = bends.OuterDiametersMm
            .Where(x => straight.OuterDiametersMm.TryGetValue(x.Key, out var straightOd) && Math.Abs(straightOd - x.Value) > 1e-6)
            .Select(x => $"{x.Key.PipeKind}/DN{x.Key.DiameterMm:0.##}: 직관 {straight.OuterDiametersMm[x.Key]:0.##}mm, 곡관 {x.Value:0.##}mm")
            .ToList();
        return mismatches.Count == 0 ? Array.Empty<string>() : new[] { $"직관과 곡관에 기록한 OD가 다른 관종/DN {mismatches.Count}건: {string.Join(", ", mismatches)}" };
    }
    /// <summary>같은 경고 문구가 요소마다 반복되므로(예: "짧은 곡선 요소") 문구별 건수로 묶어 한 줄씩만 보여준다.</summary>
    private static IReadOnlyList<string> SummarizeSuppressedWarnings(IReadOnlyList<string> messages)
    {
        if (messages.Count == 0) return Array.Empty<string>();
        var grouped = messages.GroupBy(x => x).OrderByDescending(x => x.Count());
        return new[] { $"Revit 경고 {messages.Count}건을 자동으로 무시하고 배치를 계속했습니다: "
            + string.Join(", ", grouped.Select(x => $"{x.Key} {x.Count()}건")) };
    }
    private static IReadOnlyList<string> FindMiterLimitWarnings(IReadOnlyList<PipeAlignment> alignments)
    {
        var count = 0;
        foreach (var alignment in alignments)
        {
            for (var index = 1; index + 1 < alignment.Vertices.Count; index++)
            {
                var previous = alignment.Vertices[index - 1];
                var current = alignment.Vertices[index];
                var next = alignment.Vertices[index + 1];
                var incoming = new DHBIMWATER.Core.Geometry.Vector3D(
                    current.X - previous.X, current.Y - previous.Y, current.Z - previous.Z);
                var outgoing = new DHBIMWATER.Core.Geometry.Vector3D(
                    next.X - current.X, next.Y - current.Y, next.Z - current.Z);
                if (incoming.Length <= 1e-12 || outgoing.Length <= 1e-12) continue;
                if (StraightJointCutPlane.ExceedsMiterLimit(
                    incoming, outgoing, StraightJointCutPlane.MaxMiterDeflectionDeg)) count++;
            }
        }

        return count == 0
            ? Array.Empty<string>()
            : new[] { $"되꺾임 절점 {count}곳(편각 > {StraightJointCutPlane.MaxMiterDeflectionDeg:0}°)이 있습니다. DirectShape 모델링 시 마이터 대신 관 축 직각 절단을 적용합니다." };
    }
    private static bool UsesBends(PipeAlignmentOutputMode mode)
        => mode is PipeAlignmentOutputMode.DirectShape or PipeAlignmentOutputMode.Adaptive;
    private static string Require(string? value, string name) => !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidOperationException($"{name}을 선택하세요.");
    private static IReadOnlyList<SharedParameterDefinition> GetPreviewOnlyParameterDefinitions() => new List<SharedParameterDefinition>
    {
        new() { Name = "DH_Category", SpecType = ParameterSpecType.Text, GroupType = ParameterGroupType.Data, BindingType = ParameterBindingType.Instance, Categories = new[] { ParameterCategory.GenericModel } },
        new() { Name = "DH_구경", SpecType = ParameterSpecType.Length, GroupType = ParameterGroupType.Data, BindingType = ParameterBindingType.Instance, Categories = new[] { ParameterCategory.GenericModel } }
    };
}
