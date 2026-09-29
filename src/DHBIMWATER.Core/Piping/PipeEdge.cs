namespace DHBIMWATER.Core.Piping;

public sealed class PipeEdge
{
    /// <summary>엣지 관경(mm)의 기본값. UI 창의 기본 관경 초기값과 같아야 한다(docs/152 § A-1).</summary>
    public const double DefaultDiameterMm = 100;

    private readonly List<InlineFitting> _inlineFittings = [];

    public PipeEdge(Guid id, Guid startNodeId, Guid endNodeId, IEnumerable<InlineFitting>? inlineFittings = null,
        double diameterMm = DefaultDiameterMm)
    {
        Id = id;
        StartNodeId = startNodeId;
        EndNodeId = endNodeId;
        // 관경은 엣지마다 다르다. 그린 시점의 값을 그대로 들고 다니며, 분할된 조각도 원본 값을 물려받는다.
        DiameterMm = double.IsFinite(diameterMm) && diameterMm > 0 ? diameterMm : DefaultDiameterMm;
        if (inlineFittings is not null) _inlineFittings.AddRange(inlineFittings.OrderBy(x => x.Order));
    }

    public Guid Id { get; }
    public Guid StartNodeId { get; }
    public Guid EndNodeId { get; }
    /// <summary>이 선 하나의 관경(mm). 변경은 <see cref="PipeNetwork.SetEdgeDiameter"/>를 거친다(docs/152 § A-4).</summary>
    public double DiameterMm { get; internal set; }
    public IReadOnlyList<InlineFitting> InlineFittings => _inlineFittings;

    internal void AddFitting(InlineFitting fitting) => _inlineFittings.Add(fitting);
    internal void ReplaceFittings(IEnumerable<InlineFitting> fittings)
    {
        _inlineFittings.Clear();
        _inlineFittings.AddRange(fittings.OrderBy(x => x.Order));
    }
}
