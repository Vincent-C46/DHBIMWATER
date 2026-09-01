using System.IO;
using System.Text.RegularExpressions;

namespace DHBIMWATER.Infrastructure.Repositories.Gis;

internal static class PrjFileReader
{
    public static (string? Wkt, string? RecommendedEpsg, string? Summary) Read(string path)
    {
        if (!File.Exists(path)) return (null, null, null);
        var wkt = File.ReadAllText(path);
        var epsg = wkt.Contains("Central_Belt_2010", StringComparison.OrdinalIgnoreCase) || wkt.Contains("5186", StringComparison.OrdinalIgnoreCase) ? "EPSG:5186" : null;
        return (wkt, epsg, Summarize(wkt));
    }

    private static string? Summarize(string wkt)
    {
        try
        {
            var parts = new List<string>();
            var spheroid = Value(wkt, "SPHEROID");
            if (!string.IsNullOrWhiteSpace(spheroid)) parts.Add(spheroid.Replace("GRS_1980", "GRS80", StringComparison.OrdinalIgnoreCase));
            var projection = Value(wkt, "PROJECTION");
            if (string.Equals(projection, "Transverse_Mercator", StringComparison.OrdinalIgnoreCase)) parts.Add("횡축 메르카토르(TM)");
            else if (!string.IsNullOrWhiteSpace(projection)) parts.Add(projection);
            var projcs = Value(wkt, "PROJCS");
            var meridian = Number(wkt, "central_meridian");
            if (!string.IsNullOrWhiteSpace(projcs) || meridian is not null)
            {
                var origin = projcs?.Contains("Central_Belt", StringComparison.OrdinalIgnoreCase) == true ? "중부원점" : projcs;
                if (!string.IsNullOrWhiteSpace(origin)) parts.Add(meridian is null ? origin : $"{origin}({meridian:0.##}°E)");
                else if (meridian is not null) parts.Add($"원점({meridian:0.##}°E)");
            }
            var unit = Value(wkt, "UNIT");
            if (string.Equals(unit, "Meter", StringComparison.OrdinalIgnoreCase)) parts.Add("단위 m");
            else if (!string.IsNullOrWhiteSpace(unit)) parts.Add($"단위 {unit}");
            return parts.Count == 0 ? null : string.Join(" · ", parts);
        }
        catch { return null; }
    }

    private static string? Value(string wkt, string name)
    {
        var match = Regex.Match(wkt, $"{name}\\[\\\"(?<value>[^\\\"]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static double? Number(string wkt, string name)
    {
        var match = Regex.Match(wkt, $"PARAMETER\\[\\\"{name}\\\",\\s*(?<value>-?[0-9.]+)", RegexOptions.IgnoreCase);
        return match.Success && double.TryParse(match.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
