using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Gis;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Core.Gis;

namespace DHBIMWATER.Application.UseCases.Gis;

/// <summary>
/// 관로 폴리선을 위상 그래프로 만들어 절점을 분류하고 곡관을 판정한다.
/// 읽기 전용이므로 ITransactionContext를 주입받지 않는다.
/// </summary>
public sealed class AnalyzePipeNetworkUseCase
{
    private readonly AlignmentSourceLoader _loader;
    private readonly BendSettingsProvider _settings;

    public AnalyzePipeNetworkUseCase(AlignmentSourceLoader loader, BendSettingsProvider settings)
    { _loader = loader; _settings = settings; }

    public PipeNetworkDiagnosisResult Execute(PipeNetworkDiagnosisRequest request)
    {
        if (request.SnapToleranceMm <= 0) throw new ArgumentOutOfRangeException(nameof(request.SnapToleranceMm));

        var loaded = _loader.Load(request.Files);
        var warnings = loaded.Warnings.ToList();

        var settingsResolution = request.CurrentBendSettings is null
            ? _settings.Load()
            : new BendSettingsResolution(request.CurrentBendSettings, BendSettingsSource.Project);
        var (settings, source) = settingsResolution;
        if (source == BendSettingsSource.BuiltInDefault)
            warnings.Add("규격 설정 없음 — 내장 기본값으로 판정");
        if (settings.Fittings.NeedsRestore)
            warnings.Add("곡관 치수 규격표 불일치 — [관·곡관 규격표]에서 기본값 복원 필요");

        var graph = PipeNetworkBuilder.Build(loaded.Alignments, request.SnapToleranceMm / 1000d);
        var nodes = PipeNetworkClassifier.Classify(graph);
        var authority = BendAuthorityPolicy.Evaluate(loaded.Alignments, graph, nodes, settings, request.SnapToleranceMm / 1000d);
        warnings.AddRange(authority.Warnings);
        var resolutions = authority.Kept.ToDictionary(x => x.NodeId);

        var standard = resolutions.Values.Count(x => x.Kind == BendResolutionKind.Standard);
        var none = resolutions.Values.Count(x => x.Kind == BendResolutionKind.None);
        var unresolved = resolutions.Values.Count(x => x.Kind == BendResolutionKind.Unresolved);
        var angleCounts = resolutions.Values
            .Where(x => x.Kind == BendResolutionKind.Standard)
            .GroupBy(x => x.StandardAngleDeg)
            .OrderByDescending(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Count());
        var conflicts = resolutions.Values.Where(x => !x.IsSizeConsistent).ToList();

        if (resolutions.Values.Any(x => x.Kind != BendResolutionKind.None) && resolutions.Values.All(x => !x.HasFittingSize))
            warnings.Add("곡관 치수 미입력 — 직관 차감 없음");

        if (conflicts.Count > 0) warnings.Add($"곡관 치수 불성립 {conflicts.Count}건");

        warnings.AddRange(FindAdjacentInterference(graph, resolutions));

        return new PipeNetworkDiagnosisResult(
            graph.Nodes.Count,
            graph.Edges.Count,
            nodes.GroupBy(x => x.Kind.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            standard, none, unresolved,
            authority.SuppressedCount,
            conflicts.Count,
            angleCounts,
            BuildAttention(nodes, resolutions),
            warnings);
    }

    /// <summary>
    /// 인접한 두 절점의 곡관이 겹치는지 본다. 간선 길이보다 양끝 t의 합이 크면 그 사이 직관 구간이 음수가 된다.
    /// </summary>
    private static IEnumerable<string> FindAdjacentInterference(PipeNetworkGraph graph, IReadOnlyDictionary<int, BendResolution> resolutions)
    {
        var count = 0;
        foreach (var edge in graph.Edges)
        {
            var head = LayingLength(resolutions, edge.StartNodeId);
            var tail = LayingLength(resolutions, edge.EndNodeId);
            if (head + tail <= 0) continue;

            var lengthMm = edge.Length * 1000d;
            if (head + tail > lengthMm) count++;
        }
        if (count > 0) yield return $"직관 자리 없는 구간 {count}건";
    }

    private static double LayingLength(IReadOnlyDictionary<int, BendResolution> resolutions, int nodeId)
        => resolutions.TryGetValue(nodeId, out var r) && r.Kind != BendResolutionKind.None ? r.LayingLengthMm : 0d;

    /// <summary>
    /// 사용자가 눈으로 확인해야 하는 절점을 담는다. 정상 Tee/Cross/Straight/Reducer는 집계에만 반영한다.
    /// 곡관이 판정된 절점은 정상이라도 항상 담는다 — 모델링을 돌리지 않고 곡관 배치를 검증하는 용도다.
    /// </summary>
    private static IReadOnlyList<PipeNetworkNodeReport> BuildAttention(IReadOnlyList<NodeClassification> nodes, IReadOnlyDictionary<int, BendResolution> resolutions)
    {
        var reports = new List<(int Order, PipeNetworkNodeReport Report)>();
        foreach (var node in nodes)
        {
            resolutions.TryGetValue(node.NodeId, out var bend);
            var order = node.Kind switch
            {
                _ when bend is { Kind: BendResolutionKind.Unresolved } => 0,
                _ when bend is { IsSizeConsistent: false } => 1,
                NodeKind.TooManyBranches => 2,
                NodeKind.EndPoint => 3,
                // 곡관이 실제로 판정된 절점. 위 0~3에 안 걸린 정상 곡관이 여기로 온다.
                _ when bend is not null && bend.Kind != BendResolutionKind.None => 4,
                _ => -1
            };
            if (order < 0) continue;
            reports.Add((order, ToReport(node, bend)));
        }
        return reports.OrderBy(x => x.Order).ThenBy(x => x.Report.NodeId).Select(x => x.Report).ToList();
    }

    private static PipeNetworkNodeReport ToReport(NodeClassification node, BendResolution? bend) => new(
        node.NodeId, node.Kind.ToString(), node.Degree,
        node.Position.X, node.Position.Y, node.Position.Z,
        node.DeflectionDeg, node.MaxDiameterMm, node.MinDiameterMm, node.PipeKind,
        bend?.Kind.ToString() ?? string.Empty,
        bend?.StandardAngleDeg ?? 0d, bend?.ResidualDeg ?? 0d,
        bend?.LayingLengthMm ?? 0d, bend?.CenterlineRadiusMm ?? 0d, bend?.TangentLengthMm ?? 0d,
        bend?.IsSizeConsistent ?? true);

}
