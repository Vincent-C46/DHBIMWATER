using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Gis;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.Core.Parameters;
using DHBIMWATER.Shared.Diagnostics;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Application.UseCases.Gis;

public sealed class ModelPipeAlignmentUseCase
{
    private readonly ITransactionContext _transaction;
    private readonly AlignmentSourceLoader _loader;
    private readonly IPipeAlignmentCommandRepo _alignmentRepo;
    private readonly IAlignmentStraightPlacementRepo _straightRepo;
    private readonly IAlignmentPipePlacementRepo _pipeRepo;
    private readonly IProjectLocationCommandRepo _projectLocationRepo;
    private readonly IProjectLocationQueryRepo _projectLocationQueryRepo;
    private readonly ISharedParameterRepository _sharedParameterRepo;
    private readonly BendSettingsProvider _bendSettings;
    private readonly IAdaptiveBendPlacementRepo _adaptiveBendRepo;
    private readonly IBendDirectShapeRepo _bendDirectShapeRepo;
    private readonly IReducerDirectShapeRepo _reducerRepo;
    private readonly IAlignmentStraightDirectShapeRepo _straightDirectShapeRepo;
    private readonly IElementTypeQueryRepo _typeQueryRepo;

    public ModelPipeAlignmentUseCase(ITransactionContext transaction, AlignmentSourceLoader loader, IPipeAlignmentCommandRepo alignmentRepo,
        IAlignmentStraightPlacementRepo straightRepo, IAlignmentPipePlacementRepo pipeRepo, IProjectLocationCommandRepo projectLocationRepo,
        IProjectLocationQueryRepo projectLocationQueryRepo,
        ISharedParameterRepository sharedParameterRepo, BendSettingsProvider bendSettings, IAdaptiveBendPlacementRepo adaptiveBendRepo,
        IBendDirectShapeRepo bendDirectShapeRepo, IReducerDirectShapeRepo reducerRepo, IAlignmentStraightDirectShapeRepo straightDirectShapeRepo,
        IElementTypeQueryRepo typeQueryRepo)
    { _transaction = transaction; _loader = loader; _alignmentRepo = alignmentRepo; _straightRepo = straightRepo; _pipeRepo = pipeRepo; _projectLocationRepo = projectLocationRepo; _projectLocationQueryRepo = projectLocationQueryRepo; _sharedParameterRepo = sharedParameterRepo; _bendSettings = bendSettings; _adaptiveBendRepo = adaptiveBendRepo; _bendDirectShapeRepo = bendDirectShapeRepo; _reducerRepo = reducerRepo; _straightDirectShapeRepo = straightDirectShapeRepo; _typeQueryRepo = typeQueryRepo; }

