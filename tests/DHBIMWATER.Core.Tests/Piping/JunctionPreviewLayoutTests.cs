using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

public sealed class JunctionPreviewLayoutTests
{
    private static JunctionPreviewItem Item(long id, int? order) => new(id, (int)id, "단관", "DN100",
        JunctionSymbolKind.Pipe, order, new(1, 0, 0), new(0, 1, 0), new(0, 0, 1), new(0, 1, 0), 0);

    [Fact]
    public void Layout_PreservesInputOrderAndStepSpacing()
    {
        var result = JunctionPreviewLayout.Layout([Item(1, 1), Item(2, 2)], 8, true);
        Assert.Equal([1L, 2L], result.Select(x => x.Item.ElementId));
        Assert.Equal(64, result[0].Y - result[1].Y);
    }

    [Fact]
    public void Layout_CollapsedAndUnorderedItemsUseBaseline()
    {
        var result = JunctionPreviewLayout.Layout([Item(1, 1), Item(2, null)], 8, false);
        Assert.Equal(result[0].Y, result[1].Y);
        Assert.Empty(JunctionPreviewLayout.Layout([], 8, true));
        var unordered = JunctionPreviewLayout.Layout([Item(3, null)], 8, true);
        Assert.Equal(unordered[0].Y, JunctionPreviewLayout.Layout([Item(4, 4)], 8, false)[0].Y);
    }
}
