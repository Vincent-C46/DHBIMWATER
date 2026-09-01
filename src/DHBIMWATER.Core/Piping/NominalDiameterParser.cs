using System.Text.RegularExpressions;

namespace DHBIMWATER.Core.Piping;

public static class NominalDiameterParser
{
    private static readonly Regex DnPattern = new(@"DN\s*(\d{2,4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex APattern = new(@"(\d{2,4})\s*A\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DiameterSymbolPattern = new(@"[ØøΦφD]\s*(\d{2,4})", RegexOptions.CultureInvariant);
    private static readonly Regex IntegerPattern = new(@"\d{2,4}", RegexOptions.CultureInvariant);

    /// <summary>호칭지름(mm). 유형명 우선, 다음 패밀리명.</summary>
    public static double? Parse(string? familyTypeName)
    {
        if (string.IsNullOrWhiteSpace(familyTypeName)) return null;

        var separatorIndex = familyTypeName.LastIndexOf(" : ", StringComparison.Ordinal);
        var typeName = separatorIndex < 0 ? familyTypeName : familyTypeName[(separatorIndex + 3)..];
        var familyName = separatorIndex < 0 ? null : familyTypeName[..separatorIndex];

        var match = MatchExplicit(typeName);
        if (!match.Success)
            match = IntegerPattern.Matches(typeName).Cast<Match>().LastOrDefault() ?? Match.Empty;
        if (!match.Success && familyName is not null)
            match = MatchExplicit(familyName);
        if (!match.Success) return null;

        var diameter = int.Parse(match.Groups[1].Success ? match.Groups[1].Value : match.Value);
        return diameter is >= 20 and <= 3000 ? diameter : null;
    }

    private static Match MatchExplicit(string value)
    {
        var match = DnPattern.Match(value);
        if (!match.Success) match = APattern.Match(value);
        if (!match.Success) match = DiameterSymbolPattern.Match(value);
        return match;
    }
}
