using DHBIMWATER.Shared.Errors;
namespace DHBIMWATER.Core.Gis;

/// <summary>선형 위 위치 → 측점 환산(docs/215).
/// <para>측점은 엑셀의 <b>2D Station</b>(평면 누적 연장) 기준이다 — 평면 선형과 종단 선형을 잇는 공통 키이므로 Z(경사)를 섞지 않는다.</para>
/// <para>인자 <c>distanceFromStartM</c>은 지금처럼 <b>선형을 따라간 3D 거리</b>다(직관 분할·곡관 다리 등 형상 계산은 3D 유지).
/// 3D 거리를 같은 구간의 평면 거리로 옮긴 뒤, 인접한 두 엑셀 Station 사이를 평면거리 비율로 보간한다.
/// 엑셀 점 위에서는 항상 엑셀 값과 같다. Station이 없으면 0부터 평면 누적거리다.</para></summary>
public sealed class AlignmentStationProfile
{
    private const double DegenerateEpsilon = 1e-9;
    private const double MismatchToleranceM = 0.5;
    private readonly double[] _distances;
    private readonly double[] _planar;
    private readonly IReadOnlyList<(double PlanarM, double StationM)> _sourceStations;
    /// <summary>선형 전체의 측점 증감 방향(+1 증가 / −1 감소). 역방향 선형은 Station 목록이 뒤집혀 감소한다(AlignmentSourceLoader).</summary>
    private readonly double _direction = 1d;

    public AlignmentStationProfile(PipeAlignment alignment)
    {
        ArgumentNullException.ThrowIfNull(alignment);

        _distances = new double[alignment.Vertices.Count];
        _planar = new double[alignment.Vertices.Count];
        for (var index = 1; index < alignment.Vertices.Count; index++)
        {
            var previous = alignment.Vertices[index - 1];
            var current = alignment.Vertices[index];
            var length = previous.DistanceTo(current);
            _distances[index] = _distances[index - 1] + (length > DegenerateEpsilon ? length : 0d);
            var planarLength = Math.Sqrt((current.X - previous.X) * (current.X - previous.X) + (current.Y - previous.Y) * (current.Y - previous.Y));
            _planar[index] = _planar[index - 1] + (planarLength > DegenerateEpsilon ? planarLength : 0d);
        }

        _sourceStations = alignment.VertexStationM?
            .Take(Math.Min(alignment.VertexStationM.Count, _planar.Length))
            .Select((station, index) => (Index: index, Station: station))
            .Where(x => x.Station is { } value && double.IsFinite(value))
            .Select(x => (_planar[x.Index], x.Station!.Value))
            .ToList()
            ?? [];

        HasSourceStation = _sourceStations.Count >= 2;
        if (!HasSourceStation) return;

        var totalDelta = _sourceStations[^1].StationM - _sourceStations[0].StationM;
        _direction = totalDelta < 0d ? -1d : 1d;
        for (var index = 1; index < _sourceStations.Count; index++)
        {
            var previous = _sourceStations[index - 1];
            var current = _sourceStations[index];
            if (Math.Abs((current.StationM - previous.StationM) - _direction * (current.PlanarM - previous.PlanarM)) > MismatchToleranceM)
                MismatchCount++;
        }
    }

    public bool HasSourceStation { get; }
    /// <summary>인접 엑셀 Station 간격과 평면 거리가 0.5m 넘게 다른 구간 수(docs/215 § D 경고).</summary>
    public int MismatchCount { get; }

    /// <summary>선형 전체 3D 길이(m).</summary>
    public double TotalDistance => _distances.Length == 0 ? 0d : _distances[^1];
    public double StartStation => StationAt(0d);
    public double EndStation => StationAt(TotalDistance);
    public double MinStation => Math.Min(StartStation, EndStation);
    public double MaxStation => Math.Max(StartStation, EndStation);

    /// <summary>측점 → 선형 3D 거리(docs/219 § A). <see cref="StationAt"/>은 정점 구간 안에서 거리에 대해 선형이므로
    /// (평면거리가 3D 거리에 선형이고 측점 앵커는 정점에만 있다) 정점별 측점 사이를 선형 보간하면 정확한 역함수다.
    /// 역방향(측점 감소) 선형도 같다. 선형 측점 범위 밖이면 false.</summary>
    public bool TryGetDistanceAtStation(double station, out double distanceFromStartM)
    {
        const double tolerance = 1e-6;
        distanceFromStartM = 0d;
        if (_distances.Length < 2 || !double.IsFinite(station)) return false;

        var previousStation = StationAt(_distances[0]);
        for (var index = 1; index < _distances.Length; index++)
        {
            var currentStation = StationAt(_distances[index]);
            var low = Math.Min(previousStation, currentStation);
            var high = Math.Max(previousStation, currentStation);
            if (station >= low - tolerance && station <= high + tolerance)
            {
                var span = currentStation - previousStation;
                var t = Math.Abs(span) <= DegenerateEpsilon ? 0d : Math.Clamp((station - previousStation) / span, 0d, 1d);
                distanceFromStartM = _distances[index - 1] + t * (_distances[index] - _distances[index - 1]);
                return true;
            }
            previousStation = currentStation;
        }
        return false;
    }

    /// <param name="distanceFromStartM">선형 시작점부터 선형을 따라간 3D 거리(m).</param>
    public double StationAt(double distanceFromStartM)
    {
        var planar = PlanarAt(distanceFromStartM);
        if (!HasSourceStation) return planar;

        var first = _sourceStations[0];
        if (planar < first.PlanarM) return first.StationM + _direction * (planar - first.PlanarM);

        var k = 0;
        for (var index = 1; index < _sourceStations.Count; index++)
        {
            if (_sourceStations[index].PlanarM > planar) break;
            k = index;
        }
        var anchor = _sourceStations[k];
        if (k == _sourceStations.Count - 1) return anchor.StationM + _direction * (planar - anchor.PlanarM);

        var next = _sourceStations[k + 1];
        var span = next.PlanarM - anchor.PlanarM;
        if (span <= DegenerateEpsilon) return anchor.StationM;
        return anchor.StationM + (planar - anchor.PlanarM) / span * (next.StationM - anchor.StationM);
    }

    /// <summary>정점의 측점. 엑셀 Station이 있는 정점이면 그 값 그대로다(docs/215 § C 스냅샷).</summary>
    public double StationAtVertex(int vertexIndex) => StationAt(DistanceAtVertex(vertexIndex));

    public double DistanceAtVertex(int vertexIndex)
    {
        if ((uint)vertexIndex >= (uint)_distances.Length) throw new DhbimwaterException(ErrorCodes.PA_Internal, $"정점 인덱스 {vertexIndex}가 범위(0~{_distances.Length - 1})를 벗어납니다.");
        return _distances[vertexIndex];
    }

    /// <summary>선형 3D 거리 → 같은 위치의 평면 누적거리. 범위 밖은 첫/끝 구간 비율로 외삽한다.</summary>
    private double PlanarAt(double distanceM)
    {
        var count = _distances.Length;
        if (count < 2) return distanceM;

        var i = 0;
        for (var index = 1; index < count - 1; index++)
        {
            if (_distances[index] > distanceM) break;
            i = index;
        }
        var length3D = _distances[i + 1] - _distances[i];
        if (length3D <= DegenerateEpsilon) return _planar[i] + (distanceM - _distances[i]);
        var t = (distanceM - _distances[i]) / length3D;
        return _planar[i] + t * (_planar[i + 1] - _planar[i]);
    }
}
