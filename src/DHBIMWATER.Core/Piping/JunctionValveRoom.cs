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
        bool CrossesY(JunctionWallFootprint w) => w.MinY <= center.Y && center.Y <= w.MaxY;
        bool CrossesX(JunctionWallFootprint w) => w.MinX <= center.X && center.X <= w.MaxX;
        var left = walls.Where(w => w.MaxX <= center.X && CrossesY(w)).MaxBy(w => w.MaxX);
        var right = walls.Where(w => w.MinX >= center.X && CrossesY(w)).MinBy(w => w.MinX);
        var bottom = walls.Where(w => w.MaxY <= center.Y && CrossesX(w)).MaxBy(w => w.MaxY);
        var top = walls.Where(w => w.MinY >= center.Y && CrossesX(w)).MinBy(w => w.MinY);
        if (left is null || right is null || bottom is null || top is null) return null;
        return new JunctionValveRoom(new[] { left, right, bottom, top }.DistinctBy(x => x.Id).ToList(),
            left.MaxX, bottom.MaxY, right.MinX, top.MinY,
            left.MinX, bottom.MinY, right.MaxX, top.MaxY);
    }
}
