using System.Text.RegularExpressions;

namespace DHBIMWATER.Core.Piping;

/// <summary>재질·유형·패밀리 이름 문자열 → 관종. 부재 정보 갱신이 빈 DH_관종 채울 때 씀.
/// <para>짧은 영문 코드(SP·PE·DIP)는 앞뒤 영문자 없는 단어일 때만 — `SPEC`·`PEN` 오판 막음.</para>
/// <para>여러 관종 키워드 섞이면 null — 지어내지 않음.</para></summary>
public static class PipeMaterialKeywords
{
    private static readonly (PipeMaterial Material, string[] Contains, string[] Codes)[] Rules =
    [
        (PipeMaterial.DuctileIron, ["덕타일", "주철", "ductile", "dcip"], ["DIP"]),
        (PipeMaterial.Steel, ["강관", "steel"], ["SP"]),
        (PipeMaterial.Pe, ["폴리에틸렌", "polyethylene", "hdpe"], ["PE"]),
        (PipeMaterial.Pvc, ["pvc", "염화비닐"], []),
    ];

    public static PipeMaterial? Resolve(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var found = Rules.Where(rule =>
                rule.Contains.Any(x => text.Contains(x, StringComparison.OrdinalIgnoreCase)) ||
                rule.Codes.Any(code => Regex.IsMatch(text, $"(?<![A-Za-z]){code}(?![A-Za-z])", RegexOptions.IgnoreCase)))
            .Select(rule => rule.Material)
            .Distinct()
            .ToList();
        return found.Count == 1 ? found[0] : null;
    }

    // 앞 문자열부터 판정, 처음 정해지는 값
    public static PipeMaterial? ResolveFirst(IEnumerable<string?> texts)
        => texts.Select(Resolve).FirstOrDefault(x => x is not null);
}
