using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Gis;
using Xunit;

namespace DHBIMWATER.UI.Tests.Gis;

public class StraightSegmentOrientationTests
{
    [Fact]
    public void Alignment_start_uses_own_direction_for_both_ends()
    {
        var result = StraightSegmentOrientation.Resolve(
            new Point3D(1, 2, 3), new Point3D(5, 8, 10),
            previousEnd: null, previousOwnDirection: null,
            nextStart: null, nextOwnDirection: null);

        Assert.Equal(new Vector3D(4, 6, 7), result.Own);
        Assert.Equal(result.Own, result.Start);
        Assert.Equal(result.Own, result.End);
    }

    [Fact]
    public void Horizontal_right_angle_uses_same_bisector_at_both_sides()
    {
        var joint = new Point3D(1, 0, 0);
        var upstream = StraightSegmentOrientation.Resolve(
            new Point3D(0, 0, 0), joint,
            previousEnd: null, previousOwnDirection: null,
            nextStart: joint, nextOwnDirection: Vector3D.UnitY);
        var downstream = StraightSegmentOrientation.Resolve(
            joint, new Point3D(1, 1, 0),
            previousEnd: joint, previousOwnDirection: Vector3D.UnitX,
            nextStart: null, nextOwnDirection: null);
        var component = Math.Sqrt(0.5d);

        Assert.Equal(component, upstream.End.X, 12);
        Assert.Equal(component, upstream.End.Y, 12);
        Assert.Equal(0d, upstream.End.Z, 12);
        Assert.Equal(upstream.End, downstream.Start);
    }

    [Fact]
    public void Three_dimensional_joint_uses_normalized_direction_sum()
    {
        var first = new Vector3D(2, 0, 1);
        var second = new Vector3D(0, 3, 2);
        var firstUnit = first.Normalize();
        var secondUnit = second.Normalize();
        var expected = new Vector3D(
            firstUnit.X + secondUnit.X,
            firstUnit.Y + secondUnit.Y,
            firstUnit.Z + secondUnit.Z).Normalize();
        var result = StraightSegmentOrientation.Resolve(
            new Point3D(0, 0, 0), new Point3D(2, 0, 1),
            previousEnd: null, previousOwnDirection: null,
            nextStart: new Point3D(2, 0, 1), nextOwnDirection: second);

        Assert.Equal(expected.X, result.End.X, 12);
        Assert.Equal(expected.Y, result.End.Y, 12);
        Assert.Equal(expected.Z, result.End.Z, 12);
    }

    [Fact]
    public void Trimmed_gap_keeps_each_own_direction()
    {
        var upstreamEnd = new Point3D(1, 0, 0);
        var downstreamStart = new Point3D(1.01, 0, 0);
        var upstream = StraightSegmentOrientation.Resolve(
            new Point3D(0, 0, 0), upstreamEnd,
            previousEnd: null, previousOwnDirection: null,
            nextStart: downstreamStart, nextOwnDirection: Vector3D.UnitY);
        var downstream = StraightSegmentOrientation.Resolve(
            downstreamStart, new Point3D(1.01, 1, 0),
            previousEnd: upstreamEnd, previousOwnDirection: Vector3D.UnitX,
            nextStart: null, nextOwnDirection: null);

        Assert.Equal(upstream.Own, upstream.End);
        Assert.Equal(downstream.Own, downstream.Start);
    }

    [Fact]
    public void Different_alignment_boundary_keeps_own_end_direction()
    {
        var result = StraightSegmentOrientation.Resolve(
            new Point3D(0, 0, 0), new Point3D(2, 1, 0),
            previousEnd: null, previousOwnDirection: null,
            nextStart: null, nextOwnDirection: null);

        Assert.Equal(result.Own, result.End);
    }
}
