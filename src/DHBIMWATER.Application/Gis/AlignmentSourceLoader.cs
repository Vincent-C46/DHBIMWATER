using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Core.Gis;
using DHBIMWATER.Shared.Errors;
using DHBIMWATER.Core.Piping;

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
                ?? throw new DhbimwaterException(ErrorCodes.PA_NoReaderForFile, $"'{file.FilePath}' 파일을 읽을 수 있는 리더가 없습니다.");
            var read = reader is IExcelAlignmentSourceReader excelReader && file.ExcelMapping is not null
                ? excelReader.Read(file.FilePath, file.ExcelMapping)
                : reader.Read(file.FilePath);
            warnings.AddRange(read.Warnings);
            var layers = file.Layers is not null ? new HashSet<string>(file.Layers, StringComparer.Ordinal) : null;
            // 방향 반전 대상. 레코드번호는 파일(엑셀은 시트) 단위로만 유일하므로 파일 루프 안에서 만든다.
            var reversed = file.ReversedRecordNumbers is { Count: > 0 } ? new HashSet<string>(file.ReversedRecordNumbers, StringComparer.Ordinal) : null;
            var exclusions = file.ExclusionRanges is { Count: > 0 }
                ? file.ExclusionRanges.GroupBy(x => x.RecordNumber, StringComparer.Ordinal).ToDictionary(x => x.Key, x => (IReadOnlyList<AlignmentExclusionRange>)x.ToList(), StringComparer.Ordinal)
                : null;
            var failed = 0;
            var kindFallbackCount = 0;
            var steelDefaultCount = 0;
            var materialMismatchCount = 0;
            var mixedDnCount = 0;
            // 엑셀 Station 간격과 평면 거리가 어긋나는 구간 수(docs/215 § D). 측점은 엑셀 값 사이를 비율로 보간한다.
            var stationMismatchCount = 0;
            var mixedVertexKindCount = 0;
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
                // 엑셀처럼 정점별 DN 열만 있는 파일은 파일 단위 직경·관종 필드가 없어 mappingKey가 비고, 정점 매핑 행에서 고른
                // 등급이 버려졌다 — 확정 DN 1650·STWW 400 A로 매핑해도 등급이 STWW 290(강관 기본)으로 남아 직관 OD·두께가
                // 규격표에서 안 잡혔다(2026-09-22). 정점 매핑 행의 첫 등급·첫 DN을 파일 단위 값으로 쓴다. 등급은 관로 1개당 하나다.
                if (!hasMapping && file.DiameterMappings is not null && feature.VertexDiameterRawValues is { Count: > 0 })
                {
                    var vertexMapped = feature.VertexDiameterRawValues
                        .Where(raw => !string.IsNullOrWhiteSpace(raw))
                        .Select(raw => file.DiameterMappings.TryGetValue(DiameterMappingKey.Of(raw, rawKind), out var m) ? m : null)
                        .Where(m => m is not null).Select(m => m!).ToList();
                    var firstKind = vertexMapped.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.PipeKind))?.PipeKind;
                    var firstDn = vertexMapped.FirstOrDefault(m => m.DiameterMm is > 0)?.DiameterMm;
                    if (firstKind is not null || firstDn is not null)
                    {
                        mapped = new ResolvedPipeSpecKey(firstDn, firstKind);
                        hasMapping = true;
                        if (vertexMapped.Select(m => m.PipeKind).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                            mixedVertexKindCount++;
                    }
                }
                // DN과 등급은 독립적으로 확정된다. DN을 "지정 안 함"으로 둔 행에서도 등급 지정은 살아 있어야 한다.
                var hasMappedDiameter = hasMapping && mapped!.DiameterMm is > 0;
                var hasMappedKind = hasMapping && !string.IsNullOrWhiteSpace(mapped!.PipeKind);
                var normalizedMappedKind = hasMappedKind ? PipeKindCatalog.Normalize(mapped!.PipeKind) : null;
                // DN을 등급보다 먼저 확정한다 — 강관 별칭("강관"·"SP")의 기본 등급이 DN으로 갈린다(docs/211 § A-2).
                double diameterMm; bool diameterResolved;
                if (hasMappedDiameter) { diameterMm = mapped!.DiameterMm!.Value; diameterResolved = true; }
                else if (parsed.Success) { diameterMm = parsed.DiameterMm; diameterResolved = true; }
                else if (file.ManualDiameterMm is > 0) { diameterMm = file.ManualDiameterMm.Value; diameterResolved = true; }
                else { diameterMm = 0d; diameterResolved = false; }
                if (!diameterResolved)
                {
                    unresolved++; failed++;
                }
                // 매핑 행이 등급을 확정했으면 그것을, 아니면 창과 같은 규칙으로 필드·수동·직경 셀 관종을 푼다(docs/212 § A).
                var grade = hasMappedKind
                    ? new PipeKindCatalog.GradeResolution(normalizedMappedKind ?? mapped!.PipeKind!, normalizedMappedKind is not null, false)
                    : PipeKindCatalog.ResolveGrade(rawKind, file.PipeKind, parsed.Kind, diameterMm);
                var kind = grade.Kind;
                // 창의 관종 토글이 정한 관종만 받는다(docs/212 § A-2). 필드가 다른 관종 등급·별칭을 주면 창이 "(관종 필드 사용)"으로
                // 표시한 행이라도 그 관종의 기본 등급으로 잡는다 — 창(RebuildDiameterMappings)과 같은 규칙이다.
                var materialMismatch = file.Material is { } material && grade.IsResolved && PipeKindCatalog.MaterialOf(kind) != material;
                if (materialMismatch)
                {
                    kind = DefaultGradeFor(file.Material!.Value, diameterMm);
                    materialMismatchCount++;
                }
                else if (grade.IsSteelDefault) steelDefaultCount++;
                else if (!grade.IsResolved)
                {
                    if (kind is not null) unknownKinds.Add(kind);
                    kind = DefaultGradeFor(file.Material ?? PipeMaterial.DuctileIron, diameterMm);
                    kindFallbackCount++;
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
                stationMismatchCount += new AlignmentStationProfile(alignments[^1]).MismatchCount;
                // docs/219 — 배치 제외 구간(밸브실 등)이 있는 레코드는 경계에서 잘라 조각 선형으로 바꾼다. 반전·측점 집계 뒤에 해야
                // 측점이 원본 기준으로 잡히고 불일치 경고가 조각 수만큼 부풀지 않는다.
                if (exclusions?.TryGetValue(feature.RecordNumber, out var recordRanges) == true)
                {
                    var split = AlignmentExclusionSplitter.Split(alignments[^1], recordRanges);
                    alignments.RemoveAt(alignments.Count - 1);
                    alignments.AddRange(split.Parts);
                    warnings.AddRange(ExclusionWarnings(file.FilePath, feature.RecordNumber, split));
                }
                var distinctVertexDn = vertexDiameters?.Where(x => x is > 0).Select(x => x!.Value).Distinct().ToList() ?? [];
                if (distinctVertexDn.Count > 1)
                    mixedDnCount++;
            }
            if (mixedDnCount > 0)
                warnings.Add($"{Path.GetFileName(file.FilePath)}: 구간별 DN 혼재 {mixedDnCount}건 (리듀서 미배치)");
            if (stationMismatchCount > 0)
                warnings.Add($"{Path.GetFileName(file.FilePath)}: 엑셀 측점 간격과 평면 거리가 0.5m 넘게 다른 구간 {stationMismatchCount}곳 — 측점은 엑셀 값 사이를 비율로 보간합니다. 좌표·측점 열을 확인하세요.");
            if (mixedVertexKindCount > 0)
                warnings.Add($"{Path.GetFileName(file.FilePath)}: 정점 매핑 행의 등급이 서로 달라 {mixedVertexKindCount}건은 첫 등급을 적용했습니다(등급은 관로당 하나)");
            if (failed > 0)
            {
                var field = file.DiameterField ?? "미지정";
                warnings.Add($"{Path.GetFileName(file.FilePath)}: 직경 '{field}' 해석 실패 {failed}건");
            }
            if (warnOnUnknownPipeKinds && unknownKinds.Count > 0)
                warnings.Add($"{Path.GetFileName(file.FilePath)}: 미등록 관종 {string.Join(", ", unknownKinds)}");
            if (warnOnUnknownPipeKinds && kindFallbackCount > 0)
                warnings.Add($"{Path.GetFileName(file.FilePath)}: 등급 해석 실패 {kindFallbackCount}건 → {(file.Material == PipeMaterial.Steel ? "강관 기본 등급" : PipeKindCatalog.Water1)} 적용");
            if (warnOnUnknownPipeKinds && materialMismatchCount > 0)
                warnings.Add($"{Path.GetFileName(file.FilePath)}: 관종 토글({PipeAlignmentParameters.Label(file.Material!.Value)})과 다른 관종의 등급 {materialMismatchCount}건 → 기본 등급 적용");
            if (warnOnUnknownPipeKinds && steelDefaultCount > 0)
                warnings.Add($"{Path.GetFileName(file.FilePath)}: 강관 기본 등급 적용 {steelDefaultCount}건 (DN≤300 {PipeKindCatalog.Steel290}, 그 밖 {PipeKindCatalog.Steel400A})");
        }
        return new AlignmentLoadResult(alignments, unresolved, warnings);
    }

    private static IEnumerable<string> ExclusionWarnings(string filePath, string recordNumber, AlignmentExclusionSplitResult split)
    {
        var label = $"{Path.GetFileName(filePath)} 레코드 {recordNumber}";
        if (split.AppliedRanges.Count > 0)
        {
            var names = string.Join(", ", split.AppliedRanges.Select(x => string.IsNullOrWhiteSpace(x.Name)
                ? $"{StationTextFormatter.Format(x.StartStationM)}~{StationTextFormatter.Format(x.EndStationM)}" : x.Name));
            yield return $"{label}: 배치 제외 구간 {split.AppliedRanges.Count}곳 적용 — 관 미배치 {split.ExcludedLengthM:0.###} m ({names})";
        }
        if (split.SkippedBendKindCount > 0)
            yield return $"{label}: 제외 구간 안 곡관유형 지정 정점 {split.SkippedBendKindCount}곳은 배치하지 않습니다.";
        foreach (var range in split.IgnoredRanges)
            yield return $"{label}: 제외 구간 {(string.IsNullOrWhiteSpace(range.Name) ? string.Empty : range.Name + " ")}({StationTextFormatter.Format(range.StartStationM)}~{StationTextFormatter.Format(range.EndStationM)})은 선형 측점 범위 밖이라 무시했습니다.";
    }

    /// <summary>관종별 기본 등급 — 주철관은 상수 1종관, 강관은 DN으로 갈린다(docs/211 확정 7).</summary>
    private static string DefaultGradeFor(PipeMaterial material, double diameterMm)
        => material == PipeMaterial.Steel ? PipeKindCatalog.DefaultSteelGrade(diameterMm) : PipeKindCatalog.Water1;

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
