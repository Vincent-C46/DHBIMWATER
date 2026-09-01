using System.Globalization;

namespace DHBIMWATER.Core.Gis;

/// <summary>엑셀 곡관유형 각도 표기를 해석한다.</summary>
public static class BendKindParser
{
    private static readonly char[] AngleSuffixes = ['˚', '°', 'º', '도'];

    /// <summary>각도면 도(deg) 값을 반환한다. 분기 토큰과 빈 값은 false.</summary>
    public static bool TryParseAngle(string? raw, out double angleDeg)
    {
        angleDeg = 0d;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var text = raw.Trim().TrimEnd(AngleSuffixes).Trim();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out angleDeg);
    }
}
