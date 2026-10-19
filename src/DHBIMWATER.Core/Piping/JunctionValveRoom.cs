using System.Globalization;

namespace DHBIMWATER.Core.Piping;

// 벽 평면 사각형 4점. 좌표 = 뷰 오른쪽·위쪽 실제 mm (docs/293)
public sealed record JunctionWallFootprint(long Id, IReadOnlyList<ShapePoint> Corners)
{
    public double MinX => Corners.Min(x => x.X);
    public double MaxX => Corners.Max(x => x.X);
    public double MinY => Corners.Min(x => x.Y);
    public double MaxY => Corners.Max(x => x.Y);
}

// 부재 둘러싼 4면 벽 + 안쪽(벽 안쪽 면)·바깥쪽 범위. 직교 사각형 밸브실 가정
public sealed record JunctionValveRoom(IReadOnlyList<JunctionWallFootprint> Walls,
    double InnerMinX, double InnerMinY, double InnerMaxX, double InnerMaxY,
    double OuterMinX, double OuterMinY, double OuterMaxX, double OuterMaxY)
{
    public double InnerWidth => InnerMaxX - InnerMinX;
    public double InnerHeight => InnerMaxY - InnerMinY;
    // 가로 x 세로, m 단위. 예: 2.4 x 1.8
    public string Label => $"{Meters(InnerWidth)} x {Meters(InnerHeight)}";

    private static string Meters(double mm) => (mm / 1000).ToString("0.##", CultureInfo.InvariantCulture);

    // 중심 좌·우·아래·위에서 중심을 가로지르는 벽 중 가장 가까운 것. 한 면이라도 없으면 null
    public static JunctionValveRoom? Resolve(IReadOnlyList<JunctionWallFootprint> walls, ShapePoint center)
    {
        var (left, right, bottom, top) = Sides(walls, center);
        if (left is null || right is null || bottom is null || top is null) return null;
        return new JunctionValveRoom(new[] { left, right, bottom, top }.DistinctBy(x => x.Id).ToList(),
            left.MaxX, bottom.MaxY, right.MinX, top.MinY,
            left.MinX, bottom.MinY, right.MaxX, top.MaxY);
    }

    /// <summary>4면 못 찾은 이유(미리보기 경고, docs/299). 찾았으면 null</summary>
    public static string? Explain(IReadOnlyList<JunctionWallFootprint> walls, ShapePoint center)
    {
        if (walls.Count == 0) return "밸브실 벽 없음: 부재 주변 10m 안에 직선 벽이 없습니다.";
        var (left, right, bottom, top) = Sides(walls, center);
        var missing = new[] { (left, "좌측"), (right, "우측"), (bottom, "하단"), (top, "상단") }
            .Where(x => x.Item1 is null).Select(x => x.Item2).ToList();
        return missing.Count == 0 ? null
            : $"밸브실 벽 없음: 주변 벽 {walls.Count}개 중 {string.Join("·", missing)} 벽이 부재 중심" +
              $"(X {center.X:0}, Y {center.Y:0} mm)을 가로지르지 않습니다.";
    }

    // 중심 좌·우·아래·위에서 중심을 가로지르는 벽 중 가장 가까운 것
    private static (JunctionWallFootprint? Left, JunctionWallFootprint? Right, JunctionWallFootprint? Bottom,
        JunctionWallFootprint? Top) Sides(IReadOnlyList<JunctionWallFootprint> walls, ShapePoint center)
    {
        bool CrossesY(JunctionWallFootprint w) => w.MinY <= center.Y && center.Y <= w.MaxY;
        bool CrossesX(JunctionWallFootprint w) => w.MinX <= center.X && center.X <= w.MaxX;
        return (walls.Where(w => w.MaxX <= center.X && CrossesY(w)).MaxBy(w => w.MaxX),
            walls.Where(w => w.MinX >= center.X && CrossesY(w)).MinBy(w => w.MinX),
            walls.Where(w => w.MaxY <= center.Y && CrossesX(w)).MaxBy(w => w.MaxY),
            walls.Where(w => w.MinY >= center.Y && CrossesX(w)).MinBy(w => w.MinY));
    }
}

// 밸브실 찾기 결과. Room null이면 Problem = 이유
public sealed record JunctionValveRoomLookup(JunctionValveRoom? Room, string? Problem);
