using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

public sealed class JunctionVerticalChainTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Resolve_OrdersConnectedVerticalPartsFromTee(int direction)
    {
        var nodes = new[]
        {
            new JunctionNode(2, [new(1, 0, 0, null), new(-1, 0, 0, null), new(0, 0, direction, 3)]),
            new JunctionNode(3, [new(0, 0, -direction, 2), new(0, 0, direction, 4)]),
            new JunctionNode(4, [new(0, 0, -direction, 3), new(0, 0, direction, 5)]),
            new JunctionNode(5, [new(0, 0, -direction, 4), new(0, 0, direction, 6)]),
            new JunctionNode(6, [new(0, 0, -direction, 5)])
        };

        var result = JunctionVerticalChain.Resolve("J1", nodes);

        Assert.Equal([new VerticalSlot(3, 1, 3), new VerticalSlot(4, 2, 3), new VerticalSlot(5, 3, 3), new VerticalSlot(6, 4, 3)], result.Slots);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Resolve_HorizontalOnly_HasNoSlots()
    {
        var result = JunctionVerticalChain.Resolve("J1", [new JunctionNode(1, [new(1, 0, 0, 2)]),
            new JunctionNode(2, [new(-1, 0, 0, 1)])]);
        Assert.Empty(result.Slots);
    }

    [Fact]
    public void Resolve_OpenTerminal_IncludesSlotAndWarnsOnlyForOrphan()
    {
        var result = JunctionVerticalChain.Resolve("J1", [
            new JunctionNode(1, [new(1, 0, 0, null), new(0, 0, 1, 2)]),
            new JunctionNode(2, [new(0, 0, -1, 1), new(0, 0, 1, null)])]);

        Assert.Equal([new VerticalSlot(2, 1, 2)], result.Slots);
        Assert.Empty(result.Orphans);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Resolve_ThreeConnectors_StopsAtBranch()
    {
        var result = JunctionVerticalChain.Resolve("J1", [
            new JunctionNode(1, [new(1, 0, 0, null), new(0, 0, 1, 2)]),
            new JunctionNode(2, [new(0, 0, -1, 1), new(0, 0, 1, 3), new(0, 0, 1, 4)]),
            new JunctionNode(3, [new(0, 0, -1, 2)]),
            new JunctionNode(4, [new(0, 0, -1, 2)])]);

        Assert.Equal([new VerticalSlot(2, 1, 2)], result.Slots);
        Assert.Single(result.Warnings);
        Assert.Contains("갈라져", result.Warnings[0]);
    }

    [Fact]
    public void Resolve_VerticalThenHorizontalThenVertical_SplitsIntoTwoSets()
    {
        // T(1) ↑ 단관(2) ↑ 엘보(3) → 수평관(4) → 엘보(5) ↑ 단관(6)
        var result = JunctionVerticalChain.Resolve("J1", [
            new JunctionNode(1, [new(1, 0, 0, null), new(-1, 0, 0, null), new(0, 0, 1, 2)]),
            new JunctionNode(2, [new(0, 0, -1, 1), new(0, 0, 1, 3)]),
            new JunctionNode(3, [new(0, 0, -1, 2), new(1, 0, 0, 4)]),
            new JunctionNode(4, [new(-1, 0, 0, 3), new(1, 0, 0, 5)]),
            new JunctionNode(5, [new(-1, 0, 0, 4), new(0, 0, 1, 6)]),
            new JunctionNode(6, [new(0, 0, -1, 5)])]);

        Assert.Equal([new VerticalSlot(2, 1, 2), new VerticalSlot(6, 1, 6)], result.Slots);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Resolve_RunBetweenTeeAndElbow_IsOrderedFromTee()
    {
        // 엘보(3)가 노드 목록 앞이어도 연결자 많은 T(1)가 먼저 차지 → 주관 쪽부터 1, 2
        var result = JunctionVerticalChain.Resolve("J1", [
            new JunctionNode(3, [new(0, 0, -1, 5), new(1, 0, 0, null)]),
            new JunctionNode(1, [new(1, 0, 0, null), new(-1, 0, 0, null), new(0, 0, 1, 2)]),
            new JunctionNode(2, [new(0, 0, -1, 1), new(0, 0, 1, 5)]),
            new JunctionNode(5, [new(0, 0, -1, 2), new(0, 0, 1, 3)])]);

        Assert.Equal([new VerticalSlot(2, 1, 2), new VerticalSlot(5, 2, 2)], result.Slots);
    }

    [Fact]
    public void Resolve_FloatingVerticalPart_HasNoSlots()
    {
        var result = JunctionVerticalChain.Resolve("J1", [new JunctionNode(2, [new(0, 0, 1, null)])]);
        Assert.Empty(result.Slots);
        Assert.Equal([2L], result.Orphans);
        Assert.Single(result.Warnings);
    }
}
