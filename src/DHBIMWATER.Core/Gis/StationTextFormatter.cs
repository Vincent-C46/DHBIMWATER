namespace DHBIMWATER.Core.Gis;

/// <summary>측점(m) ↔ "0+352.500" 표기(docs/219). 입력은 <see cref="StationTextParser"/> 규칙에 "STA." 접두만 더 허용한다.</summary>
public static class StationTextFormatter
{
    public static string Format(double stationM)
    {
        if (!double.IsFinite(stationM)) return string.Empty;
        // 999.9996 → "0+1000.000"이 되지 않도록 mm 단위로 먼저 반올림한다.
        var millimetres = Math.Round(stationM * 1000d, MidpointRounding.AwayFromZero);
        var sign = millimetres < 0 ? "-" : string.Empty;
        millimetres = Math.Abs(millimetres);
        var km = Math.Floor(millimetres / 1_000_000d);
        var rest = (millimetres - km * 1_000_000d) / 1000d;
        return $"{sign}{km:0}+{rest:000.000}";
    }

    /// <summary>"0+352.5" · "352.5" · "STA.0+352.5" · "sta 0+352.5" → 352.5. 해석 실패면 null.</summary>
    public static double? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim().Replace(",", string.Empty, StringComparison.Ordinal);
        if (trimmed.StartsWith("STA", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[3..].TrimStart('.', ' ');
        trimmed = trimmed.Replace(" ", string.Empty, StringComparison.Ordinal);
        var value = StationTextParser.Parse(trimmed, StationParseMode.Station, 0d);
        return value is { } v && double.IsFinite(v) ? v : null;
    }
}
