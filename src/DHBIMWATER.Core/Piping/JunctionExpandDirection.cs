namespace DHBIMWATER.Core.Piping;

public enum JunctionExpandAxis { X, Y }

public static class JunctionExpandDirection
{
    public static SymbolVector Select(SymbolVector right, SymbolVector up, JunctionExpandAxis axis, bool positive)
    {
        var vector = axis == JunctionExpandAxis.X ? right : up;
        return positive ? vector : new SymbolVector(-vector.X, -vector.Y, -vector.Z);
    }

    public static IReadOnlyList<int> WritableComponents(IReadOnlyList<bool> present,
        IReadOnlyList<bool> writable, SymbolVector local, out IReadOnlyList<int> missingNeeded)
    {
        var values = new[] { local.X, local.Y, local.Z };
        missingNeeded = Enumerable.Range(0, 3).Where(i => !present[i] && Math.Abs(values[i]) > 1e-6).ToList();
        return Enumerable.Range(0, 3).Where(i => present[i] && writable[i]).ToList();
    }
}
