using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Core.Gis;

namespace DHBIMWATER.Application.Gis;

public sealed record AlignmentLoadResult(IReadOnlyList<PipeAlignment> Alignments, int DiameterUnresolvedCount, IReadOnlyList<string> Warnings);

public sealed class AlignmentSourceLoader
{
    private readonly IReadOnlyList<IAlignmentSourceReader> _readers;
    public AlignmentSourceLoader(IEnumerable<IAlignmentSourceReader> readers) => _readers = readers.ToList();

    /// <summary>파일을 읽고 필드 매핑을 적용해 PipeAlignment 목록을 만든다. 트랜잭션·Revit 의존 없음.</summary>
    public AlignmentLoadResult Load(IReadOnlyList<AlignmentSourceFile> files, bool warnOnUnknownPipeKinds = true)
    {
        var alignments = new List<PipeAlignment>();
        var warnings = new List<string>();
        var unresolved = 0;
        foreach (var file in files)
        {
            var reader = _readers.FirstOrDefault(r => r.CanRead(file.FilePath))
                ?? throw new InvalidOperationException($"'{file.FilePath}' 파일을 읽을 수 있는 리더가 없습니다.");
            var read = reader is IExcelAlignmentSourceReader excelReader && file.ExcelMapping is not null
                ? excelReader.Read(file.FilePath, file.ExcelMapping)
                : reader.Read(file.FilePath);
            warnings.AddRange(read.Warnings);
            var layers = file.Layers is not null ? new HashSet<string>(file.Layers, StringComparer.Ordinal) : null;
            // 방향 반전 대상. 레코드번호는 파일(엑셀은 시트) 단위로만 유일하므로 파일 루프 안에서 만든다.
            var reversed = file.ReversedRecordNumbers is { Count: > 0 } ? new HashSet<string>(file.ReversedRecordNumbers, StringComparer.Ordinal) : null;
            var failed = 0;
            var kindFallbackCount = 0;
            string? example = null;
            var unknownKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var feature in read.Features)
            {
                if (layers is not null && (!feature.Attributes.TryGetValue("LAYER", out var layer) || !layers.Contains(layer))) continue;
                var rawDiameter = ReadValue(feature, file.DiameterField);
                var rawKind = ReadValue(feature, file.KindField);
                var parsed = AlignmentAttributeParser.ParseDiameter(rawDiameter);
                var mappingKey = DiameterMappingKey.Of(rawDiameter, rawKind);
                ResolvedPipeSpecKey? mapped = null;
                var hasMapping = file.DiameterMappings?.TryGetValue(mappingKey, out mapped) == true;
                // DN과 등급은 독립적으로 확정된다. DN을 "지정 안 함"으로 둔 행에서도 등급 지정은 살아 있어야 한다.
                var hasMappedDiameter = hasMapping && mapped!.DiameterMm is > 0;
                var hasMappedKind = hasMapping && !string.IsNullOrWhiteSpace(mapped!.PipeKind);
                var normalizedMappedKind = hasMappedKind ? PipeKindCatalog.Normalize(mapped!.PipeKind) : null;
                var resolvedKind = hasMappedKind
                    ? new KindResolution(normalizedMappedKind ?? mapped!.PipeKind!, normalizedMappedKind is not null)
                    : AlignmentAttributeParser.ResolveKind(rawKind, file.PipeKind, parsed.Kind);
                var kind = resolvedKind.Kind;
                if (!resolvedKind.IsNormalized)
                {
                    if (kind is not null) unknownKinds.Add(kind);
                    kind = PipeKindCatalog.Water1;
                    kindFallbackCount++;
                }
                double diameterMm; bool diameterResolved;
                if (hasMappedDiameter) { diameterMm = mapped!.DiameterMm!.Value; diameterResolved = true; }
                else if (parsed.Success) { diameterMm = parsed.DiameterMm; diameterResolved = true; }
                else if (file.ManualDiameterMm is > 0) { diameterMm = file.ManualDiameterMm.Value; diameterResolved = true; }
                else { diameterMm = 0d; diameterResolved = false; }
                if (!diameterResolved)
                {
                    unresolved++; failed++;
                    example ??= rawDiameter;
                }
                // 정점 순서 반전 = 시작점·끝점 교환. 좌표값은 그대로 두므로 진단·모델링이 같은 방향을 공유한다.
                var vertexDiameters = ResolveVertexDiameters(feature, file.DiameterMappings, rawKind);
                var isReversed = reversed?.Contains(feature.RecordNumber) == true;
                var vertices = isReversed ? feature.Vertices.Reverse().ToList() : feature.Vertices;
                alignments.Add(feature with
                {
                    PipeKind = kind,
                    DiameterMm = diameterMm,
                    Vertices = vertices,
                    VertexBendKinds = isReversed ? feature.VertexBendKinds?.Reverse().ToList() : feature.VertexBendKinds,
                    // DN은 정점 자체가 아니라 "이 정점부터 시작하는 구간" 값이다. 단순 Reverse는 경계를 한 구간 민다.
                    VertexDiameterMm = isReversed ? ReverseSegmentStartValues(vertexDiameters) : vertexDiameters,
                    VertexDiameterRawValues = isReversed ? ReverseSegmentStartValues(feature.VertexDiameterRawValues) : feature.VertexDiameterRawValues,
                    VertexStationTexts = isReversed ? feature.VertexStationTexts?.Reverse().ToList() : feature.VertexStationTexts,
                    VertexStationM = isReversed ? feature.VertexStationM?.Reverse().ToList() : feature.VertexStationM
                });
                var distinctVertexDn = vertexDiameters?.Where(x => x is > 0).Select(x => x!.Value).Distinct().ToList() ?? [];
                if (distinctVertexDn.Count > 1)
                    warnings.Add($"{Path.GetFileName(feature.SourceFile)} 레코드 {feature.RecordNumber}: DN이 구간마다 다릅니다({string.Join(", ", distinctVertexDn.Select(x => $"DN{x:0.##}"))}). 구간별 DN으로 배치하며, 변경 지점의 이경관(리듀서)은 배치하지 않습니다.");
            }
            if (failed > 0)
            {
                var field = file.DiameterField ?? "미지정";
                var value = string.IsNullOrWhiteSpace(example) ? "값 없음" : example;
                warnings.Add($"{Path.GetFileName(file.FilePath)}: 직경 필드 '{field}' 값을 해석하지 못한 레코드 {failed}건 (예: '{value}'). 곡관 판정과 관저·관정 보정이 부정확합니다.");
            }
            if (warnOnUnknownPipeKinds && unknownKinds.Count > 0)
                warnings.Add($"{Path.GetFileName(file.FilePath)}: 고정 관종 목록에 없는 값({string.Join(", ", unknownKinds)}). 입력 파일 탭에서 관종을 선택하세요.");
            if (warnOnUnknownPipeKinds && kindFallbackCount > 0)
                warnings.Add($"{Path.GetFileName(file.FilePath)}: 등급을 해석하지 못한 레코드 {kindFallbackCount}건은 상수 1종관으로 배치했습니다. 두께·관저고가 실제와 다를 수 있습니다.");
        }
        return new AlignmentLoadResult(alignments, unresolved, warnings);
    }

    private static string? ReadValue(PipeAlignment feature, string? field)
        => field is not null && feature.Attributes.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static IReadOnlyList<double?>? ResolveVertexDiameters(PipeAlignment feature, IReadOnlyDictionary<string, ResolvedPipeSpecKey>? mappings, string? kindRaw)
    {
        if (feature.VertexDiameterMm is null) return null;
        return feature.VertexDiameterMm.Select((value, index) =>
        {
            var raw = feature.VertexDiameterRawValues is { Count: > 0 } && index < feature.VertexDiameterRawValues.Count
                ? feature.VertexDiameterRawValues[index]
                : null;
            return raw is not null && mappings?.TryGetValue(DiameterMappingKey.Of(raw, kindRaw), out var mapped) == true && mapped.DiameterMm is > 0
                ? mapped.DiameterMm
                : value;
        }).ToList();
    }

    private static IReadOnlyList<T>? ReverseSegmentStartValues<T>(IReadOnlyList<T>? source)
    {
        if (source is null || source.Count == 0) return source;
        var reversed = new List<T>(source.Count);
        for (var index = source.Count - 2; index >= 0; index--) reversed.Add(source[index]);
        reversed.Add(source[0]);
        return reversed;
    }
}
