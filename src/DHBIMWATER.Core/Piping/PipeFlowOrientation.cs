using DHBIMWATER.Shared.Errors;
using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Core.Piping;

/// <summary>기점에서 관망을 순회해 엣지별 상류에서 하류 방향을 결정한다.</summary>
public sealed class PipeFlowOrientation
{
    private const double PositionToleranceMm = 0.01;
    private readonly IReadOnlyDictionary<Guid, bool> _forwards;

    private PipeFlowOrientation(IReadOnlyDictionary<Guid, bool> forwards) => _forwards = forwards;

    /// <summary>엣지 Id의 흐름이 Start에서 End 방향이면 true다.</summary>
    public bool IsForward(Guid edgeId)
        => _forwards.TryGetValue(edgeId, out var forward)
            ? forward
            : throw new DhbimwaterException(ErrorCodes.PL_EdgeWithoutFlow, $"흐름 방향이 없는 엣지입니다: {edgeId}.");   // 코드 연결(docs/240 § B)

    public static PipeFlowOrientation Build(PipeNetworkDefinition network)
    {
        ArgumentNullException.ThrowIfNull(network);
        var forwards = new Dictionary<Guid, bool>();

        while (forwards.Count < network.Edges.Count)
        {
            var root = network.Edges.First(x => !forwards.ContainsKey(x.Id)).Start;
            var queue = new Queue<Point2D>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                var enteredNode = queue.Dequeue();
                foreach (var edge in network.Edges)
                {
                    if (forwards.ContainsKey(edge.Id)) continue;
                    var entersStart = IsAt(edge.Start, enteredNode);
                    var entersEnd = IsAt(edge.End, enteredNode);
                    if (!entersStart && !entersEnd) continue;

                    forwards.Add(edge.Id, entersStart);
                    queue.Enqueue(entersStart ? edge.End : edge.Start);
                }
            }
        }

        return new PipeFlowOrientation(forwards);
    }

    private static bool IsAt(Point2D point, Point2D node) => point.DistanceTo(node) <= PositionToleranceMm;
}
