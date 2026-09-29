using System.Globalization;

namespace DHBIMWATER.Core.Gis;

/// <summary>엑셀 곡관유형 각도 표기를 해석한다.</summary>
public static class BendKindParser
{
    private static readonly char[] AngleSuffixes = ['˚', '°', 'º', '도'];

    /// <summary>각도면 도(deg) 값을 반환한다. 분기 토큰과 빈 값은 false.
    /// 핸드북 표기 "5⅝"·"11¼"·"22½"와 "5 5/8"·"5-5/8" 같은 분수 표기도 읽는다(docs/212 § D-6).</summary>
    public static bool TryParseAngle(string? raw, out double angleDeg)
    {
        angleDeg = 0d;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var text = raw.Trim().TrimEnd(AngleSuffixes).Trim();
        text = text.Replace("⅝", ".625").Replace("¼", ".25").Replace("½", ".5").Replace("⅛", ".125").Replace("¾", ".75");
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out angleDeg)) return true;
        // "5 5/8"·"5-5/8" → 정수 + 분자/분모
        var m = System.Text.RegularExpressions.Regex.Match(text, @"^(\d+)[\s-]+(\d+)/(\d+)$");
        if (!m.Success) return false;
        var denominator = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        if (denominator <= 0) return false;
        angleDeg = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)
                   + double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / denominator;
        return true;
    }

    /// <summary>표준각 표기. 핸드북 어휘(5⅝·11¼·22½)를 그대로 쓴다(docs/212 § D-5). 그 외 각은 소수 두 자리.</summary>
    public static string Label(double angleDeg) => Math.Abs(angleDeg - 5.625) < 1e-6 ? "5⅝"
        : Math.Abs(angleDeg - 11.25) < 1e-6 ? "11¼"
        : Math.Abs(angleDeg - 22.5) < 1e-6 ? "22½"
        : angleDeg.ToString("0.##", CultureInfo.InvariantCulture);
}
