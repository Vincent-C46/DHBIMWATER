using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Core.Piping;

public sealed class PipeNode
{
    public PipeNode(Guid id, Point2D position)
    {
        Id = id;
        Position = position;
    }

    public Guid Id { get; }
    /// <summary>모델 좌표(mm). 이동은 <see cref="PipeNetwork.MoveNode"/>·<see cref="PipeNetwork.MoveEdges"/>를 거친다(docs/152 § B-1).</summary>
    public Point2D Position { get; internal set; }
    public int Degree { get; internal set; }
    public NodeKind NodeKind { get; internal set; } = NodeKind.Inline;
    /// <summary>꺾인 절점에 놓을 부속 종류(docs/225 § A). Elbow가 아니게 되면 분류 단계에서 Bend로 되돌린다.</summary>
    public PipeNodeFittingKind FittingKind { get; internal set; } = PipeNodeFittingKind.Bend;
    /// <summary>사용자가 지정한 밸브부관의 밸브 쪽 방향(절점에서 바깥을 향하는 단위벡터). null이면 자동 판정이다.
    /// <para>엣지 Id가 아니라 방향으로 들고 있는 이유: 부속 삭제·분할이 엣지를 새 Guid로 다시 만들기 때문이다
    /// (<c>PipeTopologyBuilder.RemoveInlineFitting</c>).</para></summary>
    public Point2D? ValveSideDirection { get; internal set; }
}
