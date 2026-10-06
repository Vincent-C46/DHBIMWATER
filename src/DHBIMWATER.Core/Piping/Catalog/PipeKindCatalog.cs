using DHBIMWATER.Core.Gis;

namespace DHBIMWATER.Core.Piping;

/// <summary>
/// 관 <b>등급</b> 목록 — 주철관 핸드북의 덕타일주철관 등급(상수 1종관 등)과 강관 핸드북(KS D 3565)의 STWW 등급.
/// 직관 제원의 두께 조회 키이며, 이름은 "관종"이지만 실제로는 등급이다. 관종 자체는 <see cref="PipeMaterial"/>가 담당하고
/// 등급 → 관종은 <see cref="MaterialOf"/>가 푼다(docs/211 § A).
/// </summary>
// TODO: PE·PVC 등급이 추가될 때 PipeKindCatalog → PipeGradeCatalog로 개명 검토.
//       지금 개명하면 PipeAlignment·Repository·파라미터 매핑까지 광범위하게 번져 기능 이득 없이 변경만 커진다.
public static class PipeKindCatalog
{
    public const string Water1 = "상수 1종관";
    public const string Water2 = "상수 2종관";
    public const string Water3 = "상수 3종관";
    public const string Water4 = "상수 4종관";
    public const string Sewer1 = "하수 1종관";
    public const string Sewer2 = "하수 2종관";
    public const string Sewer3 = "하수 3종관";

    // 강관(KS D 3565 부표 1). DN≤300은 STWW 290·370, DN≥350은 STWW 400의 호칭두께 A·B로 갈린다(docs/211 § 0 확정 1).
    // 문자열은 `DH_관종등급`에 그대로 기록된다.
    public const string Steel290 = "STWW 290";
    public const string Steel370 = "STWW 370";
    public const string Steel400A = "STWW 400 A";
    public const string Steel400B = "STWW 400 B";

    public static IReadOnlyList<string> DuctileIronAll => PipeCatalog.Current.DuctileIronGrades;

    public static IReadOnlyList<string> SteelAll => PipeCatalog.Current.SteelGrades;

    /// <summary>등급 전체(주철관 → 강관 순). UI 콤보가 그대로 쓴다.</summary>
    public static IReadOnlyList<string> All => PipeCatalog.Current.AllGrades;

    /// <summary>해당 관종에서 고를 수 있는 등급. 규격 자료가 없는 관종(PE·PVC)은 빈 목록이다.</summary>
    public static IReadOnlyList<string> AllFor(PipeMaterial material) => material switch
    {
        PipeMaterial.DuctileIron => DuctileIronAll,
        PipeMaterial.Steel => SteelAll,
        _ => Array.Empty<string>()
    };

    /// <summary>등급 → 관종. 목록에 없는 명칭이면 null(관종을 추측하지 않는다).</summary>
    public static PipeMaterial? MaterialOf(string? pipeKind)
    {
        var normalized = Normalize(pipeKind);
        if (normalized is null) return null;
        return SteelAll.Contains(normalized, StringComparer.Ordinal) ? PipeMaterial.Steel : PipeMaterial.DuctileIron;
    }

    /// <summary>공백만 정리해 고정 목록에 정확히 대응한다("stww400a" → "STWW 400 A"). 알 수 없는 명칭은 추측하지 않는다.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var compact = string.Concat(value.Where(x => !char.IsWhiteSpace(x)));
        return All.FirstOrDefault(x => string.Equals(string.Concat(x.Where(c => !char.IsWhiteSpace(c))), compact, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>등급 없이 관종만 온 강관("강관"·"SP")의 기본 등급 — DN≤300은 STWW 290, 그 밖은 STWW 400 A(docs/211 § 0 확정 7).
    /// 규격 매핑 행에서 사용자가 바꿀 수 있다.</summary>
    public static string DefaultSteelGrade(double diameterMm) => diameterMm <= 300d ? Steel290 : Steel400A;

    /// <summary>등급·관종 해석 결과(docs/212 § A). <paramref name="Kind"/>는 규격표에서 찾을 등급명이다.</summary>
    /// <param name="Kind">확정 등급. 정규화·강관 기본 등급이 모두 실패하면 원문(또는 null).</param>
    /// <param name="IsResolved">규격표 조회가 가능한 등급으로 확정됐는지(정규화 성공 또는 강관 기본 등급).</param>
    /// <param name="IsSteelDefault">등급 없이 관종만 온 강관("강관"·"SP")을 DN으로 기본 등급 잡은 경우.</param>
    public sealed record GradeResolution(string? Kind, bool IsResolved, bool IsSteelDefault);

    /// <summary>관종 필드 → 파일 단위 수동 관종 → 직경 셀에서 분리된 관종 순으로 등급을 확정한다.
    /// 정규화되는 후보가 없고 <b>어느 후보든</b> 강관 별칭이면 DN으로 기본 등급을 잡는다(docs/211 확정 7).
    /// <para>Loader(<c>AlignmentSourceLoader</c>)와 모델링 창(<c>RebuildDiameterMappings</c>)이 <b>이 함수만</b> 쓴다 —
    /// 종전에는 창이 직경 셀의 관종을 빠뜨려 "강관_D300"이 상수 1종관으로 잡혔다(docs/212 § 0-1).</para></summary>
    public static GradeResolution ResolveGrade(string? fieldKind, string? manualKind, string? parsedKind, double diameterMm)
    {
        var resolved = AlignmentAttributeParser.ResolveKind(fieldKind, manualKind, parsedKind);
        if (resolved.IsNormalized) return new GradeResolution(resolved.Kind, true, false);
        if (IsSteelAlias(fieldKind) || IsSteelAlias(manualKind) || IsSteelAlias(parsedKind))
            return new GradeResolution(DefaultSteelGrade(diameterMm), true, true);
        return new GradeResolution(resolved.Kind, false, false);
    }

    /// <summary>값이 등급이 아니라 강관 관종 별칭("강관"·"SP")인지. <see cref="PipeAlignmentParameters.MaterialFromModelValue"/>가
    /// 이미 코드·라벨을 읽으므로 어휘를 새로 두지 않는다.</summary>
    public static bool IsSteelAlias(string? value)
        => PipeAlignmentParameters.MaterialFromModelValue(value) == PipeMaterial.Steel;
}