    /// <summary>최종 모델 전 Line DirectShape 선형 미리보기 커밋.</summary>
    public PipeAlignmentModelingResult Preview(PipeAlignmentModelingRequest request, IProgress<PipeAlignmentProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        PlacementProfiler.Begin($"미리보기 Line DirectShape / 소스 {request.Files.Count}개");
        try
        {
            progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.Loading, 0, 0));
            AlignmentLoadResult loaded;
            using (PlacementProfiler.Step("01 파일 로드"))
                loaded = _loader.Load(request.Files, warnOnUnknownPipeKinds: false);
            // 트랜잭션 전 취소 지점(docs/236 § B) — 시작 전에 눌린 취소가 여기서 끝난다.
            ThrowIfCancelled(cancellationToken);
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
                        _projectLocationRepo.SetInternalOriginSharedPositionPreservingAngle(origin.X, origin.Y);
                    var deletedPreviews = _alignmentRepo.DeletePreviews();
                    var (count, skipped, warnings, ids) = ToDirectShape(loaded.Alignments, origin, progress, cancellationToken);
                    _transaction.Commit();
                    return new PipeAlignmentModelingResult(request.OutputMode, count, skipped,
                        loaded.Warnings.Concat(FindMiterLimitWarnings(loaded.Alignments))
                            .Concat(deletedPreviews > 0 ? new[] { $"이전 선형 미리보기 {deletedPreviews:N0}개를 지우고 다시 만들었습니다." } : Array.Empty<string>())
                            .Concat(warnings).ToList(),
                        PreviewElementIds: ids);
                }
                catch (OperationCanceledException) { _transaction.Rollback(); throw Cancelled(); }
                catch { _transaction.Rollback(); throw; }
            }
        }
        finally { PlacementProfiler.End(); }
    }

    /// <summary>배치 간격 하한(mm). 0.006m 같은 오타가 수백만 split로 이어졌다(docs/209 § C). 직관 정척은 4~6m다.</summary>
    public const double MinIntervalMm = 100;

    /// <summary>취소는 오류가 아니지만 결과가 없으므로 실패 경로로 보고한다 — 부분 배치는 롤백돼 남지 않는다(docs/209 § A-3).</summary>
    private static DhbimwaterException Cancelled()
        => new(ErrorCodes.PA_Cancelled, "사용자가 배치를 취소했습니다. 이번 실행의 변경은 모두 되돌렸습니다.");

    /// <summary>트랜잭션 밖 취소 지점. 트랜잭션 안처럼 PA-304로 끝낸다 — 원문 OperationCanceledException이면 SYS-999로 보인다(docs/236 § B).</summary>
    private static void ThrowIfCancelled(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) throw Cancelled();
    }

    public PipeAlignmentModelingResult Execute(PipeAlignmentModelingRequest request, IProgress<PipeAlignmentProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (request.IntervalMm < MinIntervalMm)
            throw new DhbimwaterException(ErrorCodes.PA_InvalidInterval,
                $"배치 간격은 {MinIntervalMm / 1000d:0.##}m 이상이어야 합니다(입력 {request.IntervalMm / 1000d:0.###}m).");
        // 장거리 배치 성능 병목 비중 확정용 단계별 계측 (2026-08-24).
        // 결과 보고서: %LOCALAPPDATA%\DHBIMWATER\Logs\placement-profile.log
        // TODO: 병목이 확정되면 계측 배선을 걷어낼지 결정할 것.
        // docs/197 § A-2 — 실행마다 소요가 20배까지 갈리는데 어떤 패밀리·조건으로 돌렸는지 로그에 없어 대조가 불가능했다.
        PlacementProfiler.Begin($"최종 {request.OutputMode} / 간격 {request.IntervalMm:0}mm / 소스 {request.Files.Count}개"
            + $" / 직관 '{request.StraightFamilyTypeName}' / 곡관 '{request.BendFamilyTypeName}'"
            + $" / 공유좌표 {(request.ApplySharedCoordinates ? "적용" : "미적용")} / 기준 {request.ZDatum}");
        try { return ExecuteCore(request, progress, cancellationToken); }
        finally { PlacementProfiler.End(); }
    }

    private PipeAlignmentModelingResult ExecuteCore(PipeAlignmentModelingRequest request, IProgress<PipeAlignmentProgress>? progress, CancellationToken cancellationToken)
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
        // docs/148: 원본 XY·편각 보존. 배치 시 DN별 Global Z 보정.
        var alignments = loaded.Alignments;
        var stepVertices = alignments.Select(_ => (IReadOnlySet<int>)new HashSet<int>()).ToList();
        var origin = new AlignmentPlacementOrigin(reference?.X ?? 0, reference?.Y ?? 0, request.ZDatum, settings.StraightPipes);
        var infoContext = new PipeInfoParameterContext(settings.ActiveJointType, settings.ApplicationMode, request.ZDatum,
            IsNightWork: request.IsNightWork, BendConnection: settings.ActiveBendConnection,
            UseRestraintGland: settings.UseRestraintGland, SteelFittingClass: settings.ActiveSteelFittingClass);
        var elevationWarnings = GetElevationWarnings(alignments, request, settings.StraightPipes)
            .Concat(GetSlopeDatumWarnings(alignments, request, settings.StraightPipes));
        // 원통 DirectShape와 Adaptive 모두 같은 곡관 계획 사용. PipingSystem만 Revit 부속 자동 생성.
        BendTrimPlan? bendPlan;
        IReadOnlyList<string> bendWarnings;
        progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.PlanningBends, 0, 0));
        using (PlacementProfiler.Step("02 곡관 계획(PlanBends)"))
            (bendPlan, bendWarnings) = UsesBends(request.OutputMode)
                ? PlanBends(alignments, stepVertices, request, settings, settingsSource == BendSettingsSource.BuiltInDefault)
                : (null, Array.Empty<string>());
        IReadOnlyList<string> infeasibleWarnings = Array.Empty<string>();
        if (bendPlan is not null)
        {
            (bendPlan, infeasibleWarnings) = RemoveInfeasibleBends(bendPlan, alignments, origin);
            // docs/217 § B — 제거 후 남은 곡관끼리만 다리 겹침을 본다. 배치는 그대로, 적색 표시 + 경고만.
            (bendPlan, var overlapWarnings) = MarkLegOverlaps(bendPlan, alignments);
            infeasibleWarnings = infeasibleWarnings.Concat(overlapWarnings).ToList();
        }
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
        // 로드·곡관 계획은 토큰을 받지 않는다 — 트랜잭션을 열기 전에 한 번 확인한다(docs/236 § B).
        ThrowIfCancelled(cancellationToken);

        using (_transaction)
        {
            try
            {
                _transaction.Begin(request.OutputMode == PipeAlignmentOutputMode.DirectShape ? "직관·곡관·이경관 DirectShape 배치" : "선형 패밀리 배치", suppressWarnings: true);
                using (PlacementProfiler.Step("04 공유 매개변수 확보"))
                {
                    if (UsesBends(request.OutputMode))
                        _sharedParameterRepo.EnsureParameters(
                            PipeAlignmentParameters.Definitions.Concat(PlacementStationParameters.Definitions).ToList(),
                            AdaptiveCategoryIds(request));
                }
                if (request.ApplySharedCoordinates)
                {
                    using var sharedCoordinateScope = PlacementProfiler.Step("05 공유좌표 설정");
                    _projectLocationRepo.SetInternalOriginSharedPositionPreservingAngle(origin.X, origin.Y);
                }
                if (request.OutputMode == PipeAlignmentOutputMode.Adaptive)
                {
                    using var trueNorthScope = PlacementProfiler.Step("05a 진북각 조회");
                    origin = origin with { TrueNorthClockwiseDegrees = _projectLocationQueryRepo.GetTrueNorthToProjectNorthClockwiseDegrees() };
                }
                AlignmentStraightPlacementResult? straightResult = null;
                int count;
                int skipped;
                IReadOnlyList<string> repoWarnings;
                using (PlacementProfiler.Step("06 직관/파이프 배치 전체"))
                    (count, skipped, repoWarnings) = request.OutputMode switch
                {
                    PipeAlignmentOutputMode.DirectShape => ToStraight(straightResult = _straightDirectShapeRepo.PlaceAlong(alignments, string.Empty, request.IntervalMm / 1000d, origin, settings.StraightPipes, mergedTrims, info: infoContext, progress: progress, stepVertices: stepVertices, cancellationToken: cancellationToken), bendWarnings),
                    PipeAlignmentOutputMode.Adaptive => ToStraight(straightResult = _straightRepo.PlaceAlong(alignments, Require(request.StraightFamilyTypeName, "직관 패밀리"), request.IntervalMm / 1000d, origin, settings.StraightPipes, mergedTrims, request.StraightDiameterParameterName, request.StraightOuterDiameterParameterName, request.StraightThicknessParameterName, infoContext, progress, stepVertices, cancellationToken), bendWarnings),
                    PipeAlignmentOutputMode.PipingSystem => (_pipeRepo.PlaceAlong(alignments, Require(request.PipingSystemTypeName, "파이프 시스템 유형"), Require(request.PipeTypeName, "PipeType"), request.LevelName, request.IntervalMm / 1000d, origin, cancellationToken), 0, (IReadOnlyList<string>)Array.Empty<string>()),
                    _ => throw new DhbimwaterException(ErrorCodes.PA_Internal, $"지원하지 않는 출력 모드입니다: {request.OutputMode}")
                };
                AdaptiveBendPlacementResult bendResult;
                using (PlacementProfiler.Step("07 곡관 배치 전체"))
                    bendResult = bendPlan is null ? new AdaptiveBendPlacementResult(0, Array.Empty<string>())
                        : request.OutputMode == PipeAlignmentOutputMode.Adaptive
                            ? PlaceBendFittings(bendPlan, alignments, origin, request, infoContext, progress, cancellationToken)
                            : request.OutputMode == PipeAlignmentOutputMode.DirectShape
                                ? PlaceBendDirectShapes(bendPlan, alignments, origin, request, infoContext, progress, cancellationToken)
                                : new AdaptiveBendPlacementResult(0, Array.Empty<string>());
                ReducerPlacementResult reducerResult;
                using (PlacementProfiler.Step("08 이경관 배치 전체"))
                    reducerResult = request.OutputMode is PipeAlignmentOutputMode.DirectShape or PipeAlignmentOutputMode.Adaptive
                        && reducerPlan is not null
                        ? PlaceReducers(reducerPlan, alignments, origin, infoContext, progress, cancellationToken)
                        : new ReducerPlacementResult(0, Array.Empty<string>());
                progress?.Report(new PipeAlignmentProgress(PipeAlignmentPhase.Committing, 0, 0));
                using (PlacementProfiler.Step("09 트랜잭션 Commit")) _transaction.Commit();
                var suppressedWarningSummary = SummarizeSuppressedWarnings(_transaction.SuppressedWarnings);
                return new PipeAlignmentModelingResult(request.OutputMode, count, skipped,
                    loaded.Warnings.Concat(elevationWarnings).Concat(infeasibleWarnings).Concat(reducerPlan?.Warnings ?? Array.Empty<string>()).Concat(repoWarnings).Concat(bendResult.Warnings).Concat(reducerResult.Warnings)
                        .Concat(FindOuterDiameterMismatches(straightResult, bendResult)).Concat(suppressedWarningSummary).ToList(),
                    bendResult.Count);
            }
            catch (OperationCanceledException) { _transaction.Rollback(); throw Cancelled(); }
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
            throw new DhbimwaterException(ErrorCodes.PA_BendFamilyNotFound, $"다음 곡관 패밀리·타입을 문서에서 찾을 수 없습니다(로드 필요): {string.Join(", ", missing.Select(x => $"{x.FamilyName}:{x.TypeName}"))}");
    }

    /// <summary>
    /// 절점 곡관 판정을 폴리선 정점 차감량과 5점 가변 곡관 배치 계획으로 환산한다.
    /// </summary>
    private (BendTrimPlan? Plan, IReadOnlyList<string> Warnings) PlanBends(IReadOnlyList<PipeAlignment> alignments,
        IReadOnlyList<IReadOnlySet<int>> stepVertices, PipeAlignmentModelingRequest request, BendSettings settings, bool usesDefault)
    {
        if (request.SnapToleranceMm <= 0) throw new DhbimwaterException(ErrorCodes.PA_InvalidSnapTolerance, "스냅 허용오차는 0보다 커야 합니다.");

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
        // docs/149 §C: 엑셀 곡관유형 지정 수와 계획 수가 다르면 요약 1줄을 절점별 경고보다 먼저 보여준다.
        // 절점 병합·치수 미입력 등 줄어든 사유는 아래 절점별 경고가 담당한다.
        var declaredBends = CountDeclaredBends(alignments);
        if (declaredBends > 0 && declaredBends != plan.Placements.Count)
            warnings.Add($"엑셀 곡관유형 지정 {declaredBends}곳 중 {plan.Placements.Count}곳을 계획했습니다(차이 {Math.Abs(declaredBends - plan.Placements.Count)}곳). 아래 절점별 경고를 확인하세요.");

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

    /// <summary>
    /// 엑셀 곡관유형 열에 값이 있는 정점 수. 단부 정점(0, n-1)은 편각이 정의되지 않아
    /// <see cref="BendTrimPlanner"/>와 같은 범위(1 ~ n-2)로 센다. 곡관유형 열이 없는 선형은 제외한다.
    /// </summary>
    private static int CountDeclaredBends(IReadOnlyList<PipeAlignment> alignments)
    {
        var count = 0;
        foreach (var alignment in alignments)
        {
            if (alignment.VertexBendKinds is not { } kinds) continue;
            for (var v = 1; v < alignment.Vertices.Count - 1; v++)
                if (v < kinds.Count && !string.IsNullOrWhiteSpace(kinds[v])) count++;
        }
        return count;
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
    private AdaptiveBendPlacementResult PlaceBendFittings(BendTrimPlan plan, IReadOnlyList<PipeAlignment> alignments, AlignmentPlacementOrigin origin, PipeAlignmentModelingRequest request, PipeInfoParameterContext infoContext, IProgress<PipeAlignmentProgress>? progress, CancellationToken cancellationToken)
    {
        if (plan.Placements.Count == 0) return new AdaptiveBendPlacementResult(0, Array.Empty<string>());
        var (familyName, typeName) = ParseFamilyType(request.BendFamilyTypeName);
        var (bendPlans, outerDiameterFallbacks) = BuildBendPlans(plan, alignments, origin, request, familyName, typeName, true);
        var result = _adaptiveBendRepo.Place(bendPlans, origin, infoContext, progress, cancellationToken);
        return AddOuterDiameterFallbackWarning(result, outerDiameterFallbacks);
    }

    private AdaptiveBendPlacementResult PlaceBendDirectShapes(BendTrimPlan plan, IReadOnlyList<PipeAlignment> alignments, AlignmentPlacementOrigin origin, PipeAlignmentModelingRequest request, PipeInfoParameterContext infoContext, IProgress<PipeAlignmentProgress>? progress, CancellationToken cancellationToken)
    {
        var (bendPlans, outerDiameterFallbacks) = BuildBendPlans(plan, alignments, origin, request, string.Empty, string.Empty, false);
        return AddOuterDiameterFallbackWarning(_bendDirectShapeRepo.Place(bendPlans, origin, infoContext, progress, cancellationToken), outerDiameterFallbacks);
    }

    private static (BendTrimPlan Plan, IReadOnlyList<string> Warnings) RemoveInfeasibleBends(
        BendTrimPlan plan, IReadOnlyList<PipeAlignment> alignments, AlignmentPlacementOrigin origin)
    {
        var profiles = new Dictionary<int, AlignmentStationProfile>();
        var impossible = plan.Placements.Select(placement =>
        {
            var outerDiameterMm = origin.StraightPipeSpecs.Find(placement.PipeKind, placement.DiameterMm)?.OuterDiameterMm
                ?? origin.StraightPipeSpecs.FindOuterDiameterByDiameter(placement.DiameterMm, MaterialOf(placement.PipeKind))?.OuterDiameterMm
                ?? placement.DiameterMm;
            var stationText = GetNodeStationText(placement, alignments, profiles);
            return (Placement: placement, Feasibility: BendArcGeometry.EvaluatePlacementFeasibility(
                placement.NodeId, stationText, placement.DeflectionDeg, placement.AngleDeg,
                placement.CenterlineRadiusMm, outerDiameterMm, placement.UpstreamLegMm, placement.DownstreamLegMm));
        })
            .Where(x => !x.Feasibility.IsPlaceable)
            .ToList();
        if (impossible.Count == 0) return (plan, Array.Empty<string>());

        var excluded = impossible.Select(x => (x.Placement.AlignmentIndex, x.Placement.VertexIndex)).ToHashSet();
        var plans = plan.Plans.Select(alignmentPlan =>
        {
            var trims = alignmentPlan.Trims.ToArray();
            foreach (var (_, vertexIndex) in excluded.Where(x => x.AlignmentIndex == alignmentPlan.AlignmentIndex))
                trims[vertexIndex] = VertexTrim.None;
            return alignmentPlan with { Trims = trims };
        }).ToList();
        var placements = plan.Placements.Where(x => !excluded.Contains((x.AlignmentIndex, x.VertexIndex))).ToList();
        var details = string.Join(", ", impossible.Take(10).Select(x =>
            $"절점 {x.Feasibility.NodeId}(측점 {x.Feasibility.NodeStationText}, 실측 {x.Feasibility.AlignmentDeflectionDeg:0.##}° / 지정 {x.Feasibility.StandardAngleDeg:0.##}°, {x.Feasibility.FailureReason})"));
        if (impossible.Count > 10) details += $", 외 {impossible.Count - 10}건";
        var warning = $"배치할 수 없는 곡관 {impossible.Count}곳은 건너뛰고 직관을 절점까지 붙였습니다: {details}. 엑셀 곡관유형 열의 지정각이 실제 편각과 맞는지 확인하세요.";
        return (plan with { Plans = plans, Placements = placements }, new[] { warning });
    }

    private static (IReadOnlyList<AdaptiveBendPlacementPlan> Plans, HashSet<(string PipeKind, double DiameterMm, string SourceKind, double OuterDiameterMm)> Fallbacks) BuildBendPlans(BendTrimPlan plan, IReadOnlyList<PipeAlignment> alignments, AlignmentPlacementOrigin origin, PipeAlignmentModelingRequest request, string familyName, string typeName, bool isAdaptive)
    {
        var fallbacks = new HashSet<(string PipeKind, double DiameterMm, string SourceKind, double OuterDiameterMm)>();
        var profiles = new Dictionary<int, AlignmentStationProfile>();
        var plans = plan.Placements.Select(x =>
        {
            var alignment = alignments[x.AlignmentIndex];
            if (!profiles.TryGetValue(x.AlignmentIndex, out var profile)) profiles[x.AlignmentIndex] = profile = new AlignmentStationProfile(alignment);
            // 시·종점 측점은 절점 3D 거리 ± 다리 길이 위치를 평면 측점으로 환산한다(docs/215 § B-1).
            var nodeDistanceM = profile.DistanceAtVertex(x.VertexIndex);
            var nodeStationText = GetNodeStationText(x, alignments, profiles);
            var spec = origin.StraightPipeSpecs.Find(x.PipeKind, x.DiameterMm);
            var outerDiameterMm = spec?.OuterDiameterMm;
            if (outerDiameterMm is null && origin.StraightPipeSpecs.FindOuterDiameterByDiameter(x.DiameterMm, MaterialOf(x.PipeKind)) is { } fallback)
            {
                outerDiameterMm = fallback.OuterDiameterMm;
                fallbacks.Add((x.PipeKind, x.DiameterMm, fallback.PipeKind, fallback.OuterDiameterMm));
            }
            return new AdaptiveBendPlacementPlan(x.NodeId, familyName, typeName, x.Points, x.DiameterMm, x.WallThicknessMm,
                origin.GetZOffsetM(x.PipeKind, x.DiameterMm), isAdaptive ? request.BendDiameterParameterName : null, isAdaptive ? request.BendWallThicknessParameterName : null, isAdaptive ? request.BendOuterDiameterParameterName : null, x.IsAcceptable,
                isAdaptive ? x.RotXYDeg : Array.Empty<double>(), isAdaptive ? x.RotXZDeg : Array.Empty<double>(), x.PipeKind, alignment.SourceFile, alignment.RecordNumber, outerDiameterMm,
                x.DeflectionDeg, x.AngleDeg, x.EffectiveAllowableDeg, x.ResidualDeg, x.CenterlineRadiusMm, x.LayingLengthMm, x.Form, x.WeightKg,
                profile.StationAt(nodeDistanceM - x.UpstreamLegMm / 1000d), profile.StationAt(nodeDistanceM + x.DownstreamLegMm / 1000d), nodeStationText,
                x.HasLegOverlap);
        }).ToList();
        return (plans, fallbacks);
    }

    private static (BendTrimPlan Plan, IReadOnlyList<string> Warnings) MarkLegOverlaps(BendTrimPlan plan, IReadOnlyList<PipeAlignment> alignments)
    {
        var (placements, pairs) = BendLegOverlapDetector.Mark(plan.Placements, alignments);
        if (pairs.Count == 0) return (plan, Array.Empty<string>());

        var profiles = new Dictionary<int, AlignmentStationProfile>();
        var details = string.Join(", ", pairs.Take(10).Select(x =>
            $"절점 {x.Upstream.NodeId}(측점 {GetNodeStationText(x.Upstream, alignments, profiles)})↔절점 {x.Downstream.NodeId}(측점 {GetNodeStationText(x.Downstream, alignments, profiles)})"));
        if (pairs.Count > 10) details += $", 외 {pairs.Count - 10}건";
        var warning = $"곡관 다리가 서로 겹치는 곳 {pairs.Count}곳(엑셀 곡관유형·좌표 확인 필요) — 3D 뷰에서 적색 표시했습니다: {details}.";
        return (plan with { Placements = placements }, new[] { warning });
    }

    private static string GetNodeStationText(BendPlacement placement, IReadOnlyList<PipeAlignment> alignments,
        Dictionary<int, AlignmentStationProfile> profiles)
    {
        var alignment = alignments[placement.AlignmentIndex];
        if (alignment.VertexStationTexts is { } stationTexts && placement.VertexIndex >= 0
            && placement.VertexIndex < stationTexts.Count && !string.IsNullOrWhiteSpace(stationTexts[placement.VertexIndex]))
            return stationTexts[placement.VertexIndex]!;
        if (!profiles.TryGetValue(placement.AlignmentIndex, out var profile))
            profiles[placement.AlignmentIndex] = profile = new AlignmentStationProfile(alignment);
        return FormatStation(profile.StationAt(profile.DistanceAtVertex(placement.VertexIndex)));
    }

    private static AdaptiveBendPlacementResult AddOuterDiameterFallbackWarning(AdaptiveBendPlacementResult result, HashSet<(string PipeKind, double DiameterMm, string SourceKind, double OuterDiameterMm)> outerDiameterFallbacks)
    {
        if (outerDiameterFallbacks.Count == 0) return result;
        // 다른 등급 값을 대신 쓴 것이므로 조용히 넘어가지 않고 어떤 등급에서 가져왔는지 알린다. 관종을 넘어 빌리지는 않는다(docs/211 § B-3).
        var fallbackNotice = $"직관 제원표에 없는 등급/DN {outerDiameterFallbacks.Count}건은 같은 관종·DN의 다른 등급 외경으로 곡관을 배치했습니다(외경은 같은 관종 안에서 등급과 무관합니다): "
            + string.Join(", ", outerDiameterFallbacks
                .OrderBy(x => x.PipeKind).ThenBy(x => x.DiameterMm)
                .Select(x => $"{x.PipeKind}/DN{x.DiameterMm:0.##} ← {x.SourceKind} {x.OuterDiameterMm:0.##}mm"));
        return result with { Warnings = result.Warnings.Prepend(fallbackNotice).ToList() };
    }

    private static string FormatStation(double stationM) => $"{Math.Floor(stationM / 1000d):0}+{stationM % 1000d:000.000}";

    /// <summary>외경 폴백은 같은 관종 안에서만 한다. 알 수 없는 등급은 주철관으로 본다(관종 축 도입 전과 같은 동작).</summary>
    private static PipeMaterial MaterialOf(string pipeKind) => PipeKindCatalog.MaterialOf(pipeKind) ?? PipeMaterial.DuctileIron;

    private ReducerPlacementResult PlaceReducers(ReducerTrimPlan plan, IReadOnlyList<PipeAlignment> alignments,
        AlignmentPlacementOrigin origin, PipeInfoParameterContext info, IProgress<PipeAlignmentProgress>? progress, CancellationToken cancellationToken)
    {
        var warnings = new HashSet<string>();
        var placements = plan.Placements.Select(x =>
        {
            var alignment = alignments[x.AlignmentIndex];
            var largeDiameter = Math.Max(x.UpstreamDiameterMm, x.DownstreamDiameterMm);
            var smallDiameter = Math.Min(x.UpstreamDiameterMm, x.DownstreamDiameterMm);
            var largeSpec = origin.StraightPipeSpecs.Find(x.PipeKind, largeDiameter);
            var smallSpec = origin.StraightPipeSpecs.Find(x.PipeKind, smallDiameter);
            var largeFallback = origin.StraightPipeSpecs.FindOuterDiameterByDiameter(largeDiameter, MaterialOf(x.PipeKind));
            var smallFallback = origin.StraightPipeSpecs.FindOuterDiameterByDiameter(smallDiameter, MaterialOf(x.PipeKind));
            if (largeSpec is null || smallSpec is null)
                warnings.Add($"직관 제원이 없어 이경관 {x.PipeKind}/DN{largeDiameter:0.##}×{smallDiameter:0.##}을 호칭지름 기준 속 찬 형상으로 배치합니다.");
            return new ReducerDirectShapePlacementPlan(x, alignment.SourceFile, alignment.RecordNumber,
                largeSpec?.OuterDiameterMm ?? largeFallback?.OuterDiameterMm ?? largeDiameter, smallSpec?.OuterDiameterMm ?? smallFallback?.OuterDiameterMm ?? smallDiameter,
                largeSpec?.ThicknessMm ?? 0d, smallSpec?.ThicknessMm ?? 0d,
                origin.GetZOffsetM(x.PipeKind, x.UpstreamDiameterMm), origin.GetZOffsetM(x.PipeKind, x.DownstreamDiameterMm));
        }).ToList();
        var result = _reducerRepo.Place(placements, origin, info, progress, cancellationToken);
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
        // TODO: 제원표 확정 시 동심/편심 표준품 구분에 사용. 현재 편심은 양 끝 Z 오프셋 차이로 생성.
        return datum == ZDatum.Centerline ? ReducerAlignment.Concentric : ReducerAlignment.Eccentric;
    }

    private static (string FamilyName, string TypeName) ParseFamilyType(string? familyTypeName)
    {
        if (string.IsNullOrWhiteSpace(familyTypeName))
            throw new DhbimwaterException(ErrorCodes.PA_BendFamilyRequired, "배치할 곡관이 있습니다. 곡관 패밀리·유형을 선택하세요.");
        var separator = familyTypeName.LastIndexOf(" : ", StringComparison.Ordinal);
        if (separator <= 0 || separator >= familyTypeName.Length - 3)
            throw new DhbimwaterException(ErrorCodes.PA_FamilyNameFormat, $"곡관 패밀리·유형 표기가 '패밀리명 : 유형명' 형식이 아닙니다: {familyTypeName}");
        return (familyTypeName[..separator], familyTypeName[(separator + 3)..]);
    }

    private (int Count, int Skipped, IReadOnlyList<string> Warnings, IReadOnlyList<long> Ids) ToDirectShape(IReadOnlyList<PipeAlignment> alignments, AlignmentPlacementOrigin origin, IProgress<PipeAlignmentProgress>? progress, CancellationToken cancellationToken)
    { var result = _alignmentRepo.Create(new PipeAlignmentCreateDefinition(alignments, origin), progress, cancellationToken); return (result.CreatedCount, result.SkippedSegments, result.Warnings, result.CreatedElementIds ?? Array.Empty<long>()); }
    private const double SlopeDatumToleranceMm = 10d;

    private static IReadOnlyList<string> GetSlopeDatumWarnings(IReadOnlyList<PipeAlignment> alignments, PipeAlignmentModelingRequest request, StraightPipeSpecTable specs)
    {
        if (request.ZDatum == ZDatum.Centerline) return Array.Empty<string>();
        var count = 0;
        var maximumMm = 0d;
        foreach (var alignment in alignments)
        {
            for (var v = 0; v + 1 < alignment.Vertices.Count; v++)
            {
                var start = alignment.Vertices[v];
                var end = alignment.Vertices[v + 1];
                var length = start.DistanceTo(end);
                if (length <= 1e-9) continue;
                var dx = end.X - start.X;
                var dy = end.Y - start.Y;
                var cosine = Math.Sqrt(dx * dx + dy * dy) / length;
                if (cosine <= 1e-9) continue;
                var diameter = AlignmentDiameterProfile.DiameterAtVertex(alignment, v);
                var radiusMm = Math.Abs(PipeElevationDatum.OffsetMm(request.ZDatum, specs.Find(alignment.PipeKind, diameter), diameter));
                var errorMm = radiusMm * (1d / cosine - 1d);
                if (errorMm <= SlopeDatumToleranceMm) continue;
                count++;
                maximumMm = Math.Max(maximumMm, errorMm);
            }
        }
        return count == 0 ? Array.Empty<string>() : new[]
        {
            $"경사가 급해 구간 {count}곳에서 모델 관 위치가 입력 기준면({PipeAlignmentParameters.Label(request.ZDatum)})에서 최대 {maximumMm:0.#}mm 어긋납니다. 계획 절점의 평면좌표·편각 보존을 우선한 결과입니다."
        };
    }

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

    /// <summary>공유 매개변수를 붙일 추가 카테고리. 선택된 직관·곡관 가변패밀리의 실제 카테고리를 읽는다(docs/141).
    /// <para>가변패밀리 목록(<see cref="IElementTypeQueryRepo.GetAdaptiveComponentTypeNames"/>)은 카테고리를 가리지 않아
    /// 일반 모델이 아닌 적응형 패밀리도 고를 수 있다. 그 경우 정적 정의(일반 모델)만으로는 바인딩이 비어
    /// 배치는 되는데 DH_* 값이 하나도 기록되지 않는다.</para>
    /// <para>DirectShape는 항상 일반 모델로 만들므로 더할 것이 없다.</para></summary>
    private IReadOnlyList<int>? AdaptiveCategoryIds(PipeAlignmentModelingRequest request)
    {
        if (request.OutputMode != PipeAlignmentOutputMode.Adaptive) return null;
        var ids = new[] { request.StraightFamilyTypeName, request.BendFamilyTypeName }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => _typeQueryRepo.GetFamilyTypeCategoryId(x!))
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();
        return ids.Count > 0 ? ids : null;
    }
    private static string Require(string? value, string name) => !string.IsNullOrWhiteSpace(value) ? value : throw new DhbimwaterException(ErrorCodes.PA_SelectionRequired, $"{name}을 선택하세요.");
    private static IReadOnlyList<SharedParameterDefinition> GetPreviewOnlyParameterDefinitions() => new List<SharedParameterDefinition>
    {
        new() { Name = "DH_Category", SpecType = ParameterSpecType.Text, GroupType = ParameterGroupType.Data, BindingType = ParameterBindingType.Instance, Categories = new[] { ParameterCategory.GenericModel } }
    };
}
