using System.Globalization;
using System.Text.RegularExpressions;

namespace DHBIMWATER.Core.Gis;

public static class StationTextParser
{
    private static readonly Regex StationPattern = new(
        @"^(?:NO\.)?\s*(\d+(?:\.\d+)?)\+(\d+(?:\.\d+)?)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static double? Parse(string? raw, StationParseMode mode, double intervalM)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var match = StationPattern.Match(raw.Trim());
        if (!match.Success)
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
        if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var head)
            || !double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var tail)) return null;
        return mode == StationParseMode.NumberChain ? head * intervalM + tail : head * 1000d + tail;
    }
}
