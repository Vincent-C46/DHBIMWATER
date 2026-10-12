using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

public sealed class JunctionExpandDirectionTests
{
    [Theory]
    [InlineData(JunctionExpandAxis.Y, true, 0, 1)]
    [InlineData(JunctionExpandAxis.Y, false, 0, -1)]
    [InlineData(JunctionExpandAxis.X, true, 1, 0)]
    [InlineData(JunctionExpandAxis.X, false, -1, 0)]
    public void Select_UsesViewAxes(JunctionExpandAxis axis, bool positive, double x, double y)
    {
        var result = JunctionExpandDirection.Select(new(1, 0, 0), new(0, 1, 0), axis, positive);
        Assert.Equal(new SymbolVector(x, y, 0), result);
    }

    [Fact]
    public void WritableComponents_WritesPresentAxesAndReportsMissingNeededAxis()
    {
        var writable = JunctionExpandDirection.WritableComponents([true, true, false], [true, true, false],
            new(2, 3, 0), out var missing);
        Assert.Equal([0, 1], writable);
        Assert.Empty(missing);
        JunctionExpandDirection.WritableComponents([true, true, false], [true, true, false],
            new(0, 0, 2), out missing);
        Assert.Equal([2], missing);
        Assert.Empty(JunctionExpandDirection.WritableComponents([false, false, false], [false, false, false],
            new(0, 0, 0), out _));
        Assert.Empty(JunctionExpandDirection.WritableComponents([true, false, false], [false, false, false],
            new(1, 0, 0), out _));
    }
}
