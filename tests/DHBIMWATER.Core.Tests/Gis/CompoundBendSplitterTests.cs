using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Gis;
using Xunit;

namespace DHBIMWATER.Core.Tests.Gis;

public class CompoundBendSplitterTests
{
    // D1650 강관 곡관 다리 1000 mm × 2 → 쌍 길이 2 m.
    private static readonly Func<PipeAlignment, int, double, CompoundBendLegs?> Legs = (_, _, _) => new CompoundBendLegs(1000, 1000);

    [Fact]
    public void ParseAngles_ReadsTwoAnglesInFlowOrder()
    {
        var angles = CompoundBendSplitter.ParseAngles("22.5˚, 11.25˚");

        Assert.Equal([22.5, 11.25], angles.Select(x => x.Angle));
        Assert.Empty(CompoundBendSplitter.ParseAngles("22.5˚, MJ"));
    }

    [Fact]
    public void SingleCellPlanAndProfileBend_SplitsIntoConnectedPair()
    {
        // 평면 22.5° 뒤 종단 11.25° — 한 측점에 같이 적힌 경우.
        var vertex = new Point3D(10, 0, 0);
        var mid = Rotate(new Vector3D(1, 0, 0), 22.5);
        var outgoing = Pitch(mid, 11.25);
        var q = new Point3D(vertex.X + outgoing.X * 20, vertex.Y + outgoing.Y * 20, vertex.Z + outgoing.Z * 20);
        var alignment = Alignment([new Point3D(0, 0, 0), vertex, q], [null, "22.5˚, 11.25˚", null]);

        var result = CompoundBendSplitter.Split([alignment], Legs);

        var split = Assert.Single(result.Alignments);
        Assert.Equal(1, result.SplitCount);
        Assert.Equal(4, split.Vertices.Count);
        Assert.Equal(["22.5˚", "11.25˚"], split.VertexBendKinds!.Skip(1).Take(2));
        var (p, v1, v2, end) = (split.Vertices[0], split.Vertices[1], split.Vertices[2], split.Vertices[3]);
        Assert.Equal(2.0, v1.DistanceTo(v2), 9);
        // 상류 직선 유지 — V1은 x축 위.
        Assert.Equal(0, v1.Y, 9);
        Assert.Equal(0, v1.Z, 9);
        Assert.Equal(22.5, Angle(Sub(v1, p), Sub(v2, v1)), 6);
        Assert.Equal(11.25, Angle(Sub(v2, v1), Sub(end, v2)), 6);
        Assert.Same(q, end);
        Assert.Contains(result.Warnings, x => x.Contains("붙은 곡관 쌍 1곳"));
    }

    [Fact]
    public void CloseSamePlanePair_KeepsBothOuterLines()
    {
        // 같은 평면 22.5° + 22.5° 가 0.7 m 간격 — 다리 합 2 m보다 가깝다.
        var first = new Point3D(10, 0, 0);
        var mid = Rotate(new Vector3D(1, 0, 0), 22.5);
        var last = new Point3D(first.X + mid.X * 0.7, first.Y + mid.Y * 0.7, 0);
        var outgoing = Rotate(new Vector3D(1, 0, 0), 45);
        var q = new Point3D(last.X + outgoing.X * 20, last.Y + outgoing.Y * 20, 0);
        var alignment = Alignment([new Point3D(0, 0, 0), first, last, q], [null, "22.5˚", "22.5˚", null]);

        var split = CompoundBendSplitter.Split([alignment], Legs).Alignments[0];

        var (p, v1, v2, end) = (split.Vertices[0], split.Vertices[1], split.Vertices[2], split.Vertices[3]);
        Assert.Equal(2.0, v1.DistanceTo(v2), 9);
        Assert.Equal(0, v1.Y, 9);
        Assert.Equal(22.5, Angle(Sub(v1, p), Sub(v2, v1)), 6);
        // 하류 직선도 유지 — V2→Q 방향이 원래 하류 방향(45°)과 같다.
        Assert.Equal(0, Angle(Sub(end, v2), outgoing), 6);
    }

    [Fact]
    public void PairFartherThanLegs_IsNotSplit()
    {
        var alignment = Alignment([new Point3D(0, 0, 0), new Point3D(10, 0, 0), new Point3D(15, 2, 0), new Point3D(30, 10, 0)],
            [null, "22.5˚", "22.5˚", null]);

        var result = CompoundBendSplitter.Split([alignment], Legs);

        Assert.Equal(0, result.SplitCount);
        Assert.Same(alignment, result.Alignments[0]);
    }

    [Fact]
    public void MissingFittingSize_LeavesVertexAsIs()
    {
        var alignment = Alignment([new Point3D(0, 0, 0), new Point3D(10, 0, 0), new Point3D(20, 5, 2)], [null, "22.5˚, 11.25˚", null]);

        var result = CompoundBendSplitter.Split([alignment], (_, _, _) => null);

        Assert.Equal(0, result.SplitCount);
        Assert.Equal(3, result.Alignments[0].Vertices.Count);
    }

    private static PipeAlignment Alignment(IReadOnlyList<Point3D> vertices, IReadOnlyList<string?> kinds) => new(
        vertices, "STWW 400 A", 1650, "test.xlsx", "1", new Dictionary<string, string>(), kinds,
        VertexStationTexts: vertices.Select((_, i) => $"0+{i:000}").ToList());

    private static Vector3D Rotate(Vector3D v, double deg)
    {
        var r = deg * Math.PI / 180d;
        return new Vector3D(v.X * Math.Cos(r) - v.Y * Math.Sin(r), v.X * Math.Sin(r) + v.Y * Math.Cos(r), v.Z);
    }

    private static Vector3D Pitch(Vector3D v, double deg)
    {
        var horizontal = Math.Sqrt(v.X * v.X + v.Y * v.Y);
        var elevation = Math.Atan2(v.Z, horizontal) + deg * Math.PI / 180d;
        return new Vector3D(v.X / horizontal * Math.Cos(elevation), v.Y / horizontal * Math.Cos(elevation), Math.Sin(elevation));
    }

    private static Vector3D Sub(Point3D a, Point3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static double Angle(Vector3D a, Vector3D b)
    {
        var dot = (a.X * b.X + a.Y * b.Y + a.Z * b.Z) / (a.Length * b.Length);
        return Math.Acos(Math.Clamp(dot, -1d, 1d)) * 180d / Math.PI;
    }
}
