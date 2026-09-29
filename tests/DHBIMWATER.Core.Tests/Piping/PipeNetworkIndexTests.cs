using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

/// <summary>docs/206 § G — 절점·엣지 사전 인덱스가 List와 어긋나지 않는지. 삭제·분할·복제 뒤에도
/// 모든 엣지의 양 끝 절점이 Nodes에 있고, 고아 절점이 남지 않아야 한다.</summary>
public sealed class PipeNetworkIndexTests
{
    [Fact]
    public void RemoveSegment_LeavesNoOrphanNodesAndEveryEdgeEndpointResolves()
    {
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(0, 0), new Point2D(3000, 0));
        network.AddSegment(new Point2D(3000, 0), new Point2D(3000, 3000));
        network.AddSegment(new Point2D(3000, 3000), new Point2D(6000, 3000));
        Assert.Equal(3, network.Edges.Count);
        Assert.Equal(4, network.Nodes.Count);

        network.RemoveSegment(network.Edges[1].Id);

        Assert.Equal(2, network.Edges.Count);
        // 가운데 엣지가 사라져도 양쪽 엣지가 물고 있는 절점 4개는 그대로다(고아 절점 없음, 유실 절점 없음).
        Assert.Equal(4, network.Nodes.Count);
        AssertConsistent(network);
    }

    [Fact]
    public void SplitAndMove_KeepIndexConsistent()
    {
        var network = new PipeNetwork();
        network.AddSegment(new Point2D(0, 0), new Point2D(6000, 0));
        var split = network.SplitSegment(network.Edges[0].Id, 0.5);
        Assert.NotNull(split);
        Assert.Equal(2, network.Edges.Count);
        Assert.Equal(3, network.Nodes.Count);

        // 끝점을 끌어 다른 끝점에 붙이면 절점이 병합된다 — 병합·삭제 경로가 사전을 함께 갱신해야 한다.
        network.MoveNode(network.Nodes[^1].Id, new Point2D(0, 0));
        AssertConsistent(network);

        var clone = network.DeepClone();
        AssertConsistent(clone);
        Assert.Equal(network.Nodes.Count, clone.Nodes.Count);
        Assert.Equal(network.Edges.Count, clone.Edges.Count);
    }

    private static void AssertConsistent(PipeNetwork network)
    {
        var nodeIds = network.Nodes.Select(x => x.Id).ToHashSet();
        Assert.Equal(nodeIds.Count, network.Nodes.Count);
        foreach (var edge in network.Edges)
        {
            Assert.Contains(edge.StartNodeId, nodeIds);
            Assert.Contains(edge.EndNodeId, nodeIds);
        }
        // 어느 절점도 엣지 없이 남지 않는다(RemoveUnconnectedNodes).
        var referenced = network.Edges.SelectMany(x => new[] { x.StartNodeId, x.EndNodeId }).ToHashSet();
        Assert.True(nodeIds.SetEquals(referenced), "고아 절점 또는 유실 절점이 있다.");
    }
}
