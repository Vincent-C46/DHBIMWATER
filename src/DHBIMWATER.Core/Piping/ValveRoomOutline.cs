using DHBIMWATER.Core.Geometry;

namespace DHBIMWATER.Core.Piping;

/// <summary>
/// 밸브실 외곽 벽체 한 장이다. 좌표는 mm이며 <see cref="InnerStart"/>·<see cref="InnerEnd"/>가
/// 실내측 면(기준면)이고, <see cref="OuterStart"/>·<see cref="OuterEnd"/>는 두께 표시용 실외측 면이다.
/// </summary>
public sealed record OutlineWall(
    long ElementId,
    Point2D InnerStart,
    Point2D InnerEnd,
    Point2D OuterStart,
    Point2D OuterEnd,
    double ThicknessMm,
    /// <summary>이 벽의 <c>DH_Zone</c>(밸브실 유형). 밸브실 빌더가 찍어 둔 값이며, 없으면 빈 문자열이다(docs/161 § A).</summary>
    string Zone = "",
    long BaseLevelId = 0);

/// <summary>
/// 밸브실 안에 서 있는 기둥의 <b>수평 단면 하나</b>다(mm, 반시계). 솔리드의 수평 평면 면에서 뽑은 외곽 루프이며,
/// 수평면이 하나도 없는 형상(경사 기둥 등)에서만 볼록 껍질로 폴백한다(docs/189 § B).
/// <para><b>기둥 한 본이 단면 개수만큼 여러 항목으로 나뉜다</b> — 헌치가 붙은 기둥은 본체 단면과 헌치 단면이
/// 각각 나오며 <see cref="ElementId"/>가 서로 같다. 그래서 기둥 <b>본 수</b>는 항목 수가 아니라
/// <c>ElementId</c>의 서로 다른 개수로 세어야 한다(docs/189 § A-1).</para>
/// <para>기둥은 간섭 확인용 참조물이다 — 외곽 바운딩박스(<see cref="ValveRoomOutline.MinX"/> 등)와
/// IN/OUT 포트 앵커에 영향을 주지 않고, OSNAP 스냅 후보에도 들어가지 않는다(docs/184 § 2, docs/189 § A-3).</para>
/// </summary>
/// <param name="IsCore">같은 <c>ElementId</c> 안에서 면적이 가장 작은 단면(= 기둥 본체)이면 true.
/// 나머지는 헌치 등 덧붙은 단면으로 보고 캔버스에 점선으로 그린다(docs/189 § B-2).</param>
public sealed record OutlineColumn(long ElementId, IReadOnlyList<Point2D> Corners, bool IsCore = true);

/// <summary>외곽선까지의 임시치수 한 방향. <see cref="Hit"/>는 외곽선 위의 도달점이다.</summary>
public readonly record struct OutlineMeasure(Point2D From, Point2D Hit, double DistanceMm);

/// <summary>
/// Revit에서 피킹한 외곽 벽체들로 구성한 밸브실 레이아웃이다.
/// 모든 계산은 벽 <b>내측면</b>을 기준으로 한다(사용자 확정 사항).
/// 순수 기하 계산만 수행하며 Revit·UI에 의존하지 않는다.
/// </summary>
public sealed class ValveRoomOutline
{
    /// <summary>레이 캐스팅·스냅 판정에 쓰는 허용오차(mm).</summary>
    public const double Tolerance = 0.5;

    public static ValveRoomOutline Empty { get; } = new([]);

    public ValveRoomOutline(IReadOnlyList<OutlineWall> walls, IReadOnlyList<OutlineColumn>? columns = null)
    {
        Walls = walls;
        Columns = columns ?? [];
        if (walls.Count == 0) return;

        var xs = walls.SelectMany(w => new[] { w.InnerStart.X, w.InnerEnd.X }).ToList();
        var ys = walls.SelectMany(w => new[] { w.InnerStart.Y, w.InnerEnd.Y }).ToList();
        MinX = xs.Min(); MaxX = xs.Max(); MinY = ys.Min(); MaxY = ys.Max();
    }

    public IReadOnlyList<OutlineWall> Walls { get; }

    /// <summary>함께 가져온 기둥의 단면 목록(docs/184, docs/189). 캔버스 <b>표시 전용</b>이며 외곽 치수 계산에도
    /// OSNAP 스냅 후보에도 들어가지 않는다.
    /// <para><b>항목 수 ≠ 기둥 본 수</b> — 한 본이 단면 개수만큼 쪼개져 들어 있다(<see cref="OutlineColumn"/>).</para></summary>
    public IReadOnlyList<OutlineColumn> Columns { get; }

