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

    /// <summary>호칭지름 우선 이름 정렬기(docs/192). 문자열 정렬이 "65A"를 "200A" 아래로 보내던 것을 대신한다.
    /// <para>호칭지름을 못 읽은 이름은 뒤로 보내고, 같은 지름끼리는 이름(Ordinal)으로 가른다 —
    /// 순서가 결정적이어야 목록을 다시 만들어도 흔들리지 않는다.</para></summary>
    public static IComparer<string> NameComparer { get; } = new DiameterFirstComparer();

    private sealed class DiameterFirstComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            // 호칭지름을 못 읽은 이름(지름이 이름에 없는 밸브 등)은 숫자 목록 뒤로 보낸다.
            var byDiameter = (Parse(x) ?? double.PositiveInfinity).CompareTo(Parse(y) ?? double.PositiveInfinity);
            return byDiameter != 0 ? byDiameter : string.CompareOrdinal(x, y);
        }
    }

    /// <summary>여러 패밀리가 섞인 목록용 정렬기(docs/192 § D). <b>패밀리명으로 묶은 뒤</b> 그 안에서
    /// <see cref="NameComparer"/>(호칭지름 우선)로 줄 세운다.
    /// <para>호칭지름만으로 정렬하면 "직관 100 · 곡관 100 · 직관 200"처럼 같은 패밀리가 흩어진다.
    /// 반대로 패밀리 이름만 보는 목록(<c>"게이트밸브 65A"</c> 같은 단일 이름)에는 <see cref="NameComparer"/>를 쓴다.</para></summary>
    public static IComparer<string> FamilyGroupedComparer { get; } = new FamilyThenDiameterComparer();

    private sealed class FamilyThenDiameterComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            var byFamily = string.CompareOrdinal(FamilyPart(x), FamilyPart(y));
            return byFamily != 0 ? byFamily : NameComparer.Compare(x, y);
        }

        /// <summary>"패밀리명 : 유형명"의 앞부분. 구분자가 없으면 이름 전체가 패밀리다.</summary>
        private static string FamilyPart(string? name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var separator = name.LastIndexOf(" : ", StringComparison.Ordinal);
            return separator < 0 ? name : name[..separator];
        }
    }

    private static Match MatchExplicit(string value)
    {
        var match = DnPattern.Match(value);
        if (!match.Success) match = APattern.Match(value);
        if (!match.Success) match = DiameterSymbolPattern.Match(value);
        return match;
    }
}
