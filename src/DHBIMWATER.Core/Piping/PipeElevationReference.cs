namespace DHBIMWATER.Core.Piping;

public enum PipeElevationMode
{
    Relative,
    Absolute
}

public sealed record LevelInfo(long Id, string Name, double ElevationMm)
{
    public string DisplayLabel => $"{Name} ({ElevationMm / 1000d:+0.000;-0.000;0.000} m)";
}

public static class PipeElevationReference
{
    public static long? PickBaseLevelId(IEnumerable<long> wallBaseLevelIds, IReadOnlyList<LevelInfo> levels, out bool mixed)
    {
        var knownIds = levels.Select(level => level.Id).ToHashSet();
        var counts = wallBaseLevelIds
            .Where(id => id > 0 && knownIds.Contains(id))
            .GroupBy(id => id)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToList();

        mixed = counts.Count > 1;
        if (counts.Count == 0) return null;

        var elevations = levels.ToDictionary(level => level.Id, level => level.ElevationMm);
        return counts.OrderByDescending(item => item.Count)
            .ThenBy(item => elevations[item.Id])
            .ThenBy(item => item.Id)
            .First().Id;
    }
}