    /// <summary>외곽이 비었는지 — <b>벽</b> 기준이다. 기둥만 골라서는 외곽을 잡을 수 없다(docs/184 § C-5).</summary>
    public bool IsEmpty => Walls.Count == 0;
    public double MinX { get; }
    public double MaxX { get; }
    public double MinY { get; }
    public double MaxY { get; }
    public double WidthMm => MaxX - MinX;
    public double HeightMm => MaxY - MinY;

    /// <summary>내측면 끝점 전체의 평균. 캔버스 기준점 자동 설정에 쓴다.</summary>
    public Point2D Centroid => IsEmpty
        ? new Point2D(0, 0)
        : new Point2D(
            Walls.Average(w => (w.InnerStart.X + w.InnerEnd.X) / 2),
            Walls.Average(w => (w.InnerStart.Y + w.InnerEnd.Y) / 2));

    /// <summary>외곽 전체의 밸브실 유형(<c>DH_Zone</c>). 값이 있는 벽끼리 한 종류일 때만 그 값이고,
    /// 서로 다른 값이 둘 이상이면 빈 문자열이다(docs/161 § A-2).
    /// <para>빈 값이 섞인 것은 혼재가 아니다 — 일부 벽만 밸브실 빌더 산출물인 경우가 정상이다.
    /// 혼재일 때 최빈값을 고르지 않는 이유: 값을 지어내지 않고 사용자가 창에서 고르게 한다(확정 사항).</para></summary>
    public string Zone
    {
        get
        {
            var zones = Walls.Select(x => x.Zone).Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal).ToList();
            return zones.Count == 1 ? zones[0] : string.Empty;
        }
    }

    /// <summary>모든 내측면 좌표를 <paramref name="offset"/>만큼 평행이동한 새 외곽을 만든다.</summary>
    public ValveRoomOutline Translate(Point2D offset)
    {
        if (IsEmpty) return Empty;
        return new ValveRoomOutline(Walls.Select(w => w with
        {
            InnerStart = Shift(w.InnerStart, offset),
            InnerEnd = Shift(w.InnerEnd, offset),
            OuterStart = Shift(w.OuterStart, offset),
            OuterEnd = Shift(w.OuterEnd, offset),
            // 기둥도 같이 옮기지 않으면 기준점을 바꿨을 때 벽만 움직이고 기둥이 제자리에 남는다(docs/184 § A-4).
        }).ToList(),
        Columns.Select(c => c with { Corners = c.Corners.Select(p => Shift(p, offset)).ToList() }).ToList());
    }

    private static Point2D Shift(Point2D p, Point2D offset) => new(p.X + offset.X, p.Y + offset.Y);

    /// <summary>
    /// <paramref name="from"/>에서 좌·우로 수평 레이를 쏴 가장 가까운 내측면까지의 거리를 구한다.
    /// L자·요철 평면에서도 실제로 마주 보는 변을 찾는다. 도달하는 변이 없으면 null.
    /// </summary>
    public OutlineMeasure? MeasureHorizontal(Point2D from) => Nearest(from, HorizontalHits(from));

    /// <summary><see cref="MeasureHorizontal"/>의 수직 방향 버전이다.</summary>
    public OutlineMeasure? MeasureVertical(Point2D from) => Nearest(from, VerticalHits(from));

    private IEnumerable<Point2D> HorizontalHits(Point2D from)
    {
        foreach (var wall in Walls)
        {
            var a = wall.InnerStart; var b = wall.InnerEnd;
            var dy = b.Y - a.Y;
            if (Math.Abs(dy) <= Tolerance) continue;   // 수평 벽은 수평 레이와 평행하므로 제외
            var t = (from.Y - a.Y) / dy;
            if (t < 0 || t > 1) continue;
            yield return new Point2D(a.X + t * (b.X - a.X), from.Y);
        }
    }

    private IEnumerable<Point2D> VerticalHits(Point2D from)
    {
        foreach (var wall in Walls)
        {
            var a = wall.InnerStart; var b = wall.InnerEnd;
            var dx = b.X - a.X;
            if (Math.Abs(dx) <= Tolerance) continue;   // 수직 벽은 수직 레이와 평행하므로 제외
            var t = (from.X - a.X) / dx;
            if (t < 0 || t > 1) continue;
            yield return new Point2D(from.X, a.Y + t * (b.Y - a.Y));
        }
    }

    private static OutlineMeasure? Nearest(Point2D from, IEnumerable<Point2D> hits)
    {
        OutlineMeasure? best = null;
        foreach (var hit in hits)
        {
            var distance = from.DistanceTo(hit);
            if (distance <= Tolerance) continue;       // 변 위에 올라선 경우는 치수 표시 생략
            if (best is null || distance < best.Value.DistanceMm) best = new OutlineMeasure(from, hit, distance);
        }
        return best;
    }

    // 아래 세 후보 목록에는 기둥이 들어가지 않는다 — 기둥은 참고용 표시일 뿐이라는 사용자 확정에 따라
    // docs/184 § A-5에서 넣었던 기둥 스냅을 docs/189 § A-3에서 되돌렸다.

    /// <summary>내측면 끝점 목록. OSNAP 끝점 후보로 쓴다.</summary>
    public IEnumerable<Point2D> Endpoints()
    {
        foreach (var wall in Walls) { yield return wall.InnerStart; yield return wall.InnerEnd; }
    }

    /// <summary>내측면 중간점 목록. OSNAP 중간점 후보로 쓴다.</summary>
    public IEnumerable<Point2D> Midpoints()
    {
        foreach (var wall in Walls)
            yield return new Point2D((wall.InnerStart.X + wall.InnerEnd.X) / 2, (wall.InnerStart.Y + wall.InnerEnd.Y) / 2);
    }

    /// <summary><paramref name="point"/>에서 각 내측면에 내린 수선의 발 목록. OSNAP 근처점 후보로 쓴다.</summary>
    public IEnumerable<Point2D> NearestPoints(Point2D point)
    {
        foreach (var wall in Walls)
            yield return FootOfPerpendicular(point, wall.InnerStart, wall.InnerEnd);
    }

    private static Point2D FootOfPerpendicular(Point2D point, Point2D a, Point2D b)
    {
        var t = Math.Clamp(Segment2D.ParameterOnSegment(point, a, b), 0, 1);
        return new Point2D(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }

    /// <summary>IN 화살표가 붙을 좌측 내측면의 X. 내측면 전체의 최소 X를 좌측 벽면으로 본다.</summary>
    public double LeftFaceX => IsEmpty ? 0 : MinX;

    /// <summary>OUT 화살표가 붙을 우측 내측면의 X. 내측면 전체의 최대 X를 우측 벽면으로 본다.</summary>
    public double RightFaceX => IsEmpty ? 0 : MaxX;
    // TODO: LeftFaceX/RightFaceX는 AnchorOf로 통합 가능(docs/172 § A-2). 호출처가 사라진 뒤 정리.

    /// <summary>포트가 벽 내측면에 닿는 접점(캔버스 좌표 mm). 화살표·끝점 스냅·프리셋이 모두 이 값을 쓴다(docs/172 § A-2).</summary>
    /// <param name="fromFarEdge">Left/Right: true면 상단(MaxY)에서, false면 하단(MinY)에서 이격을 잰다(=ArrowFromTop).
    /// Top/Bottom: true면 우측(MaxX)에서, false면 좌측(MinX)에서 잰다.</param>
    public Point2D AnchorOf(PipePort port, bool fromFarEdge)
    {
        if (IsEmpty) return new Point2D(0, 0);
        return port.Side switch
        {
            OutlineSide.Left => new Point2D(MinX, fromFarEdge ? MaxY - port.OffsetMm : MinY + port.OffsetMm),
            OutlineSide.Right => new Point2D(MaxX, fromFarEdge ? MaxY - port.OffsetMm : MinY + port.OffsetMm),
            OutlineSide.Top => new Point2D(fromFarEdge ? MaxX - port.OffsetMm : MinX + port.OffsetMm, MaxY),
            OutlineSide.Bottom => new Point2D(fromFarEdge ? MaxX - port.OffsetMm : MinX + port.OffsetMm, MinY),
            _ => throw new ArgumentOutOfRangeException(nameof(port), port.Side, "지원하지 않는 변"),
        };
    }

    /// <summary>포트가 붙는 변의 길이(mm). 이격 범위 검증용.</summary>
    public double SideLengthOf(OutlineSide side) => side is OutlineSide.Left or OutlineSide.Right ? HeightMm : WidthMm;
}
